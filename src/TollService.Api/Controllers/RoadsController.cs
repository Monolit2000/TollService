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
    public async Task<IActionResult> AddRoad(AddRoadCommand command) => Ok(await _mediator.Send(command));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRoad(Guid id) => Ok(await _mediator.Send(new GetRoadByIdQuery(id)));

    [HttpPost("{roadId}/tolls")]
    public async Task<IActionResult> AddToll(Guid roadId, AddTollCommand command)
    {
        command = command with { RoadId = roadId };
        return Ok(await _mediator.Send(command));
    }

    [HttpGet("{roadId}/tolls")]
    public async Task<IActionResult> GetTolls(Guid roadId) 
        => Ok(await _mediator.Send(new GetTollsByRoadQuery(roadId)));

    [HttpPost("import/texas")]
    public async Task<IActionResult> ImportTexas() { await _importService.ImportTexasAsync(); return Ok("Imported"); }
}


