using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TollService.Infrastructure.Persistence;

namespace TollService.Api.Extensions;

public static class MigrationExtensions
{
    public static void ApplyTollMigrations(this IApplicationBuilder app)
    {
        using IServiceScope scope = app.ApplicationServices.CreateScope();
        using TollDbContext dbContext = scope.ServiceProvider.GetRequiredService<TollDbContext>();
        dbContext.Database.Migrate();
    }
}


