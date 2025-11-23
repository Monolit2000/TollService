using MediatR;
using Microsoft.AspNetCore.Mvc;
using TollService.Application.Roads.Queries;
using TollService.Application.TollPriceParser;
using TollService.Application.TollPriceParser.PA;
using TollService.Application.Tolls.Commands;
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

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TollDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateToll(Guid id, UpdateTollCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        if (result == null)
        {
            return NotFound($"Toll with id {id} not found");
        }
        return Ok(result);
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

    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<TollDto>))]
    [HttpPost("along-polyline")]
    public async Task<IActionResult> GetTollsAlongPolyline(
        [FromBody] PolylineRequestDto request,
        CancellationToken ct)
    {
        if (request.Coordinates == null || request.Coordinates.Count < 2)
        {
            return BadRequest("Polyline must contain at least 2 coordinates");
        }

        if (request.Coordinates.Any(c => c == null || c.Count < 2))
        {
            return BadRequest("Each coordinate must contain at least 2 values [longitude, latitude]");
        }

        var distanceMeters = request.DistanceMeters ?? 1;
        var result = await _mediator.Send(
            new GetTollsAlongPolylineQuery(request.Coordinates, distanceMeters), 
            ct);
        return Ok(result);
    }

    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<TollWithRouteSectionDto>))]
    [HttpPost("along-polyline-sections")]
    public async Task<IActionResult> GetTollsAlongPolylineSections(
        [FromBody] List<PolylineSectionRequestDto> sections,
        CancellationToken ct)
    {
        if (sections == null || sections.Count == 0)
        {
            return BadRequest("Sections list cannot be empty");
        }

        foreach (var section in sections)
        {
            if (section.Coordinates == null || section.Coordinates.Count < 2)
            {
                return BadRequest("Each section must contain at least 2 coordinates");
            }

            if (section.Coordinates.Any(c => c == null || c.Count < 2))
            {
                return BadRequest("Each coordinate must contain at least 2 values [longitude, latitude]");
            }
        }

        var result = await _mediator.Send(
            new GetTollsAlongPolylineSectionsQuery(sections), 
            ct);
        return Ok(result);
    }

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

    [HttpPost("parse-prices")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ParseTollPricesResult))]
    public async Task<IActionResult> ParseTollPrices(
        [FromQuery] string? url = null,
        CancellationToken ct = default)
    {
        var command = new ParseTollPricesCommand(url ?? "https://agency.illinoistollway.com/toll-rates");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("parse-plaza-names")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ParsePlazaNamesResult))]
    public async Task<IActionResult> ParsePlazaNames(
        [FromQuery] string? arcGisUrl = null,
        CancellationToken ct = default)
    {
        var command = new ParsePlazaNamesFromArcGisCommand(
            arcGisUrl ?? "https://gis.illinoisvirtualtollway.com/arcgis/rest/services/IVT/IllinoisVirtualTollway/MapServer/13/query");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("parse-exit-points-to-plazas")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ParseExitPointsToPlazasResult))]
    public async Task<IActionResult> ParseExitPointsToPlazas(
        [FromQuery] string? queryUrl = null,
        [FromQuery] string? identifyUrl = null,
        CancellationToken ct = default)
    {
        var command = new ParseExitPointsToPlazasCommand(
            queryUrl ?? "https://gis.illinoisvirtualtollway.com/arcgis/rest/services/IVT/IllinoisVirtualTollway/MapServer/18/query",
            identifyUrl ?? "https://gis.illinoisvirtualtollway.com/arcgis/rest/services/IVT/IllinoisVirtualTollway/MapServer/identify");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("parse-indiana-prices")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ParseTollPricesResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ParseIndianaTollPrices(
        [FromBody] List<IndianaTollPriceRequestDto> request,
        CancellationToken ct = default)
    {
        if (request == null || request.Count == 0)
        {
            return BadRequest("Request body cannot be empty");
        }

        try
        {
            // Сериализуем в JSON строку для команды
            var jsonContent = System.Text.Json.JsonSerializer.Serialize(request);
            var command = new ParseIndianaTollPricesCommand(jsonContent);
            var result = await _mediator.Send(command, ct);
            return Ok(result);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return BadRequest($"Invalid JSON format: {ex.Message}");
        }
    }

    [HttpPost("parse-pa-turnpike-interchanges")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(int))]
    public async Task<IActionResult> ParsePaTurnpikeInterchanges(
        [FromQuery] string? url = null,
        CancellationToken ct = default)
    {
        var command = new ParsePaTurnpikeInterchangesCommand(
            url ?? "https://www.paturnpike.com/toll-calculator");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("parse-pa-turnpike-prices")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ParseTollPricesResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ParsePaTurnpikePrices(
        [FromQuery] string? tollType = null,
        [FromQuery] string? roadwayKey = null,
        [FromQuery] string? entryInterchangeKey = null,
        [FromQuery] string? effectiveDateKey = null,
        [FromQuery] string? baseUrl = null,
        CancellationToken ct = default)
    {
        var command = new ParsePaTurnpikePricesCommand(
            TollType: tollType,
            RoadwayKey: roadwayKey,
            EntryInterchangeKey: entryInterchangeKey,
            EffectiveDateKey: effectiveDateKey,
            BaseUrl: baseUrl ?? "https://www.paturnpike.com/toll-schedule-v2/get-toll-schedule");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}




