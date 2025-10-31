using Microsoft.EntityFrameworkCore;
using TollService.Domain;

namespace TollService.Application.Common.Interfaces;

public interface ITollDbContext
{
    DbSet<Road> Roads { get; }
    DbSet<Toll> Tolls { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}


