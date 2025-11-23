using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.OH;

public record ParseOhioTollsCommand(
    List<OhioTollRequestDto> OhioTollRequestDtos) : IRequest<ParseOhioTollsResult>;

public record ParseOhioTollsResult(
    int ProcessedTolls,
    int UpdatedTolls,
    int CreatedTolls,
    List<string> Errors);

public class ParseOhioTollsCommandHandler(
    ITollDbContext _context) : IRequestHandler<ParseOhioTollsCommand, ParseOhioTollsResult>
{
    public async Task<ParseOhioTollsResult> Handle(ParseOhioTollsCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        int processedTolls = 0;
        int updatedTolls = 0;
        int createdTolls = 0;

        try
        {
            // Обрабатываем каждый toll из запроса
            foreach (var ohioToll in request.OhioTollRequestDtos)
            {
                try
                {
                    // Пропускаем записи без координат
                    if (ohioToll.lat == 0 || ohioToll.lng == 0)
                    {
                        continue;
                    }

                    // Создаем точку
                    var tollPoint = new Point(ohioToll.lng, ohioToll.lat) { SRID = 4326 };

                    // Ищем все существующие Toll в радиусе 50 метров
                    var existingTolls = await FindTollsInRadiusAsync(_context, ohioToll.lat, ohioToll.lng, 400, ct);

                    if (existingTolls.Count > 0)
                    {
                        // Обновляем все найденные Toll
                        foreach (var existingToll in existingTolls)
                        {
                            var changed = false;

                            if (!string.IsNullOrWhiteSpace(ohioToll.name) &&
                                !string.Equals(existingToll.Name, ohioToll.name, StringComparison.Ordinal))
                            {
                                existingToll.Name = ohioToll.name;
                                changed = true;
                            }

                            if (existingToll.Key != ohioToll.name)
                            {
                                existingToll.Key = ohioToll.name;
                                changed = true;
                            }

                            if (changed)
                            {
                                updatedTolls++;
                            }
                        }
                    }
                    else
                    {
                        // Создаем новый Toll
                        var newToll = new Toll
                        {
                            Id = Guid.NewGuid(),
                            Name = ohioToll.name ?? string.Empty,
                            Number = string.Empty,
                            Location = tollPoint,
                            Key = ohioToll.name ?? string.Empty,
                            Price = 0,
                            isDynamic = false
                        };

                        _context.Tolls.Add(newToll);
                        createdTolls++;
                    }

                    processedTolls++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Ошибка при обработке toll {ohioToll.name ?? "unknown"}: {ex.Message}");
                    processedTolls++;
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Ошибка при обработке данных: {ex.Message}");
        }

        // Сохраняем все изменения один раз
        if (updatedTolls > 0 || createdTolls > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return new ParseOhioTollsResult(processedTolls, updatedTolls, createdTolls, errors);
    }

    private static async Task<List<Toll>> FindTollsInRadiusAsync(
        ITollDbContext context,
        double latitude,
        double longitude,
        double radiusMeters,
        CancellationToken ct)
    {
        // Константа для преобразования метров в градусы
        // Приблизительно 111320 метров на градус на экваторе
        // Для более точного расчета можно учитывать широту, но для небольших радиусов это достаточно точно
        const double MetersPerDegree = 111_320.0;

        var point = new Point(longitude, latitude) { SRID = 4326 };

        // Преобразуем метры в градусы
        var radiusDegrees = radiusMeters / MetersPerDegree;

        var tolls = await context.Tolls
            .Where(t => t.Location != null && t.Location.IsWithinDistance(point, radiusDegrees))
            .OrderBy(t => t.Location!.Distance(point))
            .ToListAsync(ct);

        return tolls;
    }
}

