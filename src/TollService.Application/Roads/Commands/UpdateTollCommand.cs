using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Roads.Commands;

public record UpdateTollCommand(
    Guid Id,
    string? Name = null,
    decimal? Price = null,
    double? Latitude = null,
    double? Longitude = null,
    Guid? RoadId = null,
    string? Key = null,
    string? Comment = null,
    bool? IsDynamic = null) : IRequest<TollDto?>;

public class UpdateTollCommandHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<UpdateTollCommand, TollDto?>
{
    public async Task<TollDto?> Handle(UpdateTollCommand request, CancellationToken ct)
    {
        var toll = await _context.Tolls.FirstOrDefaultAsync(t => t.Id == request.Id, ct);
        
        if (toll == null)
            return null;

        if (request.Name != null)
            toll.Name = request.Name;

        if (request.Price.HasValue)
            toll.Price = request.Price.Value;

        if (request.Latitude.HasValue && request.Longitude.HasValue)
            toll.Location = new Point(request.Longitude.Value, request.Latitude.Value) { SRID = 4326 };

        if (request.RoadId.HasValue)
            toll.RoadId = request.RoadId.Value;

        if (request.Key != null)
            toll.Key = request.Key;

        if (request.Comment != null)
            toll.Comment = request.Comment;

        if (request.IsDynamic.HasValue)
            toll.isDynamic = request.IsDynamic.Value;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<TollDto>(toll);
    }
}

