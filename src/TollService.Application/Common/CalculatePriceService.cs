using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.Common;

/// <summary>
/// Данные для создания или обновления TollPrice
/// </summary>
public record TollPriceData(
    Guid TollId,
    double Amount,
    TollPaymentType PaymentType,
    AxelType AxelType = AxelType._5L,
    TollPriceDayOfWeek DayOfWeekFrom = TollPriceDayOfWeek.Any,
    TollPriceDayOfWeek DayOfWeekTo = TollPriceDayOfWeek.Any,
    TollPriceTimeOfDay TimeOfDay = TollPriceTimeOfDay.Any,
    TimeOnly TimeFrom = default,
    TimeOnly TimeTo = default,
    string? Description = null);

/// <summary>
/// Универсальный сервис для создания и управления CalculatePrice и TollPrice для динамических маршрутов.
/// </summary>
public class CalculatePriceService
{
    private readonly ITollDbContext _context;

    public CalculatePriceService(ITollDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Получает или создает CalculatePrice для пары From -> To с указанным StateCalculatorId.
    /// </summary>
    /// <param name="fromTollId">ID толла отправления</param>
    /// <param name="toTollId">ID толла назначения</param>
    /// <param name="stateCalculatorId">ID StateCalculator</param>
    /// <param name="includeTollPrices">Включать ли связанные TollPrice при загрузке</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Существующий или созданный CalculatePrice</returns>
    public async Task<CalculatePrice> GetOrCreateCalculatePriceAsync(
        Guid fromTollId,
        Guid toTollId,
        Guid stateCalculatorId,
        bool includeTollPrices = true,
        CancellationToken ct = default)
    {
        if (fromTollId == toTollId)
            throw new ArgumentException("FromTollId and ToTollId cannot be the same", nameof(toTollId));

        var query = _context.CalculatePrices.AsQueryable();

        if (includeTollPrices)
        {
            query = query.Include(cp => cp.TollPrices);
        }

        var calculatePrice = await query
            .FirstOrDefaultAsync(cp =>
                cp.FromId == fromTollId &&
                cp.ToId == toTollId &&
                cp.StateCalculatorId == stateCalculatorId,
                ct);

        if (calculatePrice == null)
        {
            calculatePrice = new CalculatePrice
            {
                Id = Guid.NewGuid(),
                StateCalculatorId = stateCalculatorId,
                FromId = fromTollId,
                ToId = toTollId,
                TollPrices = new List<TollPrice>()
            };
            _context.CalculatePrices.Add(calculatePrice);
        }

        return calculatePrice;
    }



    /// <summary>
    /// Батч-получение или создание CalculatePrice для множества пар с установкой TollPrice.
    /// Оптимизирован для работы с большим количеством пар одновременно.
    /// </summary>
    /// <param name="tollPairsWithPrices">Словарь: ключ - (FromId, ToId), значение - список готовых TollPrice для установки (может быть null)</param>
    /// <param name="stateCalculatorId">ID StateCalculator</param>
    /// <param name="includeTollPrices">Включать ли связанные TollPrice при загрузке</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Словарь: ключ - (FromId, ToId), значение - CalculatePrice</returns>
    public async Task<Dictionary<(Guid FromId, Guid ToId), CalculatePrice>> GetOrCreateCalculatePricesBatchAsync(
        Dictionary<(Guid FromId, Guid ToId), IEnumerable<TollPrice>?> tollPairsWithPrices,
        Guid stateCalculatorId,
        bool includeTollPrices = true,
        CancellationToken ct = default)
    {
        var pairs = tollPairsWithPrices.Keys.Where(p => p.FromId != p.ToId).Distinct().ToList();
        if (pairs.Count == 0)
            return new Dictionary<(Guid FromId, Guid ToId), CalculatePrice>();

        var fromIds = pairs.Select(p => p.FromId).Distinct().ToList();
        var toIds = pairs.Select(p => p.ToId).Distinct().ToList();

        var query = _context.CalculatePrices.AsQueryable();

        if (includeTollPrices)
        {
            query = query.Include(cp => cp.TollPrices);
        }

        // Загружаем все существующие CalculatePrice для этих пар
        var existingPrices = await query
            .Where(cp =>
                cp.StateCalculatorId == stateCalculatorId &&
                fromIds.Contains(cp.FromId) &&
                toIds.Contains(cp.ToId))
            .ToListAsync(ct);

        var result = new Dictionary<(Guid FromId, Guid ToId), CalculatePrice>();
        var pricesToAdd = new List<CalculatePrice>();

        // Заполняем словарь существующими CalculatePrice
        foreach (var cp in existingPrices)
        {
            var key = (cp.FromId, cp.ToId);
            if (pairs.Contains(key))
            {
                result[key] = cp;
            }
        }

        // Создаем новые CalculatePrice для пар, которых нет, и устанавливаем TollPrice
        foreach (var pair in pairs)
        {
            if (!result.ContainsKey(pair))
            {
                var newCalculatePrice = new CalculatePrice
                {
                    Id = Guid.NewGuid(),
                    StateCalculatorId = stateCalculatorId,
                    FromId = pair.FromId,
                    ToId = pair.ToId,
                    TollPrices = new List<TollPrice>()
                };

                // Устанавливаем TollPrice для нового CalculatePrice, если они предоставлены
                if (tollPairsWithPrices.TryGetValue(pair, out var tollPricesList) && tollPricesList != null)
                {
                    foreach (var tollPrice in tollPricesList)
                    {
                        if (tollPrice.Amount > 0)
                        {
                            var tollId = tollPrice.TollId == Guid.Empty ? pair.FromId : tollPrice.TollId;

                            SetTollPrice(
                                newCalculatePrice,
                                tollId,
                                tollPrice.Amount,
                                tollPrice.PaymentType,
                                tollPrice.AxelType,
                                tollPrice.DayOfWeekFrom,
                                tollPrice.DayOfWeekTo,
                                tollPrice.TimeOfDay,
                                tollPrice.Description);
                        }
                    }
                }

                result[pair] = newCalculatePrice;
                pricesToAdd.Add(newCalculatePrice);
            }
            else
            {
                // Обновляем TollPrice для существующего CalculatePrice
                var existingCalculatePrice = result[pair];

                if (tollPairsWithPrices.TryGetValue(pair, out var tollPricesList) && tollPricesList != null)
                {
                    foreach (var tollPrice in tollPricesList)
                    {
                        if (tollPrice.Amount > 0)
                        {
                            var tollId = tollPrice.TollId == Guid.Empty ? pair.FromId : tollPrice.TollId;

                            var updatedTollPrice = SetTollPrice(
                                existingCalculatePrice,
                                tollId,
                                tollPrice.Amount,
                                tollPrice.PaymentType,
                                tollPrice.AxelType,
                                tollPrice.DayOfWeekFrom,
                                tollPrice.DayOfWeekTo,
                                tollPrice.TimeOfDay,
                                tollPrice.Description);

                            // Обновляем дополнительные поля, если они есть
                            if (tollPrice.TimeFrom != default)
                            {
                                updatedTollPrice.TimeFrom = tollPrice.TimeFrom;
                            }
                            if (tollPrice.TimeTo != default)
                            {
                                updatedTollPrice.TimeTo = tollPrice.TimeTo;
                            }
                        }
                    }
                }
            }
        }

        // Добавляем новые CalculatePrice в контекст
        if (pricesToAdd.Count > 0)
        {
            _context.CalculatePrices.AddRange(pricesToAdd);
        }

        return result;
    }

    /// <summary>
    /// Создает или обновляет TollPrice через CalculatePrice.SetPriceByPaymentType.
    /// Устанавливает TollId и Description для нового TollPrice.
    /// </summary>
    /// <param name="calculatePrice">CalculatePrice для работы</param>
    /// <param name="tollId">ID толла (обычно FromId)</param>
    /// <param name="amount">Сумма</param>
    /// <param name="paymentType">Тип оплаты</param>
    /// <param name="axelType">Тип осей</param>
    /// <param name="dayOfWeekFrom">День недели начала</param>
    /// <param name="dayOfWeekTo">День недели окончания</param>
    /// <param name="timeOfDay">Время суток</param>
    /// <param name="description">Описание (опционально)</param>
    /// <returns>Созданный или обновленный TollPrice</returns>
    public TollPrice SetTollPrice(
        CalculatePrice calculatePrice,
        Guid tollId,
        double amount,
        TollPaymentType paymentType,
        AxelType axelType = AxelType._5L,
        TollPriceDayOfWeek dayOfWeekFrom = TollPriceDayOfWeek.Any,
        TollPriceDayOfWeek dayOfWeekTo = TollPriceDayOfWeek.Any,
        TollPriceTimeOfDay timeOfDay = TollPriceTimeOfDay.Any,
        string? description = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero", nameof(amount));

        // Используем SetPriceByPaymentType на CalculatePrice
        var tollPrice = calculatePrice.SetPriceByPaymentType(
            amount,
            paymentType,
            axelType,
            dayOfWeekFrom,
            dayOfWeekTo,
            timeOfDay);

        // Если это новый TollPrice (TollId == Guid.Empty), настраиваем его
        if (tollPrice.TollId == Guid.Empty)
        {
            tollPrice.TollId = tollId;
            if (!string.IsNullOrWhiteSpace(description))
            {
                tollPrice.Description = description;
            }
        }

        return tollPrice;
    }

    /// <summary>
    /// Устанавливает TollPrice напрямую к Toll без использования CalculatePrice.
    /// Использует метод Toll.SetPriceByPaymentType для создания или обновления цены.
    /// </summary>
    /// <param name="toll">Toll для установки цены</param>
    /// <param name="amount">Сумма</param>
    /// <param name="paymentType">Тип оплаты</param>
    /// <param name="axelType">Тип осей</param>
    /// <param name="dayOfWeekFrom">День недели начала</param>
    /// <param name="dayOfWeekTo">День недели окончания</param>
    /// <param name="timeOfDay">Время суток</param>
    /// <param name="description">Описание (опционально)</param>
    /// <param name="timeFrom">Время начала (опционально)</param>
    /// <param name="timeTo">Время окончания (опционально)</param>
    /// <returns>Созданный или обновленный TollPrice</returns>
    public TollPrice SetTollPriceDirectly(
        Toll toll,
        double amount,
        TollPaymentType paymentType,
        AxelType axelType = AxelType._5L,
        TollPriceDayOfWeek dayOfWeekFrom = TollPriceDayOfWeek.Any,
        TollPriceDayOfWeek dayOfWeekTo = TollPriceDayOfWeek.Any,
        TollPriceTimeOfDay timeOfDay = TollPriceTimeOfDay.Any,
        string? description = null,
        TimeOnly timeFrom = default,
        TimeOnly timeTo = default)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero", nameof(amount));

        // Используем метод Toll.SetPriceByPaymentType
        toll.SetPriceByPaymentType(
            amount,
            paymentType,
            axelType,
            dayOfWeekFrom,
            dayOfWeekTo,
            timeOfDay);

        // Находим созданный или обновленный TollPrice
        var tollPrice = toll.GetPriceByPaymentType(
            paymentType,
            axelType,
            dayOfWeekFrom,
            dayOfWeekTo,
            timeOfDay);

        if (tollPrice == null)
        {
            throw new InvalidOperationException("Failed to create or find TollPrice");
        }

        // Устанавливаем дополнительные поля
        if (!string.IsNullOrWhiteSpace(description))
        {
            tollPrice.Description = description;
        }

        if (timeFrom != default)
        {
            tollPrice.TimeFrom = timeFrom;
        }

        if (timeTo != default)
        {
            tollPrice.TimeTo = timeTo;
        }

        return tollPrice;
    }

    /// <summary>
    /// Батч-установка TollPrice напрямую к Toll без использования CalculatePrice.
    /// Загружает Toll из базы данных, если они еще не загружены.
    /// </summary>
    /// <param name="tollPricesData">Словарь: ключ - TollId, значение - список TollPriceData для установки</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Словарь: ключ - TollId, значение - список созданных или обновленных TollPrice</returns>
    public async Task<Dictionary<Guid, List<TollPrice>>> SetTollPricesDirectlyBatchAsync(
        Dictionary<Guid, IEnumerable<TollPriceData>> tollPricesData,
        CancellationToken ct = default)
    {
        if (tollPricesData.Count == 0)
            return new Dictionary<Guid, List<TollPrice>>();

        var tollIds = tollPricesData.Keys.Distinct().ToList();

        // Загружаем все Toll с их TollPrices
        var tolls = await _context.Tolls
            .Include(t => t.TollPrices)
            .Where(t => tollIds.Contains(t.Id))
            .ToListAsync(ct);

        var result = new Dictionary<Guid, List<TollPrice>>();
        var tollsToUpdate = new List<Toll>();

        foreach (var kvp in tollPricesData)
        {
            var tollId = kvp.Key;
            var pricesData = kvp.Value;

            var toll = tolls.FirstOrDefault(t => t.Id == tollId);
            if (toll == null)
            {
                continue; // Пропускаем, если Toll не найден
            }

            var createdPrices = new List<TollPrice>();

            foreach (var priceData in pricesData)
            {
                if (priceData.Amount <= 0)
                    continue;

                try
                {
                    var tollPrice = SetTollPriceDirectly(
                        toll,
                        priceData.Amount,
                        priceData.PaymentType,
                        priceData.AxelType,
                        priceData.DayOfWeekFrom,
                        priceData.DayOfWeekTo,
                        priceData.TimeOfDay,
                        priceData.Description,
                        priceData.TimeFrom,
                        priceData.TimeTo);

                    createdPrices.Add(tollPrice);
                }
                catch (Exception)
                {
                    // Логируем ошибку, но продолжаем обработку остальных цен
                    // Можно добавить логирование здесь
                    continue;
                }
            }

            if (createdPrices.Count > 0)
            {
                result[tollId] = createdPrices;
                if (!tollsToUpdate.Contains(toll))
                {
                    tollsToUpdate.Add(toll);
                }
            }
        }

        // Помечаем Toll как измененные для EF Core
        foreach (var toll in tollsToUpdate)
        {
            _context.Tolls.Update(toll);
        }

        return result;
    }

    /// <summary>
    /// Батч-установка TollPrice напрямую к Toll без использования CalculatePrice.
    /// Принимает уже готовые TollPrice объекты.
    /// </summary>
    /// <param name="tollPricesByTollId">Словарь: ключ - TollId, значение - список готовых TollPrice для установки</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Словарь: ключ - TollId, значение - список созданных или обновленных TollPrice</returns>
    public async Task<Dictionary<Guid, List<TollPrice>>> SetTollPricesDirectlyBatchAsync(
        Dictionary<Guid, IEnumerable<TollPrice>> tollPricesByTollId,
        CancellationToken ct = default)
    {
        if (tollPricesByTollId.Count == 0)
            return new Dictionary<Guid, List<TollPrice>>();

        var tollIds = tollPricesByTollId.Keys.Distinct().ToList();

        // Загружаем все Toll с их TollPrices
        var tolls = await _context.Tolls
            .Include(t => t.TollPrices)
            .Where(t => tollIds.Contains(t.Id))
            .ToListAsync(ct);

        var result = new Dictionary<Guid, List<TollPrice>>();
        var tollsToUpdate = new List<Toll>();
        var newTollPricesToAdd = new List<TollPrice>();

        foreach (var kvp in tollPricesByTollId)
        {
            var tollId = kvp.Key;
            var tollPrices = kvp.Value;

            var toll = tolls.FirstOrDefault(t => t.Id == tollId);
            if (toll == null)
            {
                continue; // Пропускаем, если Toll не найден
            }

            var processedPrices = new List<TollPrice>();

            foreach (var tollPrice in tollPrices)
            {
                if (tollPrice.Amount <= 0)
                    continue;

                // Устанавливаем TollId, если он не установлен
                if (tollPrice.TollId == Guid.Empty)
                {
                    tollPrice.TollId = tollId;
                }

                // Убеждаемся, что CalculatePriceId = null (прямая цена без CalculatePrice)
                tollPrice.CalculatePriceId = null;

                // Проверяем, существует ли уже такая цена
                var existingPrice = toll.TollPrices.FirstOrDefault(
                    tp => tp.PaymentType == tollPrice.PaymentType &&
                          tp.AxelType == tollPrice.AxelType &&
                          tp.DayOfWeekFrom == tollPrice.DayOfWeekFrom &&
                          tp.DayOfWeekTo == tollPrice.DayOfWeekTo &&
                          tp.TimeOfDay == tollPrice.TimeOfDay &&
                          !tp.IsCalculate());

                if (existingPrice != null)
                {
                    // Обновляем существующую цену
                    existingPrice.Amount = tollPrice.Amount;
                    if (!string.IsNullOrWhiteSpace(tollPrice.Description))
                    {
                        existingPrice.Description = tollPrice.Description;
                    }
                    if (tollPrice.TimeFrom != default)
                    {
                        existingPrice.TimeFrom = tollPrice.TimeFrom;
                    }
                    if (tollPrice.TimeTo != default)
                    {
                        existingPrice.TimeTo = tollPrice.TimeTo;
                    }
                    processedPrices.Add(existingPrice);
                }
                else
                {
                    // Создаем новую цену
                    if (tollPrice.Id == Guid.Empty)
                    {
                        tollPrice.Id = Guid.NewGuid();
                    }
                    toll.TollPrices.Add(tollPrice);
                    newTollPricesToAdd.Add(tollPrice);
                    processedPrices.Add(tollPrice);
                }
            }

            if (processedPrices.Count > 0)
            {
                result[tollId] = processedPrices;
                if (!tollsToUpdate.Contains(toll))
                {
                    tollsToUpdate.Add(toll);
                }
            }
        }

        // Добавляем новые TollPrice в контекст
        if (newTollPricesToAdd.Count > 0)
        {
            _context.TollPrices.AddRange(newTollPricesToAdd);
        }

        // Помечаем Toll как измененные для EF Core
        foreach (var toll in tollsToUpdate)
        {
            _context.Tolls.Update(toll);
        }

        return result;
    }

}

