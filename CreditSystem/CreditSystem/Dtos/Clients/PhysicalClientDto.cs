namespace CreditSystem.Dtos;

public sealed record PhysicalClientDto(
    string FullName,
    string PassportSeries,
    string PassportNumber,
    string? ActualAddress,
    string? Phone);
