using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Domain;

namespace TollService.Infrastructure.Persistence;

public class TollDbContext : DbContext
{
    public TollDbContext(DbContextOptions<TollDbContext> options) : base(options) { }

    public DbSet<Road> Roads => Set<Road>();
    public DbSet<Toll> Tolls => Set<Toll>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TollDbContext).Assembly);
    }
}



