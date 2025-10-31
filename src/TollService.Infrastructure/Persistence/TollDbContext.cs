using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Infrastructure.Persistence;

public class TollDbContext : DbContext, ITollDbContext
{
    public TollDbContext(DbContextOptions<TollDbContext> options) : base(options) { }

    public DbSet<Road> Roads { get; set; }
    public DbSet<Toll> Tolls { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TollDbContext).Assembly);
    }
}



