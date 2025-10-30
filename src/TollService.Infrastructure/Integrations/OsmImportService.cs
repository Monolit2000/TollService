using TollService.Infrastructure.Persistence;

namespace TollService.Infrastructure.Integrations;

public class OsmImportService
{
    private readonly OsmClient _osmClient;
    private readonly TollDbContext _context;

    public OsmImportService(OsmClient osmClient, TollDbContext context)
    {
        _osmClient = osmClient;
        _context = context;
    }

    public async Task ImportTexasAsync(CancellationToken ct = default)
    {
        // Placeholder: fetch data to validate pipeline; persisting can be added later
        await _osmClient.GetTollDataForTexasAsync(ct);
    }
}


