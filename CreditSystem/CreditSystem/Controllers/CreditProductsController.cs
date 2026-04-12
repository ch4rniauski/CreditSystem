using CreditSystem.Database;
using CreditSystem.Dtos;
using CreditSystem.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api")]
public sealed class CreditProductsController : CreditSystemControllerBase
{
    public CreditProductsController(CreditSystemContext db) : base(db)
    {
    }

    [HttpGet("credit-products")]
    public async Task<ActionResult<List<CreditProductRow>>> GetCreditProducts(CancellationToken ct)
    {
        var list = await Db.Credits.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CreditProductRow(c.Id, c.Name, c.Description, c.ClientType, c.MinAmount, c.MaxAmount,
                c.MinTermMonths, c.MaxTermMonths))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("credit-products")]
    public async Task<ActionResult<int>> CreateCreditProduct([FromBody] CreditProductWriteDto dto,
        CancellationToken ct)
    {
        var entity = new Credit
        {
            Name = dto.Name,
            Description = dto.Description,
            ClientType = dto.ClientType,
            MinAmount = dto.MinAmount,
            MaxAmount = dto.MaxAmount,
            MinTermMonths = dto.MinTermMonths,
            MaxTermMonths = dto.MaxTermMonths
        };
        Db.Credits.Add(entity);
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message is not null)
            {
                return BadRequest(new { error = message, section });
            }

            throw;
        }

        return Ok(entity.Id);
    }

    [HttpPut("credit-products/{id:int}")]
    public async Task<IActionResult> UpdateCreditProduct(int id, [FromBody] CreditProductWriteDto dto,
        CancellationToken ct)
    {
        var entity = await Db.Credits.FindAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Name = dto.Name;
        entity.Description = dto.Description;
        entity.ClientType = dto.ClientType;
        entity.MinAmount = dto.MinAmount;
        entity.MaxAmount = dto.MaxAmount;
        entity.MinTermMonths = dto.MinTermMonths;
        entity.MaxTermMonths = dto.MaxTermMonths;
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message is not null)
            {
                return BadRequest(new { error = message, section });
            }

            throw;
        }

        return NoContent();
    }

    [HttpDelete("credit-products/{id:int}")]
    public async Task<IActionResult> DeleteCreditProduct(int id, CancellationToken ct)
    {
        if (await Db.Contracts.AsNoTracking().AnyAsync(co => co.CreditId == id, ct))
        {
            return Conflict("Есть договоры по этому продукту.");
        }

        var entity = await Db.Credits.FindAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        Db.Credits.Remove(entity);
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict("Невозможно удалить: есть связанные данные.");
        }

        return NoContent();
    }

    [HttpGet("credit-products/{creditId:int}/currencies")]
    public async Task<ActionResult<List<CreditCurrencyRow>>> GetCreditCurrencies(int creditId,
        CancellationToken ct)
    {
        var list = await Db.CreditCurrencies.AsNoTracking()
            .Where(cc => cc.CreditId == creditId)
            .Join(Db.Currencies.AsNoTracking(), cc => cc.CurrencyId, cu => cu.Id,
                (cc, cu) => new CreditCurrencyRow(cu.Code))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("credit-products/{creditId:int}/currencies")]
    public async Task<IActionResult> AddCreditCurrency(int creditId, [FromBody] CreditCurrencyWriteDto dto,
        CancellationToken ct)
    {
        if (!await Db.Credits.AsNoTracking().AnyAsync(c => c.Id == creditId, ct))
        {
            return NotFound("Кредит не найден.");
        }

        if (!await Db.Currencies.AsNoTracking().AnyAsync(c => c.Id == dto.CurrencyId, ct))
        {
            return BadRequest("Валюта не найдена.");
        }

        var exists =
            await Db.CreditCurrencies.AsNoTracking().AnyAsync(cc =>
                cc.CreditId == creditId && cc.CurrencyId == dto.CurrencyId, ct);
        if (exists)
        {
            return Conflict(new { error = "Пара уже существует.", section = "currencies" });
        }

        Db.CreditCurrencies.Add(new CreditCurrency
        {
            CreditId = creditId,
            CurrencyId = dto.CurrencyId,
        });
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message is not null)
            {
                return BadRequest(new { error = message, section });
            }

            throw;
        }

        return NoContent();
    }

    [HttpPut("credit-products/{creditId:int}/currencies/{currencyId:int}")]
    public async Task<IActionResult> UpdateCreditCurrency(int creditId, int currencyId,
        [FromBody] CreditCurrencyWriteDto dto, CancellationToken ct)
    {
        if (dto.CurrencyId != currencyId)
        {
            return BadRequest();
        }

        var cc = await Db.CreditCurrencies.FindAsync([creditId, currencyId], ct);
        if (cc is null)
        {
            return NotFound();
        }

        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("credit-products/{creditId:int}/currencies/{currencyId:int}")]
    public async Task<IActionResult> DeleteCreditCurrency(int creditId, int currencyId, CancellationToken ct)
    {
        var cc = await Db.CreditCurrencies.FindAsync([creditId, currencyId], ct);
        if (cc is null)
        {
            return NotFound();
        }

        if (await Db.InterestRates.AsNoTracking().AnyAsync(ir =>
                ir.CreditId == creditId && ir.CurrencyId == currencyId, ct))
        {
            return Conflict("Сначала удалите ставки по этой паре.");
        }

        if (await Db.Contracts.AsNoTracking().AnyAsync(co =>
                co.CreditId == creditId && co.CurrencyId == currencyId, ct))
        {
            return Conflict("Пара используется в договоре.");
        }

        Db.CreditCurrencies.Remove(cc);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("credit-products/{creditId:int}/interest-rates")]
    public async Task<ActionResult<List<InterestRateRow>>> GetInterestRates(int creditId, CancellationToken ct)
    {
        var list = await Db.InterestRates.AsNoTracking()
            .Where(r => r.CreditId == creditId)
            .Join(Db.Currencies.AsNoTracking(), r => r.CurrencyId, cu => cu.Id,
                (r, cu) => new InterestRateRow(r.Id, cu.Code, r.TermFromMonths, r.TermToMonths, r.RateType,
                    r.RateValue,
                    r.AdditivePercent, r.ValidFrom, r.ValidTo))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("credit-products/interest-rates")]
    public async Task<ActionResult<int>> CreateInterestRate([FromBody] InterestRateWriteDto dto,
        CancellationToken ct)
    {
        if (!await Db.CreditCurrencies.AsNoTracking().AnyAsync(cc =>
                cc.CreditId == dto.CreditId && cc.CurrencyId == dto.CurrencyId, ct))
        {
            return BadRequest("Сначала добавьте валюту к продукту.");
        }

        decimal? rateValue = null;
        decimal? additivePercent = null;
        switch (dto.RateType)
        {
            case "fixed" when dto.RateValue is null:
                return BadRequest("Для фиксированной ставки требуется rateValue");
            case "fixed":
                rateValue = dto.RateValue;
                break;
            case "floating" when dto.AdditivePercent is null:
                return BadRequest("Для плавающей ставки требуется additivePercent");
            case "floating":
                additivePercent = dto.AdditivePercent;
                break;
            default:
                return BadRequest("Неверный тип ставки");
        }

        var duplicateExists = await Db.InterestRates.AsNoTracking().AnyAsync(r =>
                r.CreditId == dto.CreditId &&
                r.CurrencyId == dto.CurrencyId &&
                r.TermFromMonths == dto.TermFromMonths &&
                r.TermToMonths == dto.TermToMonths &&
                r.RateType == dto.RateType &&
                r.RateValue == rateValue &&
                r.AdditivePercent == additivePercent &&
                r.ValidFrom == dto.ValidFrom &&
                r.ValidTo == dto.ValidTo,
            ct);
        if (duplicateExists)
        {
            return Conflict(new { error = "Такая процентная ставка уже существует.", section = "interestRates" });
        }

        var entity = new InterestRate
        {
            CreditId = dto.CreditId,
            CurrencyId = dto.CurrencyId,
            TermFromMonths = dto.TermFromMonths,
            TermToMonths = dto.TermToMonths,
            RateType = dto.RateType,
            RateValue = rateValue,
            AdditivePercent = additivePercent,
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo
        };
        Db.InterestRates.Add(entity);
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message is not null)
            {
                return BadRequest(new { error = message, section });
            }

            return BadRequest(new { error = "Ошибка при сохранении процентной ставки.", section = "interestRates" });
        }

        return Ok(entity.Id);
    }

    [HttpPut("interest-rates/{id:int}")]
    public async Task<IActionResult> UpdateInterestRate(int id, [FromBody] InterestRateWriteDto dto,
        CancellationToken ct)
    {
        var entity = await Db.InterestRates.FindAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        decimal? rateValue = null;
        decimal? additivePercent = null;
        switch (dto.RateType)
        {
            case "fixed" when dto.RateValue is null:
                return BadRequest("Для фиксированной ставки требуется rateValue");
            case "fixed":
                rateValue = dto.RateValue;
                break;
            case "floating" when dto.AdditivePercent is null:
                return BadRequest("Для плавающей ставки требуется additivePercent");
            case "floating":
                additivePercent = dto.AdditivePercent;
                break;
            default:
                return BadRequest("Неверный тип ставки");
        }

        var oldVal = entity.RateType == "fixed" ? entity.RateValue : entity.AdditivePercent;
        var newVal = dto.RateType == "fixed" ? rateValue : additivePercent;

        Db.RatesHistories.Add(new RatesHistory
        {
            InterestRateId = entity.Id,
            OldValue = oldVal,
            NewValue = newVal ?? 0,
            OldTermFrom = entity.TermFromMonths,
            OldTermTo = entity.TermToMonths,
            NewTermFrom = dto.TermFromMonths,
            NewTermTo = dto.TermToMonths,
            ChangeDate = DateTime.UtcNow
        });

        entity.CreditId = dto.CreditId;
        entity.CurrencyId = dto.CurrencyId;
        entity.TermFromMonths = dto.TermFromMonths;
        entity.TermToMonths = dto.TermToMonths;
        entity.RateType = dto.RateType;
        entity.RateValue = rateValue;
        entity.AdditivePercent = additivePercent;
        entity.ValidFrom = dto.ValidFrom;
        entity.ValidTo = dto.ValidTo;

        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message is not null)
            {
                return BadRequest(new { error = message, section });
            }

            return BadRequest(new { error = "Ошибка при обновлении процентной ставки.", section = "interestRates" });
        }

        return NoContent();
    }

    [HttpDelete("interest-rates/{id:int}")]
    public async Task<IActionResult> DeleteInterestRate(int id, CancellationToken ct)
    {
        if (await Db.Contracts.AsNoTracking().AnyAsync(c => c.InterestRateId == id, ct))
        {
            return Conflict("Ставка используется в договоре.");
        }

        var entity = await Db.InterestRates.FindAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        Db.InterestRates.Remove(entity);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("credit-products/{creditId:int}/penalties")]
    public async Task<ActionResult<List<PenaltyRow>>> GetPenalties(int creditId, CancellationToken ct)
    {
        var list = await Db.Penalties.AsNoTracking()
            .Where(p => p.CreditId == creditId)
            .Select(p => new PenaltyRow(p.Id, p.PenaltyType, p.ValuePercent, p.ValidFrom))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("credit-products/penalties")]
    public async Task<ActionResult<int>> CreatePenalty([FromBody] PenaltyWriteDto dto, CancellationToken ct)
    {
        var entity = new Penalty
        {
            CreditId = dto.CreditId,
            PenaltyType = dto.PenaltyType,
            ValuePercent = dto.ValuePercent,
            ValidFrom = dto.ValidFrom
        };
        Db.Penalties.Add(entity);
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message is not null)
            {
                return BadRequest(new { error = message, section });
            }

            throw;
        }

        return Ok(entity.Id);
    }

    [HttpPut("penalties/{id:int}")]
    public async Task<IActionResult> UpdatePenalty(int id, [FromBody] PenaltyWriteDto dto, CancellationToken ct)
    {
        var entity = await Db.Penalties.FindAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        Db.PenaltiesHistories.Add(new PenaltiesHistory
        {
            PenaltyId = entity.Id,
            OldValue = entity.ValuePercent,
            NewValue = dto.ValuePercent,
            ChangeDate = DateTime.UtcNow
        });

        entity.CreditId = dto.CreditId;
        entity.PenaltyType = dto.PenaltyType;
        entity.ValuePercent = dto.ValuePercent;
        entity.ValidFrom = dto.ValidFrom;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("penalties/{id:int}")]
    public async Task<IActionResult> DeletePenalty(int id, CancellationToken ct)
    {
        var entity = await Db.Penalties.FindAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        Db.Penalties.Remove(entity);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }
}
