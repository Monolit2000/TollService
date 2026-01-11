using MediatR;
using Microsoft.AspNetCore.Mvc;
using TollService.Application.WeighStations.Commands;
using TollService.Application.WeighStations.Queries;
using TollService.Contracts;

namespace TollService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeighStationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public WeighStationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("add")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AddWeighStationsResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddWeighStations(
        [FromBody] List<WeighStationRequestDto> request,
        CancellationToken ct = default)
    {
        if (request == null || request.Count == 0)
        {
            return BadRequest("Request body cannot be empty");
        }

        try
        {
            var command = new AddWeighStationsCommand(request);
            var result = await _mediator.Send(command, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error processing request: {ex.Message}");
        }
    }

    [HttpPost("along-polyline")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<WeighStationsBySectionDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWeighStationsAlongPolyline(
        [FromBody] List<WeighStationSectionRequestDto> request,
        CancellationToken ct = default)
    {
        if (request == null || request.Count == 0)
        {
            return BadRequest("Request body cannot be empty");
        }

        foreach (var section in request)
        {
            if (section.Coordinates == null || section.Coordinates.Count < 2)
            {
                return BadRequest($"Section '{section.SectionId}' must contain at least 2 coordinates");
            }

            if (section.Coordinates.Any(c => c == null || c.Count < 2))
            {
                return BadRequest($"Section '{section.SectionId}': Each coordinate must contain at least 2 values [longitude, latitude]");
            }
        }

        var result = await _mediator.Send(
            new GetWeighStationsAlongPolylineQuery(request),
            ct);
        return Ok(result);
    }
}


