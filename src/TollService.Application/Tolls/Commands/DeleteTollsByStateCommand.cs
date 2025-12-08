using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Tolls.Commands;

/// <summary>
/// Удаляет все toll'ы для заданного штата по bounding box штата
/// (координаты такие же, как в OsmImportService.StateBounds).
/// Возвращает количество удалённых записей.
/// </summary>
public record DeleteTollsByStateCommand(string StateCode) : IRequest<int>;

public class DeleteTollsByStateCommandHandler(
    ITollDbContext _context) : IRequestHandler<DeleteTollsByStateCommand, int>
{
    // Границы штатов: (south, west, north, east)
    private static readonly Dictionary<string, (double south, double west, double north, double east)> StateBounds = new(StringComparer.OrdinalIgnoreCase)
    {
        { "TX", (25.8, -106.6, 36.5, -93.5) },
        { "CA", (32.5, -124.5, 42.0, -114.0) },
        { "FL", (24.5, -87.6, 31.0, -80.0) },
        { "NY", (40.5, -79.8, 45.0, -71.8) },
        { "NJ", (38.9, -75.6, 41.4, -73.9) },
        { "PA", (39.7, -80.5, 42.3, -74.7) },
        { "IL", (36.9, -91.5, 42.5, -87.0) },
        { "MD", (37.9, -79.5, 39.7, -75.0) },
        { "VA", (36.5, -83.7, 39.5, -75.2) },
        { "NC", (33.8, -84.3, 36.6, -75.4) },
        { "GA", (30.4, -85.6, 35.0, -80.8) },
        { "OH", (38.4, -84.8, 42.0, -80.5) },
        { "MI", (41.7, -90.4, 48.3, -82.1) },
        { "MA", (41.2, -73.5, 42.9, -69.9) },
        { "CT", (40.9, -73.7, 42.0, -71.8) },
        { "DE", (38.4, -75.8, 39.7, -75.0) },
        { "IN", (37.8, -88.1, 41.8, -84.8) },
        { "TN", (34.9, -90.3, 37.0, -81.7) },
        { "SC", (32.0, -83.4, 35.2, -78.5) },
        { "AL", (30.1, -88.5, 35.0, -84.9) },
        { "MS", (30.1, -91.7, 35.0, -88.1) },
        { "LA", (28.9, -94.0, 33.0, -88.8) },
        { "AR", (33.0, -94.6, 36.5, -89.7) },
        { "OK", (33.6, -103.0, 37.0, -94.4) },
        { "KS", (36.9, -102.0, 40.0, -94.6) },
        { "MO", (35.9, -95.8, 40.6, -89.1) },
        { "IA", (40.4, -96.6, 43.5, -90.1) },
        { "MN", (43.5, -97.2, 49.4, -89.5) },
        { "WI", (42.4, -92.9, 47.1, -86.8) },
        { "KY", (36.4, -89.6, 39.1, -81.9) },
        { "WV", (37.2, -82.7, 40.6, -77.7) },
        { "WA", (45.5, -124.8, 49.0, -116.9) },
        { "OR", (41.9, -124.7, 46.3, -116.5) },
        { "NV", (35.0, -120.0, 42.0, -114.0) },
        { "UT", (36.9, -114.0, 42.0, -109.0) },
        { "CO", (36.9, -109.0, 41.0, -102.0) },
        { "AZ", (31.3, -114.8, 37.0, -109.0) },
        { "NM", (31.3, -109.0, 37.0, -103.0) },
        { "ME", (42.9, -71.5, 47.5, -66.9) },
        { "AK", (51.2, -179.1, 71.4, -129.9) },
        { "HI", (18.9, -160.3, 22.4, -154.8) },
        { "ND", (45.9, -104.1, 49.0, -96.6) },
        { "SD", (42.5, -104.1, 45.9, -96.4) },
        { "NE", (39.9, -104.1, 43.1, -95.3) },
        { "MT", (44.4, -116.1, 49.0, -104.0) },
        { "WY", (40.9, -111.1, 45.0, -104.0) },
        { "ID", (41.9, -117.3, 49.0, -111.0) },
        { "RI", (41.1, -71.9, 42.0, -71.1) },
        { "VT", (42.7, -73.4, 45.0, -71.5) },
        { "NH", (42.7, -72.6, 45.3, -70.6) },
    };

    public async Task<int> Handle(DeleteTollsByStateCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StateCode))
        {
            return 0;
        }

        var stateCode = request.StateCode.Trim().ToUpperInvariant();

        if (!StateBounds.TryGetValue(stateCode, out var bounds))
        {
            // Неизвестный код штата — ничего не удаляем
            return 0;
        }

        // Создаём bounding box для штата и удаляем все toll'ы внутри него
        var boundingBox = BoundingBoxHelper.CreateBoundingBox(
            minLongitude: bounds.west,
            minLatitude: bounds.south,
            maxLongitude: bounds.east,
            maxLatitude: bounds.north);

        var tollsToDelete = await _context.Tolls
            .Where(t => t.Location != null && boundingBox.Contains(t.Location))
            .ToListAsync(ct);

        if (tollsToDelete.Count == 0)
        {
            return 0;
        }

        _context.Tolls.RemoveRange(tollsToDelete);
        await _context.SaveChangesAsync(ct);

        return tollsToDelete.Count;
    }
}


