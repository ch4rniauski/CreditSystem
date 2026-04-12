namespace CreditSystem.Dtos;

public sealed record ExpectedPaymentsReportLineDto(int InstallmentNumber, DateOnly PlannedDate, decimal ExpectedPayment);
