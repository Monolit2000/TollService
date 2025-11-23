using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser;

public record ParseExitPointsToPlazasCommand(
    string QueryUrl = "https://gis.illinoisvirtualtollway.com/arcgis/rest/services/IVT/IllinoisVirtualTollway/MapServer/18/query",
    string IdentifyUrl = "https://gis.illinoisvirtualtollway.com/arcgis/rest/services/IVT/IllinoisVirtualTollway/MapServer/identify") 
    : IRequest<ParseExitPointsToPlazasResult>;

public record ParseExitPointsToPlazasResult(
    int ProcessedExitPoints,
    int UpdatedTolls,
    List<string> Errors);

public class ParseExitPointsToPlazasCommandHandler(
    ITollDbContext _context,
    IHttpClientFactory _httpClientFactory) : IRequestHandler<ParseExitPointsToPlazasCommand, ParseExitPointsToPlazasResult>
{
    public async Task<ParseExitPointsToPlazasResult> Handle(ParseExitPointsToPlazasCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        int processedExitPoints = 0;
        int updatedTolls = 0;
        
        // Сохраняем созданные Toll для последующей группировки и удаления дубликатов
        var createdTolls = new List<Toll>();
        
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);
        
        try
        {
            // Первый запрос: получаем все exit points
            var queryUrl = $"{request.QueryUrl}?f=json&where=1%3D1%20AND%201763601274966%3D1763601274966&returnGeometry=true&spatialRel=esriSpatialRelIntersects&outFields=LABEL%2CGUID%2Cidnum%2CExitPointsSort&outSR=3857";
            
            var queryResponse = await httpClient.GetStringAsync(queryUrl, ct);
            var exitPointsResponse = JsonSerializer.Deserialize<ExitPointsResponse>(queryResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (exitPointsResponse?.Features == null || exitPointsResponse.Features.Count == 0)
            {
                return new ParseExitPointsToPlazasResult(0, 0, new List<string> { "Не найдено exit points" });
            }
            
            // Обрабатываем каждый exit point
            foreach (var exitPoint in exitPointsResponse.Features)
            {
                if (exitPoint.Geometry == null)
                    continue;
                
                try
                {
                    var x = exitPoint.Geometry.X;
                    var y = exitPoint.Geometry.Y;
                    
                    // Второй запрос: identify для получения плаз по координатам
                    var identifyUrl = BuildIdentifyUrl(request.IdentifyUrl, x, y);
                    var identifyResponse = await httpClient.GetStringAsync(identifyUrl, ct);
                    var identifyResult = JsonSerializer.Deserialize<IdentifyResponse>(identifyResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (identifyResult?.Results == null || identifyResult.Results.Count == 0)
                    {
                        processedExitPoints++;
                        continue;
                    }
                    
                    // Обрабатываем каждую найденную плазу
                    foreach (var result in identifyResult.Results)
                    {
                        if (result.Attributes == null || string.IsNullOrWhiteSpace(result.Attributes.Plaza_Name))
                            continue;
                        
                        var plazaLatStr = result.Attributes.Lat;
                        var plazaLonStr = result.Attributes.Lon;
                        
                        if (string.IsNullOrWhiteSpace(plazaLatStr) || string.IsNullOrWhiteSpace(plazaLonStr))
                            continue;
                        
                        if (!double.TryParse(plazaLatStr, System.Globalization.NumberStyles.Any, 
                            System.Globalization.CultureInfo.InvariantCulture, out var plazaLat))
                            continue;
                        
                        if (!double.TryParse(plazaLonStr, System.Globalization.NumberStyles.Any, 
                            System.Globalization.CultureInfo.InvariantCulture, out var plazaLon))
                            continue;
                        
                        if (plazaLat == 0 || plazaLon == 0)
                            continue;
                        
                        // Парсим номер плазы (может содержать буквы, например "5A")
                        var plazaNumber = ParsePlazaNumber(result.Attributes.Plaza_Num);
                        
                        // Создаем новую плазу
                        var plazaPoint = new Point(plazaLon, plazaLat) { SRID = 4326 };
                        
                        var newToll = new Toll
                        {
                            Id = Guid.NewGuid(),
                            Name = result.Attributes.Plaza_Name,
                            Number = plazaNumber.ToString(),
                            Location = plazaPoint,
                            Key = result.Attributes.Plaza_Name,
                            Price = 0,
                            isDynamic = false
                        };
                        
                        // Сохраняем в список для последующей группировки
                        createdTolls.Add(newToll);
                    }
                    
                    processedExitPoints++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Ошибка при обработке exit point {exitPoint.Attributes?.LABEL ?? "unknown"}: {ex.Message}");
                    processedExitPoints++;
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Ошибка при получении exit points: {ex.Message}");
        }
        
        // Группируем по координатам и оставляем только уникальные
        // Округляем координаты до 6 знаков после запятой для группировки (~0.1 метра точности)
        var uniqueTolls = createdTolls
            .GroupBy(t => t.Location)
            .Select(g => g.First())
            .ToList();
        
        // Добавляем уникальные Toll в контекст
        foreach (var toll in uniqueTolls)
        {
            _context.Tolls.Add(toll);
            updatedTolls++;
        }
        
        // Сохраняем все изменения один раз
        await _context.SaveChangesAsync(ct);
        
        return new ParseExitPointsToPlazasResult(processedExitPoints, updatedTolls, errors);
    }
    
    private static string BuildIdentifyUrl(string baseUrl, double x, double y)
    {
        // Создаем geometry для identify запроса
        var geometry = new { x, y };
        var geometryJson = JsonSerializer.Serialize(geometry);
        var geometryEncoded = Uri.EscapeDataString(geometryJson);
        
        // Вычисляем mapExtent на основе координат (примерный bbox)
        var extentSize = 100000; // ~1 км в Web Mercator
        var minX = x - extentSize;
        var minY = y - extentSize;
        var maxX = x + extentSize;
        var maxY = y + extentSize;
        var mapExtent = $"{minX},{minY},{maxX},{maxY}";
        
        var url = $"{baseUrl}?f=json&tolerance=15&returnGeometry=true&returnFieldName=false&returnUnformattedValues=false&imageDisplay=1181,1271,96&geometry={geometryEncoded}&geometryType=esriGeometryPoint&sr=3857&mapExtent={mapExtent}&layers=visible:13";
        
        return url;
    }
    
    private static int ParsePlazaNumber(string? plazaNum)
    {
        if (string.IsNullOrWhiteSpace(plazaNum))
            return 0;
        
        // Извлекаем числовую часть (например, "5A" -> "5", "300" -> "300")
        var match = System.Text.RegularExpressions.Regex.Match(plazaNum, @"\d+");
        if (match.Success && int.TryParse(match.Value, out var number))
        {
            return number;
        }
        
        return 0;
    }
}

// Классы для десериализации ответа Exit Points
public class ExitPointsResponse
{
    public List<ExitPointFeature> Features { get; set; } = new();
}

public class ExitPointFeature
{
    public ExitPointAttributes? Attributes { get; set; }
    public ExitPointGeometry? Geometry { get; set; }
}

public class ExitPointAttributes
{
    public string LABEL { get; set; } = string.Empty;
    public int GUID { get; set; }
    public string idnum { get; set; } = string.Empty;
    public string ExitPointsSort { get; set; } = string.Empty;
}

public class ExitPointGeometry
{
    public double X { get; set; }
    public double Y { get; set; }
}

// Классы для десериализации ответа Identify
public class IdentifyResponse
{
    public List<IdentifyResult> Results { get; set; } = new();
}

public class IdentifyResult
{
    public int LayerId { get; set; }
    public string LayerName { get; set; } = string.Empty;
    public string DisplayFieldName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public IdentifyAttributes? Attributes { get; set; }
    public IdentifyGeometry? Geometry { get; set; }
}

public class IdentifyAttributes
{
    public string Plaza_Name { get; set; } = string.Empty;
    public string Lat { get; set; } = string.Empty;
    public string Lon { get; set; } = string.Empty;
    public string Plaza_Num { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string OBJECTID { get; set; } = string.Empty;
    public string GUID_ { get; set; } = string.Empty;
    public string TYPE { get; set; } = string.Empty;
    public string Dir { get; set; } = string.Empty;
}

public class IdentifyGeometry
{
    public double X { get; set; }
    public double Y { get; set; }
    public SpatialReference? SpatialReference { get; set; }
}

public class SpatialReference
{
    public int Wkid { get; set; }
    public int LatestWkid { get; set; }
}

