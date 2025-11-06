using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Commands;

public record DeleteRoadCommand(Guid Id) : IRequest<bool>;

public class DeleteRoadCommandHandler(
    ITollDbContext _context) : IRequestHandler<DeleteRoadCommand, bool>
{
    public async Task<bool> Handle(DeleteRoadCommand request, CancellationToken ct)
    {
        var road = await _context.Roads.FirstOrDefaultAsync(r => r.Id == request.Id, ct);
        
        if (road == null)
            return false;

        _context.Roads.Remove(road);
        await _context.SaveChangesAsync(ct);

        return true;
    }
}

