// Vai trò file: Đường đọc state/legal actions có kiểm tra participant; không được tin matchId từ client là quyền truy cập.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Diagnostics;

namespace HeroChess.Api.Services;

public sealed class MatchReadService(AppDbContext db, TimeProvider clock, PlayerProfileService profiles, ILogger<MatchReadService> logger)
{
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
            MatchStateProjection.State(row.State.RootElement, result.Side == "red" ? Side.Red : Side.Black, heroNames), result.SettledAt, sides);
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
        await Authorize(playerId, matchId, ct);
        return Array.Empty<LegalMoveDto>();
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
            x.PlayerId == viewerId)).ToArray();
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
            state.TurnDeadlineAt, now, state.StateSchemaVersion, MatchStateProjection.State(state.State.RootElement, viewer), match.SettledAt);
}
