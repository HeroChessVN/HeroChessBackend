// Vai trò file: Đường đọc state/legal actions có kiểm tra participant; không được tin matchId từ client là quyền truy cập.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HeroChess.Api.Services;

public sealed class MatchReadService(AppDbContext db, TimeProvider clock)
{
    // StateAsync: Authorize rồi đọc match/state hiện tại, báo chưa start nếu chưa có bàn cờ.
    public async Task<MatchStateDto> StateAsync(Guid playerId, Guid matchId, CancellationToken ct)
    {
        var participant = await db.MatchParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId && x.PlayerId == playerId, ct)
            ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var row = await db.MatchStates.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId, ct)
            ?? throw new ApiException(409, "MATCH_NOT_STARTED", "The match has not started.");
        var match = await db.Matches.AsNoTracking().SingleAsync(x => x.Id == matchId, ct);
        return ToDto(match, row, clock.GetUtcNow(), participant.Side == "red" ? Side.Red : Side.Black);
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
        var participant = await db.MatchParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId && x.PlayerId == playerId, ct)
            ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        var row = await db.MatchStates.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == matchId, ct)
            ?? throw new ApiException(409, "MATCH_NOT_STARTED", "The match has not started.");
        var state = StateSchemaUpgrade.UpgradeToCurrent(GameJson.Read<GameState>(row.State));
        var side = participant.Side == "red" ? Side.Red : Side.Black;
        if (state.SideToMove != side || !state.Pieces.Any(p => p.PieceId == pieceId && p.Side == side))
            return Array.Empty<LegalMoveDto>();
        return new XiangqiRulesEngine().GenerateQuangTrungSpecialMoves(state, pieceId)
            .Select(x => new LegalMoveDto(x.PieceId, x.From.X, x.From.Y, x.To.X, x.To.Y, x.CapturedPieceId)).ToArray();
    }

    // Authorize: Chỉ cho player có participant trong trận đi tiếp.
    public async Task Authorize(Guid playerId, Guid matchId, CancellationToken ct)
    {
        if (!await db.MatchParticipants.AsNoTracking().AnyAsync(x => x.MatchId == matchId && x.PlayerId == playerId, ct))
            throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
    }

    // ToDto: Gộp metadata trận, state, deadline và giờ server thành DTO đồng bộ client.
    public static MatchStateDto ToDto(GameMatch match, MatchState state, DateTimeOffset now, Side viewer) =>
        new(match.Id, match.Mode, match.Status, state.Version, state.SideToMove, state.TurnIndex, state.CountedActions,
            state.TurnDeadlineAt, now, state.StateSchemaVersion, MatchStateProjection.State(state.State.RootElement, viewer), match.SettledAt);
}
