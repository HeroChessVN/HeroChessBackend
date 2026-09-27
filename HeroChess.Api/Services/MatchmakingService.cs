// Vai trò file: Singleton giữ ticket/reservation trong RAM; khi ghép được thì lưu trận và participant vào PostgreSQL.
using System.Collections.Concurrent;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Services;

public sealed class MatchmakingService(IServiceScopeFactory scopes, IOptions<MatchmakingOptions> options,
    IOptions<MatchRuntimeOptions> runtime, CatalogVersionService catalogVersions, RatingPolicy ratings, RewardPolicy rewards)
{
    // Ticket: Ticket ghép trận mutable trong RAM; khác GameMatch đã lưu DB.
    private sealed record Ticket(Guid Id, Guid PlayerId, string Mode, int Elo, DateTimeOffset CreatedAt)
    {
        public string Status { get; set; } = "waiting";
        public Guid? MatchId { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Ticket> _tickets = new();
    private readonly Dictionary<Guid, Guid> _playerTickets = new();
    private readonly ConcurrentDictionary<Guid, Guid> _reservations = new();

    // CreateAsync: Chặn player bận; ghép ranked trong cửa sổ Elo hoặc tạo bot match, rồi trả ticket.
    public async Task<MatchmakingTicketDto> CreateAsync(Guid playerId, string mode, CancellationToken cancellationToken)
    {
        mode = mode.Trim().ToLowerInvariant();
        if (mode is not ("ranked" or "bot")) throw new ApiException(400, "INVALID_MODE", "Mode must be ranked or bot.");

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var busy = await db.MatchParticipants.AsNoTracking().AnyAsync(p => p.PlayerId == playerId &&
            db.Matches.Any(m => m.Id == p.MatchId && (m.Status == "selecting" || m.Status == "active")), cancellationToken);
        if (busy || _reservations.ContainsKey(playerId)) throw new ApiException(409, "PLAYER_BUSY", "The player already has a queued or active match.");
        var elo = await db.Ratings.AsNoTracking().Where(x => x.PlayerId == playerId).Select(x => x.Elo).SingleAsync(cancellationToken);

        Ticket ticket;
        Ticket? opponent = null;
        lock (_gate)
        {
            if (_playerTickets.ContainsKey(playerId) || !_reservations.TryAdd(playerId, Guid.Empty))
                throw new ApiException(409, "PLAYER_BUSY", "The player already has a queued or active match.");
            ticket = new(Guid.NewGuid(), playerId, mode, elo, DateTimeOffset.UtcNow);
            _tickets[ticket.Id] = ticket;
            _playerTickets[playerId] = ticket.Id;
            _reservations[playerId] = ticket.Id;
            if (mode == "ranked")
                opponent = _tickets.Values.Where(x => x.Id != ticket.Id && x.Mode == "ranked" && x.Status == "waiting" && Math.Abs(x.Elo - elo) <= options.Value.EloWindow)
                    .OrderBy(x => x.CreatedAt).FirstOrDefault();
            if (mode == "bot") ticket.Status = "matching";
            if (opponent is not null) { ticket.Status = "matching"; opponent.Status = "matching"; }
        }

        try
        {
            if (mode == "bot")
            {
                var matchId = await CreateMatchAsync(db, ticket, null, cancellationToken);
                MarkMatched(ticket, null, matchId);
            }
            else if (opponent is not null)
            {
                var matchId = await CreateMatchAsync(db, opponent, ticket, cancellationToken);
                MarkMatched(opponent, ticket, matchId);
            }
        }
        catch
        {
            Remove(ticket);
            if (opponent is not null) Remove(opponent);
            throw;
        }
        return ToDto(ticket);
    }

    // Get: Chỉ cho chủ ticket xem trạng thái ghép trận.
    public MatchmakingTicketDto Get(Guid playerId, Guid ticketId)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(ticketId, out var ticket) || ticket.PlayerId != playerId)
                throw new ApiException(404, "TICKET_NOT_FOUND", "The matchmaking ticket was not found.");
            return ToDto(ticket);
        }
    }

    // Cancel: Hủy ticket còn waiting; đã thành trận thì dùng cancel selection.
    public void Cancel(Guid playerId, Guid ticketId)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(ticketId, out var ticket) || ticket.PlayerId != playerId)
                throw new ApiException(404, "TICKET_NOT_FOUND", "The matchmaking ticket was not found.");
            if (ticket.Status != "waiting") throw new ApiException(409, "TICKET_ALREADY_MATCHED", "A matched ticket can no longer be cancelled.");
            ticket.Status = "cancelled";
            _playerTickets.Remove(playerId);
            _reservations.TryRemove(playerId, out _);
        }
    }

    // Release: Bỏ reservation của player sau settlement để có thể queue tiếp.
    public void Release(Guid playerId) => _reservations.TryRemove(playerId, out _);

    // CreateMatchAsync: Tạo selecting match, frozen ruleset/reward/rating và hai participant; bot không có player account.
    private async Task<Guid> CreateMatchAsync(AppDbContext db, Ticket redTicket, Ticket? blackTicket, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ruleset = await db.Rulesets.AsNoTracking().SingleAsync(x => x.IsActive, ct);
        var contentVersion = await catalogVersions.ComputeAsync(db, ct);
        var match = new GameMatch
        {
            Id = Guid.NewGuid(), Mode = blackTicket is null ? "bot" : "ranked", Status = "selecting", RulesetId = ruleset.Id,
            RulesetSnapshot = GameJson.Document(new RulesetSnapshot(ruleset.Code, ruleset.SetupBudget, ruleset.TurnSeconds, ruleset.ActionLimit,
                ruleset.Config.RootElement.Clone(), ratings.Version, rewards.Version, runtime.Value.EloKFactor,
                runtime.Value.WinnerCoinReward, runtime.Value.DrawCoinReward)), ContentVersion = contentVersion, CreatedAt = DateTimeOffset.UtcNow
        };
        var redElo = redTicket.Elo;
        match.Participants.Add(new MatchParticipant { MatchId = match.Id, Side = "red", PlayerId = redTicket.PlayerId, ParticipantType = "human", EloBefore = redElo });
        if (blackTicket is null)
            match.Participants.Add(new MatchParticipant { MatchId = match.Id, Side = "black", ParticipantType = "bot", BotConfig = GameJson.Document(new { strategy = "capture_sp_v1" }), EloBefore = 1000 });
        else
            match.Participants.Add(new MatchParticipant { MatchId = match.Id, Side = "black", PlayerId = blackTicket.PlayerId, ParticipantType = "human", EloBefore = blackTicket.Elo });
        db.Matches.Add(match);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return match.Id;
    }

    // MarkMatched: Gắn matchId vào ticket và chuyển reservation sang trận vừa tạo.
    private void MarkMatched(Ticket first, Ticket? second, Guid matchId)
    {
        lock (_gate)
        {
            foreach (var ticket in new[] { first, second }.Where(x => x is not null))
            {
                ticket!.Status = "matched"; ticket.MatchId = matchId; _reservations[ticket.PlayerId] = matchId;
                _playerTickets.Remove(ticket.PlayerId);
            }
        }
    }

    // Remove: Dọn ticket/reservation khi tạo trận thất bại.
    private void Remove(Ticket ticket)
    {
        lock (_gate) { _tickets.Remove(ticket.Id); _playerTickets.Remove(ticket.PlayerId); _reservations.TryRemove(ticket.PlayerId, out _); }
    }
    // ToDto: Chuyển ticket RAM thành DTO; không để client truy cập object nội bộ.
    private static MatchmakingTicketDto ToDto(Ticket x) => new(x.Id, x.Mode, x.Status, x.MatchId, x.CreatedAt);
}
