using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class MatchStateProjectionTests
{
    [Fact]
    public void Opponent_cannot_read_hidden_stake_position_or_metadata()
    {
        var stakeId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var state = new GameState();
        state.Obstacles.Add(new ObstacleState(stakeId, new BoardPoint(4, 5), SkillKeys.ObstacleKindThDTuongCoc, 2));
        state.ObstacleMetadata[stakeId] = new ObstacleMetadata { Placer = Side.Red };
        state.ThdTuongCocStakes[owner] = stakeId;
        using var stored = GameJson.Document(state);

        var red = MatchStateProjection.State(stored.RootElement, Side.Red);
        var black = MatchStateProjection.State(stored.RootElement, Side.Black);
        Assert.Contains(stakeId.ToString(), red.GetRawText());
        Assert.DoesNotContain(stakeId.ToString(), black.GetRawText());
        Assert.DoesNotContain(owner.ToString(), black.GetRawText());
        Assert.DoesNotContain("\"x\":4,\"y\":5", black.GetRawText());
    }

    [Fact]
    public void Opponent_cannot_read_hidden_creation_event()
    {
        using var stored = GameJson.Document(new[]
        {
            new { type = "obstacle.created", kind = SkillKeys.ObstacleKindThDTuongCoc,
                creator = "Red", position = new { x = 4, y = 5 } }
        });
        var opponent = MatchStateProjection.Events(stored.RootElement, Side.Black);
        Assert.DoesNotContain("position", opponent.GetRawText());
        Assert.Contains("hero_skill.activated", opponent.GetRawText());
    }
}
