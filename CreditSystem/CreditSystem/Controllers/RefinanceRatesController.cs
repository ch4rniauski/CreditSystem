using CreditSystem.Database;
using CreditSystem.Dtos;
using CreditSystem.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Controllers;

[ApiController]
[Route("api/refinance-rates")]
public sealed class RefinanceRatesController : CreditSystemControllerBase
{
    public RefinanceRatesController(CreditSystemContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<ActionResult<List<RefinanceRateRow>>> GetRefinanceRates(CancellationToken ct)
    {
        var list = await Db.RefinanceRates.AsNoTracking()
            .OrderByDescending(r => r.ValidFromDate)
            .Select(r => new RefinanceRateRow(r.Id, r.ValidFromDate, r.ValidToDate, r.RatePercent))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<int>> CreateRefinanceRate([FromBody] RefinanceRateWriteDto dto,
        CancellationToken ct)
    {
        var entity = new RefinanceRate
        {
            ValidFromDate = dto.ValidFromDate,
            ValidToDate = dto.ValidToDate,
            RatePercent = dto.RatePercent
        };
        Db.RefinanceRates.Add(entity);
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

            return BadRequest(new { error = "Ошибка при создании ставки рефинансирования.", section = "refinance" });
        }

        return Ok(entity.Id);
    }
}
