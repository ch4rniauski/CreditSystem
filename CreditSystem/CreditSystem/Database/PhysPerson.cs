using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class PhysPerson
{
    public int ClientId { get; set; }

    public string FullName { get; set; } = null!;

    public string PassportSeries { get; set; } = null!;

    public string PassportNumber { get; set; } = null!;

    public string? ActualAddress { get; set; }

    public string? Phone { get; set; }

    public virtual Client Client { get; set; } = null!;

    public virtual ICollection<Guarantor> Guarantors { get; set; } = new List<Guarantor>();
}
