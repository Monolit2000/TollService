using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.Common;

/// <summary>
/// Универсальный сервис для поиска толлов по имени или ключу в рамках bounding box.
/// Поддерживает точное и частичное совпадение.
/// </summary>
public class TollSearchService
{
    private readonly ITollDbContext _context;

    public TollSearchService(ITollDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Находит толлы по имени или ключу в пределах bounding box.
    /// Сначала ищет точное совпадение (регистронезависимо), затем частичное.
    /// </summary>
    /// <param name="searchName">Имя или ключ для поиска (будет приведено к нижнему регистру)</param>
    /// <param name="boundingBox">Географические границы поиска</param>
    /// <param name="searchOptions">Опции поиска (по Name, Key или обоим)</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Список найденных толлов</returns>
    public async Task<List<Toll>> FindTollsInBoundingBoxAsync(
        string searchName,
        Polygon boundingBox,
        TollSearchOptions searchOptions = TollSearchOptions.NameOrKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(searchName))
            return new List<Toll>();

        var searchNameLower = searchName.ToLower();

        // Загружаем все tolls в пределах bounding box один раз
        var allTollsInBox = await _context.Tolls
            .Where(t => t.Location != null && boundingBox.Contains(t.Location))
            .ToListAsync(ct);

        // Сначала ищем точное совпадение в памяти
        var tolls = FindExactMatch(allTollsInBox, searchNameLower, searchOptions);

        // Если не нашли точное совпадение, пробуем частичное совпадение
        if (tolls.Count == 0)
        {
            tolls = FindPartialMatch(allTollsInBox, searchNameLower, searchOptions);
        }

        // Фильтруем: исключаем tolls с пустыми или невалидными именами/ключами
        tolls = tolls
            .Where(t => IsValidTollNameOrKey(t.Name) || IsValidTollNameOrKey(t.Key))
            .ToList();

        return tolls;
    }

    /// <summary>
    /// Находит толлы для множества имен в пределах bounding box одним запросом к БД.
    /// Загружает все tolls в bounding box один раз, затем ищет совпадения для всех имен в памяти.
    /// </summary>
    /// <param name="searchNames">Список имен для поиска</param>
    /// <param name="boundingBox">Географические границы поиска</param>
    /// <param name="searchOptions">Опции поиска (по Name, Key или обоим)</param>
    /// <param name="websiteUrl">Опциональный URL для обновления найденных толлов</param>
    /// <param name="paymentMethod">Опциональный PaymentMethod для обновления найденных толлов</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Словарь: ключ - имя для поиска, значение - список найденных толлов</returns>
    public async Task<Dictionary<string, List<Toll>>> FindMultipleTollsInBoundingBoxAsync(
        IEnumerable<string> searchNames,
        Polygon boundingBox,
        TollSearchOptions searchOptions = TollSearchOptions.NameOrKey,
        string? websiteUrl = null,
        PaymentMethod? paymentMethod = null,
        CancellationToken ct = default)
    {
        var searchNamesList = searchNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToList();

        if (searchNamesList.Count == 0)
            return new Dictionary<string, List<Toll>>();

        // Загружаем все tolls в пределах bounding box один раз
        var allTollsInBox = await _context.Tolls
            .Where(t => t.Location != null && boundingBox.Contains(t.Location))
            .ToListAsync(ct);

        var result = new Dictionary<string, List<Toll>>();
        var tollsToUpdate = new HashSet<Toll>(); // Уникальные толлы для обновления метаданных

        // Для каждого имени ищем совпадения в уже загруженной коллекции
        foreach (var searchName in searchNamesList)
        {
            var searchNameLower = searchName.ToLower();
            var tolls = new List<Toll>();

            // Сначала ищем точное совпадение
            tolls = FindExactMatch(allTollsInBox, searchNameLower, searchOptions);

            // Если не нашли точное совпадение, пробуем частичное совпадение
            if (tolls.Count == 0)
            {
                tolls = FindPartialMatch(allTollsInBox, searchNameLower, searchOptions);
            }

            // Фильтруем: исключаем tolls с пустыми или невалидными именами/ключами
            tolls = tolls
                .Where(t => IsValidTollNameOrKey(t.Name) || IsValidTollNameOrKey(t.Key))
                .ToList();

            result[searchName] = tolls;

            // Добавляем найденные толлы в список для обновления метаданных
            if (websiteUrl != null || paymentMethod != null)
            {
                foreach (var toll in tolls)
                {
                    tollsToUpdate.Add(toll);
                }
            }
        }

        // Обновляем метаданные для всех найденных толлов
        if (tollsToUpdate.Count > 0)
        {
            foreach (var toll in tollsToUpdate)
            {
                if (websiteUrl != null)
                {
                    toll.WebsiteUrl = websiteUrl;
                }
                if (paymentMethod != null)
                {
                    // Убеждаемся, что PaymentMethod инициализирован перед присваиванием
                    if (toll.PaymentMethod == null)
                    {
                        toll.PaymentMethod = PaymentMethod.Default();
                    }
                    // Обновляем все поля PaymentMethod
                    toll.PaymentMethod.Tag = paymentMethod.Tag;
                    toll.PaymentMethod.NoPlate = paymentMethod.NoPlate;
                    toll.PaymentMethod.Cash = paymentMethod.Cash;
                    toll.PaymentMethod.NoCard = paymentMethod.NoCard;
                    toll.PaymentMethod.App = paymentMethod.App;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Находит толлы по точному совпадению имени или ключа в уже загруженной коллекции
    /// </summary>
    private static List<Toll> FindExactMatch(
        List<Toll> allTollsInBox,
        string searchNameLower,
        TollSearchOptions searchOptions)
    {
        if (searchOptions.HasFlag(TollSearchOptions.Name) && searchOptions.HasFlag(TollSearchOptions.Key))
        {
            return allTollsInBox
                .Where(t =>
                    (t.Name != null && t.Name.ToLower() == searchNameLower) ||
                    (t.Key != null && t.Key.ToLower() == searchNameLower))
                .ToList();
        }
        else if (searchOptions.HasFlag(TollSearchOptions.Name))
        {
            return allTollsInBox
                .Where(t => t.Name != null && t.Name.ToLower() == searchNameLower)
                .ToList();
        }
        else if (searchOptions.HasFlag(TollSearchOptions.Key))
        {
            return allTollsInBox
                .Where(t => t.Key != null && t.Key.ToLower() == searchNameLower)
                .ToList();
        }

        return new List<Toll>();
    }

    /// <summary>
    /// Находит толлы по частичному совпадению имени или ключа в уже загруженной коллекции
    /// </summary>
    private static List<Toll> FindPartialMatch(
        List<Toll> allTollsInBox,
        string searchNameLower,
        TollSearchOptions searchOptions)
    {
        if (searchOptions.HasFlag(TollSearchOptions.Name) && searchOptions.HasFlag(TollSearchOptions.Key))
        {
            return allTollsInBox
                .Where(t =>
                    (t.Name != null && (t.Name.ToLower().Contains(searchNameLower) || searchNameLower.Contains(t.Name.ToLower()))) ||
                    (t.Key != null && (t.Key.ToLower().Contains(searchNameLower) || searchNameLower.Contains(t.Key.ToLower()))))
                .ToList();
        }
        else if (searchOptions.HasFlag(TollSearchOptions.Name))
        {
            return allTollsInBox
                .Where(t => t.Name != null && (t.Name.ToLower().Contains(searchNameLower) || searchNameLower.Contains(t.Name.ToLower())))
                .ToList();
        }
        else if (searchOptions.HasFlag(TollSearchOptions.Key))
        {
            return allTollsInBox
                .Where(t => t.Key != null && (t.Key.ToLower().Contains(searchNameLower) || searchNameLower.Contains(t.Key.ToLower())))
                .ToList();
        }

        return new List<Toll>();
    }

    /// <summary>
    /// Проверяет, является ли имя или ключ валидным (не пустой, не только подчеркивания)
    /// </summary>
    public static bool IsValidTollNameOrKey(string? nameOrKey)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey))
            return false;

        var trimmed = nameOrKey.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "_" || trimmed.All(c => c == '_'))
            return false;

        return true;
    }
}

/// <summary>
/// Опции поиска толлов
/// </summary>
[Flags]
public enum TollSearchOptions
{
    Name = 1,
    Key = 2,
    NameOrKey = Name | Key
}

