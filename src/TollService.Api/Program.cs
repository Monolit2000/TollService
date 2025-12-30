using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Api.Extensions;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Application.Mappings;
using TollService.Infrastructure.Integrations;
using TollService.Infrastructure.Persistence;
using TollService.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
    ?? "Host=db;Port=5436;Database=tolls;Username=postgres;Password=test";

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
builder.Services.AddScoped<RoadRefService>();

// Toll Price Parser Services
builder.Services.AddScoped<TollSearchService>();
builder.Services.AddScoped<ITollSearchRadiusService, TollSearchRadiusService>();
builder.Services.AddScoped<StateCalculatorService>();
builder.Services.AddScoped<TollNumberService>();
builder.Services.AddScoped<TollMatchingService>();
builder.Services.AddScoped<CalculatePriceService>();

// Configure CORS to allow all
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000",
            "https://4uscorp-web-git-feature-drag-white-rainy-76s-projects.vercel.app",
            "https://4uscorp-web-git-develop-white-rainy-76s-projects.vercel.app",
            "https://4uscorp-web.vercel.app",
            "4uscorp-web.vercel.app",
            "www.4uscorp-web.vercel.app",
            "https://www.4uscorp-web.vercel.app",
            "https://4uscorp-web.vercel.app/")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors();

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
