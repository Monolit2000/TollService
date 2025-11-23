using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.PA;

public record ParsePaTurnpikeInterchangesCommand(
    string Url = "https://www.paturnpike.com/toll-calculator") : IRequest<int>;

public class ParsePaTurnpikeInterchangesCommandHandler(
    ITollDbContext context,
    IHttpClientFactory httpClientFactory)
    : IRequestHandler<ParsePaTurnpikeInterchangesCommand, int>
{
    private static readonly double[] SearchRadiiMeters = [10, 25, 50, 100, 200];

    public async Task<int> Handle(ParsePaTurnpikeInterchangesCommand request, CancellationToken ct)
    {
        var httpClient = httpClientFactory.CreateClient();
        var html = await httpClient.GetStringAsync(request.Url, ct);

        var jsonPayload = ExtractServerDataJson(html);
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            throw new InvalidOperationException("Не удалось найти объект serverData в ответе.");
        }

        var serverData = JsonSerializer.Deserialize<ServerDataDto>(jsonPayload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (serverData == null)
        {
            return 0;
        }

        var interchanges = CollectInterchanges(serverData);
        if (interchanges.Count == 0)
        {
            return 0;
        }

        var updatedCount = 0;

        foreach (var interchange in interchanges)
        {
            var matchingToll = await FindClosestTollAsync(context,
                interchange.Latitude!.Value,
                interchange.Longitude!.Value,
                ct);

            var extractedNumber = ExtractLeadingNumber(interchange.Title);

            var cleanedTitle = NormalizeTitle(interchange.Title ?? interchange.Name);
            //var hasOrderedNumber = interchange.OrderedNumber.HasValue;
            var orderedNumber = interchange.OrderedNumber.ToString();
            var plazaKey = interchange.PlazaKey.ToString();
            var ptcExternalIdentifier = interchange.ptcExternalIdentifier;
            var targetNumber = ptcExternalIdentifier?.TrimEnd('0').TrimEnd('.'); /*?? orderedNumber;*/

            if (matchingToll == null)
            {
                // Create a new Toll
                var newToll = new Toll
                {
                    Id = Guid.NewGuid(),
                    Name = cleanedTitle,
                    Number = targetNumber,
                    Location = new Point(interchange.Longitude!.Value, interchange.Latitude!.Value) { SRID = 4326 },
                    Price = 0,
                    Key = extractedNumber,
                    isDynamic = false,
                    PaPlazaKay = interchange.PlazaKey ?? 0

                };

                context.Tolls.Add(newToll);
                updatedCount++;
            }
            else
            {
                // Update existing Toll
                var changed = false;

                //if (!string.IsNullOrWhiteSpace(cleanedTitle) &&
                //    !string.Equals(matchingToll.Name, cleanedTitle, StringComparison.Ordinal))
                
                    matchingToll.Name = cleanedTitle;
                    matchingToll.Number = targetNumber;
                    matchingToll.Key = cleanedTitle;
                    changed = true;
                

                //if (targetNumber != null && matchingToll.Number != targetNumber)
                //{
                //    matchingToll.Number = orderedNumber;
                //    changed = true;
                //}

                if (changed)
                {
                    updatedCount++;
                }
            }
        }

        if (updatedCount > 0)
        {
            await context.SaveChangesAsync(ct);
        }

        return updatedCount;
    }

    private static string? ExtractServerDataJson(string html)
    {
        const string marker = "var serverData";
        var markerIndex = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex == -1)
        {
            return null;
        }

        var startIndex = html.IndexOf('{', markerIndex);
        if (startIndex == -1)
        {
            return null;
        }

        var depth = 0;
        for (var i = startIndex; i < html.Length; i++)
        {
            var current = html[i];
            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return html[startIndex..(i + 1)];
                }
            }
        }

        return null;
    }

    private static List<InterchangeDto> CollectInterchanges(ServerDataDto serverData)
    {
        var interchanges = new Dictionary<string, InterchangeDto>(StringComparer.OrdinalIgnoreCase);
        
        if (serverData.Roadways != null && serverData.Roadways.Count > 0)
        {
            var stack = new Stack<RoadwayDto>(serverData.Roadways);

            while (stack.Count > 0)
            {
                var road = stack.Pop();
                if (road == null)
                {
                    continue;
                }

                TryAdd(road.StartInterchange);
                TryAdd(road.EndInterchange);

                if (road.ConnectedRoadways != null && road.ConnectedRoadways.Count > 0)
                {
                    foreach (var connected in road.ConnectedRoadways)
                    {
                        stack.Push(connected);
                    }
                }
            }
        }

        if (serverData.TollInterchanges != null)
        {
            foreach (var tollInterchange in serverData.TollInterchanges)
            {
                TryAdd(tollInterchange);
            }
        }

        if (serverData.IgnoredEnteringInterchanges != null)
        {
            foreach (var ignored in serverData.IgnoredEnteringInterchanges)
            {
                TryAdd(ignored.ToInterchange());
            }
        }

        if (serverData.IgnoredExitingInterchanges != null)
        {
            foreach (var ignored in serverData.IgnoredExitingInterchanges)
            {
                TryAdd(ignored.ToInterchange());
            }
        }

        return interchanges.Values.ToList();

        void TryAdd(InterchangeDto? interchange)
        {
            var title = interchange?.Title ?? interchange?.Name;
            if (interchange?.Latitude == null || interchange.Longitude == null || string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            interchange.Title = title;
            var key = interchange.Id ?? $"{interchange.Latitude:F6}:{interchange.Longitude:F6}:{title}";
            interchanges[key] = interchange;
        }
    }

    private static string? ExtractLeadingNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var match = Regex.Match(title, @"^\s*(\d+)\s*-");
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    private static async Task<Toll?> FindClosestTollAsync(
        ITollDbContext context,
        double latitude,
        double longitude,
        CancellationToken ct)
    {
        var point = new Point(longitude, latitude) { SRID = 4326 };

        foreach (var radius in SearchRadiiMeters)
        {
            var toll = await context.Tolls
                .Where(t => t.Location != null && t.Location.IsWithinDistance(point, radius))
                .OrderBy(t => t.Location!.Distance(point))
                .FirstOrDefaultAsync(ct);

            if (toll != null)
            {
                return toll;
            }
        }

        return null;
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(title, @"^\s*\d+\s*-\s*\[[^\]]+\]\s*", string.Empty);
        return cleaned.Trim();
    }

    private sealed class ServerDataDto
    {
        public List<RoadwayDto>? Roadways { get; set; }
        public List<InterchangeDto>? TollInterchanges { get; set; }
        public List<IgnoredInterchangeDto>? IgnoredEnteringInterchanges { get; set; }
        public List<IgnoredInterchangeDto>? IgnoredExitingInterchanges { get; set; }
    }

    private sealed class RoadwayDto
    {
        public string? Id { get; set; }
        public List<RoadwayDto>? ConnectedRoadways { get; set; }
        public InterchangeDto? StartInterchange { get; set; }
        public InterchangeDto? EndInterchange { get; set; }
    }

    private sealed class InterchangeDto
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Name { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? OrderedNumber { get; set; }

        public string? ptcExternalIdentifier { get; set; }
        public int? PlazaKey { get; set; }
    }

    private sealed class IgnoredInterchangeDto
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Name { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? OrderedNumber { get; set; }

        public string? ptcExternalIdentifier { get; set; }
        public int? PlazaKey { get; set; }

        public InterchangeDto ToInterchange() => new()
        {
            Id = Id,
            Title = Title ?? Name,
            Name = Name,
            Latitude = Latitude,
            Longitude = Longitude,
            OrderedNumber = OrderedNumber,
            PlazaKey = PlazaKey,
            ptcExternalIdentifier = ptcExternalIdentifier
        };
    }
}


