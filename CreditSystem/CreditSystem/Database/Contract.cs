namespace CreditSystem.Database;

public partial class Contract
{
    public int Id { get; set; }

    public int? ClientId { get; set; }

    public int? CreditId { get; set; }

    public int? CurrencyId { get; set; }

    public int? InterestRateId { get; set; }

    public decimal ContractAmount { get; set; }

    public int TermMonths { get; set; }

    public DateOnly IssueDate { get; set; }

    public string Status { get; set; } = null!;

    public string RateType { get; set; } = null!;

    public decimal? FixedInterestRate { get; set; }

    public decimal? FixedAdditivePercent { get; set; }

    public decimal? FixedEarlyPenaltyX { get; set; }

    public decimal? FixedLatePenaltyZ { get; set; }

    public decimal RemainingPrincipal { get; set; }

    public virtual Client? Client { get; set; }

    public virtual Credit? Credit { get; set; }

    public virtual Currency? Currency { get; set; }

    public virtual ICollection<Guarantor> Guarantors { get; set; } = new List<Guarantor>();

    public virtual InterestRate? InterestRate { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<Pledge> Pledges { get; set; } = new List<Pledge>();
}
