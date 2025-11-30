using TollService.Application.Roads.Commands.CalculateRoutePrice;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Roads.Calculate;

/// <summary>
/// Base calculator for route toll prices.
/// Can be inherited and extended for custom behaviour.
/// </summary>
public class RoadCalculator
{
    public virtual List<TollPriceDto> CalculateRoutePrices(
        IReadOnlyCollection<TollWithRouteSectionDto> tollsDtos,
        IReadOnlyCollection<Toll> dbTolls,
        IReadOnlyCollection<CalculatePrice> allCalculatePrices)
    {
        List<TollInfo> tollInfos = [];
        List<TollPriceDto> tollPriceDtos = [];

        var orderedTollsDtos = tollsDtos
            .OrderBy(t => t.Distance)
            .ToList();

        foreach (var tollDto in orderedTollsDtos)
        {
            var toll = dbTolls.First(t => t.Id == tollDto.Id);
            var tollInfo = new TollInfo(tollDto, toll);
            tollInfos.Add(tollInfo);
        }

        // Множество использованных имен для исключения
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        double lastDistance = 0;

        foreach (var tollInfo in tollInfos)
        {
            // Пропускаем, если имя уже использовано
            if (tollInfo.Toll.Name != null && usedNames.Contains(tollInfo.Toll.Name))
                continue;

            if (tollInfo.TollDto.Distance < lastDistance)
                continue;

            if (tollInfo.Toll.StateCalculatorId != null)
            {
                var fromName = tollInfo.Toll.Name;
                var stateCalculatorId = tollInfo.Toll.StateCalculatorId;

                // Ищем самую дальнюю toll с тем же StateCalculatorId,
                // для которой существует price (price != null)
                var tollInfoToWithPrice = tollInfos
                    .Where(t => t.TollDto.Distance > tollInfo.TollDto.Distance &&
                                t.Toll.StateCalculatorId == stateCalculatorId &&
                                t.Toll.Name != null &&
                                !usedNames.Contains(t.Toll.Name) &&
                                t.Toll.Name != tollInfo.Toll.Name)
                    .Select(t => new
                    {
                        TollInfoTo = t,
                        Price = allCalculatePrices.FirstOrDefault(p =>
                            p.From != null && p.From.Name != null &&
                            p.To != null && p.To.Name != null &&
                            p.From.Name == fromName &&
                            p.To.Name == t.Toll.Name &&
                            p.StateCalculatorId == stateCalculatorId)
                    })
                    .Where(x => x.Price != null)
                    .OrderByDescending(x => x.TollInfoTo.TollDto.Distance)
                    .FirstOrDefault();

                // Если ни для одного To нет price, ведём себя как раньше:
                // просто выбираем самую дальнюю toll и добавляем её без цены
                if (tollInfoToWithPrice == null)
                {
                    var tollInfoToFallback = tollInfos
                        .Where(t => t.TollDto.Distance > tollInfo.TollDto.Distance &&
                                    t.Toll.StateCalculatorId == stateCalculatorId &&
                                    t.Toll.Name != null &&
                                    !usedNames.Contains(t.Toll.Name) &&
                                    t.Toll.Name != tollInfo.Toll.Name)
                        .OrderByDescending(t => t.TollDto.Distance)
                        .FirstOrDefault();

                    if (tollInfoToFallback == null)
                        continue;

                    var toNameFallback = tollInfoToFallback.Toll.Name;

                    // Если цена не найдена, добавляем To и исключаем fromName
                    tollPriceDtos.Add(new TollPriceDto { Toll = tollInfoToFallback.Toll });
                    if (fromName != null) usedNames.Add(fromName);
                    //if (toNameFallback != null) usedNames.Add(toNameFallback);
                    continue;
                }

                var tollInfoTo = tollInfoToWithPrice.TollInfoTo;
                var price = tollInfoToWithPrice.Price!;
                var toName = tollInfoTo.Toll.Name;

                // Добавляем цену и исключаем оба имени
                tollPriceDtos.Add(new TollPriceDto
                {
                    Toll = tollInfoTo.Toll,
                    PayOnline = price.GetAmmountByPaymentType(TollPaymentType.Cash) == 0.0 ? price.Cash : price.GetAmmountByPaymentType(TollPaymentType.Cash),
                    IPass = price.GetAmmountByPaymentType(TollPaymentType.EZPass) == 0.0 ? price.IPass : price.GetAmmountByPaymentType(TollPaymentType.EZPass)
                });

                // Исключаем все tolls с такими же именами
                if (fromName != null) usedNames.Add(fromName);
                if (toName != null) usedNames.Add(toName);

                lastDistance = tollInfoTo.TollDto.Distance;
            }
            else
            {
                // Для tolls без StateCalculatorId используем прямые цены
                var toll = tollInfo.Toll;
                tollPriceDtos.Add(new TollPriceDto
                {
                    Toll = toll,
                    IPassOvernight = toll.IPassOvernight,
                    IPass = toll.IPass,
                    PayOnlineOvernight = toll.PayOnlineOvernight,
                    PayOnline = toll.PayOnline,
                });

                // Исключаем имя из дальнейшей обработки
                //if (toll.Name != null)
                //    usedNames.Add(toll.Name);
            }
        }

        return tollPriceDtos;
    }
}


