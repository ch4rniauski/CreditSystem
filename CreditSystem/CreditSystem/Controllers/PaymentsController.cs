using CreditSystem.Database;
using CreditSystem.Dtos;
using CreditSystem.LoanMath;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api/contracts/{id:int}/payments")]
public sealed class PaymentsController : CreditSystemControllerBase
{
    public PaymentsController(CreditSystemContext db) : base(db)
    {
    }

    [HttpGet("")]
    public async Task<ActionResult<List<PaymentRow>>> GetPayments(int id, CancellationToken ct)
    {
        if (!await Db.Contracts
                .AsNoTracking()
                .AnyAsync(c => c.Id == id, ct))
        {
            return NotFound();
        }

        var list = await Db.Payments
            .AsNoTracking()
            .Where(p => p.ContractId == id)
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.Id)
            .Select(p => new PaymentRow(
                p.Id,
                p.PaymentDate,
                p.PlannedPaymentDate,
                p.PaymentType,
                p.PrincipalAmount,
                p.InterestAmount,
                p.EarlyPenalty,
                p.LatePenalty,
                p.TotalAmount,
                p.RemainingAfterPayment,
                p.AppliedAnnualRate))
            .ToListAsync(ct);

        return Ok(list);
    }

    private static (DateOnly MonthStart, DateOnly MonthEnd) GetMonthBounds(DateOnly date)
    {
        var monthStart = new DateOnly(date.Year, date.Month, 1);
        var monthEnd = monthStart
            .AddMonths(1)
            .AddDays(-1);
        return (monthStart, monthEnd);
    }

    [HttpGet("minimum")]
    public async Task<ActionResult<PaymentMinimumDto>> GetMinimumPayment(int id, [FromQuery] DateOnly paymentDate,
        CancellationToken ct)
    {
        var contract = await Db.Contracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != StSigned)
        {
            return Conflict("Платежи только для «Оформлен».");
        }

        if (paymentDate < contract.IssueDate)
        {
            return BadRequest("Дата платежа не может быть раньше даты заключения договора.");
        }

        var paidCount = await Db.Payments
            .AsNoTracking()
            .CountAsync(p => p.ContractId == id && p.PaymentType == "monthly", ct);
        var finalPlannedPaymentDate = LoanScheduleEngine.PlannedPaymentDateForMonth(contract.IssueDate,
            contract.TermMonths - 1);
        var isWithinTerm = paymentDate <= finalPlannedPaymentDate;

        if (isWithinTerm)
        {
            var (monthStart, monthEnd) = GetMonthBounds(paymentDate);
            var alreadyPaidInMonth = await Db.Payments.AsNoTracking()
                .AnyAsync(p => p.ContractId == id &&
                               p.PaymentType == "monthly" &&
                               p.PaymentDate >= monthStart &&
                               p.PaymentDate <= monthEnd, ct);
            if (alreadyPaidInMonth)
            {
                return Conflict("В выбранном месяце уже есть платеж по этому договору.");
            }
        }

        var plannedPaymentDate = isWithinTerm
            ? LoanScheduleEngine.PlannedPaymentDateForMonth(contract.IssueDate, paidCount)
            : finalPlannedPaymentDate;
        var (rateOk, rateError, annualRate) =
            await ResolveAnnualRateForContractAtDate(contract, isWithinTerm ? plannedPaymentDate : paymentDate, ct);
        if (!rateOk)
        {
            return BadRequest(rateError);
        }

        var balance = contract.RemainingPrincipal;

        if (isWithinTerm)
        {
            var remainingMonths = contract.TermMonths - paidCount;
            var remainingIssueDate = plannedPaymentDate.AddMonths(-1);
            var schedule = LoanScheduleEngine.BuildSchedule(
                contract.RemainingPrincipal,
                annualRate!.Value,
                remainingMonths,
                remainingIssueDate);
            var line = schedule[0];
            var interest = line.InterestPortion;

            var previousMonthDate = paymentDate.AddMonths(-1);
            var (prevMonthStart, prevMonthEnd) = GetMonthBounds(previousMonthDate);
            var hasPaymentInPreviousMonth = await Db.Payments.AsNoTracking()
                .AnyAsync(p => p.ContractId == id &&
                               p.PaymentType == "monthly" &&
                               p.PaymentDate >= prevMonthStart &&
                               p.PaymentDate <= prevMonthEnd, ct);

            var lateDays = paymentDate > plannedPaymentDate
                ? paymentDate.DayNumber - plannedPaymentDate.DayNumber
                : 0;

            var firstAccrualDate = LoanScheduleEngine.FirstAccrualDate(contract.IssueDate);
            if (!hasPaymentInPreviousMonth && prevMonthEnd >= firstAccrualDate)
            {
                lateDays = Math.Max(0, paymentDate.DayNumber - plannedPaymentDate.DayNumber);
            }

            var z = contract.FixedLatePenaltyZ ?? 0;
            var latePenalty = lateDays > 0
                ? decimal.Round((balance + line.InterestPortion) * z / 100m * lateDays, 2,
                    MidpointRounding.AwayFromZero)
                : 0m;

            var requiredPrincipal = Math.Min(line.PrincipalPortion, balance);
            var minimum = decimal.Round(requiredPrincipal + interest + latePenalty, 2,
                MidpointRounding.AwayFromZero);
            var x = contract.FixedEarlyPenaltyX ?? 0;
            var maxExtraPrincipal = Math.Max(0m, balance - requiredPrincipal);
            var maxAllowedTotal = decimal.Round(minimum + maxExtraPrincipal + maxExtraPrincipal * x / 100m, 2,
                MidpointRounding.AwayFromZero);
            return Ok(new PaymentMinimumDto(minimum, interest, latePenalty, maxAllowedTotal));
        }

        var lastPaymentDate = await Db.Payments
            .AsNoTracking()
            .Where(p => p.ContractId == id)
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => (DateOnly?)p.PaymentDate)
            .FirstOrDefaultAsync(ct);

        var accrualStart = lastPaymentDate is { } lp
            ? lp.AddDays(1)
            : LoanScheduleEngine.FirstAccrualDate(contract.IssueDate);
        var interestDays = Math.Max(1, paymentDate.DayNumber - accrualStart.DayNumber + 1);
        var interestOverdue = decimal.Round(balance * annualRate!.Value / 365m * interestDays, 2,
            MidpointRounding.AwayFromZero);
        var dueDays = Math.Max(0, paymentDate.DayNumber - plannedPaymentDate.DayNumber);
        var latePenaltyOverdue = dueDays > 0
            ? decimal.Round((balance + interestOverdue) * (contract.FixedLatePenaltyZ ?? 0) / 100m * dueDays, 2,
                MidpointRounding.AwayFromZero)
            : 0m;
        var minimumOverdue = decimal.Round(interestOverdue + latePenaltyOverdue, 2, MidpointRounding.AwayFromZero);
        var maxAllowedOverdue = decimal.Round(minimumOverdue + balance, 2, MidpointRounding.AwayFromZero);
        return Ok(new PaymentMinimumDto(minimumOverdue, interestOverdue, latePenaltyOverdue, maxAllowedOverdue));
    }

    [HttpPost]
    public async Task<ActionResult<int>> PostPayment(int id, [FromBody] PaymentCreateDto dto,
        CancellationToken ct)
    {
        var contract = await Db.Contracts
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != StSigned)
        {
            return Conflict("Платежи только для «Оформлен».");
        }

        if (dto.PaymentDate < contract.IssueDate)
        {
            return BadRequest("Дата платежа не может быть раньше даты заключения договора.");
        }

        var paidCount = await Db.Payments
            .AsNoTracking()
            .CountAsync(p => p.ContractId == id && p.PaymentType == "monthly", ct);
        var finalPlannedPaymentDate = LoanScheduleEngine.PlannedPaymentDateForMonth(contract.IssueDate,
            contract.TermMonths - 1);
        var isWithinTerm = dto.PaymentDate <= finalPlannedPaymentDate;

        if (isWithinTerm)
        {
            var (monthStart, monthEnd) = GetMonthBounds(dto.PaymentDate);
            var alreadyPaidInMonth = await Db.Payments.AsNoTracking()
                .AnyAsync(p => p.ContractId == id &&
                               p.PaymentType == "monthly" &&
                               p.PaymentDate >= monthStart &&
                               p.PaymentDate <= monthEnd, ct);
            if (alreadyPaidInMonth)
            {
                return Conflict("В выбранном месяце уже есть платеж по этому договору.");
            }
        }

        var plannedPaymentDate = isWithinTerm
            ? LoanScheduleEngine.PlannedPaymentDateForMonth(contract.IssueDate, paidCount)
            : finalPlannedPaymentDate;
        var (rateOk, rateError, annualRate) =
            await ResolveAnnualRateForContractAtDate(contract, isWithinTerm ? plannedPaymentDate : dto.PaymentDate,
                ct);
        if (!rateOk)
        {
            return BadRequest(rateError);
        }

        var balance = contract.RemainingPrincipal;
        decimal interest;
        decimal latePenalty;
        decimal minimumRequired;
        decimal principalPaid;
        decimal earlyPenalty;
        var plannedDueDate = plannedPaymentDate;
        LoanScheduleEngine.ScheduleLine? line = null;

        if (isWithinTerm)
        {
            var remainingMonths = contract.TermMonths - paidCount;
            var remainingIssueDate = plannedPaymentDate.AddMonths(-1);
            var schedule = LoanScheduleEngine.BuildSchedule(
                contract.RemainingPrincipal,
                annualRate!.Value,
                remainingMonths,
                remainingIssueDate);
            line = schedule[0];
            interest = line.InterestPortion;

            var previousMonthDate = dto.PaymentDate.AddMonths(-1);
            var (prevMonthStart, prevMonthEnd) = GetMonthBounds(previousMonthDate);
            var hasPaymentInPreviousMonth = await Db.Payments.AsNoTracking()
                .AnyAsync(p => p.ContractId == id &&
                               p.PaymentType == "monthly" &&
                               p.PaymentDate >= prevMonthStart &&
                               p.PaymentDate <= prevMonthEnd, ct);

            var lateDays = dto.PaymentDate > plannedPaymentDate
                ? dto.PaymentDate.DayNumber - plannedPaymentDate.DayNumber
                : 0;

            var firstAccrualDate = LoanScheduleEngine.FirstAccrualDate(contract.IssueDate);
            if (!hasPaymentInPreviousMonth && prevMonthEnd >= firstAccrualDate)
            {
                lateDays = Math.Max(0, dto.PaymentDate.DayNumber - plannedPaymentDate.DayNumber);
            }

            latePenalty = lateDays > 0
                ? decimal.Round((balance + line.InterestPortion) * (contract.FixedLatePenaltyZ ?? 0) / 100m * lateDays,
                    2,
                    MidpointRounding.AwayFromZero)
                : 0m;

            var requiredPrincipal = Math.Min(line.PrincipalPortion, balance);
            minimumRequired = decimal.Round(requiredPrincipal + interest + latePenalty, 2,
                MidpointRounding.AwayFromZero);
            if (dto.TotalAmount < minimumRequired)
            {
                return BadRequest($"Минимальная сумма платежа: {minimumRequired:0.00}");
            }

            var x = contract.FixedEarlyPenaltyX ?? 0;
            var extraBudget = dto.TotalAmount - minimumRequired;
            var penaltyFactor = 1m + x / 100m;
            var requestedExtraPrincipal = extraBudget <= 0m
                ? 0m
                : decimal.Round(extraBudget / penaltyFactor, 2, MidpointRounding.AwayFromZero);
            var maxExtraPrincipal = Math.Max(0m, balance - requiredPrincipal);
            var extraPrincipal = Math.Min(requestedExtraPrincipal, maxExtraPrincipal);
            earlyPenalty = decimal.Round(extraPrincipal * x / 100m, 2, MidpointRounding.AwayFromZero);
            principalPaid = decimal.Round(requiredPrincipal + extraPrincipal, 2, MidpointRounding.AwayFromZero);

            decimal.Round(minimumRequired + maxExtraPrincipal + maxExtraPrincipal * x / 100m, 2,
                MidpointRounding.AwayFromZero);
        }
        else
        {
            var lastPaymentDate = await Db.Payments
                .AsNoTracking()
                .Where(p => p.ContractId == id)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => (DateOnly?)p.PaymentDate)
                .FirstOrDefaultAsync(ct);

            var accrualStart = lastPaymentDate is { } lp
                ? lp.AddDays(1)
                : LoanScheduleEngine.FirstAccrualDate(contract.IssueDate);
            var interestDays = Math.Max(1, dto.PaymentDate.DayNumber - accrualStart.DayNumber + 1);
            interest = decimal.Round(balance * annualRate!.Value / 365m * interestDays, 2,
                MidpointRounding.AwayFromZero);

            var dueDays = Math.Max(0, dto.PaymentDate.DayNumber - plannedDueDate.DayNumber);
            latePenalty = dueDays > 0
                ? decimal.Round((balance + interest) * (contract.FixedLatePenaltyZ ?? 0) / 100m * dueDays, 2,
                    MidpointRounding.AwayFromZero)
                : 0m;

            minimumRequired = decimal.Round(interest + latePenalty, 2, MidpointRounding.AwayFromZero);
            if (dto.TotalAmount < minimumRequired)
            {
                return BadRequest($"Минимальная сумма платежа: {minimumRequired:0.00}");
            }

            var maxAllowedTotal = decimal.Round(minimumRequired + balance, 2, MidpointRounding.AwayFromZero);
            if (dto.TotalAmount > maxAllowedTotal + LoanScheduleEngine.Epsilon)
            {
                return BadRequest($"Сумма слишком большая. Максимально допустимая для этой даты: {maxAllowedTotal:0.00}");
            }

            earlyPenalty = 0m;
            principalPaid = decimal.Round(dto.TotalAmount - minimumRequired, 2, MidpointRounding.AwayFromZero);
            if (principalPaid > balance)
            {
                principalPaid = balance;
            }
        }

        var remainingAfter = decimal.Round(balance - principalPaid, 2, MidpointRounding.AwayFromZero);
        if (remainingAfter < -LoanScheduleEngine.Epsilon)
        {
            return BadRequest("Переплата основного долга сверх остатка.");
        }

        var payment = new Payment
        {
            ContractId = id,
            PaymentDate = dto.PaymentDate,
            PlannedPaymentDate = line?.PlannedPaymentDate ?? plannedDueDate,
            PaymentType = "monthly",
            PrincipalAmount = principalPaid,
            InterestAmount = interest,
            AppliedAnnualRate = annualRate.Value,
            EarlyPenalty = earlyPenalty,
            LatePenalty = latePenalty,
            TotalAmount = dto.TotalAmount,
            RemainingAfterPayment = Math.Max(0, remainingAfter)
        };

        try
        {
            Db.Payments.Add(payment);
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        await Db.Entry(contract).ReloadAsync(ct);
        if (contract.RemainingPrincipal <= LoanScheduleEngine.Epsilon)
        {
            contract.Status = StDone;
            await Db.SaveChangesAsync(ct);
        }

        return Ok(payment.Id);
    }
}
