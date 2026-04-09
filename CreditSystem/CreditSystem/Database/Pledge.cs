namespace CreditSystem.Database;

public partial class Pledge
{
    public int Id { get; set; }

    public int? ContractId { get; set; }

    public string PropertyName { get; set; } = null!;

    public decimal EstimatedValue { get; set; }

    public DateOnly AssessmentDate { get; set; }

    public string PropertyType { get; set; } = null!;

    public virtual Contract? Contract { get; set; }
}
