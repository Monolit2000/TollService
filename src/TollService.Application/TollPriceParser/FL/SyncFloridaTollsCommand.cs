using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System.Linq;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.FL;

public record SyncFloridaTollsCommand() : IRequest<SyncFloridaTollsResult>;

public record SyncFloridaTollsResult(
    int ProcessedTolls,
    int UpdatedTolls,
    int CopiedPrices,
    List<string> Errors,
    string? Error = null);

/// <summary>
/// Синхронизирует tolls Флориды: для каждого toll с Key == null
/// находит ближайший toll с Key != null и копирует Number, Name, Key и все цены.
/// </summary>
public class SyncFloridaTollsCommandHandler(ITollDbContext _context)
    : IRequestHandler<SyncFloridaTollsCommand, SyncFloridaTollsResult>
{

    private List<Guid> UsedTolls = [];

    // Florida bounds: (south, west, north, east)
    private static readonly double FlMinLatitude = 24.5;
    private static readonly double FlMinLongitude = -87.6;
    private static readonly double FlMaxLatitude = 31.0;
    private static readonly double FlMaxLongitude = -80.0;

    // Радиусы поиска в метрах для поиска ближайшего toll с Key
    private static readonly double[] SearchRadiiMeters = [5000];

    public async Task<SyncFloridaTollsResult> Handle(SyncFloridaTollsCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        int processed = 0;
        int updated = 0;
        int copiedPrices = 0;

        try
        {
            // Создаем bounding box для Флориды
            var boundingBox = new Polygon(new LinearRing(new[]
            {
                new Coordinate(FlMinLongitude, FlMinLatitude),
                new Coordinate(FlMaxLongitude, FlMinLatitude),
                new Coordinate(FlMaxLongitude, FlMaxLatitude),
                new Coordinate(FlMinLongitude, FlMaxLatitude),
                new Coordinate(FlMinLongitude, FlMinLatitude)
            })) { SRID = 4326 };

            // Получаем все tolls в границах Флориды с загрузкой цен
            var floridaTolls = await _context.Tolls
                .Include(t => t.TollPrices)
                .Where(t => t.Location != null && boundingBox.Contains(t.Location))
                .ToListAsync(ct);

            // Разделяем на tolls с Key и без Key
            var tollsWithoutKey = floridaTolls
                .Where(t => string.IsNullOrWhiteSpace(t.Key))
                .ToList();

            var tollsWithKey = floridaTolls
                .Where(t => !string.IsNullOrWhiteSpace(t.Key) && t.TollPrices != null)
                .ToList();

            foreach (var tollWithoutKey in tollsWithoutKey)
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                try
                {
                    if (tollWithoutKey.Location == null)
                    {
                        errors.Add($"Toll {tollWithoutKey.Id} пропущен: нет координат");
                        continue;
                    }

                    // Ищем ближайший toll с Key != null
                    var nearestTollWithKey = FindNearestTollWithKey(
                        tollWithoutKey.Location.Y,
                        tollWithoutKey.Location.X,
                        tollsWithKey);

                    if (nearestTollWithKey == null)
                    {
                        errors.Add($"Toll {tollWithoutKey.Id} ({tollWithoutKey.Name}): не найден ближайший toll с Key");
                        continue;
                    }

                    // Копируем Number, Name, Key
                    var changed = false;
                    if (nearestTollWithKey.Number != null && tollWithoutKey.Number != nearestTollWithKey.Number)
                    {
                        tollWithoutKey.Number = nearestTollWithKey.Number;
                        changed = true;
                    }

                    if (nearestTollWithKey.Name != null && tollWithoutKey.Name != nearestTollWithKey.Name)
                    {
                        tollWithoutKey.Name = nearestTollWithKey.Name;
                        changed = true;
                    }

                    if (nearestTollWithKey.Key != null && tollWithoutKey.Key != nearestTollWithKey.Key)
                    {
                        tollWithoutKey.Key = nearestTollWithKey.Key;
                        changed = true;
                    }

                    UsedTolls.Add(nearestTollWithKey.Id);


                    if (changed)
                    {
                        updated++;
                    }

                    // Копируем все цены
                    if (nearestTollWithKey.TollPrices != null && nearestTollWithKey.TollPrices.Count > 0)
                    {
                        if (tollWithoutKey.TollPrices == null)
                        {
                            tollWithoutKey.TollPrices = new List<TollPrice>();
                        }

                        foreach (var sourcePrice in nearestTollWithKey.TollPrices)
                        {
                            // Пропускаем цены, связанные с CalculatePrice (они не должны копироваться)
                            if (sourcePrice.CalculatePriceId.HasValue)
                            {
                                continue;
                            }

                            // Проверяем, нет ли уже такой же цены
                            var existingPrice = tollWithoutKey.TollPrices.FirstOrDefault(p =>
                                p.PaymentType == sourcePrice.PaymentType &&
                                p.AxelType == sourcePrice.AxelType &&
                                p.DayOfWeekFrom == sourcePrice.DayOfWeekFrom &&
                                p.DayOfWeekTo == sourcePrice.DayOfWeekTo &&
                                p.TimeOfDay == sourcePrice.TimeOfDay &&
                                !p.CalculatePriceId.HasValue);

                            if (existingPrice != null)
                            {
                                // Обновляем существующую цену
                                existingPrice.Amount = sourcePrice.Amount;
                                existingPrice.Description = sourcePrice.Description;
                                existingPrice.TimeFrom = sourcePrice.TimeFrom;
                                existingPrice.TimeTo = sourcePrice.TimeTo;
                            }
                            else
                            {
                                // Создаем новую цену
                                var newPrice = new TollPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TollId = tollWithoutKey.Id,
                                    CalculatePriceId = null,
                                    Amount = sourcePrice.Amount,
                                    PaymentType = sourcePrice.PaymentType,
                                    AxelType = sourcePrice.AxelType,
                                    DayOfWeekFrom = sourcePrice.DayOfWeekFrom,
                                    DayOfWeekTo = sourcePrice.DayOfWeekTo,
                                    TimeOfDay = sourcePrice.TimeOfDay,
                                    TimeFrom = sourcePrice.TimeFrom,
                                    TimeTo = sourcePrice.TimeTo,
                                    Description = sourcePrice.Description
                                };

                                tollWithoutKey.TollPrices.Add(newPrice);
                                _context.TollPrices.Add(newPrice);
                                copiedPrices++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Ошибка при обработке toll {tollWithoutKey.Id}: {ex.Message}");
                }
            }

            if (updated > 0 || copiedPrices > 0)
            {
                await _context.SaveChangesAsync(ct);
            }

            return new SyncFloridaTollsResult(
                processed,
                updated,
                copiedPrices,
                errors);
        }
        catch (Exception ex)
        {
            return new SyncFloridaTollsResult(
                0,
                0,
                0,
                new List<string> { ex.Message },
                $"Ошибка при синхронизации tolls Флориды: {ex.Message}");
        }
    }

    /// <summary>
    /// Находит ближайший toll с Key != null к заданной точке.
    /// </summary>
    private Toll? FindNearestTollWithKey(
        double latitude,
        double longitude,
        List<Toll> tollsWithKey)
    {
        if (tollsWithKey.Count == 0)
        {
            return null;
        }

        var point = new Point(longitude, latitude) { SRID = 4326 };
        Toll? nearestToll = null;
        double minDistance = double.MaxValue;

        foreach (var radiusMeters in SearchRadiiMeters)
        {
            const double metersPerDegree = 111_320.0;
            var radiusDegrees = radiusMeters / metersPerDegree;

            // Ищем tolls с Key в пределах радиуса
            var candidates = tollsWithKey
                .Where(t => t.Location != null && t.Location.IsWithinDistance(point, radiusDegrees))
                .ToList();

            if (candidates.Count > 0)
            {

                // Находим ближайший
                foreach (var candidate in candidates)
                {
                    //if (UsedTolls.Contains(candidate.Id))
                    //    continue;

                    if (candidate.Location == null) continue;

                    var distance = candidate.Location.Distance(point);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearestToll = candidate;
                    }
                }

                if (nearestToll != null)
                {
                    return nearestToll;
                }
            }
        }

        return null;
    }
}

