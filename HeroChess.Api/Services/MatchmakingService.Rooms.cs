using System.Security.Cryptography;
using System.Threading.RateLimiting;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed partial class MatchmakingService
{
    private const string RoomAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int RoomCodeLength = 8;
    private readonly PartitionedRateLimiter<Guid> _roomJoins = PartitionedRateLimiter.Create<Guid, Guid>(playerId =>
        RateLimitPartition.GetFixedWindowLimiter(playerId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));

    public Task<CustomRoomDto> CreateRoomAsync(Guid playerId, CancellationToken ct) =>
        MutateRoomAsync(async db =>
        {
            var existing = await FindRoomAsync(db, playerId, null, ct);
            if (existing is not null) return await RoomDtoAsync(db, existing, playerId, ct);
            await RequireAvailableAsync(db, playerId, ct);

            string code;
            do { code = RandomNumberGenerator.GetString(RoomAlphabet, RoomCodeLength); }
            while (await db.CustomRooms.AnyAsync(x => x.Code == code, ct));
            var now = DateTimeOffset.UtcNow;
            var room = new CustomRoom { Id = Guid.NewGuid(), Code = code, OwnerId = playerId, CreatedAt = now };
            room.Members.Add(new CustomRoomMember { RoomId = room.Id, PlayerId = playerId, Seat = 1, LastSeenAt = now });
            db.CustomRooms.Add(room);
            await db.SaveChangesAsync(ct);
            return await RoomDtoAsync(db, room, playerId, ct);
        }, ct);

    public async Task<CustomRoomDto?> ReadRoomAsync(Guid playerId, Guid? roomId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = await FindRoomAsync(db, playerId, roomId, ct);
        return room is null ? null : await RoomDtoAsync(db, room, playerId, ct);
    }

    public Task<CustomRoomDto> JoinRoomAsync(Guid playerId, string? code, CancellationToken ct)
    {
        code = code?.Trim().ToUpperInvariant();
        using var lease = _roomJoins.AttemptAcquire(playerId);
        if (!lease.IsAcquired) throw new ApiException(429, "ROOM_JOIN_LIMIT", "Wait before trying another room code.");
        if (code is null || code.Length != RoomCodeLength || code.Any(c => !RoomAlphabet.Contains(c))) throw RoomNotFound();
        return MutateRoomAsync(async db =>
        {
            var id = await db.CustomRooms.Where(x => x.Code == code && x.ClosedAt == null)
                .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            var room = id is Guid roomId ? await LockRoomAsync(db, roomId, ct) : throw RoomNotFound();
            if (room.OwnerId == playerId) throw new ApiException(409, "ROOM_SELF_JOIN", "You already own this room.");
            if (room.Members.Any(x => x.PlayerId == playerId)) return await RoomDtoAsync(db, room, playerId, ct);
            if (room.Members.Count >= 2 || await HasOpenMatchAsync(db, room, ct))
                throw new ApiException(409, "ROOM_FULL", "The room cannot accept another player.");
            await RequireAvailableAsync(db, playerId, ct);
            await RequireAvailableAsync(db, room.OwnerId, ct, allowRoom: true);

            var guest = new CustomRoomMember { RoomId = room.Id, PlayerId = playerId,
                Seat = room.Members.Single().Seat == 1 ? (short)2 : (short)1, LastSeenAt = DateTimeOffset.UtcNow };
            db.CustomRoomMembers.Add(guest);
            var match = await AddRoomMatchAsync(db, room, guest.PlayerId, ct);
            await db.SaveChangesAsync(ct);
            return BuildRoomDto(room, playerId, match.Id, match.Status);
        }, ct);
    }

    public Task<CustomRoomDto> RematchRoomAsync(Guid playerId, Guid roomId, Guid previousMatchId, Guid requestId, CancellationToken ct) =>
        MutateRoomAsync(async db =>
        {
            if (previousMatchId == Guid.Empty || requestId == Guid.Empty)
                throw new ApiException(400, "INVALID_REMATCH", "Previous match and request IDs are required.");
            var room = await LockRoomAsync(db, roomId, ct);
            RequireMember(room, playerId);
            if (room.OwnerId != playerId) throw new ApiException(403, "ROOM_OWNER_REQUIRED", "Only the room owner can start a rematch.");

            var replay = await db.CustomRoomRematches.AsNoTracking()
                .SingleOrDefaultAsync(x => x.RoomId == room.Id && x.RequestId == requestId, ct);
            if (replay is not null && replay.PreviousMatchId != previousMatchId)
                throw new ApiException(409, "REMATCH_REQUEST_CONFLICT", "This request ID was used for another match.");
            replay ??= await db.CustomRoomRematches.AsNoTracking()
                .SingleOrDefaultAsync(x => x.RoomId == room.Id && x.PreviousMatchId == previousMatchId, ct);
            if (replay is not null) return await RoomDtoAsync(db, room, playerId, ct, replay.MatchId);
            if (room.CurrentMatchId != previousMatchId || room.Members.Count != 2 || await HasOpenMatchAsync(db, room, ct))
                throw new ApiException(409, "ROOM_NOT_READY", "The room is not ready for a rematch.");
            var guest = room.Members.Single(x => x.PlayerId != playerId);
            await RequireAvailableAsync(db, playerId, ct, allowRoom: true);
            await RequireAvailableAsync(db, guest.PlayerId, ct, allowRoom: true);
            var match = await AddRoomMatchAsync(db, room, guest.PlayerId, ct);
            db.CustomRoomRematches.Add(new CustomRoomRematch
            {
                RoomId = room.Id, RequestId = requestId, PreviousMatchId = previousMatchId, MatchId = match.Id
            });
            await db.SaveChangesAsync(ct);
            return BuildRoomDto(room, playerId, match.Id, match.Status);
        }, ct);

    public async Task RoomPresenceAsync(Guid playerId, Guid roomId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var updated = await db.CustomRoomMembers.Where(x => x.RoomId == roomId && x.PlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), ct);
        if (updated == 0) throw RoomNotFound();
    }

    public Task LeaveRoomAsync(Guid playerId, Guid roomId, string? reason, CancellationToken ct) =>
        MutateRoomAsync(async db =>
        {
            var room = await LockRoomAsync(db, roomId, ct);
            var member = RequireMember(room, playerId);
            var status = await MatchStatusAsync(db, room.CurrentMatchId, ct);
            if (status == "active" && reason == "leave_room")
                throw new ApiException(409, "ROOM_MATCH_ACTIVE", "Finish the match before leaving the room.");

            if (room.Members.Count == 1 || room.OwnerId == playerId && reason == "end_session")
            {
                room.ClosedAt = DateTimeOffset.UtcNow;
                db.CustomRoomMembers.RemoveRange(room.Members);
            }
            else
            {
                if (room.OwnerId == playerId) room.OwnerId = room.Members.Single(x => x.PlayerId != playerId).PlayerId;
                db.CustomRoomMembers.Remove(member);
            }
            await db.SaveChangesAsync(ct);
            return true;
        }, ct, async (scope, db) =>
        {
            if (reason is not ("leave_room" or "end_session"))
                throw new ApiException(400, "INVALID_LEAVE_REASON", "Use leave_room or end_session.");
            var room = await FindRoomAsync(db, playerId, roomId, ct) ?? throw RoomNotFound();
            if (room.CurrentMatchId is not Guid matchId || await MatchStatusAsync(db, matchId, ct) != "selecting") return;
            // Selection owns its own transaction and match lock. Run it before holding a DB connection for the room.
            try { await scope.GetRequiredService<MatchSelectionLifetime>().CancelAsync(matchId, playerId, null, ct); }
            catch (ApiException error) when (error.Code == "MATCH_NOT_SELECTING" && reason == "end_session") { }
        });

    private async Task<T> MutateRoomAsync<T>(Func<AppDbContext, Task<T>> operation, CancellationToken ct,
        Func<IServiceProvider, AppDbContext, Task>? beforeTransaction = null)
    {
        await _admission.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (beforeTransaction is not null) await beforeTransaction(scope.ServiceProvider, db);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await operation(db);
            await tx.CommitAsync(ct);
            return result;
        }
        finally { _admission.Release(); }
    }

    private async Task RequireAvailableAsync(AppDbContext db, Guid playerId, CancellationToken ct, bool allowRoom = false)
    {
        // Room matches already hold players through the database membership and open-match check.
        if (!await db.Players.AnyAsync(x => x.Id == playerId && x.Status == "active", ct) ||
            _reservations.ContainsKey(playerId) ||
            await db.MatchParticipants.AnyAsync(p => p.PlayerId == playerId &&
                db.Matches.Any(m => m.Id == p.MatchId && (m.Status == "selecting" || m.Status == "active")), ct) ||
            !allowRoom && await db.CustomRoomMembers.AnyAsync(x => x.PlayerId == playerId, ct))
            throw new ApiException(409, "PLAYER_BUSY", "The player already has a room, queued or active match.");
    }

    private async Task<GameMatch> AddRoomMatchAsync(AppDbContext db, CustomRoom room, Guid guestId, CancellationToken ct)
    {
        var match = await NewMatchAsync(db, "custom", ct);
        match.Participants.Add(new MatchParticipant { MatchId = match.Id, Side = "red", PlayerId = room.OwnerId, ParticipantType = "human" });
        match.Participants.Add(new MatchParticipant { MatchId = match.Id, Side = "black", PlayerId = guestId, ParticipantType = "human" });
        db.Matches.Add(match);
        room.CurrentMatchId = match.Id;
        return match;
    }

    private static Task<CustomRoom?> FindRoomAsync(AppDbContext db, Guid playerId, Guid? roomId, CancellationToken ct) =>
        db.CustomRooms.AsNoTracking().Include(x => x.Members).SingleOrDefaultAsync(x => x.ClosedAt == null &&
            (roomId == null || x.Id == roomId) && x.Members.Any(m => m.PlayerId == playerId), ct);

    private static async Task<CustomRoom> LockRoomAsync(AppDbContext db, Guid roomId, CancellationToken ct)
    {
        var room = await db.CustomRooms.FromSqlInterpolated(
            $"SELECT * FROM hero_chess.custom_room WHERE id={roomId} AND closed_at IS NULL FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw RoomNotFound();
        await db.Entry(room).Collection(x => x.Members).LoadAsync(ct);
        return room;
    }

    private static CustomRoomMember RequireMember(CustomRoom room, Guid playerId) =>
        room.Members.SingleOrDefault(x => x.PlayerId == playerId) ?? throw RoomNotFound();

    private static ApiException RoomNotFound() => new(404, "ROOM_NOT_FOUND", "The room was not found.");

    private static Task<string?> MatchStatusAsync(AppDbContext db, Guid? matchId, CancellationToken ct) =>
        matchId is Guid id ? db.Matches.Where(x => x.Id == id).Select(x => (string?)x.Status).SingleOrDefaultAsync(ct) : Task.FromResult<string?>(null);

    private static async Task<bool> HasOpenMatchAsync(AppDbContext db, CustomRoom room, CancellationToken ct) =>
        await MatchStatusAsync(db, room.CurrentMatchId, ct) is "selecting" or "active";

    private static async Task<CustomRoomDto> RoomDtoAsync(AppDbContext db, CustomRoom room, Guid playerId, CancellationToken ct, Guid? replayId = null)
    {
        var last = replayId ?? room.CurrentMatchId;
        return BuildRoomDto(room, playerId, last, await MatchStatusAsync(db, last, ct));
    }

    private static CustomRoomDto BuildRoomDto(CustomRoom room, Guid playerId, Guid? last, string? matchStatus)
    {
        var playing = matchStatus is "selecting" or "active";
        var owner = room.Members.Single(x => x.PlayerId == room.OwnerId);
        return new(room.Id, room.Code, room.Members.Count, 2, playing ? matchStatus! : room.Members.Count == 2 ? "ready" : "waiting",
            room.OwnerId == playerId, playing ? last : null, last, owner.LastSeenAt >= DateTimeOffset.UtcNow.AddSeconds(-90));
    }
}
