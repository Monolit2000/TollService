using NetTopologySuite.Geometries;

namespace TollService.Application.Common;

/// <summary>
/// Вспомогательный класс для создания bounding box (прямоугольника) для географических границ штата.
/// </summary>
public static class BoundingBoxHelper
{
    /// <summary>
    /// Создает Polygon (bounding box) из координат границ штата.
    /// </summary>
    /// <param name="minLongitude">Минимальная долгота (западная граница)</param>
    /// <param name="minLatitude">Минимальная широта (южная граница)</param>
    /// <param name="maxLongitude">Максимальная долгота (восточная граница)</param>
    /// <param name="maxLatitude">Максимальная широта (северная граница)</param>
    /// <returns>Polygon с SRID = 4326</returns>
    public static Polygon CreateBoundingBox(
    double minLatitude,
    double minLongitude,
    double maxLatitude,
    double maxLongitude)
    {
        var boundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(minLongitude, minLatitude),
            new Coordinate(maxLongitude, minLatitude),
            new Coordinate(maxLongitude, maxLatitude),
            new Coordinate(minLongitude, maxLatitude),
            new Coordinate(minLongitude, minLatitude)
        }))
        { SRID = 4326 };

        return boundingBox;
    }
}

