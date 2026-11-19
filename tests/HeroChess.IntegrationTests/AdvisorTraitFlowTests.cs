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
using Npgsql;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class UserMigrationTests
{
    [Fact]
    public async Task Advisor_patch_is_repeatable_and_bootstrap_preserves_live_trait_and_cooldown_settings()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await SqlFile(connection, "02_seed_catalog.sql");
            await SqlFile(connection, "07_exclusive_hero_traits.sql");
            await SqlFile(connection, "08_step6_skills.sql");
            await SqlFile(connection, "19_hero_sp_and_advisor_traits.sql");
            await SqlFile(connection, "19_hero_sp_and_advisor_traits.sql");
            await using (var changes = new NpgsqlCommand("""
                UPDATE hero_chess.team_skill SET cooldown_turns=23,description='Live description' WHERE code='thanh';
                UPDATE hero_chess.hero_trait SET parameters='{"cooldownTurns":12}' WHERE code='le-lai-cuu-chua';
                """, connection)) await changes.ExecuteNonQueryAsync();
            await SqlFile(connection, "02_seed_catalog.sql");
            await SqlFile(connection, "08_step6_skills.sql");
            await using var check = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM hero_chess.hero h JOIN hero_chess.hero_trait t ON t.id=h.trait_id
                    WHERE h.code IN ('le-lai-advisor','tran-thu-do-advisor','nguyen-nhac-advisor','nguyen-lu-advisor') AND h.setup_points=2)=4
                  AND (SELECT parameters->>'cooldownTurns' FROM hero_chess.hero_trait WHERE code='le-lai-cuu-chua')='12'
                  AND (SELECT cooldown_turns=23 AND description='Live description' FROM hero_chess.team_skill WHERE code='thanh')
                  AND (SELECT count(DISTINCT trait_id) FROM hero_chess.hero WHERE code IN ('nguyen-nhac-advisor','nguyen-lu-advisor'))=2
                  AND (SELECT count(*) FROM hero_chess.hero WHERE code IN ('nguyen-nhac-general','nguyen-lu-general') AND trait_id IS NULL AND setup_points=0)=2
                  AND (SELECT setup_points FROM hero_chess.hero WHERE code='le-khoi-elephant')=2
                  AND (SELECT setup_points FROM hero_chess.hero WHERE code='tran-hung-dao-elephant')=4
                  AND (SELECT setup_points FROM hero_chess.hero WHERE code='ly-thuong-kiet-cannon')=7
                """, connection);
            Assert.Equal(true, await check.ExecuteScalarAsync());
            var armies = new[]
            {
                new[] { "tran-thanh-tong-general", "tran-thu-do-advisor", "tran-nhat-duat-advisor", "tran-binh-trong-elephant", "tran-hung-dao-elephant",
                    "tran-quang-khai-horse", "tran-quoc-toan-horse", "pham-ngu-lao-rook", "tran-khanh-du-rook", "tran-khat-chan-cannon", "yet-kieu-cannon" },
                new[] { "le-loi-general", "le-lai-advisor", "nguyen-trai-1-advisor", "le-khoi-elephant", "trung-trac-elephant", "le-sat-horse", "luu-nhan-chu-horse",
                    "ly-thuong-kiet-rook", "tran-nguyen-han-rook", "dinh-le-cannon", "nguyen-xi-cannon" },
                new[] { "quang-trung-general", "nguyen-nhac-advisor", "nguyen-lu-advisor", "le-van-hung-elephant", "bui-thi-xuan-elephant", "ly-van-bu-horse",
                    "vo-dinh-tu-horse", "tran-quang-dieu-rook", "vo-van-dung-rook", "nguyen-van-tuyet-cannon", "nguyen-van-loc-cannon" }
            };
            foreach (var army in armies)
            {
                await using var valid = new NpgsqlCommand("""
                    WITH picked AS (SELECT * FROM hero_chess.hero WHERE code=ANY(@codes))
                    SELECT (SELECT sum(setup_points)+5=50 AND count(*)=11 AND count(DISTINCT character_id)=11 FROM picked)
                    AND NOT EXISTS (SELECT 1 FROM hero_chess.chess_class c WHERE c.code<>'SOLDIER'
                        AND c.required_count<>(SELECT count(*) FROM picked p WHERE p.class_code=c.code))
                    """, connection);
                valid.Parameters.AddWithValue("codes", army);
                Assert.Equal(true, await valid.ExecuteScalarAsync());
            }
        });
    }

    [Fact]
    public async Task Advisor_patch_rejects_an_unexpected_trait_and_rolls_back_prices()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await SqlFile(connection, "02_seed_catalog.sql");
            await using (var mismatch = new NpgsqlCommand("""
                UPDATE hero_chess.hero SET setup_points=1 WHERE code='le-khoi-elephant';
                UPDATE hero_chess.hero SET trait_id=(SELECT trait_id FROM hero_chess.hero WHERE code='da-tuong-elephant') WHERE code='le-lai-advisor';
                """, connection)) await mismatch.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(() => SqlFile(connection, "19_hero_sp_and_advisor_traits.sql"));
            await using (var rollback = new NpgsqlCommand("ROLLBACK", connection)) await rollback.ExecuteNonQueryAsync();
            await using var check = new NpgsqlCommand("SELECT setup_points FROM hero_chess.hero WHERE code='le-khoi-elephant'", connection);
            Assert.Equal((short)1, await check.ExecuteScalarAsync());
        });
    }
}

public sealed partial class MatchFlowTests
{
    [Theory]
    [InlineData("le-lai-advisor", SkillKeys.LeLaiSwapGeneral, 8)]
    [InlineData("tran-thu-do-advisor", SkillKeys.TranThuDoGeneralShield, 6)]
    public async Task Advisor_command_uses_frozen_parameters_retries_once_and_replays_both_piece_states(string code, string key, int cooldown)
    {
        var (id, red, black) = await Ranked(false);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hero = await db.Heroes.Include(h => h.Trait).SingleAsync(h => h.Code == code);
        var redId = (await db.MatchParticipants.SingleAsync(p => p.MatchId == id && p.Side == "red")).PlayerId!.Value;
        if (!await db.PlayerHeroes.AnyAsync(p => p.PlayerId == redId && p.HeroId == hero.Id))
            db.PlayerHeroes.Add(new PlayerHero { PlayerId = redId, HeroId = hero.Id, AcquiredVia = "test", AcquiredAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var entries = red.Lineup.Entries.Select(e => e.SlotNo == 4 ? e with { HeroId = hero.Id } : e).ToArray();
        var updated = await Post<LineupDto>(red.Client, $"/api/v1/lineups/{red.Lineup.Id}",
            new SaveLineupRequest(red.Lineup.Name, red.Lineup.RulesetId, entries, red.Lineup.Skills, red.Lineup.Revision), HttpMethod.Put);
        await Select((red.Client, updated), id);
        (await red.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        (await black.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        var path = $"/api/v1/matches/{id}";
        var initial = (await red.Client.GetFromJsonAsync<MatchStateDto>(path + "/state", Json))!;
        var board = GameJson.Read<GameState>(GameJson.Document(initial.State));
        var source = board.Pieces.Single(p => p.HeroId == hero.Id && p.Side == Side.Red);
        var general = board.Pieces.Single(p => p.Class == PieceClass.General && p.Side == Side.Red);
        Assert.Equal(key, source.TraitImplementationKey);
        Assert.Equal(cooldown, source.TraitParameters!.Value.GetProperty("cooldownTurns").GetInt32());
        var parameters = hero.Trait!.Parameters;
        try
        {
            hero.Trait.Parameters = JsonDocument.Parse("{\"cooldownTurns\":20,\"durationTurns\":4}");
            await db.SaveChangesAsync();
            var invalid = new MatchCommandRequest(Guid.NewGuid(), 0, GameJson.Element(new
                { type = "hero_active", pieceId = source.PieceId, skillCode = key, target = new { pieceId = "invalid" } }));
            Assert.Equal(HttpStatusCode.BadRequest, (await red.Client.PostAsJsonAsync(path + "/commands", invalid, Json)).StatusCode);
            var request = new MatchCommandRequest(Guid.NewGuid(), 0, GameJson.Element(new
                { type = "hero_active", pieceId = source.PieceId, skillCode = key, target = new { pieceId = general.PieceId } }));
            var played = await Post<MatchCommandResultDto>(red.Client, path + "/commands", request);
            var after = played.Snapshot.State.Deserialize<GameState>(GameJson.Options)!;
            Assert.Equal(1, played.SequenceNo);
            Assert.Equal(1, after.CountedActions);
            Assert.Equal(Side.Black, after.SideToMove);
            Assert.Equal(cooldown - 1, after.Pieces.Single(p => p.PieceId == source.PieceId).TraitState[SkillKeys.HeroCooldownRemainingKey]);
            Assert.True((await Post<MatchCommandResultDto>(red.Client, path + "/commands", request)).Duplicate);
            Assert.Equal(key, played.ResolvedEvents[0].GetProperty("code").GetString());
            if (key == SkillKeys.LeLaiSwapGeneral)
            {
                Assert.Equal(source.Position, after.Pieces.Single(p => p.PieceId == general.PieceId).Position);
                Assert.Equal(general.Position, after.Pieces.Single(p => p.PieceId == source.PieceId).Position);
                Assert.Equal(2, played.ResolvedEvents.EnumerateArray().Count(e => e.GetProperty("type").GetString() == "piece.moved"));
            }
            else Assert.Equal(1, Assert.Single(after.Pieces.Single(p => p.PieceId == general.PieceId).Effects).RemainingTurns);
            var move = (await black.Client.GetFromJsonAsync<LegalMoveDto[]>(path + "/legal-actions", Json))!.First();
            var reply = await Post<MatchCommandResultDto>(black.Client, path + "/commands", Command(1, move));
            Assert.Empty(reply.Snapshot.State.Deserialize<GameState>(GameJson.Options)!.Pieces.Single(p => p.PieceId == general.PieceId).Effects);
            var replay = (await black.Client.GetFromJsonAsync<ReplayPageDto>(path + "/replay?afterSequence=0", Json))!;
            var recorded = replay.Entries.First(e => e.SequenceNo == 1).StateAfter.Deserialize<GameState>(GameJson.Options)!;
            Assert.Equal(cooldown, recorded.Pieces.Single(p => p.PieceId == source.PieceId).TraitParameters!.Value.GetProperty("cooldownTurns").GetInt32());
            await Post<MatchCommandResultDto>(red.Client, path + "/commands",
                new MatchCommandRequest(Guid.NewGuid(), 2, GameJson.Element(new { type = "resign" })));
        }
        finally { hero.Trait.Parameters = parameters; await db.SaveChangesAsync(); }
    }
}

public sealed class AdvisorTurnTests
{
    [Theory]
    [InlineData(SkillKeys.LeLaiSwapGeneral)]
    [InlineData(SkillKeys.TranThuDoGeneralShield)]
    public void Simulation_advances_once_and_can_be_read_back_without_changing_the_original(string key)
    {
        var state = State(key);
        var hero = state.Pieces.Single(p => p.Class == PieceClass.Advisor);
        var general = state.Pieces.Single(p => p.Class == PieceClass.General && p.Side == Side.Red);
        var action = Assert.Single(MatchTurnRules.HeroCandidates(state, new XiangqiRulesEngine()));
        var next = BotDifficultyTests.Engine().Simulate(state, action, BotDifficultyTests.Rules(), BotDifficultyTests.Lineups())!;
        Assert.Equal(1, next.Version);
        Assert.Equal(1, next.CountedActions);
        Assert.Equal(Side.Black, next.SideToMove);
        Assert.Equal(0, state.Version);
        Assert.Empty(hero.TraitState);
        Assert.Empty(general.Effects);
        var restored = GameJson.Read<GameState>(GameJson.Document(state));
        Assert.Equal(general.Position, restored.Pieces.Single(p => p.PieceId == general.PieceId).Position);
        Assert.Empty(restored.Pieces.Single(p => p.PieceId == hero.PieceId).TraitState);
    }

    [Fact]
    public void Swap_leaving_the_general_in_check_fails_without_consuming_a_turn()
    {
        var state = State(SkillKeys.LeLaiSwapGeneral);
        state.Pieces.Add(BotDifficultyTests.Piece(PieceClass.Rook, Side.Black, 3, 5, 7));
        var action = Assert.Single(MatchTurnRules.HeroCandidates(state, new XiangqiRulesEngine()));
        var applied = MatchTurnRules.ApplyHeroSkill(state, action, new XiangqiRulesEngine());
        Assert.Equal("KING_IN_CHECK", applied.Error!.Code);
        Assert.Same(state, applied.State);
    }

    [Fact]
    public void A_shield_skill_is_considered_before_declaring_checkmate()
    {
        var state = State(SkillKeys.TranThuDoGeneralShield);
        state.Pieces.Single(p => p.Class == PieceClass.Advisor).Effects.Add(new(FactionSkillHandler.Rooted, null, 2));
        state.Pieces.Single(p => p.Class == PieceClass.General && p.Side == Side.Red).Position = new(4, 0);
        state.Pieces.AddRange(new[] { BotDifficultyTests.Piece(PieceClass.Soldier, Side.Black, 4, 1, 1),
            BotDifficultyTests.Piece(PieceClass.Rook, Side.Black, 3, 1, 7), BotDifficultyTests.Piece(PieceClass.Rook, Side.Black, 5, 1, 7),
            BotDifficultyTests.Piece(PieceClass.Rook, Side.Black, 4, 3, 7) });
        Assert.Empty(new XiangqiRulesEngine().GenerateLegalActions(state));
        var dispatcher = new CommandSkillDispatcher(new CommandSkillRegistry(Array.Empty<ICommandSkillHandler>()));
        Assert.NotNull(MatchCommandService.FindLegalSkillAction(state, new XiangqiRulesEngine(), dispatcher));
        MatchTurnRules.Finish(state, BotDifficultyTests.Rules(), dispatcher);
        Assert.Null(state.Result);
        foreach (var level in new[] { "easy", "medium", "hard" })
        {
            var decision = BotDifficultyTests.Engine().Choose(Guid.NewGuid(), state, BotConfiguration.ForRequest("bot", level),
                BotDifficultyTests.Rules(), BotDifficultyTests.Lineups(), default);
            Assert.NotNull(decision.Action);
            Assert.Equal(SkillKeys.TranThuDoGeneralShield, decision.Action.Value.GetProperty("skillCode").GetString());
        }
    }

    private static GameState State(string key) => new()
    {
        PublicRiverStakes = true,
        Pieces = [new PieceState { PieceId = Guid.NewGuid(), Side = Side.Red, Class = PieceClass.Advisor,
            Position = new(3, 0), TraitKind = "active", TraitImplementationKey = key,
            TraitParameters = GameJson.Element(key == SkillKeys.LeLaiSwapGeneral ? new { cooldownTurns = 8, durationTurns = 0 } : new { cooldownTurns = 6, durationTurns = 2 }) },
            BotDifficultyTests.Piece(PieceClass.General, Side.Red, 4, 0, 0),
            BotDifficultyTests.Piece(PieceClass.General, Side.Black, 5, 9, 0)]
    };
}
