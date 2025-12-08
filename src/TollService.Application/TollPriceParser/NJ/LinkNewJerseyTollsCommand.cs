using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NJ;

public record NewJerseyPlaza(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("lat")] double Lat,
    [property: JsonPropertyName("lng")] double Lng);

public record LinkNewJerseyTollsCommand(string JsonPayload)
    : IRequest<LinkNewJerseyTollsResult>;

public record FoundTollInfo(
    string PlazaId,
    string PlazaName,
    Guid? TollId,
    string? TollName,
    string? TollKey,
    string? TollNumber,
    double? DistanceMeters);

public record LinkNewJerseyTollsResult(
    List<FoundTollInfo> FoundTolls,
    List<string> NotFoundPlazas,
    string? Error = null);

// New Jersey bounds: (south, west, north, east) = (38.9, -75.6, 41.4, -73.9)
public class LinkNewJerseyTollsCommandHandler(
    ITollDbContext _context,
    StateCalculatorService _stateCalculatorService,
    TollNumberService _tollNumberService) : IRequestHandler<LinkNewJerseyTollsCommand, LinkNewJerseyTollsResult>
{
    private static readonly double NjMinLatitude = 38.9;
    private static readonly double NjMinLongitude = -75.6;
    private static readonly double NjMaxLatitude = 41.4;
    private static readonly double NjMaxLongitude = -73.9;

    // Радиус поиска в метрах
    private const double SearchRadiusMeters = 700.0;
    private const double MetersPerDegree = 111_320.0;

    public async Task<LinkNewJerseyTollsResult> Handle(LinkNewJerseyTollsCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new LinkNewJerseyTollsResult(
                new(),
                new(),
                "JSON payload is empty");
        }

        List<NewJerseyPlaza>? plazas;
        try
        {
            plazas = JsonSerializer.Deserialize<List<NewJerseyPlaza>>(request.JsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException jsonEx)
        {
            return new LinkNewJerseyTollsResult(
                new(),
                new(),
                $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (plazas == null || plazas.Count == 0)
        {
            return new LinkNewJerseyTollsResult(
                new(),
                new(),
                "Плазы не найдены в ответе");
        }

        // Получаем или создаем StateCalculator для New Jersey
        var newJerseyCalculator = await _stateCalculatorService.GetOrCreateStateCalculatorAsync(
            stateCode: "NJ",
            calculatorName: "New Jersey Turnpike",
            ct);

        // Создаем bounding box для New Jersey
        var njBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            NjMinLongitude,
            NjMinLatitude,
            NjMaxLongitude,
            NjMaxLatitude);

        var foundTolls = new List<FoundTollInfo>();
        var notFoundPlazas = new List<string>();

        foreach (var plaza in plazas)
        {
            try
            {
                // Создаем точку для поиска
                var searchPoint = new Point(plaza.Lng, plaza.Lat) { SRID = 4326 };
                var radiusDegrees = SearchRadiusMeters / MetersPerDegree;

                // Ищем toll'ы в радиусе от точки плазы, в пределах bounding box New Jersey
                var nearbyTolls = await _context.Tolls
                    .Where(t =>
                        t.Location != null &&
                        njBoundingBox.Contains(t.Location) &&
                        t.Location.IsWithinDistance(searchPoint, radiusDegrees))
                    .OrderBy(t => t.Location!.Distance(searchPoint))
                    .ToListAsync(ct);

                if (nearbyTolls.Count == 0)
                {
                    notFoundPlazas.Add($"{plaza.Id}: {plaza.Name} (lat: {plaza.Lat}, lng: {plaza.Lng})");
                    continue;
                }

                // Берем ближайший toll
                var toll = nearbyTolls.First();
                var distance = toll.Location!.Distance(searchPoint) * MetersPerDegree;

                // Устанавливаем данные только если плаза не исключена (14C)
                if (plaza.Id != "14C")
                {
                    // Используем TollNumberService для установки Number и StateCalculatorId
                    _tollNumberService.SetNumberAndCalculatorId(
                        new[] { toll },
                        plaza.Id,
                        newJerseyCalculator.Id,
                        updateNumberIfDifferent: true);

                    // Устанавливаем Key и Name напрямую
                    toll.Key = plaza.Id;
                    toll.Name = plaza.Name;
                }

                foundTolls.Add(new FoundTollInfo(
                    PlazaId: plaza.Id,
                    PlazaName: plaza.Name,
                    TollId: toll.Id,
                    TollName: toll.Name,
                    TollKey: toll.Key,
                    TollNumber: toll.Number,
                    DistanceMeters: distance));
            }
            catch (Exception ex)
            {
                notFoundPlazas.Add($"{plaza.Id}: {plaza.Name} - Ошибка: {ex.Message}");
            }
        }

        // Сохраняем все изменения
        await _context.SaveChangesAsync(ct);

        return new LinkNewJerseyTollsResult(
            foundTolls,
            notFoundPlazas.Distinct().ToList());
    }
}

