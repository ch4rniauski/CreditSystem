using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class CurrentDebtReport
{
    public int? Id { get; set; }

    public decimal? RemainingPrincipal { get; set; }

    public decimal? InterestDue { get; set; }

    public decimal? Penalties { get; set; }
}
