using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static HeroChess.IntegrationTests.MatchFlowTests;

namespace HeroChess.IntegrationTests;

public sealed class ReadCapacityTests(HeroChessFactory factory) : IClassFixture<HeroChessFactory>
{
    private sealed class Reads : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Count++; return ValueTask.FromResult(result); }
    }
    private static AppDbContext Context(Reads reads) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("HERO_CHESS_TEST_DB")).AddInterceptors(reads).Options);

    [Fact]
    public async Task Modern_state_uses_three_reads_and_legacy_names_are_batched_cached_and_expire()
    {
        var human = await new MatchFlowTests(factory).CreatePlayerWithLineup();
        var me = (await human.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        var ticket = await Post<MatchmakingTicketDto>(human.Client, "/api/v1/matchmaking/tickets", new CreateMatchmakingTicketRequest("bot"));
        var id = ticket.MatchId!.Value;
        await Select(human, id);
        (await human.Client.PostAsync($"/api/v1/matches/{id}/confirm", null)).EnsureSuccessStatusCode();
        try
        {
            var count = new Reads(); await using var db = Context(count);
            var clock = new NameClock(); using var names = new HeroNameLookup(clock);
            var read = new MatchReadService(db, TimeProvider.System, new PlayerProfileService(db), NullLogger<MatchReadService>.Instance, names);
            var state = await read.StateAsync(me.UserId, id, default);
            Assert.Equal(3, count.Count);
            Assert.All(state.State.GetProperty("pieces").EnumerateArray(), p => Assert.False(string.IsNullOrEmpty(p.GetProperty("heroName").GetString())));
            count.Count = 0;
            await Assert.ThrowsAsync<ApiException>(() => read.StateAsync(Guid.NewGuid(), id, default));
            Assert.Equal(1, count.Count);

            var hero = await db.Heroes.AsNoTracking().FirstAsync();
            var legacy = GameJson.Element(new { pieces = new[] { new { heroId = hero.Id }, new { heroId = hero.Id } } });
            count.Count = 0;
            Assert.Equal(hero.Name, (await names.MissingAsync(db, [legacy, legacy], default))[hero.Id]);
            Assert.Equal(1, count.Count);
            Assert.Equal(hero.Name, (await names.MissingAsync(db, [legacy], default))[hero.Id]);
            Assert.Equal(1, count.Count);
            clock.Now = clock.Now.AddSeconds(61);
            await names.MissingAsync(db, [legacy], default);
            Assert.Equal(2, count.Count);
            count.Count = 0;
            Assert.Empty(await names.MissingAsync(db, [GameJson.Element(new { pieces = new[] { new { heroId = hero.Id, heroName = "Frozen name" } } })], default));
            Assert.Equal(0, count.Count);
        }
        finally
        {
            var state = (await human.Client.GetFromJsonAsync<MatchStateDto>($"/api/v1/matches/{id}/state", Json))!;
            await Post<MatchCommandResultDto>(human.Client, $"/api/v1/matches/{id}/commands",
                new MatchCommandRequest(Guid.NewGuid(), state.Version, GameJson.Element(new { type = "resign" })));
        }
    }

    [Fact]
    public async Task Joined_profiles_keep_missing_rating_override_and_avatar_url_without_caching_wallets()
    {
        var human = await new MatchFlowTests(factory).CreatePlayerWithLineup();
        var me = (await human.Client.GetFromJsonAsync<MeDto>("/api/v1/me", Json))!;
        var count = new Reads(); await using var db = Context(count);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Ratings.Where(p => p.PlayerId == me.UserId).ExecuteDeleteAsync();
        var player = await db.Players.SingleAsync(p => p.Id == me.UserId);
        player.AvatarPath = "test.png"; await db.SaveChangesAsync();
        var http = new DefaultHttpContext(); http.Request.Scheme = "https"; http.Request.Host = new HostString("game.example");
        var profiles = new PlayerProfileService(db, new HttpContextAccessor { HttpContext = http }, new ConfigurationBuilder().Build(),
            factory.Services.GetRequiredService<IHostEnvironment>());
        count.Count = 0;
        var result = await profiles.ForPlayersAsync([me.UserId, me.UserId, Guid.NewGuid()], null, default);
        Assert.Equal(1, count.Count); Assert.Single(result);
        Assert.Equal(0, result[me.UserId].Elo);
        Assert.Equal("https://game.example/avatars/test.png", result[me.UserId].AvatarUrl);
        Assert.Equal(1234, (await profiles.ForPlayersAsync([me.UserId], new Dictionary<Guid, int> { [me.UserId] = 1234 }, default))[me.UserId].Elo);
        count.Count = 0;
        Assert.Empty(await profiles.ForPlayersAsync([], null, default)); Assert.Equal(0, count.Count);
        await transaction.RollbackAsync();
    }

    private sealed class NameClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
