using CreditSystem.Database;
using CreditSystem.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api/contracts")]
public class ContractsController : CreditSystemControllerBase
{
    public ContractsController(CreditSystemContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<ActionResult<List<ContractRow>>> GetContracts(CancellationToken ct)
    {
        var list = await Db.Contracts.AsNoTracking()
            .Join(Db.Credits.AsNoTracking(), c => c.CreditId, cr => cr.Id, (c, cr) => new { c, cr })
            .Join(Db.Currencies.AsNoTracking(), x => x.c.CurrencyId, cu => cu.Id,
                (x, cu) => new { x.c, x.cr, cu })
            .Join(Db.Clients.AsNoTracking(), x => x.c.ClientId, cl => cl.Id, (x, cl) => new { x, cl })
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

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ContractDetailsDto>> GetContractDetails(int id, CancellationToken ct)
    {
        var row = await Db.Contracts.AsNoTracking()
            .Where(c => c.Id == id)
            .Join(Db.Credits.AsNoTracking(), c => c.CreditId, cr => cr.Id, (c, cr) => new { c, cr })
            .Join(Db.Currencies.AsNoTracking(), x => x.c.CurrencyId, cu => cu.Id,
                (x, cu) => new { x.c, x.cr, cu })
            .Join(Db.Clients.AsNoTracking(), x => x.c.ClientId, cl => cl.Id, (x, cl) => new { x, cl })
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

        if (row is null)
        {
            return NotFound();
        }

        var guarantors = await Db.Guarantors.AsNoTracking()
            .Where(g => g.ContractId == id)
            .Join(Db.PhysPersons.AsNoTracking(), g => g.PhysPersonId, p => p.ClientId,
                (g, p) => new GuarantorRow(g.Id, row.CreditName, p.FullName, p.PassportSeries, p.PassportNumber))
            .ToListAsync(ct);

        var pledges = await Db.Pledges.AsNoTracking()
            .Where(p => p.ContractId == id)
            .Join(Db.Currencies.AsNoTracking(), p => p.CurrencyId, cu => cu.Id, (p, cu) => new { p, cu })
            .Select(x => new PledgeRow(x.p.Id, x.p.PropertyName, x.p.EstimatedValue, x.p.AssessmentDate,
                x.p.PropertyType, x.cu.Code))
            .ToListAsync(ct);

        row = row with { Guarantors = guarantors.ToArray(), Pledges = pledges.ToArray() };
        return Ok(row);
    }

    [HttpPost]
    public async Task<ActionResult<int>> CreateContract([FromBody] ContractCreateDto dto, CancellationToken ct)
    {
        var (ok, err, terms) =
            await TryResolveTerms(dto.CreditId, dto.CurrencyId, dto.TermMonths, dto.IssueDate, ct);
        if (!ok)
        {
            return BadRequest(err);
        }

        var penalties = await ResolvePenaltiesAtIssue(dto.CreditId, dto.IssueDate, ct);

        var contract = new Contract
        {
            ClientId = dto.ClientId,
            CreditId = dto.CreditId,
            CurrencyId = dto.CurrencyId,
            ContractAmount = dto.ContractAmount,
            TermMonths = dto.TermMonths,
            IssueDate = dto.IssueDate,
            Status = StDraft,
            InterestRateId = terms!.Value.InterestRateId,
            RateType = terms.Value.RateType,
            FixedInterestRate = terms.Value.Annual,
            FixedAdditivePercent = terms.Value.Additive,
            FixedEarlyPenaltyX = penalties.early,
            FixedLatePenaltyZ = penalties.late,
            RemainingPrincipal = dto.ContractAmount
        };
        try
        {
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(FmtDb(ex));
        }

        return Ok(contract.Id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateContract(int id, [FromBody] ContractUpdateDto dto,
        CancellationToken ct)
    {
        var contract = await Db.Contracts.FindAsync([id], ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != StDraft)
        {
            return Conflict("Договор можно менять только в статусе «Оформляется».");
        }

        if (dto.ClientId is { } cid)
        {
            contract.ClientId = cid;
        }

        if (dto.CreditId is { } crid)
        {
            contract.CreditId = crid;
        }

        if (dto.CurrencyId is { } cuid)
        {
            contract.CurrencyId = cuid;
        }

        if (dto.ContractAmount is { } amount)
        {
            contract.ContractAmount = amount;
        }

        if (dto.TermMonths is { } termMonths)
        {
            contract.TermMonths = termMonths;
        }

        if (dto.IssueDate is { } issueDate)
        {
            contract.IssueDate = issueDate;
        }

        contract.RemainingPrincipal = contract.ContractAmount;
        var (ok, err, terms) =
            await TryResolveTerms(contract.CreditId!.Value, contract.CurrencyId!.Value, contract.TermMonths,
                contract.IssueDate, ct);
        if (!ok)
        {
            return BadRequest(err);
        }

        contract.InterestRateId = terms!.Value.InterestRateId;
        contract.RateType = terms.Value.RateType;
        contract.FixedInterestRate = terms.Value.Annual;
        contract.FixedAdditivePercent = terms.Value.Additive;
        var penalties = await ResolvePenaltiesAtIssue(contract.CreditId.Value, contract.IssueDate, ct);
        contract.FixedEarlyPenaltyX = penalties.early;
        contract.FixedLatePenaltyZ = penalties.late;

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
    public async Task<IActionResult> DeleteContract(int id, CancellationToken ct)
    {
        var contract = await Db.Contracts
            .Include(c => c.Payments)
            .Include(c => c.Guarantors)
            .Include(c => c.Pledges)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != StDraft)
        {
            return Conflict("Удаление только для «Оформляется».");
        }

        Db.Payments.RemoveRange(contract.Payments);
        Db.Guarantors.RemoveRange(contract.Guarantors);
        Db.Pledges.RemoveRange(contract.Pledges);
        Db.Contracts.Remove(contract);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/sign")]
    public async Task<IActionResult> SignContract(int id, CancellationToken ct)
    {
        var contract = await Db.Contracts.FindAsync([id], ct);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != StDraft)
        {
            return Conflict();
        }

        if (contract.CreditId is null || contract.CurrencyId is null || contract.ClientId is null)
        {
            return BadRequest("Недостаточно данных договора для оформления.");
        }

        var (ok, err, terms) =
            await TryResolveTerms(contract.CreditId.Value, contract.CurrencyId.Value, contract.TermMonths,
                contract.IssueDate, ct);
        if (!ok)
        {
            return BadRequest(err);
        }

        var penalties = await ResolvePenaltiesAtIssue(contract.CreditId.Value, contract.IssueDate, ct);

        contract.InterestRateId = terms!.Value.InterestRateId;
        contract.RateType = terms.Value.RateType;
        contract.FixedInterestRate = terms.Value.Annual;
        contract.FixedAdditivePercent = terms.Value.Additive;
        contract.FixedEarlyPenaltyX = penalties.early;
        contract.FixedLatePenaltyZ = penalties.late;

        contract.Status = StSigned;
        contract.RemainingPrincipal = contract.ContractAmount;
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
}
