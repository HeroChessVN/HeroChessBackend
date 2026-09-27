// Vai trò file: Trả catalog nội dung hiện bật và ownership của actor; controller này đọc EF trực tiếp.
using HeroChess.Api.Auth;
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Account)]
[Route("api/v1")]
public sealed class CatalogController(AppDbContext db, ICurrentUser current) : ControllerBase
{
    [HttpGet("catalog")]
    // Catalog: Gộp ruleset/class/slot/hero/trait/faction/team skill thành DTO, gắn IsOwned theo player.
    public async Task<ActionResult<CatalogDto>> Catalog(CancellationToken cancellationToken)
    {
        var ruleset = await db.Rulesets.AsNoTracking().SingleAsync(x => x.IsActive, cancellationToken);
        var owned = await db.PlayerHeroes.AsNoTracking().Where(x => x.PlayerId == current.UserId && db.Players.Any(p => p.Id == current.UserId)).Select(x => x.HeroId).ToHashSetAsync(cancellationToken);
        var factionLinks = await db.HeroFactions.AsNoTracking().ToListAsync(cancellationToken);
        var traits = await db.HeroTraits.AsNoTracking().OrderBy(x => x.Code).ToListAsync(cancellationToken);
        var heroes = await db.Heroes.AsNoTracking().Include(x => x.Trait)
            .Where(x => x.IsEnabled).OrderBy(x => x.ClassCode).ThenBy(x => x.Code).ToListAsync(cancellationToken);

        return Ok(new CatalogDto(
            new(ruleset.Id, ruleset.Code, ruleset.Name, ruleset.SetupBudget, ruleset.TurnSeconds, ruleset.ActionLimit, ruleset.Config.RootElement.Clone()),
            await db.ChessClasses.AsNoTracking().OrderBy(x => x.Code).Select(x => new ChessClassDto(x.Code, x.NameVi, x.BaseSp, x.RequiredCount, x.BaseMovementCode)).ToListAsync(cancellationToken),
            await db.LineupSlots.AsNoTracking().OrderBy(x => x.SlotNo).Select(x => new LineupSlotDto(x.SlotNo, x.ClassCode, x.StartX, x.StartY)).ToListAsync(cancellationToken),
            heroes.Select(x => new HeroDto(x.Id, x.Code, x.Name, x.CharacterId, x.ClassCode, x.SetupPoints,
                x.Trait is null ? null : Trait(x.Trait), factionLinks.Where(f => f.HeroId == x.Id).Select(f => f.FactionId).ToArray(),
                x.AssetKey, owned.Contains(x.Id), x.CoinPrice.ToString())).ToArray(),
            traits.Select(Trait).ToArray(),
            await db.Factions.AsNoTracking().OrderBy(x => x.Code).Select(x => new FactionDto(x.Id, x.Code, x.Name, x.Description)).ToListAsync(cancellationToken),
            (await db.TeamSkills.AsNoTracking().Where(x => x.IsEnabled).OrderBy(x => x.Code).ToListAsync(cancellationToken))
                .Select(x => new TeamSkillDto(x.Id, x.Code, x.Name, x.FactionId, x.ImplementationKey,
                    x.Parameters.RootElement.Clone(), x.Eligibility.RootElement.Clone(), x.MaxUses, x.CooldownTurns, x.AssetKey)).ToArray()));
    }

    [HttpGet("me/heroes"), Authorize(Policy = AccountPolicies.Player)]
    // OwnedHeroes: Trả danh sách hero ID player đang sở hữu.
    public async Task<ActionResult<IReadOnlyList<Guid>>> OwnedHeroes(CancellationToken cancellationToken) =>
        Ok(await db.PlayerHeroes.AsNoTracking().Where(x => x.PlayerId == current.UserId).OrderBy(x => x.HeroId).Select(x => x.HeroId).ToListAsync(cancellationToken));

    // Trait: Chuyển entity trait sang DTO, Clone JsonElement để tách lifetime JsonDocument.
    private static TraitDto Trait(HeroTrait trait) => new(trait.Id, trait.Code, trait.Name, trait.Kind,
        trait.ImplementationKey, trait.Parameters.RootElement.Clone(), trait.Description);
}
