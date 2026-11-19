using System.Text.Json;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.Rules.Tests;

public sealed class AdvisorHeroSkillsTests
{
    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void Swap_works_across_the_palace_without_consuming_effects_or_mutating_the_input(Side side)
    {
        var (state, hero, general) = Position(side, SkillKeys.LeLaiSwapGeneral);
        hero.Effects.Add(new(SkillKeys.ShieldEffect, general.PieceId, 3));
        general.TraitState[SkillKeys.QuangTrungCooldownRemainingKey] = 2;
        state.Obstacles.Add(new(Guid.NewGuid(), Point(side, 4, 1), SkillKeys.ObstacleKindThanh, 5));
        var result = AdvisorHeroSkills.Execute(state, side, hero.PieceId, SkillKeys.LeLaiSwapGeneral, Target(general.PieceId));
        Assert.True(result.Accepted);
        Assert.Equal(general.Position, result.State.Pieces.Single(p => p.PieceId == hero.PieceId).Position);
        Assert.Equal(hero.Position, result.State.Pieces.Single(p => p.PieceId == general.PieceId).Position);
        Assert.Equal(8, result.State.Pieces.Single(p => p.PieceId == hero.PieceId).TraitState[SkillKeys.HeroCooldownRemainingKey]);
        Assert.Equal(3, Assert.Single(result.State.Pieces.Single(p => p.PieceId == hero.PieceId).Effects).RemainingTurns);
        Assert.Equal(2, result.State.Pieces.Single(p => p.PieceId == general.PieceId).TraitState[SkillKeys.QuangTrungCooldownRemainingKey]);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(Point(side, 3, 0), hero.Position);
        Assert.Equal(Point(side, 5, 2), general.Position);
        Assert.Empty(hero.TraitState);
    }

    [Theory]
    [InlineData("source-rooted", "PIECE_ROOTED")]
    [InlineData("target-rooted", "PIECE_ROOTED")]
    [InlineData("outside", "INVALID_TARGET")]
    [InlineData("source-outside", "INVALID_TARGET")]
    [InlineData("cooldown", "SKILL_ON_COOLDOWN")]
    [InlineData("not-general", "INVALID_TARGET")]
    [InlineData("dead", "INVALID_TARGET")]
    [InlineData("enemy", "INVALID_TARGET")]
    public void Invalid_swap_keeps_the_original_position(string scenario, string error)
    {
        var (state, hero, general) = Position(Side.Red, SkillKeys.LeLaiSwapGeneral);
        var target = general.PieceId;
        switch (scenario)
        {
            case "source-rooted": hero.Effects.Add(new(FactionSkillHandler.Rooted, null, 2)); break;
            case "target-rooted": general.Effects.Add(new(FactionSkillHandler.Rooted, null, 2)); break;
            case "outside": general.Position = new(4, 3); break;
            case "source-outside": hero.Position = new(2, 2); break;
            case "cooldown": hero.TraitState[SkillKeys.HeroCooldownRemainingKey] = 1; break;
            case "not-general": target = hero.PieceId; break;
            case "dead": general.Status = PieceStatus.Captured; break;
            case "enemy": target = state.Pieces.Single(p => p.Side == Side.Black).PieceId; break;
        }
        var result = AdvisorHeroSkills.Execute(state, Side.Red, hero.PieceId, SkillKeys.LeLaiSwapGeneral, Target(target));
        Assert.False(result.Accepted);
        Assert.Equal(error, result.Error!.Code);
        Assert.Same(state, result.State);
    }

    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void Shield_lasts_two_shared_turns_and_cannot_stack_with_command_shield(Side side)
    {
        var (state, hero, general) = Position(side, SkillKeys.TranThuDoGeneralShield);
        general.Position = Point(side, 4, 3); // Shield targets a General even after it has left the palace.
        hero.Effects.Add(new(FactionSkillHandler.Rooted, null, 4));
        var result = AdvisorHeroSkills.Execute(state, side, hero.PieceId, SkillKeys.TranThuDoGeneralShield, Target(general.PieceId));
        Assert.True(result.Accepted);
        var shield = Assert.Single(result.State.Pieces.Single(p => p.PieceId == general.PieceId).Effects);
        Assert.Equal(SkillKeys.ShieldEffect, shield.Code);
        Assert.Equal(hero.PieceId, shield.SourcePieceId);
        Assert.Equal(2, shield.RemainingTurns);
        var enemy = side == Side.Red ? Side.Black : Side.Red;
        var enemyTurn = TurnLifecycle.Apply(result.State, enemy).State;
        Assert.Equal(1, Assert.Single(enemyTurn.Pieces.Single(p => p.PieceId == general.PieceId).Effects).RemainingTurns);
        Assert.Equal(5, enemyTurn.Pieces.Single(p => p.PieceId == hero.PieceId).TraitState[SkillKeys.HeroCooldownRemainingKey]);
        enemyTurn.SideToMove = side;
        var command = new CommandSkillDispatcher(new CommandSkillRegistry(new ICommandSkillHandler[] { new KhienHandler() }));
        var skillId = Guid.NewGuid();
        enemyTurn.SkillStates[side].Add(new(1, skillId, null, 0, SkillKeys.Khien));
        var duplicate = command.Dispatch(enemyTurn, side, new(1, skillId, SkillKeys.Khien, 14),
            JsonSerializer.SerializeToElement(new { target = new { pieceId = general.PieceId } }));
        Assert.Equal("ALREADY_SHIELDED", duplicate.Error!.Code);
        var ownTurn = TurnLifecycle.Apply(enemyTurn, side).State;
        Assert.Empty(ownTurn.Pieces.Single(p => p.PieceId == general.PieceId).Effects);
        Assert.Empty(general.Effects);

        general.Effects.Add(new(SkillKeys.ShieldEffect, null, 3));
        Assert.Equal("ALREADY_SHIELDED", AdvisorHeroSkills.Execute(state, side, hero.PieceId,
            SkillKeys.TranThuDoGeneralShield, Target(general.PieceId)).Error!.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"cooldownTurns\":0,\"durationTurns\":2}")]
    [InlineData("{\"cooldownTurns\":6,\"durationTurns\":1}")]
    [InlineData("{\"cooldownTurns\":6,\"durationTurns\":6}")]
    [InlineData("{\"cooldownTurns\":\"6\",\"durationTurns\":2}")]
    public void Bad_shield_parameters_are_rejected_without_mutation(string json)
    {
        var (state, hero, general) = Position(Side.Red, SkillKeys.TranThuDoGeneralShield, json);
        var result = AdvisorHeroSkills.Execute(state, Side.Red, hero.PieceId, SkillKeys.TranThuDoGeneralShield, Target(general.PieceId));
        Assert.Equal("INVALID_HERO_SKILL_CONFIG", result.Error!.Code);
        Assert.Empty(general.Effects);
        Assert.Empty(hero.TraitState);
    }

    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void Palace_step_adds_orthogonal_moves_but_still_respects_root_and_self_check(Side side)
    {
        var (state, _, general) = Position(side, SkillKeys.LeLaiSwapGeneral);
        var advisor = new PieceState { PieceId = Guid.NewGuid(), Side = side, Class = PieceClass.Advisor,
            Position = Point(side, 4, 1), MovementImplementationKey = SkillKeys.AdvisorPalaceStep };
        state.Pieces.RemoveAll(p => p.Class == PieceClass.Advisor);
        state.Pieces.Add(advisor);
        var engine = new XiangqiRulesEngine();
        var moves = engine.GenerateLegalActions(state).Where(m => m.PieceId == advisor.PieceId).ToArray();
        Assert.Contains(moves, m => m.To == Point(side, 3, 1));
        Assert.Contains(moves, m => m.To == Point(side, 4, 0));
        Assert.Contains(moves, m => m.To == Point(side, 3, 2));
        Assert.DoesNotContain(moves, m => m.To == general.Position || m.To.X is < 3 or > 5 || Math.Abs(m.To.Y - m.From.Y) > 1);
        advisor.Effects.Add(new(FactionSkillHandler.Rooted, null, 1));
        Assert.DoesNotContain(engine.GenerateLegalActions(state), m => m.PieceId == advisor.PieceId);
    }

    private static (GameState, PieceState, PieceState) Position(Side side, string key, string? json = null)
    {
        var hero = new PieceState { PieceId = Guid.NewGuid(), Side = side, Class = PieceClass.Advisor,
            Position = Point(side, 3, 0), TraitKind = "active", TraitImplementationKey = key,
            TraitParameters = JsonSerializer.SerializeToElement(JsonDocument.Parse(json ?? (key == SkillKeys.LeLaiSwapGeneral
                ? "{\"cooldownTurns\":8}" : "{\"cooldownTurns\":6,\"durationTurns\":2}")).RootElement) };
        var general = new PieceState { PieceId = Guid.NewGuid(), Side = side, Class = PieceClass.General, Position = Point(side, 5, 2) };
        var state = new GameState { PublicRiverStakes = true, SideToMove = side, Pieces = [hero, general,
            new PieceState { PieceId = Guid.NewGuid(), Side = side == Side.Red ? Side.Black : Side.Red,
                Class = PieceClass.General, Position = Point(side, 4, 9) }] };
        return (state, hero, general);
    }

    private static JsonElement Target(Guid id) => JsonSerializer.SerializeToElement(new { pieceId = id });
    private static BoardPoint Point(Side side, int x, int y) => new(x, side == Side.Red ? y : 9 - y);
}
