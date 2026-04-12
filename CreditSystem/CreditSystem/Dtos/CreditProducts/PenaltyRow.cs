namespace CreditSystem.Dtos;

public sealed record PenaltyRow(int Id, string PenaltyType, decimal ValuePercent, DateOnly ValidFrom);
