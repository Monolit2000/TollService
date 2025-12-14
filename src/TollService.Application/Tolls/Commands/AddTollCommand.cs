using AutoMapper;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Contracts;
using TollService.Domain;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Tolls.Commands;

public record AddTollCommand(
    string? Name, 
    string? Key, 
    decimal Price, 
    double Latitude, 
    double Longitude, 
    string? Comment = null,
    string? WebsiteUrl = null,
    bool IsDynamic = false,
    double IPassOvernight = 0,
    double IPass = 0,
    double PayOnlineOvernight = 0,
    double PayOnline = 0) : IRequest<TollDto>;

public class AddTollCommandHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<AddTollCommand, TollDto>
{
    public async Task<TollDto> Handle(AddTollCommand request, CancellationToken ct)
    {
        var toll = new Toll
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Price = request.Price,
            Key = request.Key,
            Comment = request.Comment,
            WebsiteUrl = request.WebsiteUrl,
            isDynamic = request.IsDynamic,
            Location = new Point(request.Longitude, request.Latitude) { SRID = 4326 },
            IPassOvernight = request.IPassOvernight,
            IPass = request.IPass,
            PayOnlineOvernight = request.PayOnlineOvernight,
            PayOnline = request.PayOnline
        };

        _context.Tolls.Add(toll);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<TollDto>(toll);
    }
}


