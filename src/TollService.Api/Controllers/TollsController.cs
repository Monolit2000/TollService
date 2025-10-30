using MediatR;
using Microsoft.AspNetCore.Mvc;
using TollService.Application.Tolls.Queries;

namespace TollService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TollsController : ControllerBase
{
    private readonly IMediator _mediator;
    public TollsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("nearest")]
    public async Task<IActionResult> GetNearest([FromQuery] double lat, [FromQuery] double lon, [FromQuery] double radius)
    {
        var result = await _mediator.Send(new GetNearestTollsQuery(lat, lon, radius));
        return Ok(result);
    }
}




