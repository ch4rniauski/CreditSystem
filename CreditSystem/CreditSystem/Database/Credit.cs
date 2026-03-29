using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class Credit
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string ClientType { get; set; } = null!;

    public decimal MinAmount { get; set; }

    public decimal MaxAmount { get; set; }

    public int MinTermMonths { get; set; }

    public int MaxTermMonths { get; set; }

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual ICollection<CreditCurrency> CreditCurrencies { get; set; } = new List<CreditCurrency>();

    public virtual ICollection<InterestRate> InterestRates { get; set; } = new List<InterestRate>();

    public virtual ICollection<Penalty> Penalties { get; set; } = new List<Penalty>();
}
