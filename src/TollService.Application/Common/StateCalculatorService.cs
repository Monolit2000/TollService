using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.Common;

/// <summary>
/// Универсальный сервис для получения или создания StateCalculator.
/// </summary>
public class StateCalculatorService
{
    private readonly ITollDbContext _context;

    public StateCalculatorService(ITollDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Получает существующий StateCalculator по коду штата или создает новый, если не найден.
    /// </summary>
    /// <param name="stateCode">Код штата (например, "NY", "CA", "OK")</param>
    /// <param name="calculatorName">Имя калькулятора (используется только при создании нового)</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Существующий или созданный StateCalculator</returns>
    public async Task<StateCalculator> GetOrCreateStateCalculatorAsync(
        string stateCode,
        string calculatorName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stateCode))
            throw new ArgumentException("State code cannot be null or empty", nameof(stateCode));

        if (string.IsNullOrWhiteSpace(calculatorName))
            throw new ArgumentException("Calculator name cannot be null or empty", nameof(calculatorName));

        // Ищем существующий StateCalculator
        var calculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == stateCode, ct);

        // Если не найден, создаем новый
        if (calculator == null)
        {
            calculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = calculatorName,
                StateCode = stateCode
            };
            _context.StateCalculators.Add(calculator);
            await _context.SaveChangesAsync(ct);
        }

        return calculator;
    }
}

