namespace CreditSystem.Dtos;

public sealed record CreditProductRow(
    int Id,
    string Name,
    string? Description,
    string ClientType,
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermMonths,
    int MaxTermMonths);
