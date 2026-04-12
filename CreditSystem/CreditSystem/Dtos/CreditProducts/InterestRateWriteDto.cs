namespace CreditSystem.Dtos;

public sealed record InterestRateWriteDto(
    int CreditId,
    int CurrencyId,
    int TermFromMonths,
    int TermToMonths,
    string RateType,
    decimal? RateValue,
    decimal? AdditivePercent,
    DateOnly ValidFrom,
    DateOnly? ValidTo);
