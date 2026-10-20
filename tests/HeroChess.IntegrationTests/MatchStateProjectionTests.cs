using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class MatchStateProjectionTests
{
    [Fact]
    public void Legacy_hero_skill_data_is_not_sent_to_either_side()
    {
        var stakeId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var state = new GameState();
        state.Obstacles.Add(new ObstacleState(stakeId, new BoardPoint(4, 5), SkillKeys.ObstacleKindThDTuongCoc, 2));
        state.ObstacleMetadata[stakeId] = new ObstacleMetadata { Placer = Side.Red };
        state.ThdTuongCocStakes[owner] = stakeId;
        state.Pieces.Add(new PieceState { PieceId = owner, HeroId = Guid.NewGuid(), Side = Side.Red,
            Class = PieceClass.Elephant, TraitKind = "active", TraitImplementationKey = SkillKeys.ThDTuongCoc,
            TraitName = "Bạch Đằng Giang", TraitDescription = "Legacy trait" });
        using var stored = GameJson.Document(state);

        var red = MatchStateProjection.State(stored.RootElement, Side.Red);
        var black = MatchStateProjection.State(stored.RootElement, Side.Black);
        Assert.DoesNotContain(stakeId.ToString(), red.GetRawText());
        Assert.DoesNotContain(stakeId.ToString(), black.GetRawText());
        Assert.DoesNotContain("traitKind", red.GetRawText());
        Assert.DoesNotContain("traitDescription", black.GetRawText());
        Assert.DoesNotContain(SkillKeys.ThDTuongCoc, red.GetRawText());
        Assert.DoesNotContain("\"x\":4,\"y\":5", black.GetRawText());
    }

    [Fact]
    public void Legacy_hero_skill_events_are_not_sent()
    {
        using var stored = GameJson.Document(new[]
        {
            new { type = "obstacle.created", kind = SkillKeys.ObstacleKindThDTuongCoc,
                creator = "Red", position = new { x = 4, y = 5 } },
            new { type = "hero_skill.activated", kind = "active", creator = "Red", position = new { x = 4, y = 5 } }
        });
        var opponent = MatchStateProjection.Events(stored.RootElement, Side.Black);
        Assert.DoesNotContain("position", opponent.GetRawText());
        Assert.Equal(0, opponent.GetArrayLength());
    }
}
