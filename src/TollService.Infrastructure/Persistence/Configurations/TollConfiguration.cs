using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TollService.Domain;

namespace TollService.Infrastructure.Persistence.Configurations;

public class TollConfiguration : IEntityTypeConfiguration<Toll>
{
    public void Configure(EntityTypeBuilder<Toll> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(256);
        builder.Property(x => x.Price).HasPrecision(10, 2);
        builder.Property(x => x.Location).HasColumnType("geometry(Point,4326)");
        builder.HasIndex(x => x.Location).HasMethod("GIST");
    }
}



