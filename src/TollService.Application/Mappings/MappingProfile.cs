using AutoMapper;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Road, RoadDto>();
        CreateMap<Toll, TollDto>()
            .ForCtorParam("Latitude", opt => opt.MapFrom(src => src.Location != null ? src.Location.Y : 0))
            .ForCtorParam("Longitude", opt => opt.MapFrom(src => src.Location != null ? src.Location.X : 0))
            .ForCtorParam("NodeId", opt => opt.MapFrom(src => src.NodeId ?? 0))
            .ForCtorParam("RoadId", opt => opt.MapFrom(src => src.RoadId ?? Guid.Empty))
            .ForCtorParam("WebsiteUrl", opt => opt.MapFrom(src => src.WebsiteUrl))
            .ForCtorParam("IPassOvernight", opt => opt.MapFrom(src => src.IPassOvernight))
            .ForCtorParam("IPass", opt => opt.MapFrom(src => src.IPass))
            .ForCtorParam("PayOnlineOvernight", opt => opt.MapFrom(src => src.PayOnlineOvernight))
            .ForCtorParam("PayOnline", opt => opt.MapFrom(src => src.PayOnline));
        
        CreateMap<TollPrice, TollWithPriceDto>()
            .ForMember(dest => dest.PaymentMethod, opt => opt.MapFrom(src => new PaymentMethodDto
            {
                Tag = src.PaymentMethod.Tag,
                NoPlate = src.PaymentMethod.NoPlate,
                Cash = src.PaymentMethod.Cash,
                NoCard = src.PaymentMethod.NoCard,
                App = src.PaymentMethod.App
            }));
    }
}




