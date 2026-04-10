using CreditSystem.Database;
using CreditSystem.Dtos;
using CreditSystem.Helpers;
using CreditSystem.LoanMath;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.RegularExpressions;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api")]
public class CreditSystemController(CreditSystemContext db) : ControllerBase
{
    private const string StDraft = "Оформляется";
    private const string StSigned = "Оформлен";
    private const string StDone = "Завершён";
    private static readonly Regex CurrencyCodeRegex = new("^[A-Z]{3}$", RegexOptions.Compiled);

    private static string ClientDisplay(Client c) =>
        c.ClientType == "legal"
            ? c.LegalPerson?.Name ?? "—"
            : c.PhysPerson?.FullName ?? "—";

    private IReadOnlyList<LoanScheduleEngine.ScheduleLine> ScheduleFor(Contract c)
    {
        var rate = c.FixedInterestRate ?? 0;
        return LoanScheduleEngine.BuildSchedule(
            c.ContractAmount,
            rate,
            c.TermMonths,
            c.IssueDate);
    }

    private static bool TryNormalizeCurrencyCode(string code, out string normalized)
    {
        normalized = code.Trim().ToUpperInvariant();
        return CurrencyCodeRegex.IsMatch(normalized);
    }

    #region Currencies

    [HttpGet("currencies")]
    public async Task<ActionResult<List<CurrencyRow>>> GetCurrencies(CancellationToken ct)
    {
        var list = await db.Currencies.AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CurrencyRow(c.Id, c.Code, c.Name))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("currencies")]
    public async Task<ActionResult<int>> CreateCurrency([FromBody] CurrencyWriteDto dto, CancellationToken ct)
    {
        if (!TryNormalizeCurrencyCode(dto.Code, out var normalizedCode))
        {
            return BadRequest("Код валюты должен состоять только из букв (3 символа).");
        }

        var e = new Currency { Code = normalizedCode, Name = dto.Name };
        db.Currencies.Add(e);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is NpgsqlException npg && npg.SqlState == "23505")
            {
                return Conflict("Валюта с таким кодом уже существует.");
            }
            return Conflict(ex.InnerException?.Message ?? ex.Message);
        }

        return Ok(e.Id);
    }

    [HttpPut("currencies/{id:int}")]
    public async Task<IActionResult> UpdateCurrency(int id, [FromBody] CurrencyWriteDto dto, CancellationToken ct)
    {
        if (!TryNormalizeCurrencyCode(dto.Code, out var normalizedCode))
        {
            return BadRequest("Код валюты должен состоять только из букв (3 символа).");
        }

        var e = await db.Currencies.FindAsync([id], ct);
        if (e == null)
        {
            return NotFound();
        }
        e.Code = normalizedCode;
        e.Name = dto.Name;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is NpgsqlException npg && npg.SqlState == "23505")
            {
                return Conflict("Валюта с таким кодом уже существует.");
            }
            return Conflict(ex.InnerException?.Message ?? ex.Message);
        }

        return NoContent();
    }

    [HttpDelete("currencies/{id:int}")]
    public async Task<IActionResult> DeleteCurrency(int id, CancellationToken ct)
    {
        var used = await db.Contracts.AsNoTracking().AnyAsync(c => c.CurrencyId == id, ct)
                   || await db.CreditCurrencies.AsNoTracking().AnyAsync(cc => cc.CurrencyId == id, ct);
        if (used) return Conflict("Валюта используется.");
        var e = await db.Currencies.FindAsync([id], ct);
        if (e == null) return NotFound();
        db.Currencies.Remove(e);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    #endregion

    #region Refinance

    [HttpGet("refinance-rates")]
    public async Task<ActionResult<List<RefinanceRateRow>>> GetRefinanceRates(CancellationToken ct)
    {
        var list = await db.RefinanceRates.AsNoTracking()
            .OrderByDescending(r => r.ValidFromDate)
            .Select(r => new RefinanceRateRow(r.Id, r.ValidFromDate, r.ValidToDate, r.RatePercent))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("refinance-rates")]
    public async Task<ActionResult<int>> CreateRefinanceRate([FromBody] RefinanceRateWriteDto dto,
        CancellationToken ct)
    {
        var e = new RefinanceRate
        {
            ValidFromDate = dto.ValidFromDate,
            ValidToDate = dto.ValidToDate,
            RatePercent = dto.RatePercent
        };
        db.RefinanceRates.Add(e);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message != null)
                return BadRequest(new { error = message, section });
            return BadRequest(new { error = "Ошибка при создании ставки рефинансирования.", section = "refinance" });
        }

        return Ok(e.Id);
    }

    #endregion

    #region Credit products

    [HttpGet("credit-products")]
    public async Task<ActionResult<List<CreditProductRow>>> GetCreditProducts(CancellationToken ct)
    {
        var list = await db.Credits.AsNoTracking()
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
        var c = new Credit
        {
            Name = dto.Name,
            Description = dto.Description,
            ClientType = dto.ClientType,
            MinAmount = dto.MinAmount,
            MaxAmount = dto.MaxAmount,
            MinTermMonths = dto.MinTermMonths,
            MaxTermMonths = dto.MaxTermMonths
        };
        db.Credits.Add(c);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message != null)
                return BadRequest(new { error = message, section });
            throw;
        }
        return Ok(c.Id);
    }

    [HttpPut("credit-products/{id:int}")]
    public async Task<IActionResult> UpdateCreditProduct(int id, [FromBody] CreditProductWriteDto dto,
        CancellationToken ct)
    {
        var c = await db.Credits.FindAsync([id], ct);
        if (c == null) return NotFound();
        c.Name = dto.Name;
        c.Description = dto.Description;
        c.ClientType = dto.ClientType;
        c.MinAmount = dto.MinAmount;
        c.MaxAmount = dto.MaxAmount;
        c.MinTermMonths = dto.MinTermMonths;
        c.MaxTermMonths = dto.MaxTermMonths;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message != null)
                return BadRequest(new { error = message, section });
            throw;
        }
        return NoContent();
    }

    [HttpDelete("credit-products/{id:int}")]
    public async Task<IActionResult> DeleteCreditProduct(int id, CancellationToken ct)
    {
        if (await db.Contracts.AsNoTracking().AnyAsync(co => co.CreditId == id, ct))
            return Conflict("Есть договоры по этому продукту.");
        var c = await db.Credits.FindAsync([id], ct);
        if (c == null) return NotFound();
        db.Credits.Remove(c);
        try
        {
            await db.SaveChangesAsync(ct);
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
        var list = await db.CreditCurrencies.AsNoTracking()
            .Where(cc => cc.CreditId == creditId)
            .Join(db.Currencies.AsNoTracking(), cc => cc.CurrencyId, cu => cu.Id,
                (cc, cu) => new CreditCurrencyRow(cu.Code))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("credit-products/{creditId:int}/currencies")]
    public async Task<IActionResult> AddCreditCurrency(int creditId, [FromBody] CreditCurrencyWriteDto dto,
        CancellationToken ct)
    {
        if (!await db.Credits.AsNoTracking().AnyAsync(c => c.Id == creditId, ct)) return NotFound("Кредит не найден.");
        if (!await db.Currencies.AsNoTracking().AnyAsync(c => c.Id == dto.CurrencyId, ct))
            return BadRequest("Валюта не найдена.");
        var exists =
            await db.CreditCurrencies.AsNoTracking().AnyAsync(cc =>
                cc.CreditId == creditId && cc.CurrencyId == dto.CurrencyId, ct);
        if (exists) return Conflict(new { error = "Пара уже существует.", section = "currencies" });
        db.CreditCurrencies.Add(new CreditCurrency
        {
            CreditId = creditId,
            CurrencyId = dto.CurrencyId,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message != null)
                return BadRequest(new { error = message, section });
            throw;
        }
        return NoContent();
    }

    [HttpPut("credit-products/{creditId:int}/currencies/{currencyId:int}")]
    public async Task<IActionResult> UpdateCreditCurrency(int creditId, int currencyId,
        [FromBody] CreditCurrencyWriteDto dto, CancellationToken ct)
    {
        if (dto.CurrencyId != currencyId) return BadRequest();
        var cc = await db.CreditCurrencies.FindAsync([creditId, currencyId], ct);
        if (cc == null) return NotFound();
        // Базовая ставка удалена, здесь больше нет полей для обновления.
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("credit-products/{creditId:int}/currencies/{currencyId:int}")]
    public async Task<IActionResult> DeleteCreditCurrency(int creditId, int currencyId, CancellationToken ct)
    {
        var cc = await db.CreditCurrencies.FindAsync([creditId, currencyId], ct);
        if (cc == null) return NotFound();
        if (await db.InterestRates.AsNoTracking().AnyAsync(ir =>
                ir.CreditId == creditId && ir.CurrencyId == currencyId, ct))
            return Conflict("Сначала удалите ставки по этой паре.");
        if (await db.Contracts.AsNoTracking().AnyAsync(co =>
                co.CreditId == creditId && co.CurrencyId == currencyId, ct))
            return Conflict("Пара используется в договоре.");
        db.CreditCurrencies.Remove(cc);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("credit-products/{creditId:int}/interest-rates")]
    public async Task<ActionResult<List<InterestRateRow>>> GetInterestRates(int creditId, CancellationToken ct)
    {
        var list = await db.InterestRates.AsNoTracking()
            .Where(r => r.CreditId == creditId)
            .Join(db.Currencies.AsNoTracking(), r => r.CurrencyId, cu => cu.Id,
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
        if (!await db.CreditCurrencies.AsNoTracking().AnyAsync(cc =>
                cc.CreditId == dto.CreditId && cc.CurrencyId == dto.CurrencyId, ct))
            return BadRequest("Сначала добавьте валюту к продукту.");

        decimal? rateValue = null;
        decimal? additivePercent = null;
        if (dto.RateType == "fixed")
        {
            if (dto.RateValue == null) return BadRequest("Для фиксированной ставки требуется rateValue");
            rateValue = dto.RateValue;
        }
        else if (dto.RateType == "floating")
        {
            if (dto.AdditivePercent == null) return BadRequest("Для плавающей ставки требуется additivePercent");
            additivePercent = dto.AdditivePercent;
        }
        else
        {
            return BadRequest("Неверный тип ставки");
        }

        var duplicateExists = await db.InterestRates.AsNoTracking().AnyAsync(r =>
            r.CreditId == dto.CreditId
            && r.CurrencyId == dto.CurrencyId
            && r.TermFromMonths == dto.TermFromMonths
            && r.TermToMonths == dto.TermToMonths
            && r.RateType == dto.RateType
            && r.RateValue == rateValue
            && r.AdditivePercent == additivePercent
            && r.ValidFrom == dto.ValidFrom
            && r.ValidTo == dto.ValidTo,
            ct);
        if (duplicateExists)
            return Conflict(new { error = "Такая процентная ставка уже существует.", section = "interestRates" });

        var e = new InterestRate
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
        db.InterestRates.Add(e);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message != null)
                return BadRequest(new { error = message, section });
            return BadRequest(new { error = "Ошибка при сохранении процентной ставки.", section = "interestRates" });
        }
        return Ok(e.Id);
    }

    [HttpPut("interest-rates/{id:int}")]
    public async Task<IActionResult> UpdateInterestRate(int id, [FromBody] InterestRateWriteDto dto,
        CancellationToken ct)
    {
        var e = await db.InterestRates.FindAsync([id], ct);
        if (e == null) return NotFound();

        decimal? rateValue = null;
        decimal? additivePercent = null;
        if (dto.RateType == "fixed")
        {
            if (dto.RateValue == null) return BadRequest("Для фиксированной ставки требуется rateValue");
            rateValue = dto.RateValue;
        }
        else if (dto.RateType == "floating")
        {
            if (dto.AdditivePercent == null) return BadRequest("Для плавающей ставки требуется additivePercent");
            additivePercent = dto.AdditivePercent;
        }
        else
        {
            return BadRequest("Неверный тип ставки");
        }

        decimal? oldVal = e.RateType == "fixed" ? e.RateValue : e.AdditivePercent;
        var newVal = dto.RateType == "fixed" ? rateValue : additivePercent;

        db.RatesHistories.Add(new RatesHistory
        {
            InterestRateId = e.Id,
            OldValue = oldVal,
            NewValue = newVal ?? 0,
            OldTermFrom = e.TermFromMonths,
            OldTermTo = e.TermToMonths,
            NewTermFrom = dto.TermFromMonths,
            NewTermTo = dto.TermToMonths,
            ChangeDate = DateTime.UtcNow
        });

        e.CreditId = dto.CreditId;
        e.CurrencyId = dto.CurrencyId;
        e.TermFromMonths = dto.TermFromMonths;
        e.TermToMonths = dto.TermToMonths;
        e.RateType = dto.RateType;
        e.RateValue = rateValue;
        e.AdditivePercent = additivePercent;
        e.ValidFrom = dto.ValidFrom;
        e.ValidTo = dto.ValidTo;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message != null)
                return BadRequest(new { error = message, section });
            return BadRequest(new { error = "Ошибка при обновлении процентной ставки.", section = "interestRates" });
        }
        return NoContent();
    }

    [HttpDelete("interest-rates/{id:int}")]
    public async Task<IActionResult> DeleteInterestRate(int id, CancellationToken ct)
    {
        if (await db.Contracts.AsNoTracking().AnyAsync(c => c.InterestRateId == id, ct))
            return Conflict("Ставка используется в договоре.");
        var e = await db.InterestRates.FindAsync([id], ct);
        if (e == null) return NotFound();
        db.InterestRates.Remove(e);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("credit-products/{creditId:int}/penalties")]
    public async Task<ActionResult<List<PenaltyRow>>> GetPenalties(int creditId, CancellationToken ct)
    {
        var list = await db.Penalties.AsNoTracking()
            .Where(p => p.CreditId == creditId)
            .Select(p => new PenaltyRow(p.Id, p.PenaltyType, p.ValuePercent, p.ValidFrom))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("credit-products/penalties")]
    public async Task<ActionResult<int>> CreatePenalty([FromBody] PenaltyWriteDto dto, CancellationToken ct)
    {
        var e = new Penalty
        {
            CreditId = dto.CreditId,
            PenaltyType = dto.PenaltyType,
            ValuePercent = dto.ValuePercent,
            ValidFrom = dto.ValidFrom
        };
        db.Penalties.Add(e);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var (message, section) = ConstraintErrorHandler.GetConstraintError(ex);
            if (message != null)
                return BadRequest(new { error = message, section });
            throw;
        }
        return Ok(e.Id);
    }

    [HttpPut("penalties/{id:int}")]
    public async Task<IActionResult> UpdatePenalty(int id, [FromBody] PenaltyWriteDto dto, CancellationToken ct)
    {
        var e = await db.Penalties.FindAsync([id], ct);
        if (e == null) return NotFound();
        db.PenaltiesHistories.Add(new PenaltiesHistory
        {
            PenaltyId = e.Id,
            OldValue = e.ValuePercent,
            NewValue = dto.ValuePercent,
            ChangeDate = DateTime.UtcNow
        });
        e.CreditId = dto.CreditId;
        e.PenaltyType = dto.PenaltyType;
        e.ValuePercent = dto.ValuePercent;
        e.ValidFrom = dto.ValidFrom;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("penalties/{id:int}")]
    public async Task<IActionResult> DeletePenalty(int id, CancellationToken ct)
    {
        var e = await db.Penalties.FindAsync([id], ct);
        if (e == null) return NotFound();
        db.Penalties.Remove(e);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    #endregion

    #region Clients

    [HttpGet("clients")]
    public async Task<ActionResult<List<ClientListItemDto>>> GetClients(CancellationToken ct)
    {
        var legals = await db.LegalPersons.AsNoTracking()
            .Select(l => new ClientListItemDto(l.ClientId, "Юридическое", l.Name, l.Phone))
            .ToListAsync(ct);
        var phys = await db.PhysPersons.AsNoTracking()
            .Select(p => new ClientListItemDto(p.ClientId, "Физическое", p.FullName, p.Phone))
            .ToListAsync(ct);
        return Ok(legals.Concat(phys).OrderBy(c => c.DisplayName).ToList());
    }

    [HttpGet("clients/legal")]
    public async Task<ActionResult<List<LegalClientRow>>> GetLegalClients(CancellationToken ct)
    {
        var list = await db.LegalPersons.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l =>
                new LegalClientRow(l.ClientId, l.Name, l.OwnershipType, l.LegalAddress, l.Phone))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("clients/legal")]
    public async Task<ActionResult<int>> CreateLegalClient([FromBody] LegalClientDto dto, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new Client { ClientType = "legal" };
        db.Clients.Add(c);
        await db.SaveChangesAsync(ct);
        db.LegalPersons.Add(new LegalPerson
        {
            ClientId = c.Id,
            Name = dto.Name,
            OwnershipType = dto.OwnershipType,
            LegalAddress = dto.LegalAddress,
            Phone = dto.Phone
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(c.Id);
    }

    [HttpPut("clients/legal/{clientId:int}")]
    public async Task<IActionResult> UpdateLegalClient(int clientId, [FromBody] LegalClientDto dto,
        CancellationToken ct)
    {
        var l = await db.LegalPersons.FindAsync([clientId], ct);
        if (l == null) return NotFound();
        l.Name = dto.Name;
        l.OwnershipType = dto.OwnershipType;
        l.LegalAddress = dto.LegalAddress;
        l.Phone = dto.Phone;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("clients/physical")]
    public async Task<ActionResult<List<PhysicalClientRow>>> GetPhysicalClients(CancellationToken ct)
    {
        var list = await db.PhysPersons.AsNoTracking()
            .OrderBy(p => p.FullName)
            .Select(p => new PhysicalClientRow(p.ClientId, p.FullName, p.PassportSeries, p.PassportNumber,
                p.ActualAddress,
                p.Phone))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("clients/physical")]
    public async Task<ActionResult<int>> CreatePhysicalClient([FromBody] PhysicalClientDto dto,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var passportSeries = dto.PassportSeries.Trim().ToUpperInvariant();
        var passportNumber = dto.PassportNumber.Trim();
        var c = new Client { ClientType = "physical" };
        db.Clients.Add(c);
        try
        {
            await db.SaveChangesAsync(ct);
            db.PhysPersons.Add(new PhysPerson
            {
                ClientId = c.Id,
                FullName = dto.FullName,
                PassportSeries = passportSeries,
                PassportNumber = passportNumber,
                ActualAddress = dto.ActualAddress,
                Phone = dto.Phone
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(c.Id);
        }
        catch (DbUpdateException ex)
        {
            var (message, _) = ConstraintErrorHandler.GetConstraintError(ex);
            return Conflict(message ?? "Ошибка при создании физического лица.");
        }
    }

    [HttpPut("clients/physical/{clientId:int}")]
    public async Task<IActionResult> UpdatePhysicalClient(int clientId, [FromBody] PhysicalClientDto dto,
        CancellationToken ct)
    {
        var p = await db.PhysPersons.FindAsync([clientId], ct);
        if (p == null) return NotFound();
        var passportSeries = dto.PassportSeries.Trim().ToUpperInvariant();
        var passportNumber = dto.PassportNumber.Trim();
        p.FullName = dto.FullName;
        p.PassportSeries = passportSeries;
        p.PassportNumber = passportNumber;
        p.ActualAddress = dto.ActualAddress;
        p.Phone = dto.Phone;
        try
        {
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            var (message, _) = ConstraintErrorHandler.GetConstraintError(ex);
            return Conflict(message ?? "Ошибка при обновлении физического лица.");
        }
    }

    [HttpDelete("clients/{clientId:int}")]
    public async Task<IActionResult> DeleteClient(int clientId, CancellationToken ct)
    {
        if (await db.Contracts.AsNoTracking().AnyAsync(co => co.ClientId == clientId, ct))
            return Conflict("У клиента есть договоры.");
        var c = await db.Clients.Include(cl => cl.LegalPerson).Include(cl => cl.PhysPerson)
            .FirstOrDefaultAsync(cl => cl.Id == clientId, ct);
        if (c == null) return NotFound();

        if (c.PhysPerson != null)
        {
            var linkedGuarantors = await db.Guarantors
                .Where(g => g.PhysPersonId == c.PhysPerson.ClientId)
                .ToListAsync(ct);
            if (linkedGuarantors.Count > 0)
                db.Guarantors.RemoveRange(linkedGuarantors);
            db.PhysPersons.Remove(c.PhysPerson);
        }

        if (c.LegalPerson != null)
            db.LegalPersons.Remove(c.LegalPerson);

        db.Clients.Remove(c);

        try
        {
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            return Conflict(FmtDb(ex));
        }
    }

    #endregion

    #region Contracts — resolution helpers

    private async Task<InterestRate?> ResolveInterestRow(int creditId, int currencyId, int termMonths,
        DateOnly issueDate,
        CancellationToken ct) =>
        await db.InterestRates.AsNoTracking()
            .Where(r => r.CreditId == creditId
                        && r.CurrencyId == currencyId
                        && termMonths >= r.TermFromMonths && termMonths <= r.TermToMonths
                        && r.ValidFrom <= issueDate
                        && (r.ValidTo == null || r.ValidTo >= issueDate))
            .OrderByDescending(r => r.ValidFrom)
            .FirstOrDefaultAsync(ct);

    private async Task<RefinanceRate?> ResolveRefinance(DateOnly issueDate, CancellationToken ct) =>
        await db.RefinanceRates.AsNoTracking()
            .Where(r => r.ValidFromDate <= issueDate
                        && (r.ValidToDate == null || r.ValidToDate >= issueDate))
            .OrderByDescending(r => r.ValidFromDate)
            .FirstOrDefaultAsync(ct);

    private async Task<(bool Ok, string? Error, decimal? Annual)> ResolveAnnualRateForContractAtDate(
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

            return (true, null, fixedAnnual);
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
        if (refinance == null)
        {
            return (false, "Не найдена ставка рефинансирования НБРБ на дату расчета.", null);
        }

        return (true, null, refinance.RatePercent + additive);
    }

    private async Task<(decimal? early, decimal? late)> ResolvePenaltiesAtIssue(int creditId, DateOnly issueDate,
        CancellationToken ct)
    {
        var early = await db.Penalties.AsNoTracking()
            .Where(p => p.CreditId == creditId && p.PenaltyType == "early_repayment" && p.ValidFrom <= issueDate)
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => (decimal?)p.ValuePercent)
            .FirstOrDefaultAsync(ct);
        var late = await db.Penalties.AsNoTracking()
            .Where(p => p.CreditId == creditId && p.PenaltyType == "late_payment" && p.ValidFrom <= issueDate)
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => (decimal?)p.ValuePercent)
            .FirstOrDefaultAsync(ct);
        return (early ?? 0m, late ?? 0m);
    }

    private sealed record ResolvedTerms(decimal Annual, int InterestRateId, string RateType, decimal? Additive);

    private async Task<(bool Ok, string? Error, ResolvedTerms? Terms)> TryResolveTerms(
        int creditId, int currencyId, int termMonths, DateOnly issueDate, CancellationToken ct)
    {
        var credit = await db.Credits.AsNoTracking().FirstOrDefaultAsync(c => c.Id == creditId, ct);
        if (credit == null)
            return (false, "Кредитный продукт не найден.", null);

        if (!await db.CreditCurrencies.AsNoTracking().AnyAsync(cc => cc.CreditId == creditId && cc.CurrencyId == currencyId, ct))
            return (false, "Валюта не привязана к выбранному кредитному продукту.", null);

        if (termMonths < credit.MinTermMonths || termMonths > credit.MaxTermMonths)
            return (false, $"Срок должен быть в диапазоне {credit.MinTermMonths} - {credit.MaxTermMonths} месяцев.", null);

        var matchedRates = await db.InterestRates.AsNoTracking()
            .Where(r => r.CreditId == creditId
                        && r.CurrencyId == currencyId
                        && termMonths >= r.TermFromMonths && termMonths <= r.TermToMonths
                        && r.ValidFrom <= issueDate
                        && (r.ValidTo == null || r.ValidTo >= issueDate))
            .OrderByDescending(r => r.ValidFrom)
            .Take(2)
            .ToListAsync(ct);

        if (matchedRates.Count > 1)
            return (false,
                "Найдено несколько подходящих процентных ставок для выбранных условий. Устраните пересечение диапазонов ставок.",
                null);

        var row = matchedRates.FirstOrDefault();
        if (row == null)
        {
            var anyTermForCurrency = await db.InterestRates.AsNoTracking().AnyAsync(r =>
                r.CreditId == creditId && r.CurrencyId == currencyId
                && termMonths >= r.TermFromMonths && termMonths <= r.TermToMonths,
                ct);
            if (!anyTermForCurrency)
                return (false, "Нет процентной ставки для указанного срока в этой валюте.", null);

            var anyValidDate = await db.InterestRates.AsNoTracking().AnyAsync(r =>
                r.CreditId == creditId && r.CurrencyId == currencyId
                && r.ValidFrom <= issueDate && (r.ValidTo == null || r.ValidTo >= issueDate),
                ct);
            if (!anyValidDate)
                return (false, "Нет действующей процентной ставки на указанную дату.", null);

            return (false, "Не найдена процентная ставка для указных условий.", null);
        }

        if (row.RateType == "fixed")
        {
            if (row.RateValue is not { } rv)
                return (false, "Для фиксированной ставки задайте rate_value.", null);
            return (true, null, new ResolvedTerms(rv, row.Id, row.RateType, null));
        }

        var refr = await ResolveRefinance(issueDate, ct);
        if (refr == null)
            return (false, "Не найдена ставка рефинансирования НБРБ на дату договора.", null);
        if (row.AdditivePercent is not { } add)
            return (false, "Для плавающей ставки задайте additive_percent.", null);
        var annual = refr.RatePercent + add;
        return (true, null, new ResolvedTerms(annual, row.Id, row.RateType, add));
    }

    #endregion

    #region Contracts

    [HttpGet("contracts")]
    public async Task<ActionResult<List<ContractRow>>> GetContracts(CancellationToken ct)
    {
        var list = await db.Contracts.AsNoTracking()
            .Join(db.Credits.AsNoTracking(), c => c.CreditId, cr => cr.Id, (c, cr) => new { c, cr })
            .Join(db.Currencies.AsNoTracking(), x => x.c.CurrencyId, cu => cu.Id,
                (x, cu) => new { x.c, x.cr, cu })
            .Join(db.Clients.AsNoTracking(), x => x.c.ClientId, cl => cl.Id, (x, cl) => new { x, cl })
            .Select(z => new ContractRow(
                z.x.c.Id,
                z.x.c.ClientId ?? 0,
                z.x.cr.Name,
                z.cl.ClientType == "legal"
                    ? z.cl.LegalPerson!.Name
                    : z.cl.PhysPerson!.FullName,
                z.cl.ClientType,
                z.x.cu.Code,
                z.x.c.ContractAmount,
                z.x.c.TermMonths,
                z.x.c.IssueDate,
                z.x.c.Status,
                z.x.c.RemainingPrincipal,
                z.cl.ClientType == "physical" ? z.cl.PhysPerson!.PassportSeries : null,
                z.cl.ClientType == "physical" ? z.cl.PhysPerson!.PassportNumber : null))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("contracts/{id:int}")]
    public async Task<ActionResult<ContractDetailsDto>> GetContractDetails(int id, CancellationToken ct)
    {
        var row = await db.Contracts.AsNoTracking()
            .Where(c => c.Id == id)
            .Join(db.Credits.AsNoTracking(), c => c.CreditId, cr => cr.Id, (c, cr) => new { c, cr })
            .Join(db.Currencies.AsNoTracking(), x => x.c.CurrencyId, cu => cu.Id,
                (x, cu) => new { x.c, x.cr, cu })
            .Join(db.Clients.AsNoTracking(), x => x.c.ClientId, cl => cl.Id, (x, cl) => new { x, cl })
            .Select(z => new ContractDetailsDto(
                z.x.c.Id,
                z.x.c.ClientId ?? 0,
                z.cl.ClientType == "legal"
                    ? z.cl.LegalPerson!.Name
                    : z.cl.PhysPerson!.FullName,
                z.cl.ClientType,
                z.cl.ClientType == "physical" ? z.cl.PhysPerson!.PassportSeries : null,
                z.cl.ClientType == "physical" ? z.cl.PhysPerson!.PassportNumber : null,
                z.x.c.CreditId ?? 0,
                z.x.cr.Name,
                z.x.c.CurrencyId ?? 0,
                z.x.cu.Code,
                z.x.c.InterestRateId,
                z.x.c.ContractAmount,
                z.x.c.TermMonths,
                z.x.c.IssueDate,
                z.x.c.Status,
                z.x.c.RateType,
                z.x.c.FixedInterestRate,
                z.x.c.FixedAdditivePercent,
                z.x.c.FixedEarlyPenaltyX,
                z.x.c.FixedLatePenaltyZ,
                z.x.c.RemainingPrincipal,
                Array.Empty<GuarantorRow>(),
                Array.Empty<PledgeRow>()))
            .FirstOrDefaultAsync(ct);

        if (row == null) return NotFound();

        var guarantors = await db.Guarantors.AsNoTracking()
            .Where(g => g.ContractId == id)
            .Join(db.PhysPersons.AsNoTracking(), g => g.PhysPersonId, p => p.ClientId,
                (g, p) => new GuarantorRow(g.Id, row.CreditName, p.FullName, p.PassportSeries, p.PassportNumber))
            .ToListAsync(ct);

        var pledges = await db.Pledges.AsNoTracking()
            .Where(p => p.ContractId == id)
            .Join(db.Currencies.AsNoTracking(), p => p.CurrencyId, cu => cu.Id, (p, cu) => new { p, cu })
            .Select(x => new PledgeRow(x.p.Id, x.p.PropertyName, x.p.EstimatedValue, x.p.AssessmentDate,
                x.p.PropertyType, x.cu.Code))
            .ToListAsync(ct);

        row = row with { Guarantors = guarantors.ToArray(), Pledges = pledges.ToArray() };
        return Ok(row);
    }

    [HttpPost("contracts")]
    public async Task<ActionResult<int>> CreateContract([FromBody] ContractCreateDto dto, CancellationToken ct)
    {
        var (ok, err, terms) =
            await TryResolveTerms(dto.CreditId, dto.CurrencyId, dto.TermMonths, dto.IssueDate, ct);
        if (!ok) return BadRequest(err);
        var pen = await ResolvePenaltiesAtIssue(dto.CreditId, dto.IssueDate, ct);

        var contract = new Contract
        {
            ClientId = dto.ClientId,
            CreditId = dto.CreditId,
            CurrencyId = dto.CurrencyId,
            ContractAmount = dto.ContractAmount,
            TermMonths = dto.TermMonths,
            IssueDate = dto.IssueDate,
            Status = StDraft,
            InterestRateId = terms!.InterestRateId,
            RateType = terms.RateType,
            FixedInterestRate = terms.Annual,
            FixedAdditivePercent = terms.Additive,
            FixedEarlyPenaltyX = pen.early,
            FixedLatePenaltyZ = pen.late,
            RemainingPrincipal = dto.ContractAmount
        };
        try
        {
            db.Contracts.Add(contract);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        return Ok(contract.Id);
    }

    [HttpPut("contracts/{id:int}")]
    public async Task<IActionResult> UpdateContract(int id, [FromBody] ContractUpdateDto dto,
        CancellationToken ct)
    {
        var contract = await db.Contracts.FindAsync([id], ct);
        if (contract == null) return NotFound();
        if (contract.Status != StDraft) return Conflict("Договор можно менять только в статусе «Оформляется».");
        if (dto.ClientId is { } cid) contract.ClientId = cid;
        if (dto.CreditId is { } crid) contract.CreditId = crid;
        if (dto.CurrencyId is { } cuid) contract.CurrencyId = cuid;
        if (dto.ContractAmount is { } amt) contract.ContractAmount = amt;
        if (dto.TermMonths is { } tm) contract.TermMonths = tm;
        if (dto.IssueDate is { } idt) contract.IssueDate = idt;
        contract.RemainingPrincipal = contract.ContractAmount;
        var (ok, err, terms) =
            await TryResolveTerms(contract.CreditId!.Value, contract.CurrencyId!.Value, contract.TermMonths,
                contract.IssueDate, ct);
        if (!ok) return BadRequest(err);
        contract.InterestRateId = terms!.InterestRateId;
        contract.RateType = terms.RateType;
        contract.FixedInterestRate = terms.Annual;
        contract.FixedAdditivePercent = terms.Additive;
        var pen = await ResolvePenaltiesAtIssue(contract.CreditId.Value, contract.IssueDate, ct);
        contract.FixedEarlyPenaltyX = pen.early;
        contract.FixedLatePenaltyZ = pen.late;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        return NoContent();
    }

    [HttpDelete("contracts/{id:int}")]
    public async Task<IActionResult> DeleteContract(int id, CancellationToken ct)
    {
        var contract = await db.Contracts
            .Include(c => c.Payments)
            .Include(c => c.Guarantors)
            .Include(c => c.Pledges)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract == null) return NotFound();
        if (contract.Status != StDraft) return Conflict("Удаление только для «Оформляется».");
        db.Payments.RemoveRange(contract.Payments);
        db.Guarantors.RemoveRange(contract.Guarantors);
        db.Pledges.RemoveRange(contract.Pledges);
        db.Contracts.Remove(contract);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("contracts/{id:int}/sign")]
    public async Task<IActionResult> SignContract(int id, CancellationToken ct)
    {
        var contract = await db.Contracts.FindAsync([id], ct);
        if (contract == null) return NotFound();
        if (contract.Status != StDraft) return Conflict();

        if (contract.CreditId is null || contract.CurrencyId is null || contract.ClientId is null)
            return BadRequest("Недостаточно данных договора для оформления.");

        var (ok, err, terms) =
            await TryResolveTerms(contract.CreditId.Value, contract.CurrencyId.Value, contract.TermMonths,
                contract.IssueDate, ct);
        if (!ok) return BadRequest(err);

        var pen = await ResolvePenaltiesAtIssue(contract.CreditId.Value, contract.IssueDate, ct);

        contract.InterestRateId = terms!.InterestRateId;
        contract.RateType = terms.RateType;
        contract.FixedInterestRate = terms.Annual;
        contract.FixedAdditivePercent = terms.Additive;
        contract.FixedEarlyPenaltyX = pen.early;
        contract.FixedLatePenaltyZ = pen.late;

        contract.Status = StSigned;
        contract.RemainingPrincipal = contract.ContractAmount;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        return NoContent();
    }

    #endregion

    #region Guarantors & pledges

    [HttpGet("guarantors")]
    public async Task<ActionResult<List<GuarantorRow>>> GetGuarantors(CancellationToken ct)
    {
        var q = from g in db.Guarantors.AsNoTracking()
                join c in db.Contracts.AsNoTracking() on g.ContractId equals c.Id
                join cr in db.Credits.AsNoTracking() on c.CreditId equals cr.Id
                join p in db.PhysPersons.AsNoTracking() on g.PhysPersonId equals p.ClientId
                select new GuarantorRow(g.Id, cr.Name, p.FullName, p.PassportSeries, p.PassportNumber);
        return Ok(await q.ToListAsync(ct));
    }

    [HttpPost("guarantors")]
    public async Task<ActionResult<int>> CreateGuarantor([FromBody] GuarantorCreateDto dto, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.ContractId, ct);
        if (contract == null) return NotFound();
        if (contract.Status != StDraft) return Conflict("Только для черновика договора.");
        var clientType = await db.Clients.AsNoTracking()
            .Where(cl => cl.Id == contract.ClientId)
            .Select(cl => cl.ClientType)
            .FirstOrDefaultAsync(ct);
        if (clientType != "physical") return BadRequest("Поручители только для физлиц.");
        var exists =
            await db.Guarantors.AsNoTracking().AnyAsync(g =>
                g.ContractId == dto.ContractId && g.PhysPersonId == dto.PhysPersonClientId, ct);
        if (exists) return Conflict("Уже добавлен.");
        var g = new Guarantor { ContractId = dto.ContractId, PhysPersonId = dto.PhysPersonClientId };
        db.Guarantors.Add(g);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        return Ok(g.Id);
    }

    [HttpDelete("guarantors/{id:int}")]
    public async Task<IActionResult> DeleteGuarantor(int id, CancellationToken ct)
    {
        var g = await db.Guarantors.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return NotFound();
        if (g.Contract?.Status != StDraft) return Conflict();
        db.Guarantors.Remove(g);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("contracts/{contractId:int}/pledges")]
    public async Task<ActionResult<List<PledgeRow>>> GetPledges(int contractId, CancellationToken ct)
    {
        var list = await db.Pledges.AsNoTracking()
            .Join(db.Currencies.AsNoTracking(), p => p.CurrencyId, cu => cu.Id, (p, cu) => new { p, cu })
            .Where(x => x.p.ContractId == contractId)
            .Select(x => new PledgeRow(x.p.Id, x.p.PropertyName, x.p.EstimatedValue, x.p.AssessmentDate, x.p.PropertyType, x.cu.Code))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("contracts/{contractId:int}/pledges")]
    public async Task<ActionResult<int>> CreatePledge(int contractId, [FromBody] PledgeWriteDto dto,
        CancellationToken ct)
    {
        var c = await db.Contracts.FindAsync([contractId], ct);
        if (c == null) return NotFound();
        if (c.Status != StDraft) return Conflict();
        if (!await db.Currencies.AsNoTracking().AnyAsync(x => x.Id == dto.CurrencyId, ct))
            return BadRequest("Валюта не найдена");
        var p = new Pledge
        {
            ContractId = contractId,
            CurrencyId = dto.CurrencyId,
            PropertyName = dto.PropertyName,
            EstimatedValue = dto.EstimatedValue,
            AssessmentDate = dto.AssessmentDate,
            PropertyType = dto.PropertyType
        };
        db.Pledges.Add(p);
        await db.SaveChangesAsync(ct);
        return Ok(p.Id);
    }

    [HttpPut("pledges/{id:int}")]
    public async Task<IActionResult> UpdatePledge(int id, [FromBody] PledgeWriteDto dto, CancellationToken ct)
    {
        var p = await db.Pledges.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        if (p.Contract?.Status != StDraft) return Conflict();
        p.PropertyName = dto.PropertyName;
        p.EstimatedValue = dto.EstimatedValue;
        p.AssessmentDate = dto.AssessmentDate;
        p.PropertyType = dto.PropertyType;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("pledges/{id:int}")]
    public async Task<IActionResult> DeletePledge(int id, CancellationToken ct)
    {
        var p = await db.Pledges.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        if (p.Contract?.Status != StDraft) return Conflict();
        db.Pledges.Remove(p);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    #endregion

    #region Payments

    private static bool IsWeekend(DateOnly date)
    {
        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    [HttpGet("contracts/{id:int}/payments/minimum")]
    public async Task<ActionResult<PaymentMinimumDto>> GetMinimumPayment(int id, [FromQuery] DateOnly paymentDate,
        CancellationToken ct)
    {
        if (IsWeekend(paymentDate))
        {
            return BadRequest("Платежи принимаются только в рабочие дни (понедельник-пятница).");
        }

        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract == null)
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

        var (rateOk, rateError, annualRate) =
            await ResolveAnnualRateForContractAtDate(contract, paymentDate, ct);
        if (!rateOk)
        {
            return BadRequest(rateError);
        }

        var schedule = LoanScheduleEngine.BuildSchedule(contract.ContractAmount, annualRate!.Value, contract.TermMonths,
            contract.IssueDate);
        var paidCount =
            await db.Payments.AsNoTracking().CountAsync(p => p.ContractId == id && p.PaymentType == "monthly", ct);
        if (paidCount >= contract.TermMonths)
        {
            return BadRequest("График закрыт.");
        }

        var line = schedule[(int)paidCount];
        var lastPayDate = await db.Payments.AsNoTracking()
            .Where(p => p.ContractId == id)
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => (DateOnly?)p.PaymentDate)
            .FirstOrDefaultAsync(ct);

        var accrualStart = lastPayDate is { } lp
            ? lp.AddDays(1)
            : LoanScheduleEngine.FirstAccrualDate(contract.IssueDate);
        var days = Math.Max(1, paymentDate.DayNumber - accrualStart.DayNumber + 1);
        var balance = contract.RemainingPrincipal;
        var interest = decimal.Round(balance * annualRate.Value / 365m * days, 2, MidpointRounding.AwayFromZero);

        var lateDays = paymentDate > line.PlannedPaymentDate
            ? paymentDate.DayNumber - line.PlannedPaymentDate.DayNumber
            : 0;
        var z = contract.FixedLatePenaltyZ ?? 0;
        var latePenalty = lateDays > 0
            ? decimal.Round((balance + interest) * z / 100m * lateDays, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var minimum = decimal.Round(interest + latePenalty, 2, MidpointRounding.AwayFromZero);
        return Ok(new PaymentMinimumDto(minimum, interest, latePenalty));
    }

    [HttpPost("contracts/{id:int}/payments")]
    public async Task<ActionResult<int>> PostPayment(int id, [FromBody] PaymentCreateDto dto,
        CancellationToken ct)
    {
        if (IsWeekend(dto.PaymentDate))
        {
            return BadRequest("Платежи принимаются только в рабочие дни (понедельник-пятница).");
        }

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract == null) return NotFound();
        if (contract.Status != StSigned) return Conflict("Платежи только для «Оформлен».");

        if (dto.PaymentDate < contract.IssueDate)
        {
            return BadRequest("Дата платежа не может быть раньше даты заключения договора.");
        }

        var (rateOk, rateError, annualRate) =
            await ResolveAnnualRateForContractAtDate(contract, dto.PaymentDate, ct);
        if (!rateOk)
        {
            return BadRequest(rateError);
        }

        var schedule = LoanScheduleEngine.BuildSchedule(contract.ContractAmount, annualRate!.Value, contract.TermMonths,
            contract.IssueDate);
        var paidCount =
            await db.Payments.AsNoTracking().CountAsync(p => p.ContractId == id && p.PaymentType == "monthly", ct);
        if (paidCount >= contract.TermMonths) return BadRequest("График закрыт.");

        var line = schedule[(int)paidCount];
        var lastPayDate = await db.Payments.AsNoTracking()
            .Where(p => p.ContractId == id)
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => (DateOnly?)p.PaymentDate)
            .FirstOrDefaultAsync(ct);

        var accrualStart = lastPayDate is { } lp
            ? lp.AddDays(1)
            : LoanScheduleEngine.FirstAccrualDate(contract.IssueDate);
        var days = Math.Max(1, dto.PaymentDate.DayNumber - accrualStart.DayNumber + 1);
        var balance = contract.RemainingPrincipal;
        var interest = decimal.Round(balance * annualRate.Value / 365m * days, 2, MidpointRounding.AwayFromZero);

        var lateDays = dto.PaymentDate > line.PlannedPaymentDate
            ? dto.PaymentDate.DayNumber - line.PlannedPaymentDate.DayNumber
            : 0;
        var z = contract.FixedLatePenaltyZ ?? 0;
        var latePenalty = lateDays > 0
            ? decimal.Round((balance + interest) * z / 100m * lateDays, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var afterInterestLate = dto.TotalAmount - interest - latePenalty;
        if (afterInterestLate < 0) return BadRequest("Сумма недостаточна для процентов и штрафов.");

        var schedPrincipal = line.PrincipalPortion;
        var excess = Math.Max(0, afterInterestLate - schedPrincipal);
        var x = contract.FixedEarlyPenaltyX ?? 0;
        var earlyPenalty = decimal.Round(excess * x / 100m, 2, MidpointRounding.AwayFromZero);
        var principalPaid = decimal.Round(afterInterestLate - earlyPenalty, 2, MidpointRounding.AwayFromZero);

        if (principalPaid < 0) return BadRequest("Некорректное распределение платежа.");
        var remainingAfter = decimal.Round(balance - principalPaid, 2, MidpointRounding.AwayFromZero);
        if (remainingAfter < -LoanScheduleEngine.Epsilon) return BadRequest("Переплата основного долга сверх остатка.");

        var payment = new Payment
        {
            ContractId = id,
            PaymentDate = dto.PaymentDate,
            PlannedPaymentDate = line.PlannedPaymentDate,
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
            db.Payments.Add(payment);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        await db.Entry(contract).ReloadAsync(ct);
        if (contract.RemainingPrincipal <= LoanScheduleEngine.Epsilon)
        {
            contract.Status = StDone;
            await db.SaveChangesAsync(ct);
        }

        return Ok(payment.Id);
    }

    #endregion

    #region Reports

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
        if (!ok) return BadRequest(err);
        var annual = terms!.Annual;
        var schedule = LoanScheduleEngine.BuildSchedule(contractAmount, annual, termMonths, issueDate);
        var list = schedule.Select(l => new ExpectedPaymentsReportLineDto(l.InstallmentIndex + 1, l.PlannedPaymentDate,
            l.ScheduledTotalPayment)).ToList();
        return Ok(list);
    }

    [HttpGet("contracts/{id:int}/reports/current-debt")]
    public async Task<ActionResult<CurrentDebtReportDto>> ReportCurrentDebt(int id, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract == null) return NotFound();
        if (contract.Status == StDraft) return BadRequest("Договор ещё не оформлен.");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var (rateOk, rateError, annual) = await ResolveAnnualRateForContractAtDate(contract, today, ct);
        if (!rateOk)
        {
            return BadRequest(rateError);
        }

        var schedule = LoanScheduleEngine.BuildSchedule(contract.ContractAmount, annual!.Value, contract.TermMonths,
            contract.IssueDate);

        var lastPayDate = await db.Payments.AsNoTracking()
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

        var paidCount =
            await db.Payments.AsNoTracking().CountAsync(p => p.ContractId == id && p.PaymentType == "monthly", ct);
        decimal principalDue = 0;
        decimal latePen = 0;
        if (paidCount < schedule.Count)
        {
            var line = schedule[(int)paidCount];
            principalDue = line.PrincipalPortion;
            if (today > line.PlannedPaymentDate)
            {
                var lateDays = today.DayNumber - line.PlannedPaymentDate.DayNumber;
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
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract == null) return NotFound();
        var annual = contract.FixedInterestRate ?? 0;
        var schedule = LoanScheduleEngine.BuildSchedule(contract.ContractAmount, annual, contract.TermMonths,
            contract.IssueDate);

        var payments = await db.Payments.AsNoTracking()
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
            if (match != null)
                status = "выполнен";
            else if (today > row.PlannedPaymentDate)
                status = "просрочен";
            else
                status = "ожидается";
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
        var rateEvents = await db.RatesHistories.AsNoTracking()
            .Join(db.InterestRates.AsNoTracking(), h => h.InterestRateId, r => r.Id, (h, r) => new { h, r })
            .Where(x => x.r.CreditId == creditId)
            .Join(db.Currencies.AsNoTracking(), x => x.r.CurrencyId, cu => cu.Id, (x, cu) => new { x.h, cu.Code })
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

        var penEvents = await db.PenaltiesHistories.AsNoTracking()
            .Join(db.Penalties.AsNoTracking(), h => h.PenaltyId, p => p.Id, (h, p) => new { h, p })
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

        var all = rateEvents.Concat(penEvents).OrderByDescending(e => e.ChangeDate).ToList();
        return Ok(all);
    }

    #endregion

    private static string FmtDb(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg ? pg.MessageText : ex.Message;
}
