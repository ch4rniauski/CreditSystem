namespace CreditSystem.Dtos;

public sealed record PaymentMinimumDto(decimal MinimumAmount, decimal InterestAmount, decimal LatePenaltyAmount, decimal MaxAllowedAmount);
