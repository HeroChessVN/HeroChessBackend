using System.Text.Json;
using HeroChess.Rules.Skills;

namespace HeroChess.Api.Services;

public static class FactionSkillEligibility
{
    public static bool CanUseTranSkill(int nonSoldierHeroes, int requiredHeroes) => nonSoldierHeroes >= requiredHeroes;
    public static bool CanUseHauLeSkill(int nonSoldierHeroes, int requiredHeroes) => nonSoldierHeroes >= requiredHeroes;
    public static bool CanUseTaySonSkill(int nonSoldierHeroes, int requiredHeroes) => nonSoldierHeroes >= requiredHeroes;

    public static bool CanUse(string implementationKey, int nonSoldierHeroes, JsonElement eligibility)
    {
        // Use the same DB requirement sent in the catalogue; invalid metadata must not unlock a skill.
        if (eligibility.ValueKind != JsonValueKind.Object ||
            !eligibility.TryGetProperty("minNonSoldierFactionHeroes", out var value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var requiredHeroes) ||
            requiredHeroes is < 1 or > 11) return false;
        return implementationKey switch
        {
            FactionSkillHandler.Trap => CanUseTranSkill(nonSoldierHeroes, requiredHeroes),
            FactionSkillHandler.Forecast => CanUseHauLeSkill(nonSoldierHeroes, requiredHeroes),
            FactionSkillHandler.Rally => CanUseTaySonSkill(nonSoldierHeroes, requiredHeroes),
            _ => false
        };
    }
}
