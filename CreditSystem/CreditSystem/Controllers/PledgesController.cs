using CreditSystem.Database;
using CreditSystem.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api")]
public sealed class PledgesController : CreditSystemControllerBase
{
    public PledgesController(CreditSystemContext db) : base(db)
    {
    }

    [HttpGet("contracts/{contractId:int}/pledges")]
    public async Task<ActionResult<List<PledgeRow>>> GetPledges(int contractId, CancellationToken ct)
    {
        var list = await Db.Pledges.AsNoTracking()
            .Join(Db.Currencies.AsNoTracking(), p => p.CurrencyId, cu => cu.Id, (p, cu) => new { p, cu })
            .Where(x => x.p.ContractId == contractId)
            .Select(x => new PledgeRow(x.p.Id, x.p.PropertyName, x.p.EstimatedValue, x.p.AssessmentDate,
                x.p.PropertyType, x.cu.Code))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("contracts/{contractId:int}/pledges")]
    public async Task<ActionResult<int>> CreatePledge(int contractId, [FromBody] PledgeWriteDto dto,
        CancellationToken ct)
    {
        var contract = await Db.Contracts.FindAsync([contractId], ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != StDraft)
        {
            return Conflict();
        }

        if (!await Db.Currencies.AsNoTracking().AnyAsync(x => x.Id == dto.CurrencyId, ct))
        {
            return BadRequest("Валюта не найдена");
        }

        var pledge = new Pledge
        {
            ContractId = contractId,
            CurrencyId = dto.CurrencyId,
            PropertyName = dto.PropertyName,
            EstimatedValue = dto.EstimatedValue,
            AssessmentDate = dto.AssessmentDate,
            PropertyType = dto.PropertyType
        };
        Db.Pledges.Add(pledge);
        await Db.SaveChangesAsync(ct);
        return Ok(pledge.Id);
    }

    [HttpPut("pledges/{id:int}")]
    public async Task<IActionResult> UpdatePledge(int id, [FromBody] PledgeWriteDto dto, CancellationToken ct)
    {
        var pledge = await Db.Pledges.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (pledge is null)
        {
            return NotFound();
        }

        if (pledge.Contract?.Status != StDraft)
        {
            return Conflict();
        }

        pledge.PropertyName = dto.PropertyName;
        pledge.EstimatedValue = dto.EstimatedValue;
        pledge.AssessmentDate = dto.AssessmentDate;
        pledge.PropertyType = dto.PropertyType;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("pledges/{id:int}")]
    public async Task<IActionResult> DeletePledge(int id, CancellationToken ct)
    {
        var pledge = await Db.Pledges.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (pledge is null)
        {
            return NotFound();
        }

        if (pledge.Contract?.Status != StDraft)
        {
            return Conflict();
        }

        Db.Pledges.Remove(pledge);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }
}
