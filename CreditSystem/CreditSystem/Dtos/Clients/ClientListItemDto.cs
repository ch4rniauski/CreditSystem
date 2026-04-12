namespace CreditSystem.Dtos;

public sealed record ClientListItemDto(int InternalId, string Kind, string DisplayName, string? Phone);
