using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TollService.Domain;

namespace TollService.Infrastructure.Persistence.Configurations;

public class CalculatePriceConfiguration : IEntityTypeConfiguration<CalculatePrice>
{
    public void Configure(EntityTypeBuilder<CalculatePrice> builder)
    {
        // Составной индекс для ускорения запросов по паре (FromId, ToId)
        builder.HasIndex(x => new { x.FromId, x.ToId });

        //builder.ToTable("CalculatePrices");
    }
}


