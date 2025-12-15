using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.Tolls.Commands;

/// <summary>
/// Загружает tolls из БД, пересчитывает SerchRadiusInMeters (по всем точкам) и сохраняет обратно в БД.
/// </summary>
public record UpdateTollSearchRadiiCommand(double DefaultRadiusMeters = 500.0) : IRequest<int>;

public class UpdateTollSearchRadiiCommandHandler(
    ITollDbContext _context,
    ITollSearchRadiusService _radiusService,
    TollSearchService tollSearchService) : IRequestHandler<UpdateTollSearchRadiiCommand, int>
{
    public async Task<int> Handle(UpdateTollSearchRadiiCommand request, CancellationToken ct)
    {
        var boundingBox = BoundingBoxHelper.CreateBoundingBox(40.5, -79.8, 45.0, -71.8);

        var tolls = await tollSearchService.FindTollsInBoundingBoxAsync(40.5, -79.8, 45.0, -71.8, ct: ct);

        if (tolls.Count == 0)
            return 0;

        _radiusService.ApplyNonOverlappingRadii(tolls, defaultRadiusMeters: request.DefaultRadiusMeters);

        await _context.SaveChangesAsync(ct);
        return tolls.Count;
    }
}


