namespace CreditSystem.Dtos;

public sealed record PaymentCalendarLineDto(
    DateOnly PlannedDate,
    decimal ExpectedPayment,
    decimal ExpectedPrincipal,
    decimal ExpectedInterest,
    string Status);
