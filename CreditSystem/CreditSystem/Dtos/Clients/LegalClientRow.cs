namespace CreditSystem.Dtos;

public sealed record LegalClientRow(int ClientId, string Name, string OwnershipType, string LegalAddress, string? Phone);
