using TollService.Infrastructure.Persistence;

namespace TollService.Infrastructure.Integrations;

public class OsmImportService
{
    private readonly OsmClient _osmClient;
    private readonly TollDbContext _context;
    private readonly OsmRoadParserService _parserService;

    public OsmImportService(OsmClient osmClient, TollDbContext context, OsmRoadParserService parserService)
    {
        _osmClient = osmClient;
        _context = context;
        _parserService = parserService;
    }

    public async Task ImportTexasAsync(CancellationToken ct = default)
    {
        using var doc = await _osmClient.GetTollRoadWaysAsync(25.8, -106.6, 36.5, -93.5, ct);
        var roadsToAdd = _parserService.ParseTollRoadsFromJson(doc, "TX");

        if (roadsToAdd.Count == 0) return;

        await _context.Roads.AddRangeAsync(roadsToAdd, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task ImportLosAngelesTollRoadsAsync(CancellationToken ct = default)
    {
        using var doc = await _osmClient.GetTollRoadWaysAsync(32.5, -124.5, 42.0, -114.0, ct);
        var roadsToAdd = _parserService.ParseTollRoadsFromJson(doc, "CA");

        if (roadsToAdd.Count == 0) return;

        await _context.Roads.AddRangeAsync(roadsToAdd, ct);
        await _context.SaveChangesAsync(ct);
    }
}


