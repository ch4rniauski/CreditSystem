using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class PaymentCalendar
{
    public int? ContractId { get; set; }

    public DateOnly? PlannedDate { get; set; }
}
