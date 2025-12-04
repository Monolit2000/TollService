using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.Common;

/// <summary>
/// Универсальный сервис для поиска совпадений толлов по имени из JSON данных.
/// </summary>
public class TollMatchingService
{
    private readonly TollSearchService _tollSearchService;

    public TollMatchingService(TollSearchService tollSearchService)
    {
        _tollSearchService = tollSearchService;
    }

    /// <summary>
    /// Находит толлы, которые совпадают по имени из JSON данных в пределах bounding box.
    /// </summary>
    /// <typeparam name="T">Тип данных из JSON</typeparam>
    /// <param name="jsonData">Данные из JSON</param>
    /// <param name="nameExtractor">Функция для извлечения имени из JSON данных</param>
    /// <param name="boundingBox">Географические границы поиска</param>
    /// <param name="searchOptions">Опции поиска (по Name, Key или обоим)</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Словарь: ключ - имя из JSON, значение - список найденных толлов</returns>
    public async Task<Dictionary<string, List<Toll>>> FindMatchingTollsAsync<T>(
        IEnumerable<T> jsonData,
        Func<T, string?> nameExtractor,
        NetTopologySuite.Geometries.Polygon boundingBox,
        TollSearchOptions searchOptions = TollSearchOptions.NameOrKey,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<Toll>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in jsonData)
        {
            var name = nameExtractor(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var tolls = await _tollSearchService.FindTollsInBoundingBoxAsync(
                name,
                boundingBox,
                searchOptions,
                ct);

            if (tolls.Count > 0)
            {
                result[name] = tolls;
            }
        }

        return result;
    }

    /// <summary>
    /// Находит толлы для одной записи из JSON.
    /// </summary>
    /// <typeparam name="T">Тип данных из JSON</typeparam>
    /// <param name="jsonItem">Одна запись из JSON</param>
    /// <param name="nameExtractor">Функция для извлечения имени из JSON данных</param>
    /// <param name="boundingBox">Географические границы поиска</param>
    /// <param name="searchOptions">Опции поиска (по Name, Key или обоим)</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Список найденных толлов</returns>
    public async Task<List<Toll>> FindMatchingTollsForItemAsync<T>(
        T jsonItem,
        Func<T, string?> nameExtractor,
        NetTopologySuite.Geometries.Polygon boundingBox,
        TollSearchOptions searchOptions = TollSearchOptions.NameOrKey,
        CancellationToken ct = default)
    {
        var name = nameExtractor(jsonItem);
        if (string.IsNullOrWhiteSpace(name))
            return new List<Toll>();

        return await _tollSearchService.FindTollsInBoundingBoxAsync(
            name,
            boundingBox,
            searchOptions,
            ct);
    }
}

