using System.Text.Json;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.Rules.Tests;

public sealed class SkillTimingTests
{
    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(2, 7, 7)]
    public void Cooldown_and_one_turn_effect_share_the_same_boundary(int version, int cooldown, int afterCast)
    {
        var (state, dispatcher, action) = Command(Side.Red, SkillKeys.Khien, cooldown, 1);
        state = new GameState { SkillTimingVersion = version, PublicRiverStakes = state.PublicRiverStakes,
            SideToMove = state.SideToMove, Pieces = state.Pieces, SkillStates = state.SkillStates };
        var cast = dispatcher.Dispatch(state, Side.Red, FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[Side.Red][0]), action);
        Assert.True(cast.Accepted);
        var first = Next(cast.State);
        Assert.Equal(afterCast, first.SkillStates[Side.Red][0].CooldownRemaining);
        if (version == 1) return; // V1 keeps the handler's legacy four-turn Shield and activation tick.
        var soldier = first.Pieces.Single(p => p.Class == PieceClass.Soldier);
        Assert.Equal(1, Assert.Single(soldier.Effects).RemainingTurns);
        var second = Next(first);
        Assert.Equal(cooldown - 1, second.SkillStates[Side.Red][0].CooldownRemaining);
        Assert.Empty(second.Pieces.Single(p => p.PieceId == soldier.PieceId).Effects);
        for (var i = 1; i < cooldown; i++) second = Next(second);
        Assert.Equal(0, second.SkillStates[Side.Red][0].CooldownRemaining);
    }

    [Theory]
    [InlineData(Side.Red, SkillKeys.Thanh, 13, 6)]
    [InlineData(Side.Black, SkillKeys.Thanh, 13, 6)]
    [InlineData(Side.Red, SkillKeys.Rao, 9, 4)]
    [InlineData(Side.Black, SkillKeys.Rao, 9, 4)]
    [InlineData(Side.Red, SkillKeys.Khien, 13, 4)]
    [InlineData(Side.Black, SkillKeys.Khien, 13, 4)]
    [InlineData(Side.Red, FactionSkillHandler.Trap, 7, 6)]
    [InlineData(Side.Black, FactionSkillHandler.Trap, 7, 6)]
    [InlineData(Side.Red, FactionSkillHandler.Rally, 11, 6)]
    [InlineData(Side.Black, FactionSkillHandler.Rally, 11, 6)]
    [InlineData(Side.Red, FactionSkillHandler.Forecast, 15, 0)]
    [InlineData(Side.Black, FactionSkillHandler.Forecast, 15, 0)]
    public void New_timers_keep_their_full_value_but_existing_timers_advance(Side side, string key, int cd, int duration)
    {
        var (state, dispatcher, action) = Command(side, key, cd, duration);
        state.SkillStates[side].Add(new(2, Guid.NewGuid(), null, 3, SkillKeys.Rao, CooldownStartedAtTurn: -2));
        state.Obstacles.Add(new(Guid.NewGuid(), new(8, 4), SkillKeys.ObstacleKindRao, 3, -2));
        var cast = dispatcher.Dispatch(state, side, FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[side][0]), action);
        Assert.True(cast.Accepted, cast.Error?.Message);
        var first = Next(cast.State);
        Assert.Equal(cd, first.SkillStates[side][0].CooldownRemaining);
        Assert.Equal(2, first.SkillStates[side][1].CooldownRemaining);
        Assert.Equal(2, first.Obstacles.Single(o => o.Position == new BoardPoint(8, 4)).RemainingLifetime);
        Assert.Equal(0, state.SkillStates[side][0].CooldownRemaining);
        if (key is SkillKeys.Thanh or SkillKeys.Rao)
            Assert.Equal(duration, first.Obstacles.Single(o => o.CreatedAtTurn == 0).RemainingLifetime);
        if (key is SkillKeys.Khien or FactionSkillHandler.Rally)
            Assert.Equal(duration, Assert.Single(first.Pieces.First(p => p.Side == side && p.Class == PieceClass.Soldier).Effects).RemainingTurns);
        if (key == FactionSkillHandler.Trap)
        {
            Assert.Equal(3, first.RiverSnares.Count);
            Assert.All(first.RiverSnares, t => { Assert.Equal(duration, t.ExpiresAt - first.TurnIndex); Assert.Equal(4, t.RootDurationTurns); });
        }
        var second = Next(first);
        Assert.Equal(cd - 1, second.SkillStates[side][0].CooldownRemaining);
        var roundTrip = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(second))!;
        Assert.Equal(2, roundTrip.SkillTimingVersion);
        Assert.Equal(0, roundTrip.SkillStates[side][0].CooldownStartedAtTurn);
    }

    [Theory]
    [InlineData(Side.Red)]
    [InlineData(Side.Black)]
    public void Hich_six_covers_three_future_owner_turns_and_then_expires(Side side)
    {
        var (state, dispatcher, action) = Command(side, FactionSkillHandler.Rally, 11, 6);
        var cast = dispatcher.Dispatch(state, side, FrozenSkillSnapshotFactory.FromSkillState(state.SkillStates[side][0]), action);
        var current = cast.State;
        var opportunities = 0;
        for (var i = 1; i <= 7; i++)
        {
            current = Next(current);
            var active = current.Pieces.Single(p => p.Side == side && p.Class == PieceClass.Soldier).Effects.Any(e => e.Code == FactionSkillHandler.Rally);
            if (active && current.SideToMove == side) opportunities++;
            Assert.Equal(i <= 6, active);
        }
        Assert.Equal(3, opportunities);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"durationTurns\":0}")]
    [InlineData("{\"durationTurns\":-2}")]
    [InlineData("{\"durationTurns\":2.5}")]
    [InlineData("{\"durationTurns\":\"6\"}")]
    [InlineData("{\"durationTurns\":true}")]
    [InlineData("{\"durationTurns\":2147483647}")]
    public void Invalid_duration_does_not_spend_a_skill(string json)
    {
        var (state, dispatcher, action) = Command(Side.Red, FactionSkillHandler.Rally, 11, 6);
        var before = JsonSerializer.Serialize(state);
        var result = dispatcher.Dispatch(state, Side.Red, new(1, state.SkillStates[Side.Red][0].SkillId,
            FactionSkillHandler.Rally, 11, JsonSerializer.Deserialize<JsonElement>(json)), action);
        Assert.False(result.Accepted);
        Assert.Equal("INVALID_SKILL_CONFIG", result.Error!.Code);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData(Side.Red, 3)]
    [InlineData(Side.Black, 9)]
    public void Quang_trung_uses_frozen_config_and_does_not_tick_on_the_cast(Side side, int cd)
    {
        var state = Position(side);
        var hero = state.Pieces.Single(p => p.Class == PieceClass.General && p.Side == side);
        var qt = new PieceState { PieceId = hero.PieceId, Side = side, Class = PieceClass.General, Position = hero.Position,
            TraitImplementationKey = SkillKeys.QuangTrungSpecialMove,
            MovementImplementationKey = "general.orthogonal_range_1_no_palace", TraitParameters = Data(new { cooldownTurns = cd }) };
        qt.TraitState[SkillKeys.QuangTrungCooldownKey] = 1;
        state.Pieces.Remove(hero); state.Pieces.Add(qt);
        var result = new XiangqiRulesEngine().ApplyHeroSpecialMove(state, new(qt.PieceId, new(hero.Position!.Value.X, side == Side.Red ? 2 : 7)));
        Assert.True(result.Accepted, result.Error?.Message);
        var ticked = TurnLifecycle.Apply(result.State, result.State.SideToMove).State;
        Assert.Equal(cd, ticked.Pieces.Single(p => p.PieceId == qt.PieceId).TraitState[SkillKeys.QuangTrungCooldownRemainingKey]);
        Assert.Equal(cd - 1, Next(ticked).Pieces.Single(p => p.PieceId == qt.PieceId).TraitState[SkillKeys.QuangTrungCooldownRemainingKey]);
    }

    [Theory]
    [InlineData(SkillKeys.LeLaiSwapGeneral, 7, 0)]
    [InlineData(SkillKeys.TranThuDoGeneralShield, 5, 2)]
    [InlineData(SkillKeys.ThDTuongCoc, 5, 3)]
    public void Other_hero_timers_use_frozen_parameters(string key, int cd, int duration)
    {
        var state = Position(Side.Red);
        var hero = new PieceState { PieceId = Guid.NewGuid(), Side = Side.Red,
            Class = key == SkillKeys.ThDTuongCoc ? PieceClass.Elephant : PieceClass.Advisor,
            Position = new(4, 1), TraitKind = "active", TraitImplementationKey = key,
            TraitParameters = Data(new { cooldownTurns = cd, durationTurns = duration }) };
        state.Pieces.Add(hero);
        var king = state.Pieces.Single(p => p.Side == Side.Red && p.Class == PieceClass.General);
        var cast = key == SkillKeys.ThDTuongCoc
            ? BachDangGiang.Execute(state, Side.Red, hero.PieceId, Data(new { position = new { x = 2, y = 4 } }))
            : AdvisorHeroSkills.Execute(state, Side.Red, hero.PieceId, key, Data(new { pieceId = king.PieceId }));
        Assert.True(cast.Accepted, cast.Error?.Message);
        var first = Next(cast.State);
        Assert.Equal(cd, first.Pieces.Single(p => p.PieceId == hero.PieceId).TraitState[SkillKeys.HeroCooldownRemainingKey]);
        if (key == SkillKeys.TranThuDoGeneralShield)
            Assert.Equal(duration, Assert.Single(first.Pieces.Single(p => p.PieceId == king.PieceId).Effects).RemainingTurns);
        if (key == SkillKeys.ThDTuongCoc) Assert.Equal(duration, Assert.Single(first.Obstacles).RemainingLifetime);
    }

    [Fact]
    public void A_hidden_snare_uses_its_frozen_root_duration_without_a_five_turn_compensation()
    {
        var state = Position(Side.Black);
        var rook = new PieceState { PieceId = Guid.NewGuid(), Side = Side.Black, Class = PieceClass.Rook, Position = new(2, 8) };
        state.Pieces.Add(rook);
        state.RiverSnares.Add(new(Side.Red, new(2, 5), 10, 4));
        var move = new XiangqiRulesEngine().ApplyMove(state, new(rook.PieceId, new(2, 2)));
        Assert.True(move.Accepted);
        var first = TurnLifecycle.Apply(move.State, move.State.SideToMove).State;
        Assert.Equal(4, Assert.Single(first.Pieces.Single(p => p.PieceId == rook.PieceId).Effects).RemainingTurns);
        var ownTurns = 0;
        for (var i = 0; i < 4; i++)
        {
            if (first.SideToMove == Side.Black && first.Pieces.Single(p => p.PieceId == rook.PieceId).Effects.Any()) ownTurns++;
            first = Next(first);
        }
        Assert.Equal(2, ownTurns);
        Assert.Empty(first.Pieces.Single(p => p.PieceId == rook.PieceId).Effects);
    }

    [Fact]
    public void Public_shield_expiry_cannot_make_a_legal_preview_allow_self_check()
    {
        var state = Position(Side.Red);
        var king = state.Pieces.Single(p => p.Side == Side.Red && p.Class == PieceClass.General);
        king.Effects.Add(new(SkillKeys.ShieldEffect, null, 1, -2));
        state.Pieces.Add(new() { PieceId = Guid.NewGuid(), Side = Side.Black, Class = PieceClass.Rook, Position = new(4, 5) });
        var engine = new XiangqiRulesEngine();
        Assert.DoesNotContain(engine.GenerateLegalActions(state), m => m.PieceId == king.PieceId && m.To == new BoardPoint(4, 0));
        Assert.False(engine.ApplyMove(state, new(king.PieceId, new(4, 0))).Accepted);
    }

    static (GameState, CommandSkillDispatcher, JsonElement) Command(Side side, string key, int cd, int duration)
    {
        var state = Position(side);
        var parameters = Data(new { durationTurns = duration, rootDurationTurns = 4 });
        state.SkillStates[side].Add(new(1, Guid.NewGuid(), 2, 0, key, Parameters: parameters, CooldownTurns: cd));
        ICommandSkillHandler handler = key switch { SkillKeys.Thanh => new ThanhHandler(), SkillKeys.Rao => new RaoHandler(),
            SkillKeys.Khien => new KhienHandler(), _ => new FactionSkillHandler(key) };
        object target = key switch { SkillKeys.Thanh or SkillKeys.Rao => new { position = new { x = 2, y = side == Side.Red ? 4 : 5 } },
            SkillKeys.Khien => new { pieceId = state.Pieces.Single(p => p.Side == side && p.Class == PieceClass.Soldier).PieceId },
            FactionSkillHandler.Trap => new { positions = new[] { new { x = 2, y = 4 }, new { x = 4, y = 4 }, new { x = 6, y = 5 } } },
            _ => new { } };
        return (state, new(new CommandSkillRegistry([handler])), Data(new { type = "team_skill", slot = 1, target }));
    }

    static GameState Position(Side side) => new() { SkillTimingVersion = 2, PublicRiverStakes = true, SideToMove = side,
        Pieces = [new() { PieceId = Guid.NewGuid(), Side = Side.Red, Class = PieceClass.General, Position = new(3, 0) },
            new() { PieceId = Guid.NewGuid(), Side = Side.Black, Class = PieceClass.General, Position = new(5, 9) },
            new() { PieceId = Guid.NewGuid(), Side = side, Class = PieceClass.Soldier, Position = new(0, side == Side.Red ? 3 : 6) }] };

    static GameState Next(GameState state)
    {
        var next = state.Clone(); next.TurnIndex++;
        next.SideToMove = state.SideToMove == Side.Red ? Side.Black : Side.Red;
        return TurnLifecycle.Apply(next, next.SideToMove).State;
    }
    static JsonElement Data(object value) => JsonSerializer.SerializeToElement(value);
}
