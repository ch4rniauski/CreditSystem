namespace CreditSystem.Database;

public partial class CreditCurrency
{
    public int CreditId { get; set; }

    public int CurrencyId { get; set; }

    public virtual Credit Credit { get; set; } = null!;

    public virtual Currency Currency { get; set; } = null!;

    public virtual ICollection<InterestRate> InterestRates { get; set; } = new List<InterestRate>();
}
