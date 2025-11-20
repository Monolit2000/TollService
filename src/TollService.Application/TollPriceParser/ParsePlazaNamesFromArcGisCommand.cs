using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser;

public record ParsePlazaNamesFromArcGisCommand(
    string ArcGisUrl = "https://gis.illinoisvirtualtollway.com/arcgis/rest/services/IVT/IllinoisVirtualTollway/MapServer/13/query") 
    : IRequest<ParsePlazaNamesResult>;

public record ParsePlazaNamesResult(
    int ProcessedRoads,
    int UpdatedTolls,
    List<string> Errors);

public class ParsePlazaNamesFromArcGisCommandHandler(
    ITollDbContext _context,
    IHttpClientFactory _httpClientFactory) : IRequestHandler<ParsePlazaNamesFromArcGisCommand, ParsePlazaNamesResult>
{
    private const double ToleranceMeters = 3; // Допустимое расстояние для сопоставления (100 метров)
    
    private const int BatchSize = 500; // Размер батча дорог
    
    public async Task<ParsePlazaNamesResult> Handle(ParsePlazaNamesFromArcGisCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        int processedRoads = 0;
        int updatedTolls = 0;
        
        // Получаем все дороги с геометрией
        var roads = await _context.Roads
            .Where(r => r.Geometry != null)
            .ToListAsync(ct);
        
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10); // Увеличиваем таймаут для больших запросов
        
        // Разделяем дороги на батчи по 500
        var batches = roads
            .Where(r => r.Geometry != null && r.Geometry.Coordinates.Length >= 2)
            .Chunk(BatchSize);
        
        foreach (var batch in batches)
        {
            try
            {
                // Объединяем все полилайны из батча в один полигон
                var geometryJson = ConvertRoadsBatchToArcGisGeometry(batch);
                
                if (string.IsNullOrWhiteSpace(geometryJson))
                {
                    processedRoads += batch.Length;
                    continue;
                }
                
                // Формируем FormData
                var formData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("f", "json"),
                    new KeyValuePair<string, string>("where", ""),
                    new KeyValuePair<string, string>("returnGeometry", "true"),
                    new KeyValuePair<string, string>("spatialRel", "esriSpatialRelIntersects"),
                    new KeyValuePair<string, string>("geometry", geometryJson),
                    new KeyValuePair<string, string>("geometryType", "esriGeometryPolygon"),
                    new KeyValuePair<string, string>("inSR", "3857"),
                    new KeyValuePair<string, string>("outFields", "*"),
                    new KeyValuePair<string, string>("outSR", "3857")
                });
                
                // Делаем POST запрос
                var response = await httpClient.PostAsync(request.ArcGisUrl, formData, ct);
                response.EnsureSuccessStatusCode();
                
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var arcGisResponse = JsonSerializer.Deserialize<ArcGisResponse>(responseJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (arcGisResponse?.Features == null || arcGisResponse.Features.Count == 0)
                {
                    processedRoads += batch.Length;
                    continue;
                }
                
                // Обрабатываем каждую найденную плазу
                foreach (var feature in arcGisResponse.Features)
                {
                    
                    if(feature.Attributes.Plaza_Name.Contains("Roselle Road (EB Exit/WB Entrance)"))
                    {

                    }

                    if (feature.Attributes == null || string.IsNullOrWhiteSpace(feature.Attributes.Plaza_Name))
                        continue;
                    
                    var plazaLat = feature.Attributes.Lat;
                    var plazaLon = feature.Attributes.Lon;
                    
                    if (plazaLat == 0 || plazaLon == 0)
                        continue;
                    
                    // Ищем ближайший Toll по координатам
                    var plazaPoint = new Point(plazaLon, plazaLat) { SRID = 4326 };
                    
                    var nearestToll = await _context.Tolls
                        .Where(t => t.Location != null)
                        .OrderBy(t => t.Location!.Distance(plazaPoint))
                        .FirstOrDefaultAsync(ct);
                    
                    if (nearestToll != null && nearestToll.Location != null)
                    {
                        // Проверяем, что расстояние не превышает допустимое
                        var distanceDegrees = nearestToll.Location.Distance(plazaPoint);
                        var distanceMeters = distanceDegrees * 111320; // Конвертируем градусы в метры (приблизительно)
                        
                        if (distanceMeters <= ToleranceMeters)
                        {
                            nearestToll.Name = feature.Attributes.Plaza_Name;
                            updatedTolls++;
                        }
                    }
                }
                
                processedRoads += batch.Length;
                
                // Сохраняем изменения после каждого батча
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                var roadNames = string.Join(", ", batch.Take(5).Select(r => r.Name));
                var more = batch.Length > 5 ? $" и еще {batch.Length - 5}" : "";
                errors.Add($"Ошибка при обработке батча дорог ({batch.Length} шт.): {roadNames}{more}. Ошибка: {ex.Message}");
                processedRoads += batch.Length;
            }
        }
        
        return new ParsePlazaNamesResult(processedRoads, updatedTolls, errors);
    }
    
    private static string ConvertRoadsBatchToArcGisGeometry(IEnumerable<Road> roads)
    {
        var allRings = new List<List<List<double>>>();
        var bufferDistance = 50.0 / 111320.0; // Приблизительная конвертация метров в градусы
        
        foreach (var road in roads)
        {
            if (road.Geometry == null || road.Geometry.Coordinates.Length < 2)
                continue;
            
            Geometry? buffered = null;
            try
            {
                buffered = road.Geometry.Buffer(bufferDistance);
            }
            catch
            {
                // Если не получилось создать буфер, используем саму линию
            }
            
            var ring = new List<List<double>>();
            
            if (buffered is Polygon polygon && polygon.ExteriorRing != null)
            {
                foreach (var coord in polygon.ExteriorRing.Coordinates)
                {
                    var (x, y) = Wgs84ToWebMercator(coord.X, coord.Y);
                    ring.Add(new List<double> { x, y });
                }
            }
            else
            {
                // Если не получилось создать буфер, используем саму линию как полигон
                foreach (var coord in road.Geometry.Coordinates)
                {
                    var (x, y) = Wgs84ToWebMercator(coord.X, coord.Y);
                    ring.Add(new List<double> { x, y });
                }
                
                // Замыкаем полигон
                if (ring.Count > 0 && (ring[0][0] != ring[^1][0] || ring[0][1] != ring[^1][1]))
                {
                    ring.Add(new List<double> { ring[0][0], ring[0][1] });
                }
            }
            
            if (ring.Count > 0)
            {
                allRings.Add(ring);
            }
        }
        
        if (allRings.Count == 0)
        {
            return string.Empty;
        }
        
        // Если только один ring, возвращаем его
        // Если несколько, объединяем их в один полигон (используем первый как внешний, остальные как дыры)
        var geometry = new
        {
            rings = allRings,
            spatialReference = new { wkid = 3857 }
        };
        
        return JsonSerializer.Serialize(geometry);
    }
    
    private static string ConvertLineStringToArcGisGeometry(LineString lineString)
    {
        // Создаем буфер вокруг линии для создания полигона
        // Используем буфер 50 метров (в градусах для SRID 4326)
        var bufferDistance = 50.0 / 111320.0; // Приблизительная конвертация метров в градусы
        
        Geometry? buffered = null;
        try
        {
            buffered = lineString.Buffer(bufferDistance);
        }
        catch
        {
            // Если не получилось создать буфер, используем саму линию
        }
        
        // Преобразуем координаты в Web Mercator (3857)
        var coordinates = new List<List<List<double>>>();
        var ring = new List<List<double>>();
        
        if (buffered is Polygon polygon && polygon.ExteriorRing != null)
        {
            foreach (var coord in polygon.ExteriorRing.Coordinates)
            {
                // Преобразуем из WGS84 (4326) в Web Mercator (3857)
                var (x, y) = Wgs84ToWebMercator(coord.X, coord.Y);
                ring.Add(new List<double> { x, y });
            }
        }
        else
        {
            // Если не получилось создать буфер, используем саму линию как полигон
            foreach (var coord in lineString.Coordinates)
            {
                var (x, y) = Wgs84ToWebMercator(coord.X, coord.Y);
                ring.Add(new List<double> { x, y });
            }
            
            // Замыкаем полигон
            if (ring.Count > 0 && (ring[0][0] != ring[^1][0] || ring[0][1] != ring[^1][1]))
            {
                ring.Add(new List<double> { ring[0][0], ring[0][1] });
            }
        }
        
        if (ring.Count > 0)
        {
            coordinates.Add(ring);
        }
        
        var geometry = new
        {
            rings = coordinates,
            spatialReference = new { wkid = 3857 }
        };
        
        return JsonSerializer.Serialize(geometry);
    }
    
    private static (double x, double y) Wgs84ToWebMercator(double lon, double lat)
    {
        // Преобразование WGS84 (EPSG:4326) в Web Mercator (EPSG:3857)
        var x = lon * 20037508.34 / 180.0;
        var latRad = lat * Math.PI / 180.0;
        var y = Math.Log(Math.Tan(Math.PI / 4.0 + latRad / 2.0)) * 20037508.34 / Math.PI;
        return (x, y);
    }
}

// Классы для десериализации ответа ArcGIS
public class ArcGisResponse
{
    public List<ArcGisFeature> Features { get; set; } = new();
}

public class ArcGisFeature
{
    public ArcGisAttributes? Attributes { get; set; }
    public ArcGisGeometry? Geometry { get; set; }
}

public class ArcGisAttributes
{
    public string Plaza_Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string Plaza_Num { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
}

public class ArcGisGeometry
{
    public double X { get; set; }
    public double Y { get; set; }
}


