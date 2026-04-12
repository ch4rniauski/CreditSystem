using CreditSystem.Database;
using CreditSystem.Dtos;
using CreditSystem.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api/clients")]
public class ClientsController : CreditSystemControllerBase
{
    public ClientsController(CreditSystemContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<ActionResult<List<ClientListItemDto>>> GetClients(CancellationToken ct)
    {
        var legals = await Db.LegalPersons.AsNoTracking()
            .Select(l => new ClientListItemDto(l.ClientId, "Юридическое", l.Name, l.Phone))
            .ToListAsync(ct);
        var phys = await Db.PhysPersons.AsNoTracking()
            .Select(p => new ClientListItemDto(p.ClientId, "Физическое", p.FullName, p.Phone))
            .ToListAsync(ct);
        return Ok(legals.Concat(phys).OrderBy(c => c.DisplayName).ToList());
    }

    [HttpGet("legal")]
    public async Task<ActionResult<List<LegalClientRow>>> GetLegalClients(CancellationToken ct)
    {
        var list = await Db.LegalPersons.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l =>
                new LegalClientRow(l.ClientId, l.Name, l.OwnershipType, l.LegalAddress, l.Phone))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("legal")]
    public async Task<ActionResult<int>> CreateLegalClient([FromBody] LegalClientDto dto, CancellationToken ct)
    {
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        var client = new Client { ClientType = "legal" };
        Db.Clients.Add(client);
        await Db.SaveChangesAsync(ct);
        Db.LegalPersons.Add(new LegalPerson
        {
            ClientId = client.Id,
            Name = dto.Name,
            OwnershipType = dto.OwnershipType,
            LegalAddress = dto.LegalAddress,
            Phone = dto.Phone
        });
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(client.Id);
    }

    [HttpPut("legal/{clientId:int}")]
    public async Task<IActionResult> UpdateLegalClient(int clientId, [FromBody] LegalClientDto dto,
        CancellationToken ct)
    {
        var legal = await Db.LegalPersons.FindAsync([clientId], ct);
        if (legal is null)
        {
            return NotFound();
        }

        legal.Name = dto.Name;
        legal.OwnershipType = dto.OwnershipType;
        legal.LegalAddress = dto.LegalAddress;
        legal.Phone = dto.Phone;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("physical")]
    public async Task<ActionResult<List<PhysicalClientRow>>> GetPhysicalClients(CancellationToken ct)
    {
        var list = await Db.PhysPersons.AsNoTracking()
            .OrderBy(p => p.FullName)
            .Select(p => new PhysicalClientRow(p.ClientId, p.FullName, p.PassportSeries, p.PassportNumber,
                p.ActualAddress,
                p.Phone))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("physical")]
    public async Task<ActionResult<int>> CreatePhysicalClient([FromBody] PhysicalClientDto dto,
        CancellationToken ct)
    {
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        var passportSeries = dto.PassportSeries.Trim().ToUpperInvariant();
        var passportNumber = dto.PassportNumber.Trim();
        var client = new Client { ClientType = "physical" };
        Db.Clients.Add(client);
        try
        {
            await Db.SaveChangesAsync(ct);
            Db.PhysPersons.Add(new PhysPerson
            {
                ClientId = client.Id,
                FullName = dto.FullName,
                PassportSeries = passportSeries,
                PassportNumber = passportNumber,
                ActualAddress = dto.ActualAddress,
                Phone = dto.Phone
            });
            await Db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(client.Id);
        }
        catch (DbUpdateException ex)
        {
            var (message, _) = ConstraintErrorHandler.GetConstraintError(ex);
            return Conflict(message ?? "Ошибка при создании физического лица.");
        }
    }

    [HttpPut("physical/{clientId:int}")]
    public async Task<IActionResult> UpdatePhysicalClient(int clientId, [FromBody] PhysicalClientDto dto,
        CancellationToken ct)
    {
        var phys = await Db.PhysPersons.FindAsync([clientId], ct);
        if (phys is null)
        {
            return NotFound();
        }

        var passportSeries = dto.PassportSeries.Trim().ToUpperInvariant();
        var passportNumber = dto.PassportNumber.Trim();
        phys.FullName = dto.FullName;
        phys.PassportSeries = passportSeries;
        phys.PassportNumber = passportNumber;
        phys.ActualAddress = dto.ActualAddress;
        phys.Phone = dto.Phone;
        try
        {
            await Db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            var (message, _) = ConstraintErrorHandler.GetConstraintError(ex);
            return Conflict(message ?? "Ошибка при обновлении физического лица.");
        }
    }

    [HttpDelete("{clientId:int}")]
    public async Task<IActionResult> DeleteClient(int clientId, CancellationToken ct)
    {
        if (await Db.Contracts.AsNoTracking().AnyAsync(co => co.ClientId == clientId, ct))
        {
            return Conflict("У клиента есть договоры.");
        }

        var client = await Db.Clients.Include(cl => cl.LegalPerson).Include(cl => cl.PhysPerson)
            .FirstOrDefaultAsync(cl => cl.Id == clientId, ct);
        if (client is null)
        {
            return NotFound();
        }

        if (client.PhysPerson is not null)
        {
            var linkedGuarantors = await Db.Guarantors
                .Where(g => g.PhysPersonId == client.PhysPerson.ClientId)
                .ToListAsync(ct);
            if (linkedGuarantors.Count > 0)
            {
                Db.Guarantors.RemoveRange(linkedGuarantors);
            }

            Db.PhysPersons.Remove(client.PhysPerson);
        }

        if (client.LegalPerson is not null)
        {
            Db.LegalPersons.Remove(client.LegalPerson);
        }

        Db.Clients.Remove(client);

        try
        {
            await Db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            return Conflict(FmtDb(ex));
        }
    }
}
