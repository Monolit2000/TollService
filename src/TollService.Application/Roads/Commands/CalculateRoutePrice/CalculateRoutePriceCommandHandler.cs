using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Application.Roads.Calculate;
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
            var roadCalculator = new RoadCalculator();

            var tollsDtos = request.Tolls;

            var dbTolls = await tollDbContext.Tolls
                .Where(t => tollsDtos.Select(tt => tt.Id).Contains(t.Id))
                .Include(t => t.TollPrices)
                .ToListAsync(cancellationToken);

            // Загружаем все CalculatePrices с включенными From и To для поиска по Name
            var allCalculatePrices = await tollDbContext.CalculatePrices
                .Include(p => p.From)
                .Include(p => p.To)
                .Include(p => p.TollPrices)
                .ToListAsync(cancellationToken);

            return roadCalculator.CalculateRoutePrices(tollsDtos, dbTolls, allCalculatePrices);
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

        public List<TollPrice> TollPrices { get; set; } = [];
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
