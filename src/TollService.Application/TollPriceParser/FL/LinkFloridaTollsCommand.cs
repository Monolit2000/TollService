using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.FL;

public record LinkFloridaTollsCommand() : IRequest<LinkFloridaTollsResult>;

public record FloridaFoundTollInfo(
    string ExitRegion,
    string ExitFacility,
    string ExitName,
    string ExitNum,
    string ExitId,
    Guid TollId,
    string? TollName,
    string? TollKey,
    string? TollNumber);

public record LinkFloridaTollsResult(
    int ProcessedInterchanges,
    int CreatedTolls,
    int UpdatedTolls,
    int UpdatedPrices,
    List<FloridaFoundTollInfo> LinkedTolls,
    List<string> NotFoundInterchanges,
    List<string> Errors,
    string? Error = null);

/// <summary>
/// Загружает все точки съездов Флориды с публичного API
/// https://ftecloudprod01.com/getAllRouteAttributes
/// и линкует/создает Toll'ы по координатам и названиям,
/// а также записывает цены по осям для SunPass (как EZPass) и Cash.
/// </summary>
public class LinkFloridaTollsCommandHandler(
    ITollDbContext _context,
    IHttpClientFactory _httpClientFactory)
    : IRequestHandler<LinkFloridaTollsCommand, LinkFloridaTollsResult>
{
    private const string FloridaApiUrl = "https://ftecloudprod01.com/getAllRouteAttributes";

    // Florida bounds: (south, west, north, east) — совпадает с OsmImportService.StateBounds["FL"]
    private static readonly double FlMinLatitude = 24.5;
    private static readonly double FlMinLongitude = -87.6;
    private static readonly double FlMaxLatitude = 31.0;
    private static readonly double FlMaxLongitude = -80.0;

    // Радиусы поиска в метрах вокруг interchange
    private static readonly double[] SearchRadiiMeters = [50, 100, 200];

    public async Task<LinkFloridaTollsResult> Handle(LinkFloridaTollsCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        var notFoundInterchanges = new List<string>();
        var linkedTolls = new List<FloridaFoundTollInfo>();

        int processed = 0;
        int created = 0;
        int updated = 0;
        int updatedPrices = 0;

        FloridaRouteAttributesResponse? data;

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(60);

            var json = await httpClient.GetStringAsync(FloridaApiUrl, ct);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            data = JsonSerializer.Deserialize<FloridaRouteAttributesResponse>(json, options);
        }
        catch (Exception ex)
        {
            return new LinkFloridaTollsResult(
                0, 0, 0, 0,
                new(),
                new(),
                new() { $"Ошибка при запросе или разборе данных Флориды: {ex.Message}" },
                $"Ошибка при запросе к {FloridaApiUrl}: {ex.Message}");
        }

        if (data?.Interchanges == null || data.Interchanges.Count == 0)
        {
            return new LinkFloridaTollsResult(
                0, 0, 0, 0,
                new(),
                new(),
                new() { "Не найдены interchanges во входных данных Флориды" });
        }


        data.Interchanges.ForEach(x => x.WebsiteUrl = "https://floridasturnpike.com/system-maps/");

        foreach (var interchange in data.Interchanges)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            try
            {
                if (interchange.ExitCoordinates == null ||
                    interchange.ExitCoordinates.Count < 2)
                {
                    errors.Add($"Interchange '{interchange.ExitName}' ({interchange.ExitId}) пропущен: нет координат");
                    continue;
                }

                var lat = interchange.ExitCoordinates[0];
                var lon = interchange.ExitCoordinates[1];

                // Фильтруем только точки, попадающие во Флориду
                if (lat < FlMinLatitude || lat > FlMaxLatitude ||
                    lon < FlMinLongitude || lon > FlMaxLongitude)
                {
                    continue;
                }

                // Определяем, есть ли вообще платные тарифы для этой точки (по 2-осевому ТС)
                //var hasToll =
                //    interchange.TollPerAxlePerMethod != null &&
                //    interchange.TollPerAxlePerMethod.Any(a =>
                //        a.Axle == 5 && (a.SunPass > 0 || a.Cash > 0));

                //if (!hasToll)
                //{
                //    // Для robustness можно пропускать бесплатные съезды
                //    continue;
                //}

                var tollPoint = new Point(lon, lat) { SRID = 4326 };

                // Пытаемся найти до двух ближайших Toll'ов по координатам (как в NY/PA)
                var existingTolls = await FindClosestFloridaTollsAsync(
                    lat,
                    lon,
                    ct,
                    maxCount: 2);

                var targetTolls = new List<Toll>();
                var exitKey = $"{interchange.ExitFacility} - {interchange.ExitName} ({interchange.ExitNum})";

                if (existingTolls.Count == 0)
                {
                    // Создаем новый Toll (один)
                    var newToll = new Toll
                    {
                        Id = Guid.NewGuid(),
                        Name = interchange.ExitName ?? interchange.ExitFacility ?? "Florida toll",
                        Number = string.IsNullOrWhiteSpace(interchange.ExitNum) ? null : interchange.ExitNum,
                        Location = tollPoint,
                        Price = 0,
                        Key = exitKey,
                        isDynamic = false
                    };

                    _context.Tolls.Add(newToll);
                    targetTolls.Add(newToll);
                    created++;
                }
                else
                {

                    foreach (var existingToll in existingTolls)
                    {
                        var changed = false;

                        //var newName = interchange.ExitName ?? interchange.ExitFacility;
                        //if (!string.IsNullOrWhiteSpace(newName) &&
                        //    !string.Equals(existingToll.Name, newName, StringComparison.Ordinal))
                        //{
                        //}

                        existingToll.Name = exitKey /*newName*/;
                        changed = true;

                        if(existingToll.WebsiteUrl != interchange.WebsiteUrl)
                        {
                            existingToll.WebsiteUrl = interchange.WebsiteUrl;
                        }

                        var newNumber = string.IsNullOrWhiteSpace(interchange.ExitNum) ? null : interchange.ExitNum;
                        if (newNumber != null && existingToll.Number != newNumber)
                        {
                            existingToll.Number = newNumber;
                            changed = true;
                        }

                        if (existingToll.Key != exitKey)
                        {
                            existingToll.Key = exitKey;
                            changed = true;
                        }

                        if (changed)
                        {
                            updated++;
                        }

                        targetTolls.Add(existingToll);
                    }
                }

                // Обновляем / создаем TollPrice по осям
                if (interchange.TollPerAxlePerMethod != null && targetTolls.Count > 0)
                {
                    foreach (var axlePrice in interchange.TollPerAxlePerMethod)
                    {
                        var axelType = MapAxleToAxelType(axlePrice.Axle);
                        if (axelType == AxelType.Unknown)
                        {
                            continue;
                        }

                        foreach (var toll in targetTolls)
                        {
                            if (axlePrice.SunPass > 0)
                            {
                                toll.SetPriceByPaymentType(
                                    axlePrice.SunPass,
                                    TollPaymentType.SunPass,
                                    axelType);
                                updatedPrices++;
                            }

                            if (axlePrice.Cash > 0)
                            {
                                toll.SetPriceByPaymentType(
                                    axlePrice.Cash,
                                    TollPaymentType.Cash,
                                    axelType);
                                updatedPrices++;
                            }
                        }
                    }
                }

                foreach (var toll in targetTolls)
                {
                    linkedTolls.Add(new FloridaFoundTollInfo(
                        interchange.ExitRegion ?? string.Empty,
                        interchange.ExitFacility ?? string.Empty,
                        interchange.ExitName ?? string.Empty,
                        interchange.ExitNum ?? string.Empty,
                        interchange.ExitId ?? string.Empty,
                        toll.Id,
                        toll.Name,
                        toll.Key,
                        toll.Number));
                }
            }
            catch (Exception ex)
            {
                var key = $"{interchange.ExitFacility} | {interchange.ExitName} ({interchange.ExitId})";
                notFoundInterchanges.Add(key);
                errors.Add($"Ошибка при обработке interchange '{key}': {ex.Message}");
            }
        }

        if (created > 0 || updated > 0 || updatedPrices > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return new LinkFloridaTollsResult(
            processed,
            created,
            updated,
            updatedPrices,
            linkedTolls,
            notFoundInterchanges.Distinct().ToList(),
            errors);
    }

    /// <summary>
    /// Находит до maxCount ближайших Toll'ов к заданной точке,
    /// постепенно увеличивая радиус поиска.
    /// </summary>
    private async Task<List<Toll>> FindClosestFloridaTollsAsync(
        double latitude,
        double longitude,
        CancellationToken ct,
        int maxCount = 2)
    {
        var point = new Point(longitude, latitude) { SRID = 4326 };
        var result = new List<Toll>();

        foreach (var radiusMeters in SearchRadiiMeters)
        {
            const double metersPerDegree = 111_320.0;
            var radiusDegrees = radiusMeters / metersPerDegree;

            var tolls = await _context.Tolls
                .Where(t => t.Location != null && t.Location.IsWithinDistance(point, radiusDegrees))
                .OrderBy(t => t.Location!.Distance(point))
                .Take(maxCount)
                .ToListAsync(ct);

            if (tolls.Count > 0)
            {
                result.AddRange(tolls);

                if (result.Count >= maxCount)
                {
                    return result.Take(maxCount).ToList();
                }
            }
        }

        return result;
    }

    private static AxelType MapAxleToAxelType(int axle)
    {
        return axle switch
        {
            // В большинстве систем 2 оси = класс 1, 3 оси = класс 2 и т.д.
            2 => AxelType._1L,
            3 => AxelType._2L,
            4 => AxelType._3L,
            5 => AxelType._4L,
            6 => AxelType._5L,
            7 => AxelType._6L,
            8 => AxelType._7L,
            9 => AxelType._8L,
            _ => AxelType.Unknown
        };
    }
}

internal sealed class FloridaRouteAttributesResponse
{
    [JsonPropertyName("interchanges")]
    public List<FloridaInterchangeDto> Interchanges { get; set; } = new();
}

internal sealed class FloridaInterchangeDto
{
    [JsonPropertyName("exitRegion")]
    public string? ExitRegion { get; set; }

    [JsonPropertyName("exitRegionId")]
    public string? ExitRegionId { get; set; }

    [JsonPropertyName("exitFacility")]
    public string? ExitFacility { get; set; }

    [JsonPropertyName("exitFacilityId")]
    public string? ExitFacilityId { get; set; }

    [JsonPropertyName("exitName")]
    public string? ExitName { get; set; }

    [JsonPropertyName("exitNum")]
    public string? ExitNum { get; set; }

    [JsonPropertyName("exitId")]
    public string? ExitId { get; set; }

    [JsonPropertyName("ticketSystem")]
    public bool TicketSystem { get; set; }

    [JsonPropertyName("sunpassOnly")]
    public bool SunpassOnly { get; set; }

    [JsonPropertyName("tollByPlate")]
    public bool TollByPlate { get; set; }

    [JsonPropertyName("geometryCode")]
    public string? GeometryCode { get; set; }

    [JsonPropertyName("tollPerAxlePerMethod")]
    public List<FloridaAxlePriceDto>? TollPerAxlePerMethod { get; set; }

    [JsonPropertyName("exitCoordinates")]
    public List<double> ExitCoordinates { get; set; } = new();

    public string WebsiteUrl { get; set; }
}

internal sealed class FloridaAxlePriceDto
{
    [JsonPropertyName("axle")]
    public int Axle { get; set; }

    [JsonPropertyName("sunPass")]
    public double SunPass { get; set; }

    [JsonPropertyName("cash")]
    public double Cash { get; set; }
}


