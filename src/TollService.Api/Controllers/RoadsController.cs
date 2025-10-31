using MediatR;
using Microsoft.AspNetCore.Mvc;
using TollService.Application.Roads.Commands;
using TollService.Application.Roads.Queries;
using TollService.Infrastructure.Integrations;

namespace TollService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoadsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly OsmImportService _importService;

    public RoadsController(IMediator mediator, OsmImportService importService)
    {
        _mediator = mediator;
        _importService = importService;
    }

    [HttpPost]
    public async Task<IActionResult> AddRoad(AddRoadCommand command, CancellationToken ct) => Ok(await _mediator.Send(command, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRoad(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new GetRoadByIdQuery(id), ct));

    [HttpPost("{roadId}/tolls")]
    public async Task<IActionResult> AddToll(Guid roadId, AddTollCommand command, CancellationToken ct)
    {
        command = command with { RoadId = roadId };
        return Ok(await _mediator.Send(command, ct));
    }

    [HttpGet("{roadId}/tolls")]
    public async Task<IActionResult> GetTolls(Guid roadId, CancellationToken ct) 
        => Ok(await _mediator.Send(new GetTollsByRoadQuery(roadId), ct));

    [HttpPost("import/texas")]
    public async Task<IActionResult> ImportTexas(CancellationToken ct) 
    { 
        await _importService.ImportTexasAsync(ct); 
        return Ok("Imported"); 
    }

    [HttpPost("import/la")]
    public async Task<IActionResult> ImportLosAngeles(CancellationToken ct) 
    { 
        await _importService.ImportLosAngelesTollRoadsAsync(ct); 
        return Ok("Imported LA toll roads"); 
    }

    [HttpGet("near")]
    public async Task<IActionResult> GetRoadsNearPoint([FromQuery] double lat, [FromQuery] double lon, [FromQuery] double radius, CancellationToken ct)
        => Ok(await _mediator.Send(new GetRoadsNearPointQuery(lat, lon, radius), ct));

    [HttpGet("{roadId}/tolls/on-road")]
    public async Task<IActionResult> GetTollsOnRoad(Guid roadId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetTollsOnRoadQuery(roadId), ct));

    [HttpGet("{roadId}/distance")]
    public async Task<IActionResult> GetRoadDistance(Guid roadId, [FromQuery] double lat, [FromQuery] double lon, CancellationToken ct)
        => Ok(await _mediator.Send(new GetRoadDistanceQuery(roadId, lat, lon), ct));

    [HttpGet("{roadId}/length")]
    public async Task<IActionResult> GetRoadLength(Guid roadId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetRoadLengthQuery(roadId), ct));
}


