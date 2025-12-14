using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NH;

public record NewHampshireTollPlaza(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("highway")] string? Highway,
    [property: JsonPropertyName("tolls")] Dictionary<string, Dictionary<string, double>>? Tolls);

public record NewHampshirePricesData(
    [property: JsonPropertyName("new_hampshire_turnpike_system_tolls")] NewHampshireTurnpikeSystem? NewHampshireTurnpikeSystemTolls);

public record NewHampshireTurnpikeSystem(
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("vehicle_classes")] Dictionary<string, string>? VehicleClasses,
    [property: JsonPropertyName("toll_plazas")] List<NewHampshireTollPlaza>? TollPlazas,
    [property: JsonPropertyName("summary")] Dictionary<string, object>? Summary);

public record LinkNewHampshireTollsCommand(string JsonPayload)
    : IRequest<LinkNewHampshireTollsResult>;

public record NewHampshireFoundTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    string? TollNumber);

public record LinkNewHampshireTollsResult(
    List<NewHampshireFoundTollInfo> FoundTolls,
    List<string> NotFoundPlazas,
    int UpdatedTollsCount,
    string? Error = null);

// New Hampshire bounds: approximate (south, west, north, east) = (42.7, -72.6, 45.3, -70.6)
public class LinkNewHampshireTollsCommandHandler(
    ITollDbContext _context,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<LinkNewHampshireTollsCommand, LinkNewHampshireTollsResult>
{
    private static readonly double NhMinLatitude = 42.7;
    private static readonly double NhMinLongitude = -72.6;
    private static readonly double NhMaxLatitude = 45.3;
    private static readonly double NhMaxLongitude = -70.6;

    public async Task<LinkNewHampshireTollsResult> Handle(LinkNewHampshireTollsCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new LinkNewHampshireTollsResult(new(), new(), 0, "JSON payload is empty");
        }

        NewHampshirePricesData? data = null;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            await Task.Yield();
            // Пробуем десериализовать как обернутый объект
            data = JsonSerializer.Deserialize<NewHampshirePricesData>(request.JsonPayload, options);
        }
        catch (JsonException)
        {
            // Игнорируем, попробуем другой формат
        }

        // Если не удалось распарсить как обернутый объект, пробуем как прямой объект NewHampshireTurnpikeSystem
        if (data?.NewHampshireTurnpikeSystemTolls == null)
        {
            try
            {
                var directData = JsonSerializer.Deserialize<NewHampshireTurnpikeSystem>(request.JsonPayload, options);
                if (directData != null)
                {
                    data = new NewHampshirePricesData(directData);
                }
            }
            catch (JsonException jsonEx)
            {
                return new LinkNewHampshireTollsResult(new(), new(), 0, $"Ошибка парсинга JSON: {jsonEx.Message}. Убедитесь, что JSON содержит поле 'new_hampshire_turnpike_system_tolls' с массивом 'toll_plazas'.");
            }
        }

        if (data?.NewHampshireTurnpikeSystemTolls?.TollPlazas == null || data.NewHampshireTurnpikeSystemTolls.TollPlazas.Count == 0)
        {
            // Проверяем, что вообще было распарсено
            if (data == null)
            {
                return new LinkNewHampshireTollsResult(new(), new(), 0, "Не удалось распарсить JSON. Проверьте структуру данных.");
            }
            if (data.NewHampshireTurnpikeSystemTolls == null)
            {
                return new LinkNewHampshireTollsResult(new(), new(), 0, "Поле 'new_hampshire_turnpike_system_tolls' не найдено в JSON.");
            }
            return new LinkNewHampshireTollsResult(new(), new(), 0, "Плазы не найдены в ответе (массив 'toll_plazas' пуст или отсутствует).");
        }

        // Создаем bounding box для New Hampshire
        var nhBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            NhMinLongitude,
            NhMinLatitude,
            NhMaxLongitude,
            NhMaxLatitude);

        // Собираем все уникальные имена плаз для оптимизированного поиска
        var allPlazaNames = data.NewHampshireTurnpikeSystemTolls.TollPlazas
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p.Name)
            .Distinct()
            .ToList();

        if (allPlazaNames.Count == 0)
        {
            return new LinkNewHampshireTollsResult(
                new(),
                new(),
                0,
                "Не найдено ни одного имени плазы");
        }

        // Оптимизированный поиск tolls: один запрос к БД
        var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allPlazaNames,
            nhBoundingBox,
            TollSearchOptions.Key,
            websiteUrl: null,
            paymentMethod: null,
            ct);

        var foundTolls = new List<NewHampshireFoundTollInfo>();
        var notFoundPlazas = new List<string>();
        var tollsToUpdatePrices = new Dictionary<Guid, List<TollPriceData>>();

        // Функция для маппинга класса на AxelType
        static AxelType MapClassToAxelType(string className)
        {
            return className.ToLower() switch
            {
                "class_5" => AxelType._5L,
                "class_6" => AxelType._6L,
                _ => AxelType.Unknown
            };
        }

        foreach (var plaza in data.NewHampshireTurnpikeSystemTolls.TollPlazas)
        {
            if (string.IsNullOrWhiteSpace(plaza.Name))
            {
                notFoundPlazas.Add("Plaza with empty name");
                continue;
            }

            // Ищем tolls по имени плазы (ключи в словаре хранятся в оригинальном регистре)
            if (!tollsByPlazaName.TryGetValue(plaza.Name, out var plazaTolls) || plazaTolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.Name);
                continue;
            }

            // Обрабатываем цены для каждой найденной плазы
            foreach (var toll in plazaTolls)
            {
                // Обрабатываем цены из plaza.Tolls
                if (plaza.Tolls != null && plaza.Tolls.Count > 0)
                {
                    foreach (var vehicleClass in plaza.Tolls)
                    {
                        var axelType = MapClassToAxelType(vehicleClass.Key);
                        if (axelType == AxelType.Unknown)
                        {
                            continue; // Пропускаем неизвестные классы
                        }

                        var classPrices = vehicleClass.Value;
                        if (classPrices == null || classPrices.Count == 0)
                        {
                            continue;
                        }

                        // Обрабатываем Cash цены (case-insensitive поиск)
                        var cashPrice = classPrices.FirstOrDefault(kvp =>
                            kvp.Key.Equals("cash", StringComparison.OrdinalIgnoreCase)).Value;
                        if (cashPrice > 0)
                        {
                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: cashPrice,
                                PaymentType: TollPaymentType.Cash,
                                AxelType: axelType,
                                Description: $"New Hampshire {plaza.Name} - {vehicleClass.Key} - Cash"));
                        }

                        // Обрабатываем EZPass цены (case-insensitive поиск)
                        var ezpassPrice = classPrices.FirstOrDefault(kvp =>
                            kvp.Key.Equals("ezpass", StringComparison.OrdinalIgnoreCase)).Value;
                        if (ezpassPrice > 0)
                        {
                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: ezpassPrice,
                                PaymentType: TollPaymentType.EZPass,
                                AxelType: axelType,
                                Description: $"New Hampshire {plaza.Name} - {vehicleClass.Key} - EZPass"));
                        }
                    }
                }

                foundTolls.Add(new NewHampshireFoundTollInfo(
                    PlazaName: plaza.Name,
                    TollId: toll.Id,
                    TollName: toll.Name,
                    TollKey: toll.Key,
                    TollNumber: toll.Number));
            }
        }

        // Батч-установка цен
        int updatedTollsCount = 0;
        if (tollsToUpdatePrices.Count > 0)
        {
            // Конвертируем List в IEnumerable для метода
            var tollsToUpdatePricesEnumerable = tollsToUpdatePrices.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TollPriceData>)kvp.Value);

            var updatedPricesResult = await _calculatePriceService.SetTollPricesDirectlyBatchAsync(
                tollsToUpdatePricesEnumerable,
                null,
                ct);
            updatedTollsCount = updatedPricesResult.Count;
        }

        // Сохраняем все изменения
        await _context.SaveChangesAsync(ct);

        return new LinkNewHampshireTollsResult(
            foundTolls,
            notFoundPlazas.Distinct().ToList(),
            updatedTollsCount);
    }
}

