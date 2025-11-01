using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Mappings;
using TollService.Application.Common.Interfaces;
using TollService.Infrastructure.Integrations;
using TollService.Infrastructure.Persistence;
using TollService.Infrastructure.Services;
using TollService.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
    ?? "Host=db;Port=5432;Database=tolls;Username=postgres;Password=postgres";

builder.Services.AddDbContext<TollDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.UseNetTopologySuite();
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    }));
builder.Services.AddScoped<ITollDbContext>(sp => sp.GetRequiredService<TollDbContext>());

builder.Services.AddMediatR(typeof(MappingProfile).Assembly);
builder.Services.AddAutoMapper(typeof(MappingProfile).Assembly);
builder.Services.AddHttpClient<OsmClient>();
builder.Services.AddScoped<OsmRoadParserService>();
builder.Services.AddScoped<OsmTollParserService>();
builder.Services.AddScoped<OsmImportService>();
builder.Services.AddScoped<ISpatialQueryService, SpatialQueryService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", context =>
{
    context.Response.Redirect("/swagger/index.html", permanent: false);
    return Task.CompletedTask;
});

app.MapControllers();

// Apply EF Core migrations on startup
app.ApplyTollMigrations();

app.Run();
