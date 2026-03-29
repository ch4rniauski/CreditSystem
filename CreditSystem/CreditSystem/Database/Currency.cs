using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class Currency
{
    public int Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual ICollection<CreditCurrency> CreditCurrencies { get; set; } = new List<CreditCurrency>();

    public virtual ICollection<InterestRate> InterestRates { get; set; } = new List<InterestRate>();
}
