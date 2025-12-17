using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Tolls.Queries;

public record GetTollsByBoundingBoxQuery(
    double MinLatitude, 
    double MinLongitude, 
    double MaxLatitude, 
    double MaxLongitude) : IRequest<List<TollDto>>;

public class GetTollsByBoundingBoxQueryHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<GetTollsByBoundingBoxQuery, List<TollDto>>
{
    public async Task<List<TollDto>> Handle(GetTollsByBoundingBoxQuery request, CancellationToken ct)
    {
        var boundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(request.MinLongitude, request.MinLatitude),
            new Coordinate(request.MaxLongitude, request.MinLatitude),
            new Coordinate(request.MaxLongitude, request.MaxLatitude),
            new Coordinate(request.MinLongitude, request.MaxLatitude),
            new Coordinate(request.MinLongitude, request.MinLatitude) 
        })) { SRID = 4326 };

        var tolls = await _context.Tolls
            .Where(t => t.Location != null && boundingBox.Contains(t.Location))
            .Include(t => t.TollPrices.Where(tp => !tp.CalculatePriceId.HasValue))
            .ToListAsync(ct);

        return MapToTollDtos(tolls);
    }

    public List<TollDto> MapToTollDtos(List<Toll> tolls)
    {
        return tolls.Select(t => new TollDto(
            id: t.Id,
            name: t.Name ?? string.Empty,
            nodeId: t.NodeId ?? 0,
            price: t.Price,
            latitude: t.Location?.Y ?? 0,    
            longitude: t.Location?.X ?? 0,   
            roadId: t.RoadId ?? Guid.Empty,
            key: t.Key,
            comment: t.Comment,
            isDynamic: t.isDynamic,
            iPassOvernight: t.IPassOvernight,
            iPass: t.IPass,
            payOnlineOvernight: t.PayOnlineOvernight,
            payOnline: t.PayOnline
        )
        {
            Tag = t.PaymentMethod.Tag,
            NoPlate = t.PaymentMethod.NoPlate,
            Cash = t.PaymentMethod.Cash,
            NoCard = t.PaymentMethod.NoCard,
            App = t.PaymentMethod.App,
            WebsiteUrl = t.WebsiteUrl,
            SerchRadiusInMeters = t.SerchRadiusInMeters,
            Number = t.Number ?? "N/A",
            TollPrices = t.TollPrices.Select(tp => new TollWithPriceDto
            {
                Id = tp.Id,
                TollId = tp.TollId,
                CalculatePriceId = tp.CalculatePriceId,
                PaymentType = tp.PaymentType,
                AxelType = tp.AxelType,
                TimeOfDay = tp.TimeOfDay,
                DayOfWeekFrom = tp.DayOfWeekFrom,
                DayOfWeekTo = tp.DayOfWeekTo,
                TimeFrom = tp.TimeFrom,
                TimeTo = tp.TimeTo,
                Description = tp.Description,
                Amount = tp.Amount
            }).ToList()
        }).ToList();
    }

}





