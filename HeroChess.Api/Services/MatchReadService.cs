// Vai trò file: Đường đọc state/legal actions có kiểm tra participant; không được tin matchId từ client là quyền truy cập.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using HeroChess.Rules.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.RateLimiting;

namespace HeroChess.Api.Services;

public sealed class MatchReadService(AppDbContext db, TimeProvider clock, PlayerProfileService profiles, ILogger<MatchReadService> logger)
{
    private static readonly MemoryCache ForecastCache = new(new MemoryCacheOptions { SizeLimit = 128 });
    private static readonly ConcurrencyLimiter ForecastLimiter = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = 1, QueueLimit = 4, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });

    public async Task<object> ForecastAsync(Guid playerId, Guid matchId, int version, CommandSkillDispatcher dispatcher, CancellationToken ct)
    {
        var key = (playerId, matchId, version);
        if (ForecastCache.TryGetValue<JsonElement>(key, out var cached)) return cached;
        using var lease = await ForecastLimiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) throw new ApiException(429, "FORECAST_BUSY", "The forecast worker is busy.");
        if (ForecastCache.TryGetValue<JsonElement>(key, out cached)) return cached;

        var actor = await db.MatchParticipants.AsNoTracking().SingleOrDefaultAsync(p => p.MatchId == matchId && p.PlayerId == playerId, ct)
            ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var action = await db.MatchActions.AsNoTracking().SingleOrDefaultAsync(a => a.MatchId == matchId && a.SequenceNo == version &&
            a.Kind == "team_skill" && a.ActorSide == actor.Side, ct);
        if (action is null || !action.ResolvedEvents.RootElement.EnumerateArray().Any(e =>
            e.TryGetProperty("type", out var type) && type.GetString() == "team_skill.activated" &&
            e.TryGetProperty("code", out var code) && code.GetString() == FactionSkillHandler.Forecast))
            throw new ApiException(404, "FORECAST_NOT_FOUND", "No forecast was activated by this player at that version.");
        if (!await db.MatchStates.AnyAsync(s => s.MatchId == matchId && s.Version == version, ct))
            throw new ApiException(409, "STALE_STATE", "The board has already changed.");
        var match = await db.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId, ct);
        var participants = await db.MatchParticipants.AsNoTracking().Where(p => p.MatchId == matchId).ToListAsync(ct);
        var lineups = participants.ToDictionary(p => p.Side == "red" ? Side.Red : Side.Black, p => GameJson.Read<FrozenLineup>(p.LineupSnapshot));
        var state = GameJson.Read<GameState>(action.StateAfter);
        var side = actor.Side == "red" ? Side.Red : Side.Black;
        state.RiverSnares.RemoveAll(trap => trap.Owner != side);
        var prediction = new BotEngine(dispatcher).Forecast(state, GameJson.Read<RulesetSnapshot>(match.RulesetSnapshot), lineups, ct);
        var result = GameJson.Element(new { version, prediction });
        ForecastCache.Set(key, result, new MemoryCacheEntryOptions
        {
            Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        });
        return result;
    }
    // StateAsync: Authorize rồi đọc match/state hiện tại, báo chưa start nếu chưa có bàn cờ.
    public async Task<MatchStateDto> StateAsync(Guid playerId, Guid matchId, CancellationToken ct)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var result = await (from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
            join state in db.MatchStates.AsNoTracking() on match.Id equals state.MatchId into states
            from state in states.DefaultIfEmpty()
            where participant.MatchId == matchId && participant.PlayerId == playerId
            select new { match.Id, match.Mode, match.Status, match.SettledAt, State = state, participant.Side }).SingleOrDefaultAsync(ct)
            ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        if (result.State is null) throw new ApiException(409, "MATCH_NOT_STARTED", "The match has not started.");
        var queryMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var row = result.State;
        var sides = await SidesAsync(matchId, playerId, ct);
        var heroNames = await HeroNamesAsync(row.State.RootElement, ct);
        var dto = new MatchStateDto(result.Id, result.Mode, result.Status, row.Version, row.SideToMove, row.TurnIndex,
            row.CountedActions, row.TurnDeadlineAt, clock.GetUtcNow(), row.StateSchemaVersion,
            MatchStateProjection.State(row.State.RootElement, result.Side == "red" ? Side.Red : Side.Black, heroNames), result.SettledAt, sides, sides.FirstOrDefault(x => x.IsBot)?.Bot);
        logger.LogDebug("match.state timing match={MatchId} version={Version} queryMs={QueryMs:F1} totalMs={TotalMs:F1}",
            matchId, row.Version, queryMs, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return dto;
    }

    // LegalAsync: Sinh nước hợp lệ từ snapshot server bằng Rules chung; không ghi state.
    public async Task<IReadOnlyList<LegalMoveDto>> LegalAsync(Guid playerId, Guid matchId, CancellationToken ct)
    {
        await Authorize(playerId, matchId, ct);
        var row = await db.MatchStates.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId, ct)
            ?? throw new ApiException(409, "MATCH_NOT_STARTED", "The match has not started.");
        var state = GameJson.Read<GameState>(row.State);
        // Phase 3.4: v3 → v4 state schema upgrade. Idempotent. Ensures GenerateLegalActions works with v3 state.
        state = StateSchemaUpgrade.UpgradeToCurrent(state);
        return new XiangqiRulesEngine().GenerateLegalActions(state).Select(x => new LegalMoveDto(x.PieceId, x.From.X, x.From.Y, x.To.X, x.To.Y, x.CapturedPieceId)).ToArray();
    }

    public async Task<IReadOnlyList<LegalMoveDto>> HeroActionsAsync(Guid playerId, Guid matchId, Guid pieceId, CancellationToken ct)
    {
        var side = await db.MatchParticipants.AsNoTracking().Where(x => x.MatchId == matchId && x.PlayerId == playerId)
            .Select(x => x.Side).SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var row = await db.MatchStates.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId, ct);
        if (row is null || row.SideToMove != side) return [];
        var state = StateSchemaUpgrade.UpgradeToCurrent(GameJson.Read<GameState>(row.State));
        if (!state.PublicRiverStakes) return [];
        return new XiangqiRulesEngine().GenerateQuangTrungSpecialMoves(state, pieceId)
            .Select(x => new LegalMoveDto(x.PieceId, x.From.X, x.From.Y, x.To.X, x.To.Y, x.CapturedPieceId)).ToArray();
    }

    // Authorize: Chỉ cho player có participant trong trận đi tiếp.
    public async Task Authorize(Guid playerId, Guid matchId, CancellationToken ct)
    {
        if (!await db.MatchParticipants.AsNoTracking().AnyAsync(x => x.MatchId == matchId && x.PlayerId == playerId, ct))
            throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
    }

    // SidesAsync: Hồ sơ người chơi của cả hai phe; phe bot chỉ có isBot.
    public async Task<IReadOnlyList<MatchSidePlayerDto>> SidesAsync(Guid matchId, Guid viewerId, CancellationToken ct)
    {
        var participants = await db.MatchParticipants.AsNoTracking().Where(x => x.MatchId == matchId).OrderBy(x => x.Side).ToListAsync(ct);
        var playerIds = participants.Where(x => x.PlayerId is not null).Select(x => x.PlayerId!.Value).ToArray();
        var playerProfiles = await profiles.ForPlayersAsync(playerIds, null, ct);
        return participants.Select(x => new MatchSidePlayerDto(x.Side, x.ParticipantType == "bot",
            x.PlayerId is not null && playerProfiles.TryGetValue(x.PlayerId.Value, out var profile) ? profile : null,
            x.PlayerId == viewerId, BotConfiguration.Client(x.BotConfig))).ToArray();
    }

    // HeroNamesAsync: Tên tướng theo heroId để bù cho quân lưu từ state cũ chưa có heroName.
    private async Task<Dictionary<Guid, string>> HeroNamesAsync(JsonElement stored, CancellationToken ct)
    {
        var heroIds = stored.TryGetProperty("pieces", out var pieces) && pieces.ValueKind == JsonValueKind.Array
            ? pieces.EnumerateArray().Where(p => p.TryGetProperty("heroId", out _)).Select(p => p.GetProperty("heroId").GetGuid()).Distinct().ToArray()
            : [];
        if (heroIds.Length == 0) return [];
        return await db.Heroes.AsNoTracking().Where(x => heroIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    }

    // ToDto: Gộp metadata trận, state, deadline và giờ server thành DTO đồng bộ client.
    public static MatchStateDto ToDto(GameMatch match, MatchState state, DateTimeOffset now, Side viewer) =>
        new(match.Id, match.Mode, match.Status, state.Version, state.SideToMove, state.TurnIndex, state.CountedActions,
            state.TurnDeadlineAt, now, state.StateSchemaVersion, MatchStateProjection.State(state.State.RootElement, viewer), match.SettledAt, Bot: BotConfiguration.Client(match.Participants.FirstOrDefault(p => p.ParticipantType == "bot")?.BotConfig));
}
