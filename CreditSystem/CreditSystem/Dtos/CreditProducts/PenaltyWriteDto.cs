namespace CreditSystem.Dtos;

public sealed record PenaltyWriteDto(int CreditId, string PenaltyType, decimal ValuePercent, DateOnly ValidFrom);
