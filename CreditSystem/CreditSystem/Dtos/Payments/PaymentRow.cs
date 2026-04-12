namespace CreditSystem.Dtos;

public sealed record PaymentRow(
    int Id,
    DateOnly PaymentDate,
    DateOnly PlannedPaymentDate,
    string PaymentType,
    decimal PrincipalAmount,
    decimal InterestAmount,
    decimal? EarlyPenalty,
    decimal? LatePenalty,
    decimal TotalAmount,
    decimal RemainingAfterPayment,
    decimal AppliedAnnualRate);
