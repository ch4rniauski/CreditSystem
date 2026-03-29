namespace CreditSystem.Database;

public partial class LegalPerson
{
    public int ClientId { get; set; }

    public string Name { get; set; } = null!;

    public string OwnershipType { get; set; } = null!;

    public string LegalAddress { get; set; } = null!;

    public string? Phone { get; set; }

    public virtual Client Client { get; set; } = null!;
}
