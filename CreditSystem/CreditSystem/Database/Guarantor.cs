namespace CreditSystem.Database;

public partial class Guarantor
{
    public int Id { get; set; }

    public int? ContractId { get; set; }

    public int? PhysPersonId { get; set; }

    public virtual Contract? Contract { get; set; }

    public virtual PhysPerson? PhysPerson { get; set; }
}
