namespace CreditSystem.Dtos;

public sealed record CreditHistoryEventDto(
    DateTime ChangeDate,
    string? Kind,
    string? CurrencyCode,
    decimal? OldValuePercent,
    decimal? NewValuePercent,
    int? OldTermFrom,
    int? OldTermTo,
    int? NewTermFrom,
    int? NewTermTo,
    string? PenaltyType);
