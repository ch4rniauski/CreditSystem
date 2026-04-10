using System;
using System.Collections.Generic;

namespace CreditSystem.Database;

public partial class Payment
{
    public int Id { get; set; }

    public int? ContractId { get; set; }

    public DateOnly PaymentDate { get; set; }

    public DateOnly PlannedPaymentDate { get; set; }

    public string PaymentType { get; set; } = null!;

    public decimal PrincipalAmount { get; set; }

    public decimal InterestAmount { get; set; }

    public decimal AppliedAnnualRate { get; set; }

    public decimal? EarlyPenalty { get; set; }

    public decimal? LatePenalty { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal RemainingAfterPayment { get; set; }

    public virtual Contract? Contract { get; set; }
}
