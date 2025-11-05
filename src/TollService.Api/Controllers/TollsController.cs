using MediatR;
using Microsoft.AspNetCore.Mvc;
using TollService.Application.Roads.Commands;
using TollService.Application.Roads.Queries;
using TollService.Application.Tolls.Queries;
using TollService.Contracts;
using TollService.Infrastructure.Integrations;

namespace TollService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TollsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly OsmImportService _importService;

    public TollsController(IMediator mediator, OsmImportService importService)
    {
        _mediator = mediator;
        _importService = importService;
    }

    [HttpGet("nearest")]
    public async Task<IActionResult> GetNearest([FromQuery] double lat, [FromQuery] double lon, [FromQuery] double radius)
    {
        var result = await _mediator.Send(new GetNearestTollsQuery(lat, lon, radius));
        return Ok(result);
    }

    [HttpPost("import/state/{stateCode}")]
    public async Task<IActionResult> ImportState(string stateCode, CancellationToken ct)
    {
        await _importService.ImportTollsForStateAsync(stateCode, ct);
        return Ok($"Imported toll points for {stateCode}");
    }

    [HttpPost("add")]
    public async Task<IActionResult> AddToll(Guid roadId, AddTollCommand command, CancellationToken ct)
    {
        return Ok(await _mediator.Send(command, ct));
    }

    [HttpPost("import/all-states")]
    public async Task<IActionResult> ImportAllStates(CancellationToken ct)
    {
        await _importService.ImportTollsForAllStatesAsync(ct);
        return Ok("Imported toll points for all states");
    }

    [HttpPost("import/all-states-parallel")]
    public async Task<IActionResult> ImportAllStatesParallel(CancellationToken ct)
    {
        await _importService.ImportTollsForAllStatesAsyncParallel(ct);
        return Ok("Imported toll points for all states (parallel)");
    }

    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<TollDto>))]
    [HttpGet("by-bounding-box")]
    public async Task<IActionResult> GetTollsByBoundingBox(
    [FromQuery] double minLat,
    [FromQuery] double minLon,
    [FromQuery] double maxLat,
    [FromQuery] double maxLon,
    CancellationToken ct)
    => Ok(await _mediator.Send(new GetTollsByBoundingBoxQuery(minLat, minLon, maxLat, maxLon), ct));

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteToll(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteTollCommand(id), ct);
        if (!result)
        {
            return NotFound();
        }
        return Ok();
    }
}




