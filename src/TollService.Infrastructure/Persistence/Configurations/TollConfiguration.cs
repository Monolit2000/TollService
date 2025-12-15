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
        builder.Property(x => x.SerchRadiusInMeters).HasDefaultValue(0);
        builder.Property(x => x.NodeId).IsRequired(false);
        builder.HasIndex(x => x.Location).HasMethod("GIST");
        builder.Property(x => x.Key).IsRequired(false);
        builder.Property(x => x.Comment).IsRequired(false);
        builder.Property(x => x.WebsiteUrl).HasMaxLength(512).IsRequired(false);

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

        builder.Property(x => x.IPassOvernight);
        builder.Property(x => x.IPass);
        builder.Property(x => x.PayOnlineOvernight);
        builder.Property(x => x.PayOnline);
    }
}



