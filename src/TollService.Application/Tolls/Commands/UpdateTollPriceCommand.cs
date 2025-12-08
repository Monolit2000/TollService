using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Tolls.Commands;

public record UpdateTollPriceCommand(
    Guid Id,
    double? Amount = null,
    TollPaymentType? PaymentType = null,
    PaymentMethod? PaymentMethod = null,
    AxelType? AxelType = null,
    TollPriceTimeOfDay? TimeOfDay = null,
    TollPriceDayOfWeek? DayOfWeekFrom = null,
    TollPriceDayOfWeek? DayOfWeekTo = null,
    TimeOnly? TimeFrom = null,
    TimeOnly? TimeTo = null,
    string? Description = null) : IRequest<TollWithPriceDto?>;

public class UpdateTollPriceCommandHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<UpdateTollPriceCommand, TollWithPriceDto?>
{
    public async Task<TollWithPriceDto?> Handle(UpdateTollPriceCommand request, CancellationToken ct)
    {
        var tollPrice = await _context.TollPrices
            .FirstOrDefaultAsync(tp => tp.Id == request.Id, ct);

        if (tollPrice == null)
            return null;

        if (request.Amount.HasValue)
            tollPrice.Amount = request.Amount.Value;

        if (request.PaymentType.HasValue)
            tollPrice.PaymentType = request.PaymentType.Value;

        if (request.PaymentMethod != null)
        {
            tollPrice.PaymentMethod.Tag = request.PaymentMethod.Tag;
            tollPrice.PaymentMethod.NoPlate = request.PaymentMethod.NoPlate;
            tollPrice.PaymentMethod.Cash = request.PaymentMethod.Cash;
            tollPrice.PaymentMethod.NoCard = request.PaymentMethod.NoCard;
            tollPrice.PaymentMethod.App = request.PaymentMethod.App;
        }

        if (request.AxelType.HasValue)
            tollPrice.AxelType = request.AxelType.Value;

        if (request.TimeOfDay.HasValue)
            tollPrice.TimeOfDay = request.TimeOfDay.Value;

        if (request.DayOfWeekFrom.HasValue)
            tollPrice.DayOfWeekFrom = request.DayOfWeekFrom.Value;

        if (request.DayOfWeekTo.HasValue)
            tollPrice.DayOfWeekTo = request.DayOfWeekTo.Value;

        if (request.TimeFrom.HasValue)
            tollPrice.TimeFrom = request.TimeFrom.Value;

        if (request.TimeTo.HasValue)
            tollPrice.TimeTo = request.TimeTo.Value;

        if (request.Description != null)
            tollPrice.Description = request.Description;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<TollWithPriceDto>(tollPrice);
    }
}

