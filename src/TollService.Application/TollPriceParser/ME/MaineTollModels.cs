namespace TollService.Application.TollPriceParser.ME;

public record MaineTollLocation(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] int Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("lat")] double Lat,
    [property: System.Text.Json.Serialization.JsonPropertyName("lng")] double Lng);

public record MaineTollsData(
    [property: System.Text.Json.Serialization.JsonPropertyName("total_locations")] int TotalLocations,
    [property: System.Text.Json.Serialization.JsonPropertyName("locations")] List<MaineTollLocation>? Locations);

public record MaineApiResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("sFee")] double SFee,
    [property: System.Text.Json.Serialization.JsonPropertyName("sFeeAnnual")] double? SFeeAnnual,
    [property: System.Text.Json.Serialization.JsonPropertyName("sFeeRoundtripEZ")] double SFeeRoundtripEZ,
    [property: System.Text.Json.Serialization.JsonPropertyName("sFeeError")] string? SFeeError);

public record MaineTollPriceData(
    [property: System.Text.Json.Serialization.JsonPropertyName("fromId")] int FromId,
    [property: System.Text.Json.Serialization.JsonPropertyName("fromName")] string FromName,
    [property: System.Text.Json.Serialization.JsonPropertyName("toId")] int ToId,
    [property: System.Text.Json.Serialization.JsonPropertyName("toName")] string ToName,
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] double Cash,
    [property: System.Text.Json.Serialization.JsonPropertyName("ezPass")] double EzPass,
    [property: System.Text.Json.Serialization.JsonPropertyName("error")] string? Error);

public record MaineTollPricesCollection(
    [property: System.Text.Json.Serialization.JsonPropertyName("prices")] List<MaineTollPriceData>? Prices,
    [property: System.Text.Json.Serialization.JsonPropertyName("fetchedAt")] DateTime? FetchedAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("totalCount")] int? TotalCount,
    [property: System.Text.Json.Serialization.JsonPropertyName("successCount")] int? SuccessCount,
    [property: System.Text.Json.Serialization.JsonPropertyName("errorCount")] int? ErrorCount);

/// <summary>
/// Обёртка над результатом FetchMaineTollPricesCommand,
/// которую возвращает API и которую удобно передавать в парсер как есть.
/// Пример структуры:
/// {
///   "prices": { ... MaineTollPricesCollection ... },
///   "totalCombinations": 506,
///   "successCount": 438,
///   "errorCount": 68,
///   "errors": [ ... ],
///   "error": null
/// }
/// </summary>
public record MaineFullPricesResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("prices")] MaineTollPricesCollection? Prices,
    [property: System.Text.Json.Serialization.JsonPropertyName("totalCombinations")] int? TotalCombinations,
    [property: System.Text.Json.Serialization.JsonPropertyName("successCount")] int? SuccessCount,
    [property: System.Text.Json.Serialization.JsonPropertyName("errorCount")] int? ErrorCount,
    [property: System.Text.Json.Serialization.JsonPropertyName("errors")] List<string>? Errors,
    [property: System.Text.Json.Serialization.JsonPropertyName("error")] string? Error);

public record MainePaymentMethods(
    [property: System.Text.Json.Serialization.JsonPropertyName("tag")] bool Tag,
    [property: System.Text.Json.Serialization.JsonPropertyName("plate")] bool Plate,
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] bool Cash,
    [property: System.Text.Json.Serialization.JsonPropertyName("card")] bool Card,
    [property: System.Text.Json.Serialization.JsonPropertyName("app")] bool App);

