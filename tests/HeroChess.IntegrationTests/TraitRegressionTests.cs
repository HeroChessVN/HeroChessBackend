// Vai trò file: Kiểm tra trait không phải movement không vô tình thay luật đi quân; sửa dữ liệu test và khôi phục trong finally.
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Contracts;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;
public sealed partial class MatchFlowTests
{
    [Theory]
    [InlineData(FactionSkillHandler.Trap)]
    [InlineData(FactionSkillHandler.Forecast)]
    [InlineData(FactionSkillHandler.Rally)]
    public async Task Faction_skill_requires_five_members_excluding_soldiers_and_keeps_private_data_on_the_casting_side(string key)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var faction = new Faction { Id = Guid.NewGuid(), Code = $"test-{Guid.NewGuid():N}", Name = "Test faction" };
        var skill = new TeamSkill { Id = Guid.NewGuid(), Code = $"zz-{Guid.NewGuid():N}", Name = "Test faction skill",
            FactionId = faction.Id, ImplementationKey = key, IsEnabled = true, CooldownTurns = 8,
            Eligibility = JsonDocument.Parse("""{"minNonSoldierFactionHeroes":8,"keepThisSetting":true}""") };
        db.Factions.Add(faction);
        db.TeamSkills.Add(skill);
        await db.SaveChangesAsync();
        var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Database", "18_faction_skill_requirements.sql"));
        await db.Database.ExecuteSqlRawAsync(migration);
        await db.Database.ExecuteSqlRawAsync(migration);
        var migrated = (await db.TeamSkills.AsNoTracking().SingleAsync(s => s.Id == skill.Id)).Eligibility.RootElement;
        Assert.Equal(5, migrated.GetProperty("minNonSoldierFactionHeroes").GetInt32());
        Assert.True(migrated.GetProperty("keepThisSetting").GetBoolean());
        try
        {
            var (id, red, black) = await Ranked(false);
            var slots = red.Lineup.Skills.Select(s => new LineupSkillInput(s.SlotNo, s.SlotNo == 1 ? skill.Id : s.SkillId)).ToArray();
            var request = new SaveLineupRequest(red.Lineup.Name, red.Lineup.RulesetId, red.Lineup.Entries, slots, red.Lineup.Revision);
            var lineupUrl = $"/api/v1/lineups/{red.Lineup.Id}";
            using var rejected = await red.Client.PutAsJsonAsync(lineupUrl, request, Json);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            Assert.Contains("FACTION_REQUIREMENT_NOT_MET", await rejected.Content.ReadAsStringAsync());

            var heroIds = red.Lineup.Entries.Where(e => e.SlotNo < 12).Select(e => e.HeroId).Distinct().Take(5).ToArray();
            var soldiers = red.Lineup.Entries.Where(e => e.SlotNo >= 12).Select(e => e.HeroId).Distinct();
            db.HeroFactions.AddRange(heroIds.Take(4).Concat(soldiers).Select(heroId => new HeroFaction { HeroId = heroId, FactionId = faction.Id }));
            await db.SaveChangesAsync();
            using var fourHeroes = await red.Client.PutAsJsonAsync(lineupUrl, request, Json);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, fourHeroes.StatusCode);
            Assert.Contains("FACTION_REQUIREMENT_NOT_MET", await fourHeroes.Content.ReadAsStringAsync());
            db.HeroFactions.Add(new HeroFaction { HeroId = heroIds[4], FactionId = faction.Id });
            await db.SaveChangesAsync();
            skill.Eligibility = JsonDocument.Parse("""{"minNonSoldierFactionHeroes":6,"keepThisSetting":true}""");
            await db.SaveChangesAsync();
            var catalog = (await red.Client.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json))!;
            Assert.Equal(6, catalog.TeamSkills.Single(s => s.Id == skill.Id).Eligibility.GetProperty("minNonSoldierFactionHeroes").GetInt32());
            using var fiveBelowSix = await red.Client.PutAsJsonAsync(lineupUrl, request, Json);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, fiveBelowSix.StatusCode);
            Assert.Contains("FACTION_REQUIREMENT_NOT_MET", await fiveBelowSix.Content.ReadAsStringAsync());
            skill.Eligibility = JsonDocument.Parse("""{"minNonSoldierFactionHeroes":5,"keepThisSetting":true}""");
            await db.SaveChangesAsync();
            var lineup = await Post<LineupDto>(red.Client, lineupUrl, request, HttpMethod.Put);
            await Select((red.Client, lineup), id);
            (await red.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
            (await black.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();

            var action = JsonSerializer.SerializeToElement(new { type = "team_skill", slot = 1,
                target = new { positions = new[] { new { x = 2, y = 4 }, new { x = 4, y = 4 }, new { x = 6, y = 5 } } } });
            var url = $"/api/v1/matches/{id}";
            var played = await Post<MatchCommandResultDto>(red.Client, $"{url}/commands", new MatchCommandRequest(Guid.NewGuid(), 0, action));
            Assert.Equal("black", played.Snapshot.SideToMove);
            if (key == FactionSkillHandler.Trap)
            {
                Assert.Equal(3, played.Snapshot.State.GetProperty("riverSnares").GetArrayLength());
                var enemy = (await black.Client.GetFromJsonAsync<MatchStateDto>($"{url}/state", Json))!;
                Assert.Equal(0, enemy.State.GetProperty("riverSnares").GetArrayLength());
                var replay = (await black.Client.GetFromJsonAsync<ReplayPageDto>($"{url}/replay?afterSequence=0", Json))!;
                Assert.Equal(0, Assert.Single(replay.Entries).StateAfter.GetProperty("riverSnares").GetArrayLength());
            }
            else if (key == FactionSkillHandler.Forecast)
            {
                var forecast = $"{url}/forecast?version={played.SequenceNo}";
                var result = await red.Client.GetFromJsonAsync<JsonElement>(forecast, Json);
                Assert.Equal(played.SequenceNo, result.GetProperty("version").GetInt32());
                var repeated = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => red.Client.GetFromJsonAsync<JsonElement>(forecast, Json)));
                Assert.All(repeated, reply => Assert.True(JsonElement.DeepEquals(result, reply)));
                using var hidden = await black.Client.GetAsync(forecast);
                Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            }
            else
            {
                var soldiersAfterSkill = played.Snapshot.State.GetProperty("pieces").EnumerateArray()
                    .Where(p => p.GetProperty("side").GetString() == "red" && p.GetProperty("class").GetString() == "soldier").ToArray();
                Assert.Equal(5, soldiersAfterSkill.Length);
                Assert.All(soldiersAfterSkill, p => Assert.Contains(p.GetProperty("effects").EnumerateArray(),
                    e => e.GetProperty("code").GetString() == FactionSkillHandler.Rally));
            }
            await Post<MatchCommandResultDto>(black.Client, $"{url}/commands",
                new MatchCommandRequest(Guid.NewGuid(), played.SequenceNo, JsonSerializer.SerializeToElement(new { type = "resign" })));
        }
        finally
        {
            skill.IsEnabled = false;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Ranked_public_stake_is_frozen_and_announced_to_both_sides_once()
    {
        var (id, red, black) = await Ranked(false);
        Guid heroId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            heroId = await db.Heroes.Where(h => h.Code == "tran-hung-dao-elephant").Select(h => h.Id).SingleAsync();
            var redId = (await db.MatchParticipants.SingleAsync(p => p.MatchId == id && p.Side == "red")).PlayerId!.Value;
            if (!await db.PlayerHeroes.AnyAsync(p => p.PlayerId == redId && p.HeroId == heroId))
            {
                db.PlayerHeroes.Add(new PlayerHero { PlayerId = redId, HeroId = heroId, AcquiredVia = "test", AcquiredAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        var entries = red.Lineup.Entries.Select(e => e.SlotNo == 3 ? e with { HeroId = heroId, CosmeticId = null } : e).ToArray();
        var updated = await Post<LineupDto>(red.Client, $"/api/v1/lineups/{red.Lineup.Id}", new SaveLineupRequest(red.Lineup.Name,
            red.Lineup.RulesetId, entries, red.Lineup.Skills, red.Lineup.Revision), HttpMethod.Put);
        await Select((red.Client, updated), id);
        (await red.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        (await black.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        var initial = (await red.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
        var piece = initial.State.GetProperty("pieces").EnumerateArray().Single(p => p.GetProperty("heroId").GetGuid() == heroId);
        var pieceId = piece.GetProperty("pieceId").GetGuid();
        Assert.Equal("tran_hung_dao_tuong.coc", piece.GetProperty("traitImplementationKey").GetString());

        using var redSocket = await Connect(red.Client);
        using var blackSocket = await Connect(black.Client);
        await Subscribe(redSocket, id); await Subscribe(blackSocket, id);
        Assert.Equal("match.state", (await Receive(redSocket)).GetProperty("type").GetString());
        Assert.Equal("match.state", (await Receive(blackSocket)).GetProperty("type").GetString());
        var request = new MatchCommandRequest(Guid.NewGuid(), initial.Version, JsonSerializer.SerializeToElement(new
            { type = "hero_active", pieceId, skillCode = "tran_hung_dao_tuong.coc", target = new { position = new { x = 4, y = 5 } } }));
        var path = $"/api/v1/matches/{id}/commands";
        var accepted = await Post<MatchCommandResultDto>(red.Client, path, request);
        Assert.Equal(1, accepted.Snapshot.CountedActions);
        Assert.Equal("black", accepted.Snapshot.SideToMove);
        foreach (var socket in new[] { redSocket, blackSocket })
        {
            var update = await Receive(socket);
            Assert.Equal("match.changed", update.GetProperty("type").GetString());
            var payload = update.GetProperty("payload");
            Assert.Equal(accepted.SequenceNo, payload.GetProperty("version").GetInt32());
            Assert.Equal("Bạch Đằng Giang", payload.GetProperty("skillUsed").GetProperty("skillName").GetString());
        }
        Assert.True((await Post<MatchCommandResultDto>(red.Client, path, request)).Duplicate);
        var replay = (await black.Client.GetFromJsonAsync<ReplayPageDto>($"/api/v1/matches/{id}/replay?afterSequence=0", Json))!;
        var entry = Assert.Single(replay.Entries);
        Assert.Equal("hero_active", entry.Kind);
        var stake = Assert.Single(entry.StateAfter.GetProperty("obstacles").EnumerateArray());
        Assert.Equal(2, stake.GetProperty("remainingLifetime").GetInt32());
        Assert.Equal("red", entry.StateAfter.GetProperty("obstacleMetadata").GetProperty(stake.GetProperty("obstacleId").GetString()!).GetProperty("placer").GetString());
        await Post<MatchCommandResultDto>(black.Client, path, new MatchCommandRequest(Guid.NewGuid(), accepted.SequenceNo,
            JsonSerializer.SerializeToElement(new { type = "resign" })));
    }

    [Theory]
    [InlineData("passive")]
    [InlineData("active")]
    public async Task Non_movement_trait_is_frozen_and_keeps_base_moves(string kind)
    {
        var human = await CreatePlayerWithLineup();
        Guid heroId, traitId = Guid.NewGuid(); Guid? previousTrait;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            heroId = await db.LineupEntries.Where(x => x.LineupId == human.Lineup.Id && x.ClassCode == "SOLDIER").Select(x => x.HeroId).FirstAsync();
            var hero = await db.Heroes.SingleAsync(x => x.Id == heroId); previousTrait = hero.TraitId;
            db.HeroTraits.Add(new HeroTrait { Id = traitId, Code = $"review-{traitId:N}", Name = "Review trait", Kind = kind, ImplementationKey = "review.unimplemented" });
            hero.TraitId = traitId; await db.SaveChangesAsync();
        }
        try
        {
            var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
            var id = ticket.MatchId!.Value; await Select(human, id);
            (await human.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
            var state = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
            var piece = state.State.GetProperty("pieces").EnumerateArray().Single(x => x.GetProperty("heroId").GetGuid() == heroId && x.GetProperty("side").GetString() == "red");
            Assert.Equal(kind, piece.GetProperty("traitKind").GetString());
            Assert.Equal("review.unimplemented", piece.GetProperty("traitImplementationKey").GetString());
            Assert.Equal(JsonValueKind.Null, piece.GetProperty("movementImplementationKey").ValueKind);
            var moves = (await human.Client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{id}/legal-actions", Json))!;
            Assert.Contains(moves, x => x.PieceId == piece.GetProperty("pieceId").GetGuid());
            await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{id}/commands", new MatchCommandRequest(Guid.NewGuid(), 0, JsonSerializer.SerializeToElement(new { type = "resign" })));
        }
        finally
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Heroes.SingleAsync(x => x.Id == heroId)).TraitId = previousTrait;
            await db.SaveChangesAsync();
            await db.HeroTraits.Where(x => x.Id == traitId).ExecuteDeleteAsync();
        }
    }
}
