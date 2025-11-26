using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.KS;

public record CreateKansasStateCalculatorCommand(
    KansasCalculatorRequestDto Request) : IRequest<CreateKansasStateCalculatorResult>;

public record CreateKansasStateCalculatorResult(
    Guid StateCalculatorId,
    int CreatedPrices,
    int UpdatedPrices,
    List<string> Errors);

public class CreateKansasStateCalculatorCommandHandler(
    ITollDbContext _context) : IRequestHandler<CreateKansasStateCalculatorCommand, CreateKansasStateCalculatorResult>
{
    public async Task<CreateKansasStateCalculatorResult> Handle(CreateKansasStateCalculatorCommand request, CancellationToken ct)
    {
        var errors = new List<string>();

        // 1. Получаем или создаем StateCalculator для Kansas
        var ksCalculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == "KS", ct);

        if (ksCalculator == null)
        {
            ksCalculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = "Kansas Turnpike",
                StateCode = "KS"
            };
            _context.StateCalculators.Add(ksCalculator);
            await _context.SaveChangesAsync(ct);
        }

        // 2. Загружаем существующие CalculatePrice для этого калькулятора
        var existingPrices = await _context.CalculatePrices
            .Where(cp => cp.StateCalculatorId == ksCalculator.Id)
            .ToListAsync(ct);

        // 3. Готовим словарь Toll по номеру (Number = plaza.Value)
        var plazaNumbers = request.Request.Plazas
            .Select(p => p.Value.ToString())
            .Distinct()
            .ToList();

        // Ограничиваемся толлами, которые лежат в географических границах Канзаса
        // Примерный bounding box: (36.9, -102.0, 40.0, -94.6)
        var tolls = await _context.Tolls
            .Where(t =>
                t.Location != null &&
                t.Location.Y >= 36.9 && t.Location.Y <= 40.0 &&   // широта
                t.Location.X >= -102.0 && t.Location.X <= -94.6 && // долгота
                t.Number != null &&
                plazaNumbers.Contains(t.Number))
            .ToListAsync(ct);

        // Убедимся, что все эти toll-ы привязаны к StateCalculator Kansas
        foreach (var toll in tolls)
        {
            if (toll.StateCalculatorId != ksCalculator.Id)
            {
                toll.StateCalculatorId = ksCalculator.Id;
            }
        }

        int created = 0;
        int updated = 0;

        var allRates = request.Request.CtsRates;
        var plazas = request.Request.Plazas;
        var vehicleClasses = request.Request.VehicleClasses;

        // 4. Для каждой пары (entry, exit) и каждого класса считаем цену и создаем/обновляем CalculatePrice
        foreach (var entry in plazas)
        {
            foreach (var exit in plazas)
            {
                // Пропускаем одинаковые точки
                if (entry.Value == exit.Value)
                    continue;

                foreach (var vehicleClass in vehicleClasses)
                {
                    var result = CalculateToll(entry, exit, vehicleClass, allRates);

                    // Если тариф 0, можно пропустить (нет начислений)
                    if (result.TBR == 0m && result.IBR == 0m)
                    {
                        continue;
                    }

                    var fromToll = tolls.FirstOrDefault(t => t.Number == entry.Value.ToString());
                    if (fromToll == null)
                    {
                        errors.Add($"From toll not found for plaza value {entry.Value}");
                        continue;
                    }

                    if(exit.Value == 217)
                    {

                    }
                    
                    var toToll = tolls.FirstOrDefault(t => t.Number == exit.Value.ToString());
                    if (toToll.Name == "Eastern Entrance")
                    {

                    }
                    if (toToll == null)
                    {
                        errors.Add($"To toll not found for plaza value {exit.Value}");
                        continue;
                    }

                    var existingPrice = existingPrices
                        .FirstOrDefault(cp => cp.FromId == fromToll.Id && cp.ToId == toToll.Id);

                    if (existingPrice != null)
                    {
                        existingPrice.IPass = (double)result.TBR;
                        existingPrice.Online = (double)result.TBR;
                        existingPrice.Cash = (double)result.IBR;
                        updated++;
                    }
                    else
                    {
                        var calculatePrice = new CalculatePrice
                        {
                            Id = Guid.NewGuid(),
                            StateCalculatorId = ksCalculator.Id,
                            FromId = fromToll.Id,
                            ToId = toToll.Id,
                            IPass = (double)result.TBR,
                            Online = (double)result.TBR,
                            Cash = (double)result.IBR
                        };
                        _context.CalculatePrices.Add(calculatePrice);
                        existingPrices.Add(calculatePrice);
                        created++;
                    }
                }
            }
        }

        await _context.SaveChangesAsync(ct);

        return new CreateKansasStateCalculatorResult(
            ksCalculator.Id,
            created,
            updated,
            errors);
    }

    // Перенос логики Vue калькулятора в C#
    private static KansasTollResult CalculateToll(
        KansasPlazaDto entry,
        KansasPlazaDto exit,
        KansasVehicleClassDto vehicleClass,
        List<KansasCtsRateDto> allRates)
    {
        if (vehicleClass.Axles != 5)
            return new KansasTollResult(0m, 0m);

        // 1. Проверка на наличие всех данных
        if (entry == null || exit == null || vehicleClass == null)
        {
            return new KansasTollResult(0m, 0m);
        }

        // 2. Фильтрация и расчет
        var relevantRates = allRates
            // Фильтр по классу авто
            .Where(zone => zone.Class == vehicleClass.Axles)

            // Фильтр по направлению (берем зоны МЕЖДУ въездом и выездом)
            .Where(zone => entry.Value > exit.Value
                ? zone.ZoneCode >= exit.Value && zone.ZoneCode <= entry.Value  // Движение на Запад/Юг
                : zone.ZoneCode >= entry.Value && zone.ZoneCode <= exit.Value) // Движение на Восток/Север

            // Исключение зоны 183 (Topeka I-70), если это не точка въезда/выезда
            .Where(zone => zone.ZoneCode != 183 || entry.Value == 183 || exit.Value == 183)

            // Специальная логика для зоны 50 (Wichita East)
            .Where(zone =>
                zone.ZoneCode != 50 ||
                (entry.Value != 50 && exit.Value != 50) ||
                (entry.Value >= 50 && exit.Value >= 50))

            // Специальная логика для зоны 202 (West Lawrence)
            .Where(zone =>
                zone.ZoneCode != 202 ||
                (entry.Value != 202 && exit.Value != 202) ||
                (entry.Value >= 202 && exit.Value >= 202))
            .ToList();

        // 3. Суммирование
        decimal totalTbr = relevantRates.Sum(r => r.TransponderRate);

        // 4. Округление TBR до 2 знаков
        totalTbr = Math.Round(totalTbr, 2, MidpointRounding.AwayFromZero);

        // 5. Расчет IBR (цена без транспондера = TBR * 2)
        decimal totalIbr = Math.Round(totalTbr * 2, 2, MidpointRounding.AwayFromZero);

        return new KansasTollResult(totalTbr, totalIbr);
    }

    private readonly record struct KansasTollResult(decimal TBR, decimal IBR);
}


