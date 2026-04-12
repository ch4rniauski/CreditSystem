namespace CreditSystem.Dtos;

public sealed record ClientCreditLoadReportRow(
    int ClientId,
    string ClientDisplay,
    string ClientType,
    int ActiveContractsCount,
    int CompletedContractsCount,
    decimal TotalIssuedAmount,
    decimal TotalRemainingPrincipal,
    decimal AverageTermMonths,
    decimal AverageInterestRate,
    int OverduePaymentsCount,
    int ScheduledPaymentsCount,
    decimal OverduePaymentShare);
