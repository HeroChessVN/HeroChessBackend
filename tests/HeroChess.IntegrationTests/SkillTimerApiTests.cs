using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class MatchFlowTests
{
    [Fact]
    public async Task Database_timers_reach_catalog_and_new_matches_but_cannot_change_a_frozen_match()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var skill = await db.TeamSkills.SingleAsync(s => s.ImplementationKey == SkillKeys.Khien);
        var original = skill.Parameters.RootElement.Clone();
        var originalCd = skill.CooldownTurns;
        try
        {
            skill.CooldownTurns = 17; skill.Parameters = GameJson.Document(new { durationTurns = 8 });
            await db.SaveChangesAsync();
            var (id, red, black) = await Ranked();
            var url = $"/api/v1/matches/{id}";
            var initial = (await red.Client.GetFromJsonAsync<MatchStateDto>(url + "/state", Json))!;
            Assert.Equal(2, Game(initial).SkillTimingVersion);
            var frozenSkill = Game(initial).SkillStates[Side.Red].Single(s => s.SkillId == skill.Id);
            Assert.Equal(17, frozenSkill.CooldownTurns);
            Assert.Equal(8, frozenSkill.Parameters!.Value.GetProperty("durationTurns").GetInt32());
            skill.CooldownTurns = 9; skill.Parameters = GameJson.Document(new { durationTurns = 2 });
            await db.SaveChangesAsync();
            var catalog = (await red.Client.GetFromJsonAsync<CatalogDto>("/api/v1/catalog", Json))!;
            Assert.Equal(9, catalog.TeamSkills.Single(s => s.Id == skill.Id).CooldownTurns);
            Assert.Equal(2, catalog.TeamSkills.Single(s => s.Id == skill.Id).Parameters.GetProperty("durationTurns").GetInt32());
            var piece = Game(initial).Pieces.First(p => p.Side == Side.Red && p.Class == PieceClass.Soldier);
            var command = new MatchCommandRequest(Guid.NewGuid(), initial.Version, GameJson.Element(new {
                type = "team_skill", slot = frozenSkill.SlotNo, target = new { pieceId = piece.PieceId } }));
            var cast = await Post<MatchCommandResultDto>(red.Client, url + "/commands", command);
            Assert.Equal(17, Game(cast.Snapshot).SkillStates[Side.Red].Single(s => s.SkillId == skill.Id).CooldownRemaining);
            Assert.Equal(8, Assert.Single(Game(cast.Snapshot).Pieces.Single(p => p.PieceId == piece.PieceId).Effects).RemainingTurns);
            Assert.True((await Post<MatchCommandResultDto>(red.Client, url + "/commands", command)).Duplicate);
            var afterEnemy = await AnyMove(black.Client, id);
            Assert.Equal(16, Game(afterEnemy.Snapshot).SkillStates[Side.Red].Single(s => s.SkillId == skill.Id).CooldownRemaining);
            Assert.Equal(7, Assert.Single(Game(afterEnemy.Snapshot).Pieces.Single(p => p.PieceId == piece.PieceId).Effects).RemainingTurns);
            var afterTimeout = await ExpireTurn(red.Client, id);
            Assert.Equal(15, Game(afterTimeout).SkillStates[Side.Red].Single(s => s.SkillId == skill.Id).CooldownRemaining);
            await Post<MatchCommandResultDto>(black.Client, url + "/commands",
                new MatchCommandRequest(Guid.NewGuid(), afterTimeout.Version, GameJson.Element(new { type = "resign" })));
        }
        finally
        {
            skill.CooldownTurns = originalCd; skill.Parameters = JsonDocument.Parse(original.GetRawText());
            await db.SaveChangesAsync();
        }
    }
}
