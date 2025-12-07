using TollService.Domain;

namespace TollService.Application.Common;

/// <summary>
/// Универсальный сервис для обработки всех комбинаций пар толлов (entry -> exit).
/// Пропускает случаи, когда entry и exit - это один и тот же toll.
/// </summary>
public static class TollPairProcessor
{
    /// <summary>
    /// Обрабатывает все комбинации пар entry -> exit толлов.
    /// Пропускает случаи, когда entry и exit - это один и тот же toll.
    /// </summary>
    /// <param name="entryTolls">Список entry толлов (откуда)</param>
    /// <param name="exitTolls">Список exit толлов (куда)</param>
    /// <param name="processor">Функция обработки для каждой пары (entryToll, exitToll)</param>
    public static void ProcessAllPairs(
        IEnumerable<Toll> entryTolls,
        IEnumerable<Toll> exitTolls,
        Action<Toll, Toll> processor)
    {
        if (processor == null)
            throw new ArgumentNullException(nameof(processor));

        foreach (var entryToll in entryTolls)
        {
            foreach (var exitToll in exitTolls)
            {
                // Пропускаем, если entry и exit это один и тот же toll
                if (entryToll.Id == exitToll.Id)
                    continue;

                processor(entryToll, exitToll);
            }
        }
    }

    /// <summary>
    /// Обрабатывает все комбинации пар entry -> exit толлов и возвращает результаты обработки.
    /// Пропускает случаи, когда entry и exit - это один и тот же toll.
    /// </summary>
    /// <typeparam name="TResult">Тип результата обработки</typeparam>
    /// <param name="entryTolls">Список entry толлов (откуда)</param>
    /// <param name="exitTolls">Список exit толлов (куда)</param>
    /// <param name="processor">Функция обработки для каждой пары (entryToll, exitToll), возвращающая результат</param>
    /// <returns>Коллекция результатов обработки</returns>
    public static List<TResult> ProcessAllPairs<TResult>(
        IEnumerable<Toll> entryTolls,
        IEnumerable<Toll> exitTolls,
        Func<Toll, Toll, TResult> processor)
    {
        if (processor == null)
            throw new ArgumentNullException(nameof(processor));

        var results = new List<TResult>();

        foreach (var entryToll in entryTolls)
        {
            foreach (var exitToll in exitTolls)
            {
                // Пропускаем, если entry и exit это один и тот же toll
                if (entryToll.Id == exitToll.Id)
                    continue;

                var result = processor(entryToll, exitToll);
                results.Add(result);
            }
        }

        return results;
    }

    /// <summary>
    /// Обрабатывает все комбинации пар entry -> exit толлов и собирает результаты в словарь.
    /// Пропускает случаи, когда entry и exit - это один и тот же toll.
    /// </summary>
    /// <typeparam name="TResult">Тип результата обработки</typeparam>
    /// <param name="entryTolls">Список entry толлов (откуда)</param>
    /// <param name="exitTolls">Список exit толлов (куда)</param>
    /// <param name="processor">Функция обработки для каждой пары (entryToll, exitToll), возвращающая результат</param>
    /// <returns>Словарь: ключ - (FromId, ToId), значение - результат обработки</returns>
    public static Dictionary<(Guid FromId, Guid ToId), TResult> ProcessAllPairsToDictionary<TResult>(
        IEnumerable<Toll> entryTolls,
        IEnumerable<Toll> exitTolls,
        Func<Toll, Toll, TResult> processor)
    {
        if (processor == null)
            throw new ArgumentNullException(nameof(processor));

        var results = new Dictionary<(Guid FromId, Guid ToId), TResult>();

        foreach (var entryToll in entryTolls)
        {
            foreach (var exitToll in exitTolls)
            {
                // Пропускаем, если entry и exit это один и тот же toll
                if (entryToll.Id == exitToll.Id)
                    continue;

                var pairKey = (entryToll.Id, exitToll.Id);
                var result = processor(entryToll, exitToll);
                results[pairKey] = result;
            }
        }

        return results;
    }

    /// <summary>
    /// Обрабатывает все комбинации пар entry -> exit толлов и собирает результаты в словарь списков.
    /// Полезно, когда для одной пары может быть несколько результатов (например, несколько TollPrice).
    /// Пропускает случаи, когда entry и exit - это один и тот же toll.
    /// </summary>
    /// <typeparam name="TResult">Тип результата обработки</typeparam>
    /// <param name="entryTolls">Список entry толлов (откуда)</param>
    /// <param name="exitTolls">Список exit толлов (куда)</param>
    /// <param name="processor">Функция обработки для каждой пары (entryToll, exitToll), возвращающая коллекцию результатов</param>
    /// <returns>Словарь: ключ - (FromId, ToId), значение - список результатов обработки</returns>
    public static Dictionary<(Guid FromId, Guid ToId), List<TResult>> ProcessAllPairsToDictionaryList<TResult>(
        IEnumerable<Toll> entryTolls,
        IEnumerable<Toll> exitTolls,
        Func<Toll, Toll, IEnumerable<TResult>> processor)
    {
        if (processor == null)
            throw new ArgumentNullException(nameof(processor));

        var results = new Dictionary<(Guid FromId, Guid ToId), List<TResult>>();

        foreach (var entryToll in entryTolls)
        {
            foreach (var exitToll in exitTolls)
            {
                // Пропускаем, если entry и exit это один и тот же toll
                if (entryToll.Id == exitToll.Id)
                    continue;

                var pairKey = (entryToll.Id, exitToll.Id);

                // Инициализируем список для пары, если его еще нет
                if (!results.TryGetValue(pairKey, out var resultList))
                {
                    resultList = new List<TResult>();
                    results[pairKey] = resultList;
                }

                // Обрабатываем пару и добавляем результаты в список
                var pairResults = processor(entryToll, exitToll);
                resultList.AddRange(pairResults);
            }
        }

        return results;
    }
}

