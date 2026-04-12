namespace CreditSystem.Dtos;

public sealed record PhysicalClientRow(
    int ClientId,
    string FullName,
    string PassportSeries,
    string PassportNumber,
    string? ActualAddress,
    string? Phone);
