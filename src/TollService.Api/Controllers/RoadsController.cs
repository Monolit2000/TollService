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
}


