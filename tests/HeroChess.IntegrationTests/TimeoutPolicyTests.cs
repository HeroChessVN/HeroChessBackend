// Vai trò file: Regression cho mất lượt, AFK, đang chiếu và giới hạn action; gọi timeout với thời gian kiểm soát được.
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using HeroChess.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroChess.IntegrationTests;
public sealed partial class MatchFlowTests
{
    // ExpireTurn: Gọi timeout tại deadline, kiểm tra gọi lặp không tạo thêm action.
    private async Task<MatchStateDto> ExpireTurn(HttpClient client, Guid id)
    {
        var before = (await client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MatchCommandService>();
        Assert.True(await service.TimeoutAsync(id, before.Version, before.DeadlineAt!.Value, CancellationToken.None));
        Assert.False(await service.TimeoutAsync(id, before.Version, before.DeadlineAt.Value, CancellationToken.None));
        return (await client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
    }
    // AnyMove: Đọc legal moves rồi gửi một nước hợp lệ.
    private static async Task<MatchCommandResultDto> AnyMove(HttpClient client, Guid id)
    {
        var state = (await client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
        var moves = (await client.GetFromJsonAsync<LegalMoveDto[]>($"/api/v1/matches/{id}/legal-actions", Json))!;
        return await Post<MatchCommandResultDto>(client, $"/api/v1/matches/{id}/commands", Command(state.Version, moves[0]));
    }
    // Game: Deserialize state DTO sang GameState để assert.
    private static GameState Game(MatchStateDto dto) => dto.State.Deserialize<GameState>(GameJson.Options)!;

    [Fact]
    // Two_timeouts_on_same_players_turns_forfeit_and_settle_ranked_match: Hai lần hết giờ trên hai lượt riêng liên tiếp của cùng player dẫn đến AFK và settlement.
    public async Task Two_timeouts_on_same_players_turns_forfeit_and_settle_ranked_match()
    {
        var (id, red, black) = await Ranked();
        var before = (await black.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        var first = await ExpireTurn(red.Client, id);
        Assert.Equal("active", first.Status); Assert.Equal(1, first.CountedActions);
        Assert.Equal(1, Game(first).ConsecutiveTimeouts[Side.Red]); Assert.Equal(0, Game(first).ConsecutiveTimeouts[Side.Black]);
        var second = await ExpireTurn(black.Client, id);
        Assert.Equal("active", second.Status); Assert.Equal(1, Game(second).ConsecutiveTimeouts[Side.Black]);
        var terminal = await ExpireTurn(red.Client, id);
        Assert.Equal("completed", terminal.Status); Assert.Equal(3, terminal.CountedActions); Assert.Equal(3, terminal.TurnIndex);
        Assert.Equal("black_win", Game(terminal).Result); Assert.Equal("afk", Game(terminal).EndReason);
        Assert.Equal(2, Game(terminal).ConsecutiveTimeouts[Side.Red]); Assert.NotNull(terminal.SettledAt);
        var after = (await black.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        Assert.Equal(long.Parse(before.CoinBalance!) + 10, long.Parse(after.CoinBalance!));
        var replay = (await red.Client.GetFromJsonAsync<ReplayPageDto>($"/api/v1/matches/{id}/replay?limit=100", Json))!;
        Assert.Equal("afk", replay.Entries.Last().StateAfter.GetProperty("endReason").GetString());
    }

    [Fact]
    // Valid_move_resets_only_actors_afk_streak_and_invalid_command_does_not: Nước hợp lệ reset chuỗi AFK của actor; command lỗi không reset.
    public async Task Valid_move_resets_only_actors_afk_streak_and_invalid_command_does_not()
    {
        var (id, red, black) = await Ranked();
        await ExpireTurn(red.Client, id); await ExpireTurn(black.Client, id);
        var bad = new MatchCommandRequest(Guid.NewGuid(), 2, JsonSerializer.SerializeToElement(new { type = "move", pieceId = Guid.NewGuid(), to = new { x = 0, y = 0 } }));
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, (await red.Client.PostAsJsonAsync($"/api/v1/matches/{id}/commands", bad, Json)).StatusCode);
        var unchanged = (await red.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
        Assert.Equal(1, Game(unchanged).ConsecutiveTimeouts[Side.Red]);
        var moved = await AnyMove(red.Client, id);
        Assert.Equal(0, Game(moved.Snapshot).ConsecutiveTimeouts[Side.Red]); Assert.Equal(1, Game(moved.Snapshot).ConsecutiveTimeouts[Side.Black]);
        await AnyMove(black.Client, id);
        var timeoutAgain = await ExpireTurn(red.Client, id);
        Assert.Equal("active", timeoutAgain.Status); Assert.Equal(1, Game(timeoutAgain).ConsecutiveTimeouts[Side.Red]);
        await Post<MatchCommandResultDto>(black.Client, $"/api/v1/matches/{id}/commands", new MatchCommandRequest(Guid.NewGuid(), timeoutAgain.Version, JsonSerializer.SerializeToElement(new { type = "resign" })));
    }

    [Theory]
    [InlineData(true, 0, "black_win", "timeout_in_check")]
    [InlineData(false, 1, "black_win", "afk")]
    [InlineData(false, 0, "red_win", "action_limit_sp")]
    // Timeout_on_action_150_prioritizes_direct_loss_before_remaining_sp: Ở action 150, thua vì chiếu/AFK được xét trước so SP.
    public async Task Timeout_on_action_150_prioritizes_direct_loss_before_remaining_sp(bool check, int streak, string result, string reason)
    {
        var (id, red, _) = await Ranked();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.MatchStates.SingleAsync(x => x.MatchId == id);
            var state = GameJson.Read<GameState>(row.State);
            state.CountedActions = 149; state.TurnIndex = 149;
            state.ConsecutiveTimeouts[Side.Red] = streak;
            state.Pieces = new()
            {
                new() { PieceId = Guid.NewGuid(), Side = Side.Red, Class = PieceClass.General, Position = new(4, 0), SetupPoints = 0 },
                new() { PieceId = Guid.NewGuid(), Side = Side.Black, Class = PieceClass.General, Position = new(3, 9), SetupPoints = 0 },
                new() { PieceId = Guid.NewGuid(), Side = Side.Red, Class = PieceClass.Soldier, Position = new(0, 3), SetupPoints = 100 },
                new() { PieceId = Guid.NewGuid(), Side = Side.Black, Class = PieceClass.Rook, Position = check ? new(4, 2) : new(0, 8), SetupPoints = 1 }
            };
            row.CountedActions = state.CountedActions; row.TurnIndex = state.TurnIndex; row.State = GameJson.Document(state);
            await db.SaveChangesAsync();
        }
        var terminal = await ExpireTurn(red.Client, id);
        Assert.Equal("completed", terminal.Status); Assert.Equal(150, terminal.CountedActions); Assert.Equal(150, terminal.TurnIndex);
        Assert.Equal(result, Game(terminal).Result); Assert.Equal(reason, Game(terminal).EndReason); Assert.NotNull(terminal.SettledAt);
    }

    [Fact]
    // Older_snapshots_default_afk_streak_to_zero_and_clone_preserves_it_independently: Snapshot cũ mặc định streak 0 và clone không dùng chung dictionary.
    public void Older_snapshots_default_afk_streak_to_zero_and_clone_preserves_it_independently()
    {
        var old = JsonSerializer.Deserialize<GameState>("{\"stateSchemaVersion\":2}", GameJson.Options)!;
        Assert.Equal(0, old.ConsecutiveTimeouts[Side.Red]); Assert.Equal(0, old.ConsecutiveTimeouts[Side.Black]);
        old.ConsecutiveTimeouts[Side.Red] = 1;
        var clone = old.Clone(); clone.ConsecutiveTimeouts[Side.Red] = 0;
        Assert.Equal(1, old.ConsecutiveTimeouts[Side.Red]);
        var replayed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(old, GameJson.Options), GameJson.Options)!;
        Assert.Equal(1, replayed.ConsecutiveTimeouts[Side.Red]);
    }
}
