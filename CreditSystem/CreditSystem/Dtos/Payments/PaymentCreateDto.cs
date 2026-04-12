namespace CreditSystem.Dtos;

public sealed record PaymentCreateDto(DateOnly PaymentDate, decimal TotalAmount);
