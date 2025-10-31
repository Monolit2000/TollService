using AutoMapper;
using MediatR;
using TollService.Contracts;
using TollService.Domain;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Commands;

public record AddRoadCommand(string Name, string HighwayType, bool IsToll) : IRequest<RoadDto>;

public class AddRoadCommandHandler : IRequestHandler<AddRoadCommand, RoadDto>
{
    private readonly ITollDbContext _context;
    private readonly IMapper _mapper;

    public AddRoadCommandHandler(ITollDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<RoadDto> Handle(AddRoadCommand request, CancellationToken ct)
    {
        var road = new Road
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            HighwayType = request.HighwayType,
            IsToll = request.IsToll
        };

        _context.Roads.Add(road);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<RoadDto>(road);
    }
}


