namespace CreditSystem.Dtos;

public sealed record InterestRateRow(
    int Id,
    string CurrencyCode,
    int TermFromMonths,
    int TermToMonths,
    string RateType,
    decimal? RateValue,
    decimal? AdditivePercent,
    DateOnly ValidFrom,
    DateOnly? ValidTo);
