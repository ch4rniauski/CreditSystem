namespace CreditSystem.Dtos;

public sealed record ContractDistributionReportRow(
    string GroupValue,
    int ContractsCount,
    decimal TotalAmount,
    decimal AverageAmount);
