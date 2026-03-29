using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class InterestRate
{
    public int Id { get; set; }

    public int? CreditId { get; set; }

    public int? CurrencyId { get; set; }

    public int TermFromMonths { get; set; }

    public int TermToMonths { get; set; }

    public string RateType { get; set; } = null!;

    public decimal? RateValue { get; set; }

    public decimal? AdditivePercent { get; set; }

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual Credit? Credit { get; set; }

    public virtual CreditCurrency? CreditCurrency { get; set; }

    public virtual Currency? Currency { get; set; }

    public virtual ICollection<RatesHistory> RatesHistories { get; set; } = new List<RatesHistory>();
}
