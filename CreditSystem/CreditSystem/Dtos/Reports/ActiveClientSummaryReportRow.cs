namespace CreditSystem.Dtos;

public sealed record ActiveClientSummaryReportRow(
    int ClientId,
    string ClientDisplay,
    int ActiveContractsCount,
    decimal TotalIssuedAmount,
    decimal TotalRemainingPrincipal,
    decimal AverageMonthlyPayment);
