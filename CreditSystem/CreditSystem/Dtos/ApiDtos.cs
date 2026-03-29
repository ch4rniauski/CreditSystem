namespace CreditSystem.Dtos;

public record CurrencyRow(int Id, string Code, string Name);

public record CurrencyWriteDto(string Code, string Name);

public record CreditProductRow(
    int Id,
    string Name,
    string? Description,
    string ClientType,
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermMonths,
    int MaxTermMonths);

public record CreditProductWriteDto(
    string Name,
    string? Description,
    string ClientType,
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermMonths,
    int MaxTermMonths);

public record CreditCurrencyRow(string CurrencyCode, decimal BaseInterestRate);

public record CreditCurrencyWriteDto(int CurrencyId, decimal BaseInterestRate);

public record InterestRateRow(
    int Id,
    string CurrencyCode,
    int TermFromMonths,
    int TermToMonths,
    string RateType,
    decimal? RateValue,
    decimal? AdditivePercent,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

public record InterestRateWriteDto(
    int CreditId,
    int CurrencyId,
    int TermFromMonths,
    int TermToMonths,
    string RateType,
    decimal? RateValue,
    decimal? AdditivePercent,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

public record PenaltyRow(int Id, string PenaltyType, decimal ValuePercent, DateOnly ValidFrom);

public record PenaltyWriteDto(int CreditId, string PenaltyType, decimal ValuePercent, DateOnly ValidFrom);

public record RefinanceRateRow(int Id, DateOnly ValidFromDate, DateOnly? ValidToDate, decimal RatePercent);

public record RefinanceRateWriteDto(DateOnly ValidFromDate, DateOnly? ValidToDate, decimal RatePercent);

public record LegalClientDto(string Name, string OwnershipType, string LegalAddress, string? Phone);

public record PhysicalClientDto(
    string FullName,
    string PassportSeries,
    string PassportNumber,
    string? ActualAddress,
    string? Phone);

public record ClientListItemDto(int InternalId, string Kind, string DisplayName, string? Phone);

public record LegalClientRow(int ClientId, string Name, string OwnershipType, string LegalAddress, string? Phone);

public record PhysicalClientRow(
    int ClientId,
    string FullName,
    string PassportSeries,
    string PassportNumber,
    string? ActualAddress,
    string? Phone);

public record ContractRow(
    int Id,
    string CreditName,
    string ClientDisplay,
    string ClientType,
    string CurrencyCode,
    decimal ContractAmount,
    int TermMonths,
    DateOnly IssueDate,
    string Status,
    decimal RemainingPrincipal);

public record ContractDraftDto(
    string CreditProductName,
    string ClientDisplayName,
    string CurrencyCode,
    decimal ContractAmount,
    int TermMonths,
    DateOnly IssueDate,
    string Status,
    decimal RemainingPrincipal);

public record ContractCreateDto(
    int ClientId,
    int CreditId,
    int CurrencyId,
    decimal ContractAmount,
    int TermMonths,
    DateOnly IssueDate);

public record ContractUpdateDto(
    int? ClientId,
    int? CreditId,
    int? CurrencyId,
    decimal? ContractAmount,
    int? TermMonths,
    DateOnly? IssueDate);

public record GuarantorRow(
    int InternalId,
    string ContractCreditName,
    string GuarantorFullName,
    string PassportSeries,
    string PassportNumber);

public record GuarantorCreateDto(int ContractId, int PhysPersonClientId);

public record PledgeRow(
    int InternalId,
    string PropertyName,
    decimal EstimatedValue,
    DateOnly AssessmentDate,
    string PropertyType);

public record PledgeWriteDto(
    string PropertyName,
    decimal EstimatedValue,
    DateOnly AssessmentDate,
    string PropertyType);

public record PaymentCreateDto(DateOnly PaymentDate, decimal TotalAmount);

public record ExpectedPaymentsReportLineDto(int InstallmentNumber, DateOnly PlannedDate, decimal ExpectedPayment);

public record CurrentDebtReportDto(
    decimal LatePenaltyAccrued,
    decimal InterestDue,
    decimal PrincipalDueThisPeriod,
    decimal RemainingPrincipal);

public record PaymentCalendarLineDto(
    DateOnly PlannedDate,
    decimal ExpectedPayment,
    decimal ExpectedPrincipal,
    decimal ExpectedInterest,
    string Status);

public record CreditHistoryEventDto(
    DateTime ChangeDate,
    string? Kind,
    string? CurrencyCode,
    decimal? OldValuePercent,
    decimal? NewValuePercent,
    int? OldTermFrom,
    int? OldTermTo,
    int? NewTermFrom,
    int? NewTermTo,
    string? PenaltyType);

public record ContractTermsSnapshotDto(
    decimal AnnualRateFrozen,
    string RateType,
    decimal EarlyPenaltyX,
    decimal LatePenaltyZ);

public record Report7QueryDto(
    int CreditId,
    int CurrencyId,
    decimal ContractAmount,
    int TermMonths,
    DateOnly IssueDate);
