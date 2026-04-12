namespace CreditSystem.Dtos;

public sealed record ContractRow(
    int Id,
    int ClientId,
    string CreditName,
    string ClientDisplay,
    string ClientType,
    string CurrencyCode,
    decimal ContractAmount,
    int TermMonths,
    DateOnly IssueDate,
    string Status,
    decimal RemainingPrincipal,
    string? ClientPassportSeries,
    string? ClientPassportNumber);
