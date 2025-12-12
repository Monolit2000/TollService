using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Tolls.Commands;

public record DeleteTollPriceCommand(Guid Id) : IRequest<bool>;

public class DeleteTollPriceCommandHandler(
    ITollDbContext _context) : IRequestHandler<DeleteTollPriceCommand, bool>
{
    public async Task<bool> Handle(DeleteTollPriceCommand request, CancellationToken ct)
    {
        var tollPrice = await _context.TollPrices
            .FirstOrDefaultAsync(tp => tp.Id == request.Id, ct);

        if (tollPrice == null)
            return false;

        _context.TollPrices.Remove(tollPrice);
        await _context.SaveChangesAsync(ct);

        return true;
    }
}



