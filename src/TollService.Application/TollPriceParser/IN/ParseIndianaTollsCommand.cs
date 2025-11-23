using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.IN;

public record ParseIndianaTollsCommand(
    List<IndianaTollRequestDto> IndianaTollRequestDtos) : IRequest<ParseIndianaTollsResult>;

public record ParseIndianaTollsResult(
    int ProcessedTolls,
    int UpdatedTolls,
    int CreatedTolls,
    List<string> Errors);

public class ParseIndianaTollsCommandHandler(
    ITollDbContext _context) : IRequestHandler<ParseIndianaTollsCommand, ParseIndianaTollsResult>
{
    public async Task<ParseIndianaTollsResult> Handle(ParseIndianaTollsCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        int processedTolls = 0;
        int updatedTolls = 0;
        int createdTolls = 0;

        try
        {


            // Обрабатываем каждый toll из JSON
            foreach (var indianaToll in request.IndianaTollRequestDtos)
            {
                try
                {
                    // Пропускаем записи без координат
                    if (indianaToll.lat == 0 || indianaToll.lng == 0)
                    {
                        continue;
                    }

                    // Создаем точку
                    var tollPoint = new Point(indianaToll.lng, indianaToll.lat) { SRID = 4326 };

                    // Ищем все существующие Toll в радиусе 50 метров
                    var existingTolls = await FindTollsInRadiusAsync(_context, indianaToll.lat, indianaToll.lng, 100, ct);

                    if (existingTolls.Count > 0)
                    {
                        // Обновляем все найденные Toll
                        foreach (var existingToll in existingTolls)
                        {
                            var changed = false;

                            if (!string.IsNullOrWhiteSpace(indianaToll.name) &&
                                !string.Equals(existingToll.Name, indianaToll.name, StringComparison.Ordinal))
                            {
                                existingToll.Name = indianaToll.name;
                                changed = true;
                            }

                            // Используем barrier_number как Number
                            var barrierNumber = indianaToll.barrier_number ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(barrierNumber) &&
                                existingToll.Number != barrierNumber)
                            {
                                existingToll.Number = barrierNumber;
                                changed = true;
                            }

                            if (existingToll.Key != indianaToll.name)
                            {
                                existingToll.Key = indianaToll.name;
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
                        //// Создаем новый Toll
                        //var newToll = new Toll
                        //{
                        //    Id = Guid.NewGuid(),
                        //    Name = indianaToll.Name ?? string.Empty,
                        //    Number = indianaToll.BarrierNumber ?? string.Empty,
                        //    Location = tollPoint,
                        //    Key = indianaToll.Name ?? string.Empty,
                        //    Price = 0,
                        //    isDynamic = false
                        //};

                        //_context.Tolls.Add(newToll);
                        //createdTolls++;
                    }

                    processedTolls++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Ошибка при обработке toll {indianaToll.name ?? "unknown"}: {ex.Message}");
                    processedTolls++;
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Ошибка при десериализации JSON: {ex.Message}");
        }

        // Сохраняем все изменения один раз
        if (updatedTolls > 0 || createdTolls > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return new ParseIndianaTollsResult(processedTolls, updatedTolls, createdTolls, errors);
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

// DTO классы для десериализации JSON
public class IndianaTollDto
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("toll")]
    public string Toll { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("ramps")]
    public string Ramps { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("mile")]
    public int Mile { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("lat")]
    public double Lat { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("lng")]
    public double Lng { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("direction")]
    public string Direction { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("barrier_number")]
    public string BarrierNumber { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("system")]
    public string System { get; set; } = string.Empty;
}

