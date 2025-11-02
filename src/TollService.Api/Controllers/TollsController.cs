using MediatR;
using Microsoft.AspNetCore.Mvc;
using TollService.Application.Roads.Queries;
using TollService.Application.Tolls.Queries;
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

    [HttpPost("import/all-states")]
    public async Task<IActionResult> ImportAllStates(CancellationToken ct)
    {
        await _importService.ImportTollsForAllStatesAsync(ct);
        return Ok("Imported toll points for all states");
    }

    [HttpGet("by-bounding-box")]
    public async Task<IActionResult> GetTollsByBoundingBox(
    [FromQuery] double minLat,
    [FromQuery] double minLon,
    [FromQuery] double maxLat,
    [FromQuery] double maxLon,
    CancellationToken ct)
    => Ok(await _mediator.Send(new GetTollsByBoundingBoxQuery(minLat, minLon, maxLat, maxLon), ct));
}




