using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TollService.Domain;

namespace TollService.Infrastructure.Persistence.Configurations;

public class RoadConfiguration : IEntityTypeConfiguration<Road>
{
    public void Configure(EntityTypeBuilder<Road> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(256);
        builder.Property(x => x.HighwayType).HasMaxLength(64);
        builder.Property(x => x.State).IsRequired(false);
        builder.Property(x => x.Ref).HasMaxLength(64).IsRequired(false);
        builder.Property(x => x.Geometry).HasColumnType("geometry(LineString,4326)");
        builder.HasIndex(x => x.Geometry).HasMethod("GIST");
        builder.HasMany(x => x.Tolls).WithOne(t => t.Road).HasForeignKey(t => t.RoadId);
    }
}



