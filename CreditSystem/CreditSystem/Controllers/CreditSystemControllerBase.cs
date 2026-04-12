using CreditSystem.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CreditSystem.Controllers;

public abstract class CreditSystemControllerBase : ControllerBase
{
    protected const string StDraft = "Оформляется";
    protected const string StSigned = "Оформлен";
    protected const string StDone = "Завершён";

    protected readonly CreditSystemContext Db;

    protected CreditSystemControllerBase(CreditSystemContext db)
    {
        Db = db;
    }

    protected static decimal AnnualFractionFromPercent(decimal annualPercent)
    {
        return annualPercent / 100m;
    }

    private async Task<RefinanceRate?> ResolveRefinance(DateOnly issueDate, CancellationToken ct)
    {
        return await Db.RefinanceRates
            .AsNoTracking()
            .Where(r => r.ValidFromDate <= issueDate &&
                        (r.ValidToDate == null || r.ValidToDate >= issueDate))
            .OrderByDescending(r => r.ValidFromDate)
            .FirstOrDefaultAsync(ct);
    }

    protected async Task<(bool Ok, string? Error, decimal? Annual)> ResolveAnnualRateForContractAtDate(
        Contract contract,
        DateOnly date,
        CancellationToken ct)
    {
        if (contract.RateType == "fixed")
        {
            if (contract.FixedInterestRate is not { } fixedAnnual)
            {
                return (false, "Нет ставки по договору.", null);
            }

            return (true, null, AnnualFractionFromPercent(fixedAnnual));
        }

        if (contract.RateType != "floating")
        {
            return (false, "Некорректный тип ставки в договоре.", null);
        }

        if (contract.FixedAdditivePercent is not { } additive)
        {
            return (false, "Для плавающей ставки не задана добавка.", null);
        }

        var refinance = await ResolveRefinance(date, ct);
        if (refinance is null)
        {
            return (false, "Не найдена ставка рефинансирования НБРБ на дату расчета.", null);
        }

        return (true, null, AnnualFractionFromPercent(refinance.RatePercent + additive));
    }

    protected async Task<(decimal? early, decimal? late)> ResolvePenaltiesAtIssue(int creditId, DateOnly issueDate,
        CancellationToken ct)
    {
        var early = await Db.Penalties
            .AsNoTracking()
            .Where(p => p.CreditId == creditId && p.PenaltyType == "early_repayment" && p.ValidFrom <= issueDate)
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => (decimal?)p.ValuePercent)
            .FirstOrDefaultAsync(ct);

        var late = await Db.Penalties
            .AsNoTracking()
            .Where(p => p.CreditId == creditId && p.PenaltyType == "late_payment" && p.ValidFrom <= issueDate)
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => (decimal?)p.ValuePercent)
            .FirstOrDefaultAsync(ct);

        return (early ?? 0m, late ?? 0m);
    }

    protected async Task<(bool Ok, string? Error, (decimal Annual, int InterestRateId, string RateType, decimal? Additive)? Terms)>
        TryResolveTerms(int creditId, int currencyId, int termMonths, DateOnly issueDate, CancellationToken ct)
    {
        var credit = await Db.Credits
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == creditId, ct);
        if (credit is null)
        {
            return (false, "Кредитный продукт не найден.", null);
        }

        if (!await Db.CreditCurrencies
                .AsNoTracking()
                .AnyAsync(cc => cc.CreditId == creditId && cc.CurrencyId == currencyId, ct))
        {
            return (false, "Валюта не привязана к выбранному кредитному продукту.", null);
        }

        if (termMonths < credit.MinTermMonths || termMonths > credit.MaxTermMonths)
        {
            return (false, $"Срок должен быть в диапазоне {credit.MinTermMonths} - {credit.MaxTermMonths} месяцев.", null);
        }

        var matchedRates = await Db.InterestRates
            .AsNoTracking()
            .Where(r => r.CreditId == creditId &&
                        r.CurrencyId == currencyId &&
                        termMonths >= r.TermFromMonths && termMonths <= r.TermToMonths &&
                        r.ValidFrom <= issueDate &&
                        (r.ValidTo == null || r.ValidTo >= issueDate))
            .OrderByDescending(r => r.ValidFrom)
            .Take(2)
            .ToListAsync(ct);

        if (matchedRates.Count > 1)
        {
            return (false,
                "Найдено несколько подходящих процентных ставок для выбранных условий. Устраните пересечение диапазонов ставок.",
                null);
        }

        var row = matchedRates.FirstOrDefault();
        if (row is null)
        {
            var anyTermForCurrency = await Db.InterestRates
                .AsNoTracking()
                .AnyAsync(r =>
                    r.CreditId == creditId && r.CurrencyId == currencyId &&
                    termMonths >= r.TermFromMonths && termMonths <= r.TermToMonths,
                    ct);
            if (!anyTermForCurrency)
            {
                return (false, "Нет процентной ставки для указанного срока в этой валюте.", null);
            }

            var anyValidDate = await Db.InterestRates
                .AsNoTracking()
                .AnyAsync(r =>
                    r.CreditId == creditId && r.CurrencyId == currencyId &&
                    r.ValidFrom <= issueDate && (r.ValidTo == null || r.ValidTo >= issueDate),
                    ct);
            if (!anyValidDate)
            {
                return (false, "Нет действующей процентной ставки на указанную дату.", null);
            }

            return (false, "Не найдена процентная ставка для указных условий.", null);
        }

        if (row.RateType == "fixed")
        {
            if (row.RateValue is not { } rv)
            {
                return (false, "Для фиксированной ставки задайте rate_value.", null);
            }

            return (true, null, (rv, row.Id, row.RateType, null));
        }

        var refr = await ResolveRefinance(issueDate, ct);
        if (refr is null)
        {
            return (false, "Не найдена ставка рефинансирования НБРБ на дату договора.", null);
        }

        if (row.AdditivePercent is not { } add)
        {
            return (false, "Для плавающей ставки задайте additive_percent.", null);
        }

        var annual = refr.RatePercent + add;
        return (true, null, (annual, row.Id, row.RateType, add));
    }

    protected static string FmtDb(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg ? pg.MessageText : ex.Message;
    }
}
