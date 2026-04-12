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

    [HttpGet("reports/contracts/distribution")]
    public async Task<ActionResult<List<ContractDistributionReportRow>>> ReportContractsDistribution(
        [FromQuery] string? groupBy,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var contracts = await LoadContractReportSources(ct);

        if (fromDate is { } startDate)
        {
            contracts = contracts.Where(c => c.IssueDate >= startDate).ToList();
        }

        if (toDate is { } endDate)
        {
            contracts = contracts.Where(c => c.IssueDate <= endDate).ToList();
        }

        var mode = NormalizeGroupBy(groupBy);
        var result = contracts
            .GroupBy(c => GetDistributionGroupValue(c, mode))
            .Select(g => new ContractDistributionReportRow(
                g.Key,
                g.Count(),
                decimal.Round(g.Sum(x => x.ContractAmount), 2, MidpointRounding.AwayFromZero),
                decimal.Round(g.Average(x => x.ContractAmount), 2, MidpointRounding.AwayFromZero)))
            .OrderByDescending(x => x.ContractsCount)
            .ThenBy(x => x.GroupValue)
            .ToList();

        return Ok(result);
    }

    [HttpGet("reports/client-credit-load")]
    public async Task<ActionResult<List<ClientCreditLoadReportRow>>> ReportClientCreditLoad(CancellationToken ct)
    {
        var contracts = await LoadContractReportSources(ct);
        var relevantContracts = contracts
            .Where(c => c.Status != StDraft)
            .ToList();

        var contractIds = relevantContracts
            .Select(c => c.ContractId)
            .ToList();

        var payments = await Db.Payments
            .AsNoTracking()
            .Where(p => p.ContractId != null && contractIds.Contains(p.ContractId.Value) && p.PaymentType == "monthly")
            .ToListAsync(ct);

        var result = new List<ClientCreditLoadReportRow>();
        foreach (var clientGroup in relevantContracts.GroupBy(c => new { c.ClientId, c.ClientDisplay, c.ClientType }))
        {
            var clientContracts = clientGroup.ToList();
            var activeContractsCount = clientContracts.Count(c => c.Status == StSigned);
            var completedContractsCount = clientContracts.Count(c => c.Status == StDone);
            var totalIssuedAmount = decimal.Round(clientContracts.Sum(c => c.ContractAmount), 2, MidpointRounding.AwayFromZero);
            var totalRemainingPrincipal = decimal.Round(clientContracts.Sum(c => c.RemainingPrincipal), 2, MidpointRounding.AwayFromZero);
            var averageTermMonths = decimal.Round(clientContracts.Average(c => (decimal)c.TermMonths), 2, MidpointRounding.AwayFromZero);
            var averageInterestRate = decimal.Round(clientContracts.Average(c => c.FixedInterestRate), 2, MidpointRounding.AwayFromZero);

            var overduePaymentsCount = 0;
            var scheduledPaymentsCount = 0;
            foreach (var contract in clientContracts)
            {
                var schedule = LoanScheduleEngine.BuildSchedule(
                    contract.ContractAmount,
                    AnnualFractionFromPercent(contract.FixedInterestRate),
                    contract.TermMonths,
                    contract.IssueDate);

                scheduledPaymentsCount += schedule.Count;
                overduePaymentsCount += payments.Count(p =>
                    p.ContractId == contract.ContractId &&
                    p.PaymentDate > p.PlannedPaymentDate);
            }

            var overduePaymentShare = scheduledPaymentsCount > 0
                ? decimal.Round((decimal)overduePaymentsCount * 100m / scheduledPaymentsCount, 2, MidpointRounding.AwayFromZero)
                : 0m;

            result.Add(new ClientCreditLoadReportRow(
                clientGroup.Key.ClientId,
                clientGroup.Key.ClientDisplay,
                ClientTypeLabel(clientGroup.Key.ClientType),
                activeContractsCount,
                completedContractsCount,
                totalIssuedAmount,
                totalRemainingPrincipal,
                averageTermMonths,
                averageInterestRate,
                overduePaymentsCount,
                scheduledPaymentsCount,
                overduePaymentShare));
        }

        return Ok(result
            .OrderByDescending(x => x.TotalRemainingPrincipal)
            .ThenBy(x => x.ClientDisplay)
            .ToList());
    }

    [HttpGet("reports/contracts/collateral")]
    public async Task<ActionResult<List<ContractCollateralReportRow>>> ReportContractCollateral(CancellationToken ct)
    {
        var contracts = await LoadContractReportSources(ct);
        var relevantContracts = contracts
            .Where(c => c.Status != StDraft)
            .ToList();

        var contractIds = relevantContracts
            .Select(c => c.ContractId)
            .ToList();

        var pledges = await Db.Pledges
            .AsNoTracking()
            .Where(p => p.ContractId != null && contractIds.Contains(p.ContractId.Value))
            .ToListAsync(ct);

        var guarantorCounts = await Db.Guarantors
            .AsNoTracking()
            .Where(g => g.ContractId != null && contractIds.Contains(g.ContractId.Value))
            .GroupBy(g => g.ContractId!.Value)
            .Select(g => new { ContractId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var guarantorMap = guarantorCounts.ToDictionary(x => x.ContractId, x => x.Count);

        var result = relevantContracts
            .Select(contract =>
            {
                var pledgeValue = pledges
                    .Where(p => p.ContractId == contract.ContractId && p.CurrencyId == contract.CurrencyId)
                    .Sum(p => p.EstimatedValue);
                var guarantorCount = guarantorMap.GetValueOrDefault(contract.ContractId);

                var coverageCoefficient = contract.RemainingPrincipal > 0
                    ? decimal.Round(pledgeValue / contract.RemainingPrincipal, 4, MidpointRounding.AwayFromZero)
                    : 0m;

                return new ContractCollateralReportRow(
                    contract.ContractId,
                    contract.CreditName,
                    contract.ClientDisplay,
                    contract.CurrencyCode,
                    decimal.Round(contract.ContractAmount, 2, MidpointRounding.AwayFromZero),
                    decimal.Round(contract.RemainingPrincipal, 2, MidpointRounding.AwayFromZero),
                    decimal.Round(pledgeValue, 2, MidpointRounding.AwayFromZero),
                    coverageCoefficient,
                    guarantorCount > 0,
                    guarantorCount);
            })
            .OrderByDescending(x => x.CoverageCoefficient)
            .ThenBy(x => x.ContractId)
            .ToList();

        return Ok(result);
    }

    [HttpGet("reports/active-clients")]
    public async Task<ActionResult<List<ActiveClientSummaryReportRow>>> ReportActiveClients(CancellationToken ct)
    {
        var contracts = await LoadContractReportSources(ct);
        var activeContracts = contracts
            .Where(c => c.Status == StSigned)
            .ToList();

        var result = activeContracts
            .GroupBy(c => new { c.ClientId, c.ClientDisplay })
            .Select(group =>
            {
                var groupContracts = group.ToList();
                var averageMonthlyPayment = groupContracts
                    .SelectMany(contract => LoanScheduleEngine.BuildSchedule(
                            contract.ContractAmount,
                            AnnualFractionFromPercent(contract.FixedInterestRate),
                            contract.TermMonths,
                            contract.IssueDate)
                        .Select(line => line.ScheduledTotalPayment))
                    .DefaultIfEmpty(0m)
                    .Average();

                return new ActiveClientSummaryReportRow(
                    group.Key.ClientId,
                    group.Key.ClientDisplay,
                    groupContracts.Count,
                    decimal.Round(groupContracts.Sum(c => c.ContractAmount), 2, MidpointRounding.AwayFromZero),
                    decimal.Round(groupContracts.Sum(c => c.RemainingPrincipal), 2, MidpointRounding.AwayFromZero),
                    decimal.Round(averageMonthlyPayment, 2, MidpointRounding.AwayFromZero));
            })
            .OrderByDescending(x => x.TotalRemainingPrincipal)
            .ThenBy(x => x.ClientDisplay)
            .ToList();

        return Ok(result);
    }

    [HttpGet("reports/credit-products/summary")]
    public async Task<ActionResult<List<CreditProductSummaryReportRow>>> ReportCreditProductSummary(CancellationToken ct)
    {
        var contracts = await LoadContractReportSources(ct);
        var relevantContracts = contracts
            .Where(c => c.Status != StDraft)
            .ToList();

        var result = relevantContracts
            .GroupBy(c => new { c.CreditId, c.CreditName })
            .Select(group =>
            {
                var groupContracts = group.ToList();
                return new CreditProductSummaryReportRow(
                    group.Key.CreditId,
                    group.Key.CreditName,
                    groupContracts.Count,
                    decimal.Round(groupContracts.Sum(c => c.ContractAmount), 2, MidpointRounding.AwayFromZero),
                    decimal.Round(groupContracts.Average(c => c.ContractAmount), 2, MidpointRounding.AwayFromZero),
                    decimal.Round(groupContracts.Average(c => (decimal)c.TermMonths), 2, MidpointRounding.AwayFromZero));
            })
            .OrderByDescending(x => x.ContractsCount)
            .ThenBy(x => x.CreditName)
            .ToList();

        return Ok(result);
    }

    [HttpGet("reports/contracts/nearing-completion")]
    public async Task<ActionResult<List<NearCompletionContractReportRow>>> ReportNearCompletionContracts(
        [FromQuery] decimal thresholdPercent,
        CancellationToken ct)
    {
        if (thresholdPercent <= 0 || thresholdPercent > 100)
        {
            return BadRequest("Порог должен быть больше 0 и не больше 100.");
        }

        var contracts = await LoadContractReportSources(ct);
        var relevantContracts = contracts
            .Where(c => c.Status != StDraft)
            .ToList();

        var result = relevantContracts
            .Where(contract => contract.ContractAmount > 0)
            .Select(contract =>
            {
                var remainingPercent = decimal.Round(contract.RemainingPrincipal * 100m / contract.ContractAmount, 2, MidpointRounding.AwayFromZero);
                var repaidPercent = decimal.Round(100m - remainingPercent, 2, MidpointRounding.AwayFromZero);
                var schedule = LoanScheduleEngine.BuildSchedule(
                    contract.ContractAmount,
                    AnnualFractionFromPercent(contract.FixedInterestRate),
                    contract.TermMonths,
                    contract.IssueDate);

                var expectedCompletionDate = schedule.Count > 0
                    ? schedule[^1].PlannedPaymentDate
                    : contract.IssueDate;

                return new NearCompletionContractReportRow(
                    contract.ContractId,
                    contract.CreditName,
                    contract.ClientDisplay,
                    decimal.Round(contract.ContractAmount, 2, MidpointRounding.AwayFromZero),
                    decimal.Round(contract.RemainingPrincipal, 2, MidpointRounding.AwayFromZero),
                    repaidPercent,
                    remainingPercent,
                    expectedCompletionDate);
            })
            .Where(x => x.RemainingPercent <= thresholdPercent)
            .OrderByDescending(x => x.RepaidPercent)
            .ThenBy(x => x.ContractId)
            .ToList();

        return Ok(result);
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

    private async Task<List<ContractReportSource>> LoadContractReportSources(CancellationToken ct)
    {
        return await Db.Contracts
            .AsNoTracking()
            .Join(Db.Credits.AsNoTracking(), c => c.CreditId, cr => cr.Id, (c, cr) => new { c, cr })
            .Join(Db.Currencies.AsNoTracking(), x => x.c.CurrencyId, cu => cu.Id, (x, cu) => new { x.c, x.cr, cu })
            .Join(Db.Clients.AsNoTracking(), x => x.c.ClientId, cl => cl.Id, (x, cl) => new { x.c, x.cr, x.cu, cl })
            .Select(x => new ContractReportSource(
                x.c.Id,
                x.c.ClientId ?? 0,
                x.cl.ClientType == "legal"
                    ? x.cl.LegalPerson!.Name
                    : x.cl.PhysPerson!.FullName,
                x.cl.ClientType,
                x.cr.Id,
                x.cr.Name,
                x.c.CurrencyId ?? 0,
                x.cu.Code,
                x.c.Status,
                x.c.ContractAmount,
                x.c.TermMonths,
                x.c.IssueDate,
                x.c.RemainingPrincipal,
                x.c.RateType,
                x.c.FixedInterestRate ?? 0m,
                x.cr.MinAmount,
                x.cr.MaxAmount,
                x.cr.MinTermMonths,
                x.cr.MaxTermMonths))
            .ToListAsync(ct);
    }

    private static string NormalizeGroupBy(string? groupBy)
    {
        return groupBy?.Trim().ToLowerInvariant() switch
        {
            "clienttype" => "clienttype",
            "currency" => "currency",
            "ratetype" => "ratetype",
            "amountrange" => "amountrange",
            "termrange" => "termrange",
            _ => "status"
        };
    }

    private static string GetDistributionGroupValue(ContractReportSource contract, string groupBy)
    {
        return groupBy switch
        {
            "clienttype" => ClientTypeLabel(contract.ClientType),
            "currency" => contract.CurrencyCode,
            "ratetype" => RateTypeLabel(contract.RateType),
            "amountrange" => $"{contract.CreditMinAmount:0.##} - {contract.CreditMaxAmount:0.##}",
            "termrange" => $"{contract.CreditMinTermMonths} - {contract.CreditMaxTermMonths} мес.",
            _ => contract.Status
        };
    }

    private static string ClientTypeLabel(string clientType)
    {
        return clientType == "legal" ? "Юридическое лицо" : "Физическое лицо";
    }

    private static string RateTypeLabel(string rateType)
    {
        return rateType == "fixed" ? "Фиксированная" : "Плавающая";
    }

    private sealed record ContractReportSource(
        int ContractId,
        int ClientId,
        string ClientDisplay,
        string ClientType,
        int CreditId,
        string CreditName,
        int CurrencyId,
        string CurrencyCode,
        string Status,
        decimal ContractAmount,
        int TermMonths,
        DateOnly IssueDate,
        decimal RemainingPrincipal,
        string RateType,
        decimal FixedInterestRate,
        decimal CreditMinAmount,
        decimal CreditMaxAmount,
        int CreditMinTermMonths,
        int CreditMaxTermMonths);
}
