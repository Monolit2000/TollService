using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Tolls.Commands;

public record CreateTollPriceCommand(
    Guid? TollId,
    Guid? CalculatePriceId,
    double Amount,
    TollPaymentType PaymentType,
    PaymentMethod? PaymentMethod = null,
    AxelType AxelType = AxelType._5L,
    TollPriceDayOfWeek DayOfWeekFrom = TollPriceDayOfWeek.Any,
    TollPriceDayOfWeek DayOfWeekTo = TollPriceDayOfWeek.Any,
    TollPriceTimeOfDay TimeOfDay = TollPriceTimeOfDay.Any,
    TimeOnly TimeFrom = default,
    TimeOnly TimeTo = default,
    string? Description = null) : IRequest<TollWithPriceDto?>;

public class CreateTollPriceCommandHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<CreateTollPriceCommand, TollWithPriceDto?>
{
    public async Task<TollWithPriceDto?> Handle(CreateTollPriceCommand request, CancellationToken ct)
    {
        // Валидация: должен быть указан либо TollId, либо CalculatePriceId
        if (!request.TollId.HasValue && !request.CalculatePriceId.HasValue)
        {
            return null;
        }

        // Проверяем существование Toll, если указан TollId
        if (request.TollId.HasValue)
        {
            var tollExists = await _context.Tolls.AnyAsync(t => t.Id == request.TollId.Value, ct);
            if (!tollExists)
            {
                return null;
            }
        }

        // Проверяем существование CalculatePrice, если указан CalculatePriceId
        if (request.CalculatePriceId.HasValue)
        {
            var calculatePriceExists = await _context.CalculatePrices
                .AnyAsync(cp => cp.Id == request.CalculatePriceId.Value, ct);
            if (!calculatePriceExists)
            {
                return null;
            }
        }

        var tollPrice = new TollPrice(
            tollId: request.TollId,
            calculatePriceId: request.CalculatePriceId,
            amount: request.Amount,
            paymentType: request.PaymentType,
            axelType: request.AxelType,
            dayOfWeekFrom: request.DayOfWeekFrom,
            dayOfWeekTo: request.DayOfWeekTo,
            timeOfDay: request.TimeOfDay,
            timeFrom: request.TimeFrom,
            timeTo: request.TimeTo,
            description: request.Description);

        _context.TollPrices.Add(tollPrice);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<TollWithPriceDto>(tollPrice);
    }
}

