using CreditSystem.Database;
using CreditSystem.Dtos;
using CreditSystem.LoanMath;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api")]
public sealed class ReportsController : CreditSystemControllerBase
{
    public ReportsController(CreditSystemContext db) : base(db)
    {
    }

    [HttpGet("reports/expected-payments")]
    public async Task<ActionResult<List<ExpectedPaymentsReportLineDto>>> ReportExpectedPayments(
        [FromQuery] int creditId,
        [FromQuery] int currencyId,
        [FromQuery] decimal contractAmount,
        [FromQuery] int termMonths,
        [FromQuery] DateOnly issueDate,
        CancellationToken ct)
    {
        var (ok, err, terms) = await TryResolveTerms(creditId, currencyId, termMonths, issueDate, ct);
        if (!ok)
        {
            return BadRequest(err);
        }

        var annual = AnnualFractionFromPercent(terms!.Value.Annual);
        var schedule = LoanScheduleEngine.BuildSchedule(contractAmount, annual, termMonths, issueDate);
        var list = schedule
            .Select(l => new ExpectedPaymentsReportLineDto(l.InstallmentIndex + 1, l.PlannedPaymentDate,
                l.ScheduledTotalPayment))
            .ToList();
        return Ok(list);
    }

    [HttpGet("contracts/{id:int}/reports/current-debt")]
    public async Task<ActionResult<CurrentDebtReportDto>> ReportCurrentDebt(int id, CancellationToken ct)
    {
        var contract = await Db.Contracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status == StDraft)
        {
            return BadRequest("Договор ещё не оформлен.");
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var (rateOk, rateError, annual) = await ResolveAnnualRateForContractAtDate(contract, today, ct);
        if (!rateOk)
        {
            return BadRequest(rateError);
        }

        var schedule = LoanScheduleEngine.BuildSchedule(contract.ContractAmount, annual!.Value, contract.TermMonths,
            contract.IssueDate);

        var lastPayDate = await Db.Payments
            .AsNoTracking()
            .Where(p => p.ContractId == id)
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => (DateOnly?)p.PaymentDate)
            .FirstOrDefaultAsync(ct);

        var accrualStart = lastPayDate is { } lp
            ? lp.AddDays(1)
            : LoanScheduleEngine.FirstAccrualDate(contract.IssueDate);
        var days = Math.Max(1, today.DayNumber - accrualStart.DayNumber + 1);
        var interestDue = decimal.Round(contract.RemainingPrincipal * annual.Value / 365m * days, 2,
            MidpointRounding.AwayFromZero);

        var paidCount = await Db.Payments
            .AsNoTracking()
            .CountAsync(p => p.ContractId == id && p.PaymentType == "monthly", ct);
        decimal principalDue;
        decimal latePen = 0;
        if (paidCount < schedule.Count)
        {
            var line = schedule[paidCount];
            principalDue = line.PrincipalPortion;
            if (today > line.PlannedPaymentDate)
            {
                var lateDays = today.DayNumber - line.PlannedPaymentDate.DayNumber;
                var z = contract.FixedLatePenaltyZ ?? 0;
                var baseAmt = contract.RemainingPrincipal + interestDue;
                latePen = decimal.Round(baseAmt * z / 100m * lateDays, 2, MidpointRounding.AwayFromZero);
            }
        }
        else
        {
            principalDue = contract.RemainingPrincipal;
            var lastPlannedDate = schedule.Count > 0 ? schedule[^1].PlannedPaymentDate : today;
            if (today > lastPlannedDate)
            {
                var lateDays = today.DayNumber - lastPlannedDate.DayNumber;
                var z = contract.FixedLatePenaltyZ ?? 0;
                var baseAmt = contract.RemainingPrincipal + interestDue;
                latePen = decimal.Round(baseAmt * z / 100m * lateDays, 2, MidpointRounding.AwayFromZero);
            }
        }

        return Ok(new CurrentDebtReportDto(latePen, interestDue, principalDue, contract.RemainingPrincipal));
    }

    [HttpGet("contracts/{id:int}/reports/payment-calendar")]
    public async Task<ActionResult<List<PaymentCalendarLineDto>>> ReportPaymentCalendar(int id,
        CancellationToken ct)
    {
        var contract = await Db.Contracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return NotFound();
        }

        var annual = AnnualFractionFromPercent(contract.FixedInterestRate ?? 0);
        var schedule = LoanScheduleEngine.BuildSchedule(contract.ContractAmount, annual, contract.TermMonths,
            contract.IssueDate);

        var payments = await Db.Payments
            .AsNoTracking()
            .Where(p => p.ContractId == id && p.PaymentType == "monthly")
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var list = new List<PaymentCalendarLineDto>();
        foreach (var row in schedule)
        {
            var match = payments
                .Where(p => p.PlannedPaymentDate == row.PlannedPaymentDate)
                .OrderByDescending(p => p.PaymentDate)
                .FirstOrDefault();

            string status;
            if (match is not null)
            {
                status = "выполнен";
            }
            else if (today > row.PlannedPaymentDate)
            {
                status = "просрочен";
            }
            else
            {
                status = "ожидается";
            }

            list.Add(new PaymentCalendarLineDto(row.PlannedPaymentDate, row.ScheduledTotalPayment,
                row.PrincipalPortion,
                row.InterestPortion, status));
        }

        return Ok(list);
    }

    [HttpGet("credit-products/{creditId:int}/reports/history")]
    public async Task<ActionResult<List<CreditHistoryEventDto>>> ReportCreditHistory(int creditId,
        CancellationToken ct)
    {
        var rateEvents = await Db.RatesHistories
            .AsNoTracking()
            .Join(Db.InterestRates.AsNoTracking(), h => h.InterestRateId, r => r.Id, (h, r) => new { h, r })
            .Where(x => x.r.CreditId == creditId)
            .Join(Db.Currencies.AsNoTracking(), x => x.r.CurrencyId, cu => cu.Id, (x, cu) => new { x.h, cu.Code })
            .Select(z => new CreditHistoryEventDto(
                z.h.ChangeDate,
                "interest_rate",
                z.Code,
                z.h.OldValue,
                z.h.NewValue,
                z.h.OldTermFrom,
                z.h.OldTermTo,
                z.h.NewTermFrom,
                z.h.NewTermTo,
                null))
            .ToListAsync(ct);

        var penEvents = await Db.PenaltiesHistories
            .AsNoTracking()
            .Join(Db.Penalties.AsNoTracking(), h => h.PenaltyId, p => p.Id, (h, p) => new { h, p })
            .Where(x => x.p.CreditId == creditId)
            .Select(z => new CreditHistoryEventDto(
                z.h.ChangeDate,
                "penalty",
                null,
                z.h.OldValue,
                z.h.NewValue,
                null,
                null,
                null,
                null,
                z.p.PenaltyType))
            .ToListAsync(ct);

        var all = rateEvents
            .Concat(penEvents)
            .OrderByDescending(e => e.ChangeDate)
            .ToList();
        return Ok(all);
    }
}
