namespace CreditSystem.Dtos;

public sealed record ContractTermsSnapshotDto(
    decimal AnnualRateFrozen,
    string RateType,
    decimal EarlyPenaltyX,
    decimal LatePenaltyZ);
