using HeroChess.Rules.Skills;

namespace HeroChess.Api.Services;

public static class FactionSkillEligibility
{
    public const int TranRequiredHeroes = 5;
    public const int HauLeRequiredHeroes = 5;
    public const int TaySonRequiredHeroes = 5;

    public static bool CanUseTranSkill(int nonSoldierHeroes) => nonSoldierHeroes >= TranRequiredHeroes;
    public static bool CanUseHauLeSkill(int nonSoldierHeroes) => nonSoldierHeroes >= HauLeRequiredHeroes;
    public static bool CanUseTaySonSkill(int nonSoldierHeroes) => nonSoldierHeroes >= TaySonRequiredHeroes;

    public static bool CanUse(string implementationKey, int nonSoldierHeroes) => implementationKey switch
    {
        FactionSkillHandler.Trap => CanUseTranSkill(nonSoldierHeroes),
        FactionSkillHandler.Forecast => CanUseHauLeSkill(nonSoldierHeroes),
        FactionSkillHandler.Rally => CanUseTaySonSkill(nonSoldierHeroes),
        _ => false
    };
}
