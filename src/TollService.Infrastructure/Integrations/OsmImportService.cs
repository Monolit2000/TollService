using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TollService.Infrastructure.Persistence;

namespace TollService.Infrastructure.Integrations;

public class OsmImportService
{
    private readonly OsmClient _osmClient;
    private readonly TollDbContext _context;
    private readonly OsmRoadParserService _parserService;
    private readonly OsmTollParserService _tollParserService;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly Dictionary<string, (double south, double west, double north, double east)> StateBounds = new()
    {
        { "TX", (25.8, -106.6, 36.5, -93.5) },      // Texas
        { "CA", (32.5, -124.5, 42.0, -114.0) },     // California
        { "FL", (24.5, -87.6, 31.0, -80.0) },      // Florida
        { "NY", (40.5, -79.8, 45.0, -71.8) },      // New York
        { "NJ", (38.9, -75.6, 41.4, -73.9) },      // New Jersey
        { "PA", (39.7, -80.5, 42.3, -74.7) },      // Pennsylvania
        { "IL", (36.9, -91.5, 42.5, -87.0) },      // Illinois
        { "MD", (37.9, -79.5, 39.7, -75.0) },      // Maryland
        { "VA", (36.5, -83.7, 39.5, -75.2) },      // Virginia
        { "NC", (33.8, -84.3, 36.6, -75.4) },      // North Carolina
        { "GA", (30.4, -85.6, 35.0, -80.8) },      // Georgia
        { "OH", (38.4, -84.8, 42.0, -80.5) },      // Ohio
        { "MI", (41.7, -90.4, 48.3, -82.1) },       // Michigan
        { "MA", (41.2, -73.5, 42.9, -69.9) },      // Massachusetts
        { "CT", (40.9, -73.7, 42.0, -71.8) },      // Connecticut
        { "DE", (38.4, -75.8, 39.7, -75.0) },      // Delaware
        { "IN", (37.8, -88.1, 41.8, -84.8) },      // Indiana
        { "TN", (34.9, -90.3, 37.0, -81.7) },      // Tennessee
        { "SC", (32.0, -83.4, 35.2, -78.5) },      // South Carolina
        { "AL", (30.1, -88.5, 35.0, -84.9) },      // Alabama
        { "MS", (30.1, -91.7, 35.0, -88.1) },      // Mississippi
        { "LA", (28.9, -94.0, 33.0, -88.8) },      // Louisiana
        { "AR", (33.0, -94.6, 36.5, -89.7) },      // Arkansas
        { "OK", (33.6, -103.0, 37.0, -94.4) },     // Oklahoma
        { "KS", (36.9, -102.0, 40.0, -94.6) },     // Kansas
        { "MO", (35.9, -95.8, 40.6, -89.1) },      // Missouri
        { "IA", (40.4, -96.6, 43.5, -90.1) },      // Iowa
        { "MN", (43.5, -97.2, 49.4, -89.5) },      // Minnesota
        { "WI", (42.4, -92.9, 47.1, -86.8) },       // Wisconsin
        { "KY", (36.4, -89.6, 39.1, -81.9) },      // Kentucky
        { "WV", (37.2, -82.7, 40.6, -77.7) },      // West Virginia
        { "WA", (45.5, -124.8, 49.0, -116.9) },    // Washington
        { "OR", (41.9, -124.7, 46.3, -116.5) },    // Oregon
        { "NV", (35.0, -120.0, 42.0, -114.0) },    // Nevada
        { "UT", (36.9, -114.0, 42.0, -109.0) },    // Utah
        { "CO", (36.9, -109.0, 41.0, -102.0) },    // Colorado
        { "AZ", (31.3, -114.8, 37.0, -109.0) },    // Arizona
        { "NM", (31.3, -109.0, 37.0, -103.0) },    // New Mexico
        { "ME", (42.9, -71.5, 47.5, -66.9) },      // Maine

        { "AK", (51.2, -179.1, 71.4, -129.9) },    // Alaska (растянут по широте)
        { "HI", (18.9, -160.3, 22.4, -154.8) },  // Hawaii

        { "ND", (45.9, -104.1, 49.0, -96.6) },   // North Dakota
        { "SD", (42.5, -104.1, 45.9, -96.4) },   // South Dakota
        { "NE", (39.9, -104.1, 43.1, -95.3) },   // Nebraska
        { "MT", (44.4, -116.1, 49.0, -104.0) },  // Montana
        { "WY", (40.9, -111.1, 45.0, -104.0) },  // Wyoming
        { "ID", (41.9, -117.3, 49.0, -111.0) },  // Idaho

        { "RI", (41.1, -71.9, 42.0, -71.1) },    // Rhode Island
        { "VT", (42.7, -73.4, 45.0, -71.5) }     // Vermont
    };

    public OsmImportService(
        OsmClient osmClient,
        TollDbContext context, 
        OsmRoadParserService parserService, 
        OsmTollParserService tollParserService, 
        IServiceScopeFactory scopeFactory)
    {
        _osmClient = osmClient;
        _context = context;
        _parserService = parserService;
        _tollParserService = tollParserService;
        _scopeFactory = scopeFactory;
    }

    #region Parallel

    public async Task ImportStateAsyncForParallel(string stateCode, CancellationToken ct = default)
    {
        if (!StateBounds.TryGetValue(stateCode.ToUpperInvariant(), out var bounds))
            throw new ArgumentException($"State code '{stateCode}' not found.", nameof(stateCode));

        using var doc = await _osmClient.GetTollRoadWaysAsync(bounds.south, bounds.west, bounds.north, bounds.east, ct);
        var roadsToAdd = _parserService.ParseTollRoadsFromJson(doc, stateCode.ToUpperInvariant());

        if (roadsToAdd.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TollDbContext>();
        
        // Get existing WayIds to avoid duplicates
        var existingWayIds = await context.Roads
            .Where(r => r.WayId.HasValue)
            .Select(r => r.WayId!.Value)
            .ToHashSetAsync(ct);

        // Filter out roads that already exist by WayId
        var uniqueRoadsToAdd = roadsToAdd
            .Where(r => !r.WayId.HasValue || !existingWayIds.Contains(r.WayId.Value))
            .ToList();

        if (uniqueRoadsToAdd.Count == 0) return;

        await context.Roads.AddRangeAsync(uniqueRoadsToAdd, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task ImportAllStatesAsyncParallel(CancellationToken ct = default)
    {
        try
        {
            var tasks = StateBounds.Keys.Select(async stateCode =>
            {
                try
                {
                    await ImportStateAsyncForParallel(stateCode, ct);
                    Console.WriteLine($"Imported roads for {stateCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to import roads for {stateCode}: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical error in ImportAllStatesAsyncParallel: {ex.Message}");
            throw;
        }
    }




    public async Task ImportTollsForStateAsyncForParallel(string stateCode, CancellationToken ct = default)
    {
        if (!StateBounds.TryGetValue(stateCode.ToUpperInvariant(), out var bounds))
            throw new ArgumentException($"State code '{stateCode}' not found.", nameof(stateCode));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TollDbContext>();
        
        var existingRoads = await context.Roads
            .Where(r => r.State == stateCode.ToUpperInvariant() && r.WayId.HasValue)
            .ToListAsync(ct);

        if (existingRoads.Count == 0) return;

        using var doc = await _osmClient.GetTollPointsAsync(bounds.south, bounds.west, bounds.north, bounds.east, ct);
        var tollsToAdd = _tollParserService.ParseTollPointsFromJson(doc, stateCode.ToUpperInvariant(), existingRoads);

        if (tollsToAdd.Count == 0) return;

        // Get existing NodeIds to avoid duplicates
        var existingNodeIds = await context.Tolls
            .Where(t => t.NodeId.HasValue)
            .Select(t => t.NodeId!.Value)
            .ToHashSetAsync(ct);

        // Filter out tolls that already exist by NodeId
        var uniqueTollsToAdd = tollsToAdd
            .Where(t => !t.NodeId.HasValue || !existingNodeIds.Contains(t.NodeId.Value))
            .ToList();

        if (uniqueTollsToAdd.Count == 0) return;

        await context.Tolls.AddRangeAsync(uniqueTollsToAdd, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task ImportTollsForAllStatesAsyncParallel(CancellationToken ct = default)
    {
        try
        {
            var tasks = StateBounds.Keys.Select(async stateCode =>
            {
                try
                {
                    await ImportTollsForStateAsyncForParallel(stateCode, ct);
                    Console.WriteLine($"Imported tolls for {stateCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to import tolls for {stateCode}: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical error in ImportTollsForAllStatesAsyncParallel: {ex.Message}");
            throw;
        }
    }

    #endregion

    public async Task ImportStateAsync(string stateCode, CancellationToken ct = default)
    {
        if (!StateBounds.TryGetValue(stateCode.ToUpperInvariant(), out var bounds))
        {
            throw new ArgumentException($"State code '{stateCode}' is not supported or not found in bounds dictionary.", nameof(stateCode));
        }

        using var doc = await _osmClient.GetTollRoadWaysAsync(bounds.south, bounds.west, bounds.north, bounds.east, ct);
        var roadsToAdd = _parserService.ParseTollRoadsFromJson(doc, stateCode.ToUpperInvariant());

        if (roadsToAdd.Count == 0) return;

        // Get existing WayIds to avoid duplicates
        var existingWayIds = await _context.Roads
            .Where(r => r.WayId.HasValue)
            .Select(r => r.WayId!.Value)
            .ToHashSetAsync(ct);

        // Filter out roads that already exist by WayId
        var uniqueRoadsToAdd = roadsToAdd
            .Where(r => !r.WayId.HasValue || !existingWayIds.Contains(r.WayId.Value))
            .ToList();

        if (uniqueRoadsToAdd.Count == 0) return;

        await _context.Roads.AddRangeAsync(uniqueRoadsToAdd, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task ImportAllStatesAsync(CancellationToken ct = default)
    {
        foreach (var stateCode in StateBounds.Keys)
        {
            try
            {
                await ImportStateAsync(stateCode, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to import {stateCode}: {ex.Message}");
            }
        }
    }

    public async Task ImportTollsForStateAsync(string stateCode, CancellationToken ct = default)
    {
        if (!StateBounds.TryGetValue(stateCode.ToUpperInvariant(), out var bounds))
        {
            throw new ArgumentException($"State code '{stateCode}' is not supported or not found in bounds dictionary.", nameof(stateCode));
        }

        // Get existing roads for this state with WayId
        var existingRoads = await _context.Roads
            .Where(r => r.State == stateCode.ToUpperInvariant() && r.WayId.HasValue)
            .ToListAsync(ct);

        //if (existingRoads.Count == 0)
        //{
        //    return; // No roads found, cannot import tolls
        //}

        using var doc = await _osmClient.GetTollPointsAsync(bounds.south, bounds.west, bounds.north, bounds.east, ct);
        var tollsToAdd = _tollParserService.ParseTollPointsFromJson(doc, stateCode.ToUpperInvariant(), existingRoads);

        if (tollsToAdd.Count == 0) return;

        // Get existing NodeIds to avoid duplicates
        var existingNodeIds = await _context.Tolls
            .Where(t => t.NodeId.HasValue)
            .Select(t => t.NodeId!.Value)
            .ToHashSetAsync(ct);

        // Filter out tolls that already exist by NodeId
        var uniqueTollsToAdd = tollsToAdd
            .Where(t => !t.NodeId.HasValue || !existingNodeIds.Contains(t.NodeId.Value))
            .ToList();

        if (uniqueTollsToAdd.Count == 0) return;

        await _context.Tolls.AddRangeAsync(uniqueTollsToAdd, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task ImportTollsForAllStatesAsync(CancellationToken ct = default)
    {
        foreach (var stateCode in StateBounds.Keys)
        {
            try
            {
                await ImportTollsForStateAsync(stateCode, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to import tolls for {stateCode}: {ex.Message}");
            }
        }
    }
}


