namespace CreditSystem.Dtos;

public sealed record Report7QueryDto(
    int CreditId,
    int CurrencyId,
    decimal ContractAmount,
    int TermMonths,
    DateOnly IssueDate);
