using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Tolls.Queries;

public record GetTollsByBoundingBoxQuery(
    double MinLatitude, 
    double MinLongitude, 
    double MaxLatitude, 
    double MaxLongitude) : IRequest<List<TollDto>>;

public class GetTollsByBoundingBoxQueryHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<GetTollsByBoundingBoxQuery, List<TollDto>>
{
    public async Task<List<TollDto>> Handle(GetTollsByBoundingBoxQuery request, CancellationToken ct)
    {
        var boundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(request.MinLongitude, request.MinLatitude),
            new Coordinate(request.MaxLongitude, request.MinLatitude),
            new Coordinate(request.MaxLongitude, request.MaxLatitude),
            new Coordinate(request.MinLongitude, request.MaxLatitude),
            new Coordinate(request.MinLongitude, request.MinLatitude) 
        })) { SRID = 4326 };

        var tolls = await _context.Tolls
            .Where(t => t.Location != null && boundingBox.Contains(t.Location))
            .ToListAsync(ct);

        return _mapper.Map<List<TollDto>>(tolls);
    }
}





