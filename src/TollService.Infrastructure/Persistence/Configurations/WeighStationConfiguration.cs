using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TollService.Domain.WeighStations;

namespace TollService.Infrastructure.Persistence.Configurations;

public class WeighStationConfiguration : IEntityTypeConfiguration<WeighStation>
{
    public void Configure(EntityTypeBuilder<WeighStation> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(256);
        builder.Property(x => x.Address).HasMaxLength(512);
        builder.Property(x => x.Web).HasMaxLength(512);
        builder.Property(x => x.Location).HasColumnType("geometry(Point,4326)");
        builder.HasIndex(x => x.Location).HasMethod("GIST");
    }
}

