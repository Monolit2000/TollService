using Microsoft.EntityFrameworkCore;
using TollService.Domain;
using TollService.Domain.WeighStations;

namespace TollService.Application.Common.Interfaces;

public interface ITollDbContext
{
    DbSet<Road> Roads { get; }
    DbSet<Toll> Tolls { get; }
    DbSet<StateCalculator> StateCalculators { get; set; }

    public DbSet<CalculatePrice> CalculatePrices { get; set; }
    public DbSet<TollPrice> TollPrices { get; set; }
    public DbSet<WeighStation> WeighStations { get; set; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}


