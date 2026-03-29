namespace CreditSystem.Database;

public partial class RefinanceRate
{
    public int Id { get; set; }

    public DateOnly ValidFromDate { get; set; }

    public DateOnly? ValidToDate { get; set; }

    public decimal RatePercent { get; set; }
}
