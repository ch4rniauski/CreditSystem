namespace CreditSystem.Dtos;

public sealed record RefinanceRateRow(int Id, DateOnly ValidFromDate, DateOnly? ValidToDate, decimal RatePercent);
