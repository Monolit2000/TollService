using AutoMapper;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Contracts;
using TollService.Domain;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Commands;

public record AddTollCommand( string? Name, string? Key, decimal Price, double Latitude, double Longitude) : IRequest<TollDto>;

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
            Location = new Point(request.Longitude, request.Latitude) { SRID = 4326 }
        };

        _context.Tolls.Add(toll);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<TollDto>(toll);
    }
}


