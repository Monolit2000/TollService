using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Commands;

public record DeleteTollCommand(Guid Id) : IRequest<bool>;

public class DeleteTollCommandHandler(
    ITollDbContext _context) : IRequestHandler<DeleteTollCommand, bool>
{
    public async Task<bool> Handle(DeleteTollCommand request, CancellationToken ct)
    {
        var toll = await _context.Tolls.FirstOrDefaultAsync(t => t.Id == request.Id, ct);
        
        if (toll == null)
            return false;

        _context.Tolls.Remove(toll);
        await _context.SaveChangesAsync(ct);

        return true;
    }
}

