namespace CreditSystem.Dtos;

public sealed record GuarantorRow(
    int InternalId,
    int ContractId,
    int PhysPersonClientId,
    string ContractCreditName,
    string GuarantorFullName,
    string PassportSeries,
    string PassportNumber);
