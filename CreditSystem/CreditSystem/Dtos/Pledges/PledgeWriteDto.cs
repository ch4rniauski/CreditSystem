namespace CreditSystem.Dtos;

public sealed record PledgeWriteDto(
    string PropertyName,
    decimal EstimatedValue,
    DateOnly AssessmentDate,
    string PropertyType,
    int CurrencyId);
