using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class Penalty
{
    public int Id { get; set; }

    public int? CreditId { get; set; }

    public string PenaltyType { get; set; } = null!;

    public decimal ValuePercent { get; set; }

    public DateOnly ValidFrom { get; set; }

    public virtual Credit? Credit { get; set; }

    public virtual ICollection<PenaltiesHistory> PenaltiesHistories { get; set; } = new List<PenaltiesHistory>();
}
