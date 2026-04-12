namespace CreditSystem.Dtos;

public sealed record PledgeRow(
    int InternalId,
    string PropertyName,
    decimal EstimatedValue,
    DateOnly AssessmentDate,
    string PropertyType,
    string? CurrencyCode);
