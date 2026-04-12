namespace CreditSystem.Dtos;

public sealed record CreditProductWriteDto(
    string Name,
    string? Description,
    string ClientType,
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermMonths,
    int MaxTermMonths);
