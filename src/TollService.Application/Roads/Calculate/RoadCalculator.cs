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
                // Ищем самую дальнюю toll с тем же StateCalculatorId, которая еще не использована
                var tollInfoTo = tollInfos
                    .Where(t => t.TollDto.Distance > tollInfo.TollDto.Distance &&
                                t.Toll.StateCalculatorId == tollInfo.Toll.StateCalculatorId &&
                                t.Toll.Name != null &&
                                !usedNames.Contains(t.Toll.Name) &&
                                t.Toll.Name != tollInfo.Toll.Name)
                    .OrderByDescending(t => t.TollDto.Distance)
                    .FirstOrDefault();

                if (tollInfoTo == null)
                    continue;

                var fromName = tollInfo.Toll.Name;
                var toName = tollInfoTo.Toll.Name;

                // Ищем цену по Name вместо Id
                var price = allCalculatePrices.FirstOrDefault(p =>
                    p.From != null && p.From.Name != null &&
                    p.To != null && p.To.Name != null &&
                    p.From.Name == fromName &&
                    p.To.Name == toName &&
                    p.StateCalculatorId == tollInfo.Toll.StateCalculatorId);

                if (price == null)
                {
                    // Если цена не найдена, добавляем To и исключаем оба имени
                    tollPriceDtos.Add(new TollPriceDto { Toll = tollInfoTo.Toll });
                    if (fromName != null) usedNames.Add(fromName);
                    //if (toName != null) usedNames.Add(toName);
                    continue;
                }

                // Добавляем цену и исключаем оба имени
                tollPriceDtos.Add(new TollPriceDto
                {
                    Toll = tollInfoTo.Toll,
                    PayOnline = price.Cash,
                    IPass = price.IPass
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


