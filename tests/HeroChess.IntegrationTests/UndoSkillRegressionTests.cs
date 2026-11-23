using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static HeroChess.IntegrationTests.MatchFlowTests;

namespace HeroChess.IntegrationTests;

public sealed class UndoSkillRegressionTests(HeroChessFactory factory) : IClassFixture<HeroChessFactory>
{
    [Theory]
    [InlineData(SkillKeys.Thanh)]
    [InlineData(SkillKeys.Rao)]
    [InlineData(SkillKeys.Khien)]
    [InlineData(FactionSkillHandler.Trap)]
    [InlineData(FactionSkillHandler.Rally)]
    [InlineData(FactionSkillHandler.Forecast)]
    [InlineData(SkillKeys.QuangTrungSpecialMove)]
    [InlineData(SkillKeys.ThDTuongCoc)]
    [InlineData(SkillKeys.LeLaiSwapGeneral)]
    [InlineData(SkillKeys.TranThuDoGeneralShield)]
    [InlineData("hidden_snare_hit")]
    [InlineData("public_stake_hit")]
    [InlineData("hoanh_soc_capture")]
    [InlineData("hoanh_soc_consume")]
    [InlineData("elephant.alternating_distance")]
    [InlineData(SkillKeys.AdvisorPalaceStep)]
    public async Task Undo_restores_full_skill_state_and_reusing_the_action_does_not_double_tick(string key)
    {
        await factory.Services.GetRequiredService<BotTurnScheduler>().StopAsync(default);
        var human = await new MatchFlowTests(factory).CreatePlayerWithLineup();
        var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        var id = ticket.MatchId!.Value;
        await Select(human, id);
        (await human.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        var path = $"/api/v1/matches/{id}";
        GameState initial;
        JsonElement action;
        Guid blackSoldierId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.MatchStates.SingleAsync(s => s.MatchId == id);
            initial = GameJson.Read<GameState>(row.State);
            Assert.Equal(2, initial.SkillTimingVersion);
            initial.Pieces.Clear();
            var king = Piece(PieceClass.General, Side.Red, 3, 0);
            var soldier = Piece(PieceClass.Soldier, Side.Red, 0, 3);
            var black = Piece(PieceClass.Soldier, Side.Black, 8, 6);
            black.Effects.Add(new(SkillKeys.ShieldEffect, null, 3, -2));
            blackSoldierId = black.PieceId;
            initial.Pieces.AddRange([king, soldier, Piece(PieceClass.General, Side.Black, 5, 9), black]);
            var obstacleId = Guid.NewGuid();
            initial.Obstacles.Add(new(obstacleId, new(8, 4), SkillKeys.ObstacleKindRao, 4, -2));
            initial.ObstacleMetadata[obstacleId] = new() { Placer = Side.Red };
            initial.SkillStates[Side.Red][1] = initial.SkillStates[Side.Red][1] with { CooldownRemaining = 4, CooldownStartedAtTurn = -2 };
            var team = await db.TeamSkills.AsNoTracking().SingleOrDefaultAsync(s => s.IsEnabled && s.ImplementationKey == key);
            if (team is not null)
            {
                var participant = await db.MatchParticipants.SingleAsync(p => p.MatchId == id && p.Side == "red");
                var lineup = GameJson.Read<FrozenLineup>(participant.LineupSnapshot);
                var frozen = new FrozenSkill(1, team.Id, key, 2, team.CooldownTurns!.Value,
                    team.Name, team.Description, team.Parameters.RootElement.Clone());
                participant.LineupSnapshot = GameJson.Document(lineup with { Skills = [frozen, .. lineup.Skills.Skip(1)] });
                initial.SkillStates[Side.Red][0] = new(1, team.Id, 2, 0, key,
                    team.Name, team.Description, frozen.Parameters, frozen.CooldownTurns);
                object target = key switch
                {
                    SkillKeys.Thanh or SkillKeys.Rao => new { position = new { x = 2, y = 4 } },
                    SkillKeys.Khien => new { pieceId = soldier.PieceId },
                    FactionSkillHandler.Trap => new { positions = new[] { new { x = 2, y = 4 }, new { x = 4, y = 4 }, new { x = 6, y = 5 } } },
                    _ => new { }
                };
                action = GameJson.Element(new { type = "team_skill", slot = 1, target });
            }
            else if (key is SkillKeys.QuangTrungSpecialMove or SkillKeys.ThDTuongCoc or SkillKeys.LeLaiSwapGeneral or SkillKeys.TranThuDoGeneralShield)
            {
                var trait = await db.HeroTraits.AsNoTracking().SingleAsync(t => t.ImplementationKey == key);
                var actor = Piece(key == SkillKeys.QuangTrungSpecialMove ? PieceClass.General :
                    key == SkillKeys.ThDTuongCoc ? PieceClass.Elephant : PieceClass.Advisor, Side.Red,
                    key == SkillKeys.QuangTrungSpecialMove ? 3 : 4, key == SkillKeys.QuangTrungSpecialMove ? 0 : key == SkillKeys.ThDTuongCoc ? 2 : 1,
                    key, "active", trait.Parameters.RootElement.Clone(), key == SkillKeys.QuangTrungSpecialMove ? "general.orthogonal_range_1_no_palace" : null);
                if (key == SkillKeys.QuangTrungSpecialMove)
                {
                    initial.Pieces.Remove(king); king = actor;
                    actor.TraitState[SkillKeys.QuangTrungCooldownKey] = 1;
                }
                initial.Pieces.Add(actor);
                object target = key switch
                {
                    SkillKeys.QuangTrungSpecialMove => new { to = new { x = 3, y = 2 } },
                    SkillKeys.ThDTuongCoc => new { position = new { x = 2, y = 4 } },
                    _ => new { pieceId = king.PieceId }
                };
                action = GameJson.Element(new { type = "hero_active", pieceId = actor.PieceId, skillCode = key, target });
            }
            else
            {
                var passive = key.StartsWith("hoanh_soc") ? SkillKeys.HoanhSoc :
                    key is "elephant.alternating_distance" or SkillKeys.AdvisorPalaceStep ? key : null;
                var actor = Piece(key == "elephant.alternating_distance" ? PieceClass.Elephant :
                    key == SkillKeys.AdvisorPalaceStep ? PieceClass.Advisor : PieceClass.Rook,
                    Side.Red, 2, 2, passive, passive is null ? null : "special_move", movement: passive);
                BoardPoint to = new(2, 6);
                if (key == "hidden_snare_hit") initial.RiverSnares.Add(new(Side.Black, new(2, 4), 10, 4));
                if (key == "public_stake_hit")
                {
                    var stake = Guid.NewGuid();
                    var owner = Piece(PieceClass.Elephant, Side.Black, 6, 7);
                    initial.Pieces.Add(owner);
                    initial.Obstacles.Add(new(stake, new(2, 4), SkillKeys.ObstacleKindThDTuongCoc, 3, -2));
                    initial.ObstacleMetadata[stake] = new() { Placer = Side.Black };
                    initial.ThdTuongCocStakes[owner.PieceId] = stake;
                }
                if (key.StartsWith("hoanh_soc"))
                {
                    if (key == "hoanh_soc_capture") initial.Pieces.Add(Piece(PieceClass.Horse, Side.Black, 2, 6));
                    else
                    {
                        actor.TraitState[SkillKeys.HoanhSocChargedKey] = 1;
                        initial.Pieces.Add(Piece(PieceClass.Soldier, Side.Red, 3, 2));
                        to = new(5, 2);
                    }
                }
                if (key == "elephant.alternating_distance")
                {
                    actor.Position = new(2, 0);
                    actor.TraitState["lastMoveDistance"] = 1; to = new(5, 3);
                }
                if (key == SkillKeys.AdvisorPalaceStep)
                {
                    actor.Position = new(4, 1); to = new(4, 2);
                }
                initial.Pieces.Add(actor);
                action = GameJson.Element(new { type = "move", pieceId = actor.PieceId, to });
            }
            row.State = GameJson.Document(initial);
            (await db.MatchActions.SingleAsync(a => a.MatchId == id && a.SequenceNo == 0)).StateAfter = GameJson.Document(initial);
            await db.SaveChangesAsync();
        }

        var cast = await Send(0, action);
        if (key == FactionSkillHandler.Forecast)
        {
            using var blocked = await human.Client.PostAsJsonAsync(path + "/commands", Request(cast.SequenceNo, new { type = "undo" }), Json);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
            cast = await Send(cast.SequenceNo, GameJson.Element(new { type = "end_turn" }));
        }
        await using (var replyScope = factory.Services.CreateAsyncScope())
        {
            var replies = new XiangqiRulesEngine().GenerateLegalActions(cast.Snapshot.State.Deserialize<GameState>(GameJson.Options)!);
            var move = replies.OrderByDescending(m => m.PieceId == blackSoldierId).First();
            cast = await replyScope.ServiceProvider.GetRequiredService<MatchCommandService>().ExecuteBotAsync(id,
                Request(cast.SequenceNo, new { type = "move", pieceId = move.PieceId, to = move.To }), null, default);
        }
        var undoRequest = Request(cast.SequenceNo, new { type = "undo" });
        var undo = await Post<MatchCommandResultDto>(human.Client, path + "/commands", undoRequest);
        Assert.Equal(0, undo.ResolvedEvents[0].GetProperty("targetSequence").GetInt32());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var restored = GameJson.Read<GameState>((await db.MatchStates.AsNoTracking().SingleAsync(s => s.MatchId == id)).State);
            restored.Version = initial.Version;
            Assert.True(JsonElement.DeepEquals(GameJson.Element(initial), GameJson.Element(restored)), key);
        }
        Assert.True((await Post<MatchCommandResultDto>(human.Client, path + "/commands", undoRequest)).Duplicate);
        var repeat = await Send(undo.SequenceNo, action);
        if (key == FactionSkillHandler.Forecast) repeat = await Send(repeat.SequenceNo, GameJson.Element(new { type = "end_turn" }));
        var repeated = repeat.Snapshot.State.Deserialize<GameState>(GameJson.Options)!;
        Assert.Equal(2, Assert.Single(repeated.Pieces.Single(p => p.PieceId == blackSoldierId).Effects).RemainingTurns);
        Assert.Equal(3, repeated.SkillStates[Side.Red][1].CooldownRemaining);
        await Send(repeat.SequenceNo, GameJson.Element(new { type = "resign" }));

        Task<MatchCommandResultDto> Send(int version, JsonElement command) => Post<MatchCommandResultDto>(human.Client, path + "/commands", new MatchCommandRequest(Guid.NewGuid(), version, command));
        static MatchCommandRequest Request(int version, object command) => new(Guid.NewGuid(), version, GameJson.Element(command));
    }

    private static PieceState Piece(PieceClass kind, Side side, int x, int y, string? trait = null,
        string? traitKind = null, JsonElement? parameters = null, string? movement = null) => new()
    { PieceId = Guid.NewGuid(), Class = kind, Side = side, Position = new(x, y), SetupPoints = 1,
        TraitImplementationKey = trait, TraitKind = traitKind, TraitParameters = parameters, MovementImplementationKey = movement };
}
