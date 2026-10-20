// Vai trò file: Kiểm tra trait không phải movement không vô tình thay luật đi quân; sửa dữ liệu test và khôi phục trong finally.
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;
public sealed partial class MatchFlowTests
{
    [Theory]
    [InlineData("passive")]
    [InlineData("active")]
    // Non_movement_trait_keeps_base_moves_without_exposing_trait: Trait trong DB không đi vào trận mới.
    public async Task Non_movement_trait_keeps_base_moves_without_exposing_trait(string kind)
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
            Assert.False(piece.TryGetProperty("traitKind", out _));
            Assert.False(piece.TryGetProperty("movementImplementationKey", out _));
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
