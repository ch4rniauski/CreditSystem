namespace CreditSystem.Dtos;

public sealed record ContractDraftDto(
    string CreditProductName,
    string ClientDisplayName,
    string CurrencyCode,
    decimal ContractAmount,
    int TermMonths,
    DateOnly IssueDate,
    string Status,
    decimal RemainingPrincipal);
