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

    public async Task<JsonDocument> GetTollRoadWaysForTexasAsync(CancellationToken ct = default)
    {
        // Texas BBOX: approximately 25.8,-106.6,36.5,-93.5
        return await GetTollRoadWaysAsync(25.8, -106.6, 36.5, -93.5, ct);
    }

    public async Task<JsonDocument> GetTollPointsAsync(double south, double west, double north, double east, CancellationToken ct = default)
    {
        string query = $@"[out:json][timeout:600];

// 1. Платные дороги
(
  way[""highway""][""toll""~""^(yes|1|true)$""]({south},{west},{north},{east});
  relation[""route""=""toll""]({south},{west},{north},{east});
)->.tollroads;

// 2. Извлекаем все узлы этих дорог
node(w.tollroads)->.toll_nodes;

// 3. Выбираем только нужные типы узлов
(
  node.toll_nodes[""barrier""=""toll_booth""];
  node.toll_nodes[""highway""=""toll_gantry""];
)->.toll_points;

// 4. Для найденных узлов находим их родительские дороги
way(bn.toll_points)->.parent_ways;

// 5. Выводим всё: узлы и дороги (без geometry)
(
  .toll_points;
  .parent_ways;
)->.result;

.result out body;";

        var url = $"https://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(query)}";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }
}




