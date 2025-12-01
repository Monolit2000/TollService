using System.Linq;
using System.Text.Json;
using MediatR;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.TollPriceParser.ME;

public record FetchMaineTollPricesCommand(string TollsJsonContent)
    : IRequest<FetchMaineTollPricesResult>;

public record FetchMaineTollPricesResult(
    MaineTollPricesCollection Prices,
    int TotalCombinations,
    int SuccessCount,
    int ErrorCount,
    List<string> Errors,
    string? Error = null);

public class FetchMaineTollPricesCommandHandler(
    IHttpClientFactory _httpClientFactory) : IRequestHandler<FetchMaineTollPricesCommand, FetchMaineTollPricesResult>
{
    private const string ApiBaseUrl = "https://www.maineturnpike.com/api/maps/Calculate";
    private const int VehicleType = 5; // Passenger car

    public async Task<FetchMaineTollPricesResult> Handle(FetchMaineTollPricesCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        var prices = new List<MaineTollPriceData>();
        int successCount = 0;
        int errorCount = 0;

        if (string.IsNullOrWhiteSpace(request.TollsJsonContent))
        {
            return new FetchMaineTollPricesResult(
                new MaineTollPricesCollection(new(), DateTime.UtcNow, 0, 0, 0),
                0, 0, 0, new(),
                "TollsJsonContent не может быть пустым");
        }

        MaineTollsData? data;
        try
        {
            data = JsonSerializer.Deserialize<MaineTollsData>(request.TollsJsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException jsonEx)
        {
            return new FetchMaineTollPricesResult(
                new MaineTollPricesCollection(new(), DateTime.UtcNow, 0, 0, 0),
                0, 0, 0, new(),
                $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.Locations == null || data.Locations.Count == 0)
        {
            return new FetchMaineTollPricesResult(
                new MaineTollPricesCollection(new(), DateTime.UtcNow, 0, 0, 0),
                0, 0, 0, new(),
                "Локации не найдены в JSON файле");
        }

        // Создаем HTTP клиент
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Проходим по всем комбинациям локаций
        var locations = data.Locations;
        var totalCombinations = locations.Count * (locations.Count - 1); // исключаем одинаковые
        var processedCount = 0;

        foreach (var fromLocation in locations)
        {
            foreach (var toLocation in locations)
            {
                // Пропускаем, если это одна и та же локация
                if (fromLocation.Id == toLocation.Id)
                    continue;

                processedCount++;
                
                try
                {
                    // Небольшая задержка между запросами, чтобы не перегружать API
                    if (processedCount > 1)
                    {
                        await Task.Delay(100, ct); // 100ms задержка
                    }

                    // Делаем запрос к API
                    var apiUrl = $"{ApiBaseUrl}/{fromLocation.Id}/{toLocation.Id}/{VehicleType}";
                    var response = await httpClient.GetStringAsync(apiUrl, ct);
                    
                    MaineApiResponse? apiResponse;
                    try
                    {
                        apiResponse = JsonSerializer.Deserialize<MaineApiResponse>(response, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch (JsonException jsonEx)
                    {
                        var errorMsg = $"Ошибка парсинга ответа API для {fromLocation.Id}->{toLocation.Id}: {jsonEx.Message}";
                        errors.Add(errorMsg);
                        prices.Add(new MaineTollPriceData(
                            fromLocation.Id,
                            fromLocation.Name,
                            toLocation.Id,
                            toLocation.Name,
                            0,
                            0,
                            errorMsg));
                        errorCount++;
                        continue;
                    }

                    if (apiResponse == null)
                    {
                        var errorMsg = $"Пустой ответ API для {fromLocation.Id}->{toLocation.Id}";
                        errors.Add(errorMsg);
                        prices.Add(new MaineTollPriceData(
                            fromLocation.Id,
                            fromLocation.Name,
                            toLocation.Id,
                            toLocation.Name,
                            0,
                            0,
                            errorMsg));
                        errorCount++;
                        continue;
                    }

                    // Проверяем наличие ошибки в ответе
                    if (!string.IsNullOrWhiteSpace(apiResponse.SFeeError))
                    {
                        var errorMsg = $"Ошибка API для {fromLocation.Id}->{toLocation.Id}: {apiResponse.SFeeError}";
                        errors.Add(errorMsg);
                        prices.Add(new MaineTollPriceData(
                            fromLocation.Id,
                            fromLocation.Name,
                            toLocation.Id,
                            toLocation.Name,
                            0,
                            0,
                            errorMsg));
                        errorCount++;
                        continue;
                    }

                    // Сохраняем успешные данные
                    prices.Add(new MaineTollPriceData(
                        fromLocation.Id,
                        fromLocation.Name,
                        toLocation.Id,
                        toLocation.Name,
                        apiResponse.SFee,
                        apiResponse.SFeeRoundtripEZ,
                        null));
                    successCount++;

                    // Логируем прогресс каждые 50 запросов
                    if ((processedCount % 50) == 0)
                    {
                        Console.WriteLine($"Обработано {processedCount}/{totalCombinations} комбинаций...");
                    }
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Ошибка при обработке {fromLocation.Id}->{toLocation.Id}: {ex.Message}";
                    errors.Add(errorMsg);
                    prices.Add(new MaineTollPriceData(
                        fromLocation.Id,
                        fromLocation.Name,
                        toLocation.Id,
                        toLocation.Name,
                        0,
                        0,
                        errorMsg));
                    errorCount++;
                }
            }
        }

        // Возвращаем результаты в response
        var pricesCollection = new MaineTollPricesCollection(
            prices,
            DateTime.UtcNow,
            totalCombinations,
            successCount,
            errorCount);

        return new FetchMaineTollPricesResult(
            pricesCollection,
            totalCombinations,
            successCount,
            errorCount,
            errors);
    }
}

