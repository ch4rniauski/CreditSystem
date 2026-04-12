namespace CreditSystem.Dtos;

public sealed record CurrentDebtReportDto(
    decimal LatePenaltyAccrued,
    decimal InterestDue,
    decimal PrincipalDueThisPeriod,
    decimal RemainingPrincipal);
