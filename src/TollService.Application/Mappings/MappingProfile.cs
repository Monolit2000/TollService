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
            .ForCtorParam("Longitude", opt => opt.MapFrom(src => src.Location != null ? src.Location.X : 0));
    }
}




