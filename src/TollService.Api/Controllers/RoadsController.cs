using MediatR;
using Microsoft.AspNetCore.Mvc;
using TollService.Application.Roads.Commands;
using TollService.Application.Roads.Queries;
using TollService.Contracts;
using TollService.Infrastructure.Integrations;
using TollService.Infrastructure.Services;

namespace TollService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoadsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly OsmImportService _importService;
    private readonly RoadRefService _roadRefService;

    public RoadsController(IMediator mediator, OsmImportService importService, RoadRefService roadRefService)
    {
        _mediator = mediator;
        _importService = importService;
        _roadRefService = roadRefService;
    }

    [HttpPost("addRoad")]
    public async Task<IActionResult> AddRoad(AddRoadCommand command, CancellationToken ct) => Ok(await _mediator.Send(command, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRoad(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new GetRoadByIdQuery(id), ct));

    [HttpPut("{id}/update")]
    public async Task<IActionResult> EditRoad(Guid id, UpdateRoadCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        if (result == null)
        {
            return NotFound($"Road with id {id} not found");
        }
        return Ok(result);
    }

    //[HttpPost("{roadId}/tolls")]
    //public async Task<IActionResult> AddToll(Guid roadId, AddTollCommand command, CancellationToken ct)
    //{
    //    return Ok(await _mediator.Send(command, ct));
    //}

    [HttpPost("addToll")]
    public async Task<IActionResult> AddToll(Guid roadId, AddTollCommand command, CancellationToken ct)
    {
        return Ok(await _mediator.Send(command, ct));
    }

    [HttpDelete("tolls/{id}")]
    public async Task<IActionResult> DeleteToll(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteTollCommand(id), ct);
        if (!result)
        {
            return NotFound($"Toll with id {id} not found");
        }
        return NoContent();
    }

    [HttpGet("{roadId}/tolls")]
    public async Task<IActionResult> GetTolls(Guid roadId, CancellationToken ct) 
        => Ok(await _mediator.Send(new GetTollsByRoadQuery(roadId), ct));

    [HttpPost("import/state/{stateCode}")]
    public async Task<IActionResult> ImportState(string stateCode, CancellationToken ct)
    {
        await _importService.ImportStateAsync(stateCode, ct);
        return Ok($"Imported toll roads for {stateCode}");
    }

    [HttpPost("import/all-states")]
    public async Task<IActionResult> ImportAllStates(CancellationToken ct)
    {
        await _importService.ImportAllStatesAsync(ct);
        return Ok("Imported toll roads for all states");
    }

    [HttpPost("import/all-states-parallel")]
    public async Task<IActionResult> ImportAllStatesParallel(CancellationToken ct)
    {
        await _importService.ImportAllStatesAsyncParallel(ct);
        return Ok("Imported toll roads for all states (parallel)");
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

    [HttpGet("stats/by-state/{state}")]
    public async Task<IActionResult> GetTotalRoadDistanceByState(string state, CancellationToken ct)
    {
        var totalLength = await _mediator.Send(new GetTotalRoadDistanceByStateQuery(state), ct);
        return Ok(new { State = state, TotalLengthKm = totalLength });
    }

    [HttpGet("names/by-state/{state}")]
    public async Task<IActionResult> GetRoadNamesByState(string state, CancellationToken ct)
        => Ok(await _mediator.Send(new GetRoadNamesByStateQuery(state), ct));

    [HttpPost("fill-missing-refs")]
    public async Task<IActionResult> FillMissingRefs(CancellationToken ct)
    {
        var updatedCount = await _roadRefService.FillMissingRefsAsync(ct);
        return Ok(new { UpdatedCount = updatedCount, Message = $"Updated {updatedCount} roads with missing Ref values" });
    }

    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RoadWithGeometryDto>))]
    [HttpGet("by-bounding-box")]
    public async Task<IActionResult> GetRoadsByBoundingBox(
        [FromQuery] double minLat, 
        [FromQuery] double minLon, 
        [FromQuery] double maxLat, 
        [FromQuery] double maxLon, 
        CancellationToken ct)
        => Ok(await _mediator.Send(new GetRoadsByBoundingBoxQuery(minLat, minLon, maxLat, maxLon), ct));



    [HttpPost("asdasdsa-asdasd-asdasdasdsadasd")]
    public async Task<IActionResult> asdasdasdsaddasdasdasd(CancellationToken ct)
    {
        var updatedCount = await _roadRefService.FillMissingRefsAsync(ct);
        return Ok(new { UpdatedCount = updatedCount, Message = $"Updated {updatedCount} roads with missing Ref values" });
    }


    [HttpPost("NEW-NEW-asdasdasdsadasdNEWNEWNEWNEWNEWNEW")]
    public async Task<IActionResult> asdasdsda(CancellationToken ct)
    {
        var updatedCount = await _roadRefService.FillMissingRefsAsync(ct);
        return Ok(new { UpdatedCount = updatedCount, Message = $"Updated {updatedCount} roads with missing Ref values" });
    }
}


