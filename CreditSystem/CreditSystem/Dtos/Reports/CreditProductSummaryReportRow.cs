namespace CreditSystem.Dtos;

public sealed record CreditProductSummaryReportRow(
    int CreditId,
    string CreditName,
    int ContractsCount,
    decimal TotalIssuedAmount,
    decimal AverageContractAmount,
    decimal AverageTermMonths);
