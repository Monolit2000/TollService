using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries.CalculatePriceFromToToll
{
    public record CalculationResult(Guid TollFrom, Guid TollTo, double Price);

    public record CalculatePriceFromToTollQuerty(Guid TollFrom, Guid TollTo) : IRequest<CalculationResult>;

    public class CalculatePriceFromToTollQuertyHandler(
        ITollDbContext tollDbContext) : IRequestHandler<CalculatePriceFromToTollQuerty, CalculationResult>
    {
        public async Task<CalculationResult> Handle(CalculatePriceFromToTollQuerty request, CancellationToken cancellationToken)
        {
            //var price = await ;

            throw new NotImplementedException();
        }
    }
}
