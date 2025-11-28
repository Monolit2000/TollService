using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TollService.Domain;

namespace TollService.Infrastructure.Persistence.Configurations;

public class TollPriceConfiguration : IEntityTypeConfiguration<TollPrice>
{
    public void Configure(EntityTypeBuilder<TollPrice> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount)
            .HasColumnType("double precision");

        builder.Property(x => x.PaymentType)
            .HasConversion<int>();

        builder.Property(x => x.TimeOfDay)
            .HasConversion<int>();

        builder.Property(x => x.Description)
            .HasMaxLength(512)
            .IsRequired(false);

        builder.HasOne(x => x.Toll)
            .WithMany(t => t.TollPrices)
            .HasForeignKey(x => x.TollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("TollPrices");
    }
}


