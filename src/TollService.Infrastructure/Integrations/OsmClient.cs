using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TollService.Infrastructure.Integrations;

public class OsmClient
{
    private readonly HttpClient _httpClient;
    public OsmClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<JsonDocument> GetTollDataForTexasAsync(CancellationToken ct = default)
    {
        string query = """
        [out:json][timeout:600];
        area["name"="Texas"]["admin_level"="4"]->.tx;
        (
          node["highway"="toll_gantry"](area.tx);
          way["highway"="toll_gantry"](area.tx);
          node["barrier"="toll_booth"](area.tx);
          way["barrier"="toll_booth"](area.tx);
        );
        out center;
        """;

        var url = $"https://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(query)}";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }

    public async Task<JsonDocument> GetTollRoadWaysAsync(double south, double west, double north, double east, CancellationToken ct = default)
    {
        string query = $@"[out:json][timeout:600];
way[""toll""=""yes""][""highway""~""motorway|trunk""]({south},{west},{north},{east});
out geom tags;";

        var url = $"https://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(query)}";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }
}




