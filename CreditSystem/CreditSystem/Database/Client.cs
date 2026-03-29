using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class Client
{
    public int Id { get; set; }

    public string ClientType { get; set; } = null!;

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual LegalPerson? LegalPerson { get; set; }

    public virtual PhysPerson? PhysPerson { get; set; }
}
