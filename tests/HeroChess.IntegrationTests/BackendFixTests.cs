using System.Text;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Rules;
using HeroChess.Rules.Effects;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class BackendFixTests
{
    [Fact]
    public async Task Match_gates_are_bounded_and_cancellation_does_not_block_next_waiter()
    {
        var registry = new MatchLockRegistry();
        var id = Guid.NewGuid();
        var gate = registry.For(id);
        Assert.Same(gate, registry.For(id));
        Assert.InRange(Enumerable.Range(0, 10_000).Select(_ => registry.For(Guid.NewGuid())).Distinct().Count(), 1, 4096);
        await gate.WaitAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancelled = gate.WaitAsync(cancellation.Token);
        var next = gate.WaitAsync(CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
        gate.Release();
        await next.WaitAsync(TimeSpan.FromSeconds(1));
        gate.Release();
        await gate.WaitAsync(CancellationToken.None);
        gate.Release();
    }

    [Theory]
    [InlineData("!")]
    [InlineData("broken")]
    [InlineData("-1|00000000-0000-0000-0000-000000000001")]
    [InlineData("3155378976000000000|00000000-0000-0000-0000-000000000001")]
    [InlineData("1|bad-guid")]
    public async Task Invalid_history_cursor_returns_400(string decoded)
    {
        var cursor = decoded == "!" ? decoded : Convert.ToBase64String(Encoding.UTF8.GetBytes(decoded));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options;
        await using var db = new AppDbContext(options);
        var error = await Assert.ThrowsAsync<ApiException>(() => new MatchHistoryService(db).ListAsync(Guid.NewGuid(), cursor, 10, CancellationToken.None));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("INVALID_CURSOR", error.Code);
        var auditError = await Assert.ThrowsAsync<ApiException>(() => new AdminService(db, TimeProvider.System).AuditAsync(cursor, 10, CancellationToken.None));
        Assert.Equal(400, auditError.StatusCode);
        Assert.Equal("INVALID_CURSOR", auditError.Code);
    }

    [Fact]
    public void Effect_snapshot_roundtrip_preserves_controllers_and_typed_payload()
    {
        var position = new BoardPoint(4, 0);
        var assigned = Guid.NewGuid();
        var state = new GameState();
        state.EffectInstances.Add(EffectFactory.Create(Guid.NewGuid(), SkillKeys.BinhLamThuyHien, Guid.NewGuid(),
            Side.Black, 1, 3, new[] { position }, new Dictionary<BoardPoint, Side> { [position] = Side.Red },
            new Dictionary<string, object?> { ["kind"] = "terrain", ["assignedPieceId"] = assigned }));
        using var stored = GameJson.Document(state);
        var restored = Assert.Single(GameJson.Read<GameState>(stored).EffectInstances);
        Assert.Equal(Side.Red, restored.PositionControllers[position]);
        Assert.Equal("terrain", restored.PayloadString("kind"));
        Assert.Equal(assigned, restored.PayloadGuid("assignedPieceId"));
        var projected = MatchStateProjection.State(stored.RootElement, Side.Red);
        Assert.Equal("red", projected.GetProperty("effectInstances")[0].GetProperty("positionControllers").GetProperty("4,0").GetString());
    }

    [Theory]
    [InlineData("{\"x\":\"4\",\"y\":0}")]
    [InlineData("{\"x\":null,\"y\":0}")]
    [InlineData("{\"x\":{},\"y\":0}")]
    [InlineData("{\"x\":9,\"y\":0}")]
    [InlineData("{\"x\":4,\"y\":\"0\"}")]
    [InlineData("{\"x\":4,\"y\":10}")]
    [InlineData("null")]
    public void Invalid_phan_ky_position_never_targets_entire_effect(string position)
    {
        var id = Guid.NewGuid();
        var invalid = JsonSerializer.Deserialize<JsonElement>($"{{\"effectId\":\"{id}\",\"position\":{position}}}");
        Assert.Null(RiverGeometry.TryParseEffectIdAndPosition(invalid).effectId);
        var omitted = JsonSerializer.SerializeToElement(new { effectId = id });
        Assert.Equal(id, RiverGeometry.TryParseEffectIdAndPosition(omitted).effectId);
    }
}
