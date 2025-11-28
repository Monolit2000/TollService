using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NJ;

public record AtlanticExpresswayTollRate(
    [property: System.Text.Json.Serialization.JsonPropertyName("plaza_name")] string PlazaName,
    [property: System.Text.Json.Serialization.JsonPropertyName("entry_name")] string? EntryName,
    [property: System.Text.Json.Serialization.JsonPropertyName("classification")] string? Classification,
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] double? Cash,
    [property: System.Text.Json.Serialization.JsonPropertyName("ez_pass_frequent_user")] double? EzPassFrequentUser);

public record AtlanticExpresswayPricesData(
    [property: System.Text.Json.Serialization.JsonPropertyName("state")] string? State,
    [property: System.Text.Json.Serialization.JsonPropertyName("road")] string? Road,
    [property: System.Text.Json.Serialization.JsonPropertyName("vehicle_class_id")] int? VehicleClassId,
    [property: System.Text.Json.Serialization.JsonPropertyName("description")] string? Description,
    [property: System.Text.Json.Serialization.JsonPropertyName("total_checked")] int? TotalChecked,
    [property: System.Text.Json.Serialization.JsonPropertyName("toll_rates")] List<AtlanticExpresswayTollRate>? TollRates);

public record LinkAtlanticExpresswayPricesCommand(string JsonPayload)
    : IRequest<LinkAtlanticExpresswayPricesResult>;

public record AtlanticExpresswayLinkedTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    decimal? OldPrice,
    decimal? NewPrice);

public record LinkAtlanticExpresswayPricesResult(
    List<AtlanticExpresswayLinkedTollInfo> LinkedTolls,
    List<string> NotFoundPlazas,
    int UpdatedCount,
    string? Error = null);

public class LinkAtlanticExpresswayPricesCommandHandler(
    ITollDbContext _context) : IRequestHandler<LinkAtlanticExpresswayPricesCommand, LinkAtlanticExpresswayPricesResult>
{
    // New Jersey bounds: (south, west, north, east) = (38.9, -75.6, 41.4, -73.9)
    private static readonly double NjMinLatitude = 38.9;
    private static readonly double NjMinLongitude = -75.6;
    private static readonly double NjMaxLatitude = 41.4;
    private static readonly double NjMaxLongitude = -73.9;

    public async Task<LinkAtlanticExpresswayPricesResult> Handle(LinkAtlanticExpresswayPricesCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new LinkAtlanticExpresswayPricesResult(new(), new(), 0, "JSON payload is empty");
        }

        AtlanticExpresswayPricesData? data;
        try
        {
            await Task.Yield();
            data = JsonSerializer.Deserialize<AtlanticExpresswayPricesData>(request.JsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException jsonEx)
        {
            return new LinkAtlanticExpresswayPricesResult(new(), new(), 0, $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.TollRates == null || data.TollRates.Count == 0)
        {
            return new LinkAtlanticExpresswayPricesResult(new(), new(), 0, "Плазы не найдены в ответе");
        }

        // Создаем bounding box для New Jersey
        var njBoundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(NjMinLongitude, NjMinLatitude),
            new Coordinate(NjMaxLongitude, NjMinLatitude),
            new Coordinate(NjMaxLongitude, NjMaxLatitude),
            new Coordinate(NjMinLongitude, NjMaxLatitude),
            new Coordinate(NjMinLongitude, NjMinLatitude)
        }))
        { SRID = 4326 };

        var linkedTolls = new List<AtlanticExpresswayLinkedTollInfo>();
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;

        foreach (var rate in data.TollRates)
        {
            if (string.IsNullOrWhiteSpace(rate.PlazaName))
            {
                notFoundPlazas.Add("Plaza with empty name");
                continue;
            }

            // Ищем ВСЕ tolls по plaza_name в пределах New Jersey
            // Сначала ищем точное совпадение (без учета регистра через ToLower)
            var plazaNameLower = rate.PlazaName.ToLower();
            var tolls = await _context.Tolls
                .Where(t =>
                    t.Location != null &&
                    njBoundingBox.Contains(t.Location) &&
                    t.Name != null &&
                    t.Name.ToLower() == plazaNameLower)
                .ToListAsync(ct);

            // Если не нашли точное совпадение, ищем по части имени (учитывая возможные суффиксы типа NB, SB)
            if (tolls.Count == 0)
            {
                // Пробуем найти по основной части имени (без суффиксов NB, SB, etc.)
                var nameWithoutSuffix = rate.PlazaName.TrimEnd();
                var commonSuffixes = new[] { " NB", " SB", " NX", " SX", " NE", " SE" };
                foreach (var suffix in commonSuffixes)
                {
                    if (nameWithoutSuffix.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        nameWithoutSuffix = nameWithoutSuffix.Substring(0, nameWithoutSuffix.Length - suffix.Length).Trim();
                        break;
                    }
                }

                var nameWithoutSuffixLower = nameWithoutSuffix.ToLower();
                var plazaNameLowerForContains = rate.PlazaName.ToLower();
                
                // Загружаем все tolls в пределах bounding box и фильтруем в памяти
                var allTollsInBox = await _context.Tolls
                    .Where(t =>
                        t.Location != null &&
                        njBoundingBox.Contains(t.Location) &&
                        t.Name != null)
                    .ToListAsync(ct);

                // Ищем только если имя toll содержит plaza_name И длина имени toll не намного меньше plaza_name
                // Это исключает случаи, когда короткое имя (например, "12") содержится в длинном ("Mays Landing (Exit 12)")
                var minTollNameLength = Math.Max(plazaNameLowerForContains.Length * 0.6, nameWithoutSuffixLower.Length * 0.6);
                tolls = allTollsInBox
                    .Where(t => 
                        t.Name!.ToLower().Contains(plazaNameLowerForContains) ||
                        t.Name!.ToLower().Contains(nameWithoutSuffixLower) ||
                        plazaNameLowerForContains.Contains(t.Name.ToLower()))
                    .ToList();
            }

            if (tolls.Count == 0)
            {
                notFoundPlazas.Add(rate.PlazaName);
                continue;
            }

            // Определяем цены из rates
            decimal? price = null;
            double? iPassPrice = null;
            // Используем cash для Price и PayOnline
            if (rate.Cash.HasValue)
            {
                price = (decimal)rate.Cash.Value;
            }
            else if (rate.EzPassFrequentUser.HasValue)
            {
                price = (decimal)rate.EzPassFrequentUser.Value;
            }

            // Используем ez_pass_frequent_user для IPass
            if (rate.EzPassFrequentUser.HasValue)
            {
                iPassPrice = rate.EzPassFrequentUser.Value;
            }

            // Устанавливаем цены для всех найденных tolls
            foreach (var toll in tolls)
            {
                var oldPrice = toll.Price;
                
                if (price.HasValue)
                {
                    toll.Price = price.Value;
                    toll.PayOnline = (double)price.Value;
                }

                if (iPassPrice.HasValue)
                {
                    toll.IPass = iPassPrice.Value;
                }

                linkedTolls.Add(new AtlanticExpresswayLinkedTollInfo(
                    PlazaName: rate.PlazaName,
                    TollId: toll.Id,
                    TollName: toll.Name,
                    OldPrice: oldPrice,
                    NewPrice: price));

                updatedCount++;
            }
        }

        await _context.SaveChangesAsync(ct);

        return new LinkAtlanticExpresswayPricesResult(
            linkedTolls,
            notFoundPlazas,
            updatedCount);
    }
}

