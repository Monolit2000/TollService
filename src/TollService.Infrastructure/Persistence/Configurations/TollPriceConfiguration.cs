using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TollService.Domain;

namespace TollService.Infrastructure.Persistence.Configurations;

public class TollPriceConfiguration : IEntityTypeConfiguration<TollPrice>
{
    public void Configure(EntityTypeBuilder<TollPrice> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Amount)
            .HasColumnType("double precision");

        builder.Property(x => x.PaymentType)
            .HasConversion<int>();

        // Настройка PaymentMethod как owned entity с отдельными колонками
        builder.OwnsOne(x => x.PaymentMethod, pm =>
        {
            pm.Property(p => p.Tag)
                .HasColumnName("PaymentMethod_Tag")
                .HasDefaultValue(false);
            pm.Property(p => p.NoPlate)
                .HasColumnName("PaymentMethod_NoPlate")
                .HasDefaultValue(false);
            pm.Property(p => p.Cash)
                .HasColumnName("PaymentMethod_Cash")
                .HasDefaultValue(false);
            pm.Property(p => p.NoCard)
                .HasColumnName("PaymentMethod_NoCard")
                .HasDefaultValue(false);
            pm.Property(p => p.App)
                .HasColumnName("PaymentMethod_App")
                .HasDefaultValue(false);
        });

        builder.Property(x => x.TimeOfDay)
            .HasConversion<int>();

        builder.Property(x => x.DayOfWeekFrom)
            .HasConversion<int>();

        builder.Property(x => x.DayOfWeekTo)
            .HasConversion<int>();

        builder.Property(x => x.Description)
            .HasMaxLength(512)
            .IsRequired(false);

        builder.Property(x => x.TollId)
            .IsRequired(false);

        //builder.Property(x => x.IsCalculate)
        //    .HasDefaultValue(false);

        builder.HasOne(x => x.Toll)
            .WithMany(t => t.TollPrices)
            .HasForeignKey(x => x.TollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("TollPrices");
    }
}



