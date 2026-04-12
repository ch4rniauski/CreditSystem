namespace CreditSystem.Dtos;

public sealed record NearCompletionContractReportRow(
    int ContractId,
    string CreditName,
    string ClientDisplay,
    decimal ContractAmount,
    decimal RemainingPrincipal,
    decimal RepaidPercent,
    decimal RemainingPercent,
    DateOnly ExpectedCompletionDate);
