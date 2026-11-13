using System.Text.Json;
using HeroChess.Api.Services;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class FactionSkillEligibilityTests
{
    [Theory]
    [InlineData(FactionSkillHandler.Trap)]
    [InlineData(FactionSkillHandler.Forecast)]
    [InlineData(FactionSkillHandler.Rally)]
    public void Requirement_follows_the_catalogue_value_for_each_faction(string key)
    {
        foreach (var required in new[] { 1, 5, 8, 11 })
        {
            using var eligibility = JsonDocument.Parse($$"""{"minNonSoldierFactionHeroes":{{required}},"otherSetting":true}""");
            Assert.False(FactionSkillEligibility.CanUse(key, required - 1, eligibility.RootElement));
            Assert.True(FactionSkillEligibility.CanUse(key, required, eligibility.RootElement));
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"minNonSoldierFactionHeroes":"5"}""")]
    [InlineData("""{"minNonSoldierFactionHeroes":0}""")]
    [InlineData("""{"minNonSoldierFactionHeroes":-1}""")]
    [InlineData("""{"minNonSoldierFactionHeroes":12}""")]
    [InlineData("""{"minNonSoldierFactionHeroes":5.5}""")]
    [InlineData("""{"minNonSoldierFactionHeroes":2147483648}""")]
    public void Invalid_requirement_does_not_unlock_a_faction_skill(string json)
    {
        using var eligibility = JsonDocument.Parse(json);
        Assert.False(FactionSkillEligibility.CanUse(FactionSkillHandler.Trap, 11, eligibility.RootElement));
    }

    [Fact]
    public void Unknown_skill_does_not_use_a_faction_requirement()
    {
        using var eligibility = JsonDocument.Parse("""{"minNonSoldierFactionHeroes":5}""");
        Assert.False(FactionSkillEligibility.CanUse("unknown", 11, eligibility.RootElement));
    }
}
