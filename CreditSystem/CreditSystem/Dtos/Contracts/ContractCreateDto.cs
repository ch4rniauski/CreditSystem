namespace CreditSystem.Dtos;

public sealed record ContractCreateDto(
    int ClientId,
    int CreditId,
    int CurrencyId,
    decimal ContractAmount,
    int TermMonths,
    DateOnly IssueDate);
