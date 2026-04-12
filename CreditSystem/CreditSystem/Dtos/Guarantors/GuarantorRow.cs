namespace CreditSystem.Dtos;

public sealed record GuarantorRow(
    int InternalId,
    string ContractCreditName,
    string GuarantorFullName,
    string PassportSeries,
    string PassportNumber);
