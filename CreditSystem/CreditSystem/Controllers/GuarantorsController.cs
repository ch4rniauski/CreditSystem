using CreditSystem.Database;
using CreditSystem.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api/guarantors")]
public sealed class GuarantorsController : CreditSystemControllerBase
{
    public GuarantorsController(CreditSystemContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<ActionResult<List<GuarantorRow>>> GetGuarantors(CancellationToken ct)
    {
        var query = from g in Db.Guarantors.AsNoTracking()
                    join c in Db.Contracts.AsNoTracking() on g.ContractId equals c.Id
                    join cr in Db.Credits.AsNoTracking() on c.CreditId equals cr.Id
                    join p in Db.PhysPersons.AsNoTracking() on g.PhysPersonId equals p.ClientId
                    select new GuarantorRow(g.Id, g.ContractId ?? 0, g.PhysPersonId ?? 0, cr.Name, p.FullName,
                        p.PassportSeries, p.PassportNumber);
        return Ok(await query.ToListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<int>> CreateGuarantor([FromBody] GuarantorCreateDto dto, CancellationToken ct)
    {
        var contract = await Db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.ContractId, ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != StDraft)
        {
            return Conflict("Только для черновика договора.");
        }

        var clientType = await Db.Clients.AsNoTracking()
            .Where(cl => cl.Id == contract.ClientId)
            .Select(cl => cl.ClientType)
            .FirstOrDefaultAsync(ct);
        if (clientType != "physical")
        {
            return BadRequest("Поручители только для физлиц.");
        }

        var exists =
            await Db.Guarantors.AsNoTracking().AnyAsync(g =>
                g.ContractId == dto.ContractId && g.PhysPersonId == dto.PhysPersonClientId, ct);
        if (exists)
        {
            return Conflict("Уже добавлен.");
        }

        var guarantor = new Guarantor { ContractId = dto.ContractId, PhysPersonId = dto.PhysPersonClientId };
        Db.Guarantors.Add(guarantor);
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        return Ok(guarantor.Id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateGuarantor(int id, [FromBody] GuarantorCreateDto dto, CancellationToken ct)
    {
        var guarantor = await Db.Guarantors.Include(g => g.Contract).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guarantor is null)
        {
            return NotFound();
        }

        if (guarantor.Contract?.Status != StDraft)
        {
            return Conflict("Только для черновика договора.");
        }

        var contract = await Db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.ContractId, ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != StDraft)
        {
            return Conflict("Только для черновика договора.");
        }

        var clientType = await Db.Clients.AsNoTracking()
            .Where(cl => cl.Id == contract.ClientId)
            .Select(cl => cl.ClientType)
            .FirstOrDefaultAsync(ct);
        if (clientType != "physical")
        {
            return BadRequest("Поручители только для физлиц.");
        }

        var duplicate = await Db.Guarantors.AsNoTracking().AnyAsync(g =>
            g.Id != id && g.ContractId == dto.ContractId && g.PhysPersonId == dto.PhysPersonClientId, ct);
        if (duplicate)
        {
            return Conflict("Уже добавлен.");
        }

        guarantor.ContractId = dto.ContractId;
        guarantor.PhysPersonId = dto.PhysPersonClientId;
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteGuarantor(int id, CancellationToken ct)
    {
        var guarantor = await Db.Guarantors.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (guarantor is null)
        {
            return NotFound();
        }

        if (guarantor.Contract?.Status != StDraft)
        {
            return Conflict();
        }

        Db.Guarantors.Remove(guarantor);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }
}
