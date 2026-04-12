namespace CreditSystem.Dtos;

public sealed record ContractCollateralReportRow(
    int ContractId,
    string CreditName,
    string ClientDisplay,
    string CurrencyCode,
    decimal ContractAmount,
    decimal RemainingPrincipal,
    decimal PledgeValue,
    decimal CoverageCoefficient,
    bool HasGuarantors,
    int GuarantorCount);
