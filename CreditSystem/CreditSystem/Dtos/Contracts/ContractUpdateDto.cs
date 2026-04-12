namespace CreditSystem.Dtos;

public sealed record ContractUpdateDto(
    int? ClientId,
    int? CreditId,
    int? CurrencyId,
    decimal? ContractAmount,
    int? TermMonths,
    DateOnly? IssueDate);
