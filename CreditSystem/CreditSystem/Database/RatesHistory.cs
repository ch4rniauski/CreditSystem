using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class RatesHistory
{
    public int Id { get; set; }

    public int? InterestRateId { get; set; }

    public DateTime ChangeDate { get; set; }

    public decimal? OldValue { get; set; }

    public decimal NewValue { get; set; }

    public int? OldTermFrom { get; set; }

    public int? OldTermTo { get; set; }

    public int? NewTermFrom { get; set; }

    public int? NewTermTo { get; set; }

    public virtual InterestRate? InterestRate { get; set; }
}
