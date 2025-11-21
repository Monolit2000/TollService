using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Roads.Commands.CalculateRoutePrice
{

    public record CalculateRoutePriceCommand(List<TollWithRouteSectionDto> Tolls) : IRequest<List<TollPriceDto>>;

    public class CalculateRoutePriceCommandHandler(
        ITollDbContext tollDbContext) : IRequestHandler<CalculateRoutePriceCommand, List<TollPriceDto>>
    {
        public async Task<List<TollPriceDto>> Handle(CalculateRoutePriceCommand request, CancellationToken cancellationToken)
        {
            List<TollInfo> tollInfos = [];
            List<TollPriceDto> tollPriceDtos = [];

            var tollsDtos = request.Tolls.OrderBy(t => t.Distance).ToList();
            var dbTolls = await tollDbContext.Tolls
                .Where(t => tollsDtos.Select(tt => tt.Id).Contains(t.Id))
                .ToListAsync(cancellationToken);

            foreach(var tollDto in tollsDtos)
            {
                var toll = dbTolls.First(t => t.Id == tollDto.Id);
                var tollInfo = new TollInfo(tollDto, toll);
                tollInfos.Add(tollInfo);
            }

            // Загружаем все CalculatePrices с включенными From и To для поиска по Name
            var allCalculatePrices = await tollDbContext.CalculatePrices
                .Include(p => p.From)
                .Include(p => p.To)
                .ToListAsync(cancellationToken);

            // Множество использованных имен для исключения
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tollInfo in tollInfos)
            {
                // Пропускаем, если имя уже использовано
                if (tollInfo.Toll.Name != null && usedNames.Contains(tollInfo.Toll.Name))
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
                        //usedNames.Add(toll.Name);
                }
            }

            return tollPriceDtos;
        }
    }

    public class TollInfo
    {
        public TollWithRouteSectionDto TollDto { get; set; }
        public Toll Toll { get; set; }

        public TollInfo(TollWithRouteSectionDto tollWithRouteSectionDto, Toll toll)
        {
            TollDto = tollWithRouteSectionDto;
            Toll = toll;
        }
    }

    public class RouteTollCost
    {
        public List<TollPriceDto> TollPriceDtos { get; set; } = [];

        public List<TollPriceFromToDto> TollPriceFromToDtos { get; set; } = [];
    }

    public class TollPriceDto
    {
        public Toll Toll { get; set; }
        public double IPassOvernight { get; set; }

        public double IPass { get; set; }

        public double PayOnlineOvernight { get; set; }

        public double PayOnline { get; set; }
    }

    public class TollPriceFromToDto
    {
        public Toll From { get; set; }
        public Toll To { get; set; }
        public double IPrice { get; set; } = 0;
        public double Cash { get; set; } = 0;

    }
}
