using CreditSystem.Database;
using CreditSystem.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.RegularExpressions;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api/currencies")]
public partial class CurrenciesController : CreditSystemControllerBase
{
    public CurrenciesController(CreditSystemContext db) : base(db)
    {
    }

    private static readonly Regex CurrencyCodeRegex = MyRegex();

    private static bool TryNormalizeCurrencyCode(string code, out string normalized)
    {
        normalized = code
            .Trim()
            .ToUpperInvariant();
        return CurrencyCodeRegex.IsMatch(normalized);
    }

    [HttpGet]
    public async Task<ActionResult<List<CurrencyRow>>> GetCurrencies(CancellationToken ct)
    {
        var list = await Db.Currencies
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CurrencyRow(c.Id, c.Code, c.Name))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<int>> CreateCurrency([FromBody] CurrencyWriteDto dto, CancellationToken ct)
    {
        if (!TryNormalizeCurrencyCode(dto.Code, out var normalizedCode))
        {
            return BadRequest("Код валюты должен состоять только из букв (3 символа).");
        }

        var entity = new Currency { Code = normalizedCode, Name = dto.Name };
        Db.Currencies.Add(entity);
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is NpgsqlException { SqlState: "23505" })
            {
                return Conflict("Валюта с таким кодом уже существует.");
            }

            return Conflict(ex.InnerException?.Message ?? ex.Message);
        }

        return Ok(entity.Id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCurrency(int id, [FromBody] CurrencyWriteDto dto, CancellationToken ct)
    {
        if (!TryNormalizeCurrencyCode(dto.Code, out var normalizedCode))
        {
            return BadRequest("Код валюты должен состоять только из букв (3 символа).");
        }

        var entity = await Db.Currencies.FindAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Code = normalizedCode;
        entity.Name = dto.Name;
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is NpgsqlException { SqlState: "23505" })
            {
                return Conflict("Валюта с таким кодом уже существует.");
            }

            return Conflict(ex.InnerException?.Message ?? ex.Message);
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCurrency(int id, CancellationToken ct)
    {
        var used = await Db.Contracts
            .AsNoTracking()
            .AnyAsync(c => c.CurrencyId == id, ct) ||
                   await Db.CreditCurrencies
                       .AsNoTracking()
                       .AnyAsync(cc => cc.CurrencyId == id, ct);
        if (used)
        {
            return Conflict("Валюта используется.");
        }

        var entity = await Db.Currencies.FindAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        Db.Currencies.Remove(entity);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
