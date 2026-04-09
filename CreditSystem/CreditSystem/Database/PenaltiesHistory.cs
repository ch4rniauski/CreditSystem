namespace CreditSystem.Database;

public partial class PenaltiesHistory
{
    public int Id { get; set; }

    public int? PenaltyId { get; set; }

    public DateTime ChangeDate { get; set; }

    public decimal? OldValue { get; set; }

    public decimal NewValue { get; set; }

    public virtual Penalty? Penalty { get; set; }
}
