namespace CreditSystem.Dtos;

public sealed record RefinanceRateWriteDto(DateOnly ValidFromDate, DateOnly? ValidToDate, decimal RatePercent);
