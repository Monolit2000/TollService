using TollService.Domain;

namespace TollService.Application.Common;

/// <summary>
/// Универсальный сервис для установки Number найденным толлам.
/// </summary>
public class TollNumberService
{
    /// <summary>
    /// Устанавливает Number и StateCalculatorId для списка найденных толлов.
    /// </summary>
    /// <param name="tolls">Список толлов для обработки</param>
    /// <param name="number">Значение Number для установки</param>
    /// <param name="stateCalculatorId">ID StateCalculator для установки</param>
    /// <param name="updateNumberIfDifferent">Если true, обновляет Number даже если он уже установлен, но отличается</param>
    public void SetNumberAndCalculatorId(
        IEnumerable<Toll> tolls,
        string? number,
        Guid? stateCalculatorId,
        bool updateNumberIfDifferent = true)
    {
        foreach (var toll in tolls)
        {
            // Устанавливаем Number
            if (!string.IsNullOrWhiteSpace(number))
            {
                if (updateNumberIfDifferent)
                {
                    if (toll.Number != number)
                    {
                        toll.Number = number;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(toll.Number))
                    {
                        toll.Number = number;
                    }
                }
            }

            // Устанавливаем StateCalculatorId
            if (stateCalculatorId.HasValue)
            {
                toll.StateCalculatorId = stateCalculatorId.Value;
            }
        }
    }

    /// <summary>
    /// Устанавливает Number для списка толлов на основе функции-маппера.
    /// </summary>
    /// <param name="tolls">Список толлов для обработки</param>
    /// <param name="numberMapper">Функция, которая извлекает Number из данных толла</param>
    /// <param name="stateCalculatorId">ID StateCalculator для установки</param>
    public void SetNumberAndCalculatorId<T>(
        IEnumerable<Toll> tolls,
        Func<T, string?> numberMapper,
        T sourceData,
        Guid? stateCalculatorId,
        bool updateNumberIfDifferent = true)
    {
        var number = numberMapper(sourceData);
        SetNumberAndCalculatorId(tolls, number, stateCalculatorId, updateNumberIfDifferent);
    }
}

