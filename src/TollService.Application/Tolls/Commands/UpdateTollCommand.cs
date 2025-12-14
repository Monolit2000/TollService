using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Tolls.Commands;

public record UpdateTollCommand(
    Guid Id,
    string? Name = null,
    decimal? Price = null,
    double? Latitude = null,
    double? Longitude = null,
    Guid? RoadId = null,
    string? Key = null,
    string? Comment = null,
    string? WebsiteUrl = null,
    bool? IsDynamic = null,
    double? IPassOvernight = null,
    double? IPass = null,
    double? PayOnlineOvernight = null,
    double? PayOnline = null) : IRequest<TollDto?>;

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

        if (request.WebsiteUrl != null)
            toll.WebsiteUrl = request.WebsiteUrl;

        if (request.IsDynamic.HasValue)
            toll.isDynamic = request.IsDynamic.Value;

        if (request.IPassOvernight.HasValue)
            toll.IPassOvernight = request.IPassOvernight.Value;

        if (request.IPass.HasValue)
            toll.IPass = request.IPass.Value;

        if (request.PayOnlineOvernight.HasValue)
            toll.PayOnlineOvernight = request.PayOnlineOvernight.Value;

        if (request.PayOnline.HasValue)
            toll.PayOnline = request.PayOnline.Value;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<TollDto>(toll);
    }
}

