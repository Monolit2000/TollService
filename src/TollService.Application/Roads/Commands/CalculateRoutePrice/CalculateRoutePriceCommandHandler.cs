using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Dynamic;
using System.Linq;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Roads.Commands.CalculateRoutePrice
{

    public record CalculateRoutePriceCommand(List<TollWithRouteSectionDto> Tolls) : IRequest<RouteTollCost>;

    public class CalculateRoutePriceCommandHandler(
        ITollDbContext tollDbContext) : IRequestHandler<CalculateRoutePriceCommand, RouteTollCost>
    {
        public async Task<RouteTollCost> Handle(CalculateRoutePriceCommand request, CancellationToken cancellationToken)
        {
            List<TollInfo> tollInfos = [];
            List<TollPriceDto> tollPriceDtos = [];
            List<TollPriceFromToDto> tollPriceFromToDtos = [];

            var tollsDtos = request.Tolls.OrderBy(t => t.Distance).ToList();
            var dbTolls = await tollDbContext.Tolls.Where(t => tollsDtos.Select(tt => tt.Id).Contains(t.Id)).ToListAsync();

            foreach(var tollDto in tollsDtos)
            {
                var toll = dbTolls.First(t => t.Id == tollDto.Id);

                var tollInfo = new TollInfo(tollDto, toll);

                tollInfos.Add(tollInfo);
            }

            List<TollInfo> usedTolls = [];

            foreach (var tollInfo in tollInfos)
            {
                if (usedTolls.Contains(tollInfo))
                    continue;

                if (tollInfo.Toll.StateCalculatorId != null)
                {
                    var tollInfoTo = tollInfos.Where(
                        t => t.TollDto.Distance > tollInfo.TollDto.Distance &&
                        !usedTolls.Contains(t) &&
                        t.Toll.Id != tollInfo.Toll.Id &&
                        t.Toll.StateCalculatorId == tollInfo.Toll.StateCalculatorId).FirstOrDefault();

                    if (tollInfoTo == null)
                        continue;

                    usedTolls.Add(tollInfo);
                    usedTolls.Add(tollInfoTo);

                    var From = tollInfo.Toll;
                    var To = tollInfoTo.Toll;

                    var price = await tollDbContext.CalculatePrices.FirstOrDefaultAsync(
                        p => p.FromId == From.Id && p.ToId == To.Id);

                    if(price == null)
                        tollPriceFromToDtos.Add(new TollPriceFromToDto { From = From, To = To});

                    tollPriceFromToDtos.Add(new TollPriceFromToDto {From = From, To = To, Price = price.IPass});
                }

                var toll = tollInfo.Toll;
                usedTolls.Add(tollInfo);
                tollPriceDtos.Add(new TollPriceDto { Toll = toll, Price = toll.IPassOvernight });
            }

            var routeTollCost = new RouteTollCost { TollPriceDtos = tollPriceDtos, TollPriceFromToDtos = tollPriceFromToDtos };

            return routeTollCost;
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
        public double Price { get; set; }
    }

    public class TollPriceFromToDto
    {
        public Toll From { get; set; }
        public Toll To { get; set; }
        public double Price { get; set; } = 0;
    }
}
