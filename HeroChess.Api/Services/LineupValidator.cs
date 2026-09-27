// Vai trò file: Kiểm tra đội hình trước khi lưu/chọn: slot, class, ownership, SP, skin và skill; chưa xử lý ngưỡng phe chi tiết.
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed record LineupValidationResult(int TotalSp, int Budget, IReadOnlyList<LineupValidationError> Errors,
    IReadOnlyDictionary<int, string> SlotClasses);

public sealed class LineupValidator(AppDbContext db)
{
    // ValidateAsync: Đọc catalog/ownership và gom tất cả lỗi validation, tổng SP và ánh xạ slot; không ghi DB.
    public async Task<LineupValidationResult> ValidateAsync(Guid playerId, SaveLineupRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<LineupValidationError>();
        var ruleset = await db.Rulesets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RulesetId && x.IsActive, cancellationToken);
        if (ruleset is null) errors.Add(new("RULESET_UNAVAILABLE", "The selected ruleset is not active."));
        var budget = ruleset?.SetupBudget ?? 0;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
            errors.Add(new("INVALID_NAME", "Lineup name must contain 1 to 80 characters."));

        var slots = await db.LineupSlots.AsNoTracking().ToDictionaryAsync(x => (int)x.SlotNo, x => x.ClassCode, cancellationToken);
        if (request.Entries.Count != 16) errors.Add(new("ENTRY_COUNT", "A lineup must contain exactly 16 entries."));
        foreach (var duplicate in request.Entries.GroupBy(x => x.SlotNo).Where(x => x.Count() > 1))
            errors.Add(new("DUPLICATE_SLOT", "A lineup slot may only be used once.", duplicate.Key));
        foreach (var slot in slots.Keys.Except(request.Entries.Select(x => x.SlotNo)))
            errors.Add(new("MISSING_SLOT", "The required lineup slot is missing.", slot));

        var heroIds = request.Entries.Select(x => x.HeroId).Distinct().ToArray();
        var heroes = await db.Heroes.AsNoTracking().Where(x => heroIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var owned = await db.PlayerHeroes.AsNoTracking().Where(x => x.PlayerId == playerId && heroIds.Contains(x.HeroId)).Select(x => x.HeroId).ToHashSetAsync(cancellationToken);
        foreach (var entry in request.Entries)
        {
            if (!slots.TryGetValue(entry.SlotNo, out var expectedClass)) { errors.Add(new("UNKNOWN_SLOT", "The lineup slot does not exist.", entry.SlotNo)); continue; }
            if (!heroes.TryGetValue(entry.HeroId, out var hero)) { errors.Add(new("HERO_NOT_FOUND", "The selected hero does not exist.", entry.SlotNo)); continue; }
            if (hero.ClassCode != expectedClass) errors.Add(new("WRONG_CLASS", $"Slot {entry.SlotNo} requires {expectedClass}.", entry.SlotNo));
            if (!hero.IsEnabled) errors.Add(new("HERO_DISABLED", "The selected hero is disabled.", entry.SlotNo));
            if (!owned.Contains(hero.Id)) errors.Add(new("HERO_NOT_OWNED", "The selected hero is not owned.", entry.SlotNo));
        }
        foreach (var duplicateCharacter in request.Entries.Where(x => heroes.TryGetValue(x.HeroId, out var h) && h.CharacterId is not null)
                     .GroupBy(x => heroes[x.HeroId].CharacterId).Where(x => x.Count() > 1))
            foreach (var entry in duplicateCharacter) errors.Add(new("DUPLICATE_CHARACTER", "A historical character cannot appear twice in one lineup.", entry.SlotNo));

        var cosmetics = request.Entries.Where(x => x.CosmeticId is not null).Select(x => x.CosmeticId!.Value).Distinct().ToArray();
        var ownedCosmetics = await db.PlayerCosmetics.AsNoTracking().Where(x => x.PlayerId == playerId && cosmetics.Contains(x.CosmeticId)).Select(x => x.CosmeticId).ToHashSetAsync(cancellationToken);
        var cosmeticRows = await db.Cosmetics.AsNoTracking().Where(x => cosmetics.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var entry in request.Entries.Where(x => x.CosmeticId is not null))
        {
            var id = entry.CosmeticId!.Value;
            if (!cosmeticRows.TryGetValue(id, out var cosmetic) || cosmetic.HeroId != entry.HeroId || !cosmetic.IsEnabled)
                errors.Add(new("INVALID_COSMETIC", "The cosmetic is unavailable for this hero.", entry.SlotNo));
            else if (!ownedCosmetics.Contains(id)) errors.Add(new("COSMETIC_NOT_OWNED", "The cosmetic is not owned.", entry.SlotNo));
        }

        var totalSp = request.Entries.Where(x => heroes.ContainsKey(x.HeroId)).Sum(x => heroes[x.HeroId].SetupPoints);
        if (ruleset is not null && totalSp > ruleset.SetupBudget) errors.Add(new("SP_BUDGET_EXCEEDED", $"Lineup costs {totalSp} SP but the budget is {ruleset.SetupBudget}."));

        if (request.Skills.Count != 3) errors.Add(new("SKILL_COUNT", "A lineup must contain exactly three team skills."));
        if (request.Skills.Select(x => x.SlotNo).Distinct().Count() != request.Skills.Count || request.Skills.Any(x => x.SlotNo is < 1 or > 3))
            errors.Add(new("INVALID_SKILL_SLOTS", "Team skill slots must be unique values from 1 to 3."));
        if (request.Skills.Select(x => x.SkillId).Distinct().Count() != request.Skills.Count)
            errors.Add(new("DUPLICATE_SKILL", "The same team skill cannot be selected twice."));
        var skillIds = request.Skills.Select(x => x.SkillId).Distinct().ToArray();
        var skills = await db.TeamSkills.AsNoTracking().Where(x => skillIds.Contains(x.Id)).ToListAsync(cancellationToken);
        if (skills.Count != skillIds.Length || skills.Any(x => !x.IsEnabled)) errors.Add(new("SKILL_UNAVAILABLE", "One or more team skills are unavailable."));
        if (skills.Count(x => x.FactionId is not null) > 1) errors.Add(new("MULTIPLE_FACTION_SKILLS", "At most one faction team skill may be active."));

        return new(totalSp, budget, errors, slots);
    }
}
