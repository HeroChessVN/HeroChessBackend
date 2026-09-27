// Vai trò file: Chọn và khóa đội hình; giữ kín hero đối thủ cho tới khi cả hai confirm, rồi tạo bàn cờ frozen.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using HeroChess.Rules;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class MatchSelectionService(AppDbContext db, LineupValidator validator, MatchLockRegistry locks, TimeProvider clock,
    CatalogVersionService catalogVersions)
{
    // SelectAsync: Kiểm tra quyền/revision/ruleset, validate và snapshot đội hình; bot dùng bản sao đội hình người chơi.
    public async Task<MatchSelectionDto> SelectAsync(Guid playerId, Guid matchId, SelectLineupRequest request, CancellationToken ct)
    {
        var gate = locks.For(matchId); await gate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var match = await LoadLockedMatch(matchId, ct);
            var participant = RequireHuman(match, playerId);
            if (match.Status != "selecting") throw new ApiException(409, "MATCH_NOT_SELECTING", "The match is no longer accepting selections.");
            if (participant.ConfirmedAt is not null) throw new ApiException(409, "SELECTION_LOCKED", "The confirmed selection cannot be changed.");
            var lineup = await db.Lineups.AsNoTracking().Include(x => x.Entries).Include(x => x.Skills)
                .SingleOrDefaultAsync(x => x.Id == request.LineupId && x.PlayerId == playerId, ct)
                ?? throw new ApiException(404, "LINEUP_NOT_FOUND", "The lineup was not found.");
            if (lineup.Revision != request.ExpectedRevision) throw new ApiException(409, "STALE_REVISION", "The lineup revision has changed.");
            if (lineup.RulesetId != match.RulesetId) throw new ApiException(422, "RULESET_MISMATCH", "The lineup uses a different ruleset.");
            var input = new SaveLineupRequest(lineup.Name, lineup.RulesetId,
                lineup.Entries.Select(x => new LineupEntryInput(x.SlotNo, x.HeroId, x.CosmeticId)).ToArray(),
                lineup.Skills.Select(x => new LineupSkillInput(x.SlotNo, x.TeamSkillId)).ToArray(), lineup.Revision);
            var validation = await validator.ValidateAsync(playerId, input, ct);
            if (validation.Errors.Count > 0) throw new ApiException(422, "INVALID_LINEUP", "The lineup is no longer valid.", validation.Errors);
            var frozen = await Freeze(lineup, validation.TotalSp, ct);
            participant.SourceLineupId = lineup.Id;
            participant.SourceLineupRevision = lineup.Revision;
            participant.LineupSnapshot = GameJson.Document(frozen);

            if (match.Mode == "bot")
            {
                var bot = match.Participants.Single(x => x.ParticipantType == "bot");
                bot.LineupSnapshot = GameJson.Document(frozen with { LineupId = Guid.Empty, Revision = 1 });
                bot.ConfirmedAt = clock.GetUtcNow();
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToDto(match, playerId);
        }
        finally { gate.Release(); }
    }

    // ConfirmAsync: Participant đã khóa thì trả lại trạng thái; còn lại kiểm tra catalog và lineup, khóa rồi start nếu đủ hai bên.
    public async Task<MatchSelectionDto> ConfirmAsync(Guid playerId, Guid matchId, CancellationToken ct)
    {
        var gate = locks.For(matchId); await gate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var match = await LoadLockedMatch(matchId, ct);
            var participant = RequireHuman(match, playerId);
            if (match.Status == "active") { await transaction.CommitAsync(ct); return ToDto(match, playerId); }
            if (match.Status != "selecting") throw new ApiException(409, "MATCH_NOT_SELECTING", "The match cannot be confirmed.");
            if (participant.ConfirmedAt is not null) { await transaction.CommitAsync(ct); return ToDto(match, playerId); }
            if (participant.SourceLineupId is null) throw new ApiException(422, "SELECTION_REQUIRED", "Select a lineup before confirming.");
            if (match.ContentVersion != await catalogVersions.ComputeAsync(db, ct))
                throw new ApiException(409, "CONTENT_VERSION_CHANGED", "Catalog content changed. Cancel this selection and create a new match.");
            var lineup = await db.Lineups.AsNoTracking().Include(x => x.Entries).Include(x => x.Skills)
                .SingleOrDefaultAsync(x => x.Id == participant.SourceLineupId && x.PlayerId == playerId, ct)
                ?? throw new ApiException(422, "SELECTED_LINEUP_MISSING", "The selected lineup no longer exists.");
            if (lineup.Revision != participant.SourceLineupRevision) throw new ApiException(409, "STALE_REVISION", "The selected lineup revision has changed.");
            var input = new SaveLineupRequest(lineup.Name, lineup.RulesetId,
                lineup.Entries.Select(x => new LineupEntryInput(x.SlotNo, x.HeroId, x.CosmeticId)).ToArray(),
                lineup.Skills.Select(x => new LineupSkillInput(x.SlotNo, x.TeamSkillId)).ToArray(), lineup.Revision);
            var validation = await validator.ValidateAsync(playerId, input, ct);
            if (validation.Errors.Count > 0) throw new ApiException(422, "INVALID_LINEUP", "The selected lineup is no longer valid.", validation.Errors);
            participant.LineupSnapshot = GameJson.Document(await Freeze(lineup, validation.TotalSp, ct));
            if (match.Mode == "bot")
            {
                var bot = match.Participants.Single(x => x.ParticipantType == "bot");
                var frozen = GameJson.Read<FrozenLineup>(participant.LineupSnapshot);
                bot.LineupSnapshot = GameJson.Document(frozen with { LineupId = Guid.Empty, Revision = 1 });
            }
            participant.ConfirmedAt ??= clock.GetUtcNow();
            if (match.Participants.All(x => x.ConfirmedAt is not null)) Start(match);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToDto(match, playerId);
        }
        finally { gate.Release(); }
    }

    // GetAsync: Đọc selection đã lọc dữ liệu theo người xem.
    public async Task<MatchSelectionDto> GetAsync(Guid playerId, Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches.AsNoTracking().Include(x => x.Participants).SingleOrDefaultAsync(x => x.Id == matchId, ct)
            ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");
        _ = RequireHuman(match, playerId);
        return ToDto(match, playerId);
    }

    // LoadLockedMatch: Lấy match cùng participant dưới row lock PostgreSQL để serialize mutation.
    private async Task<GameMatch> LoadLockedMatch(Guid id, CancellationToken ct) =>
        await db.Matches.FromSqlInterpolated($"SELECT * FROM hero_chess.game_match WHERE id={id} FOR UPDATE")
            .Include(x => x.Participants).SingleOrDefaultAsync(ct)
        ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");

    // RequireHuman: Tìm participant thuộc player đang gọi; người ngoài nhận MATCH_NOT_FOUND.
    private static MatchParticipant RequireHuman(GameMatch match, Guid playerId) =>
        match.Participants.SingleOrDefault(x => x.PlayerId == playerId)
        ?? throw new ApiException(404, "MATCH_NOT_FOUND", "The match was not found.");

    // Freeze: Sao chép hero/class/SP, vị trí, key trait và skill để trận không phụ thuộc lineup nguồn về sau.
    private async Task<FrozenLineup> Freeze(SavedLineup lineup, int totalSp, CancellationToken ct)
    {
        var heroIds = lineup.Entries.Select(x => x.HeroId).ToArray();
        var heroes = await db.Heroes.AsNoTracking().Include(x => x.Trait).Where(x => heroIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var slots = await db.LineupSlots.AsNoTracking().ToDictionaryAsync(x => x.SlotNo, ct);
        var skillIds = lineup.Skills.Select(x => x.TeamSkillId).ToArray();
        var skills = await db.TeamSkills.AsNoTracking().Where(x => skillIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return new FrozenLineup(lineup.Id, lineup.Revision, totalSp,
            lineup.Entries.OrderBy(x => x.SlotNo).Select(x => new FrozenPiece(x.SlotNo, x.HeroId, x.ClassCode, heroes[x.HeroId].SetupPoints,
                slots[x.SlotNo].StartX, slots[x.SlotNo].StartY,
                heroes[x.HeroId].Trait?.Kind == "special_move" ? heroes[x.HeroId].Trait!.ImplementationKey : null,
                x.CosmeticId, heroes[x.HeroId].Trait?.Kind, heroes[x.HeroId].Trait?.ImplementationKey)).ToArray(),
            lineup.Skills.OrderBy(x => x.SlotNo).Select(x => new FrozenSkill(x.SlotNo, x.TeamSkillId, skills[x.TeamSkillId].ImplementationKey,
                skills[x.TeamSkillId].MaxUses, skills[x.TeamSkillId].CooldownTurns ?? 0)).ToArray());
    }

    // Start: Tạo 32 piece với ID riêng, xoay vị trí Black, lưu state version 0 và action start; không chạy gameplay skill.
    private void Start(GameMatch match)
    {
        var now = clock.GetUtcNow();
        var rules = GameJson.Read<RulesetSnapshot>(match.RulesetSnapshot);
        var state = new GameState { RulesetCode = rules.Code, ContentVersion = match.ContentVersion };
        foreach (var participant in match.Participants.OrderBy(x => x.Side))
        {
            var side = participant.Side == "red" ? Side.Red : Side.Black;
            var lineup = GameJson.Read<FrozenLineup>(participant.LineupSnapshot);
            foreach (var frozen in lineup.Pieces)
            {
                var start = side == Side.Red ? new BoardPoint(frozen.StartX, frozen.StartY) : new BoardPoint(8 - frozen.StartX, 9 - frozen.StartY);
                if (!Enum.TryParse<PieceClass>(frozen.ClassCode, true, out var pieceClass)) throw new ApiException(422, "UNSUPPORTED_CLASS", $"Unsupported class {frozen.ClassCode}.");
                state.Pieces.Add(new PieceState { PieceId = Guid.NewGuid(), HeroId = frozen.HeroId, Side = side, Class = pieceClass,
                    SetupPoints = frozen.SetupPoints, Position = start, StartPosition = start, MovementImplementationKey = frozen.MovementImplementationKey,
                    TraitKind = frozen.TraitKind, TraitImplementationKey = frozen.TraitImplementationKey });
            }
            state.SkillStates[side].AddRange(lineup.Skills.Select(x => new SkillState(x.SlotNo, x.SkillId, x.MaxUses, 0, x.ImplementationKey)));
        }
        match.Status = "active"; match.StartedAt = now;
        var stateDoc = GameJson.Document(state);
        match.State = new MatchState { MatchId = match.Id, Version = 0, SideToMove = "red", TurnIndex = 0, CountedActions = 0,
            TurnDeadlineAt = now.AddSeconds(rules.TurnSeconds), StateSchemaVersion = state.StateSchemaVersion, State = stateDoc, UpdatedAt = now };
        db.MatchActions.Add(new MatchAction { Id = Guid.NewGuid(), MatchId = match.Id, SequenceNo = 0, CommandId = Guid.NewGuid(), Kind = "start",
            RequestPayload = GameJson.Document(new { }), ResolvedEvents = GameJson.Document(new[] { new { type = "match.started" } }),
            StateAfter = GameJson.Document(state), StateSchemaVersion = state.StateSchemaVersion, ReceivedAt = now, CommittedAt = now });
    }

    // ToDto: Trả trạng thái confirm và skill công khai; chỉ trả ID/revision lineup của chính người xem.
    private static MatchSelectionDto ToDto(GameMatch match, Guid viewer)
    {
        var sides = match.Participants.OrderBy(x => x.Side).Select(x =>
        {
            var skills = x.LineupSnapshot.RootElement.TryGetProperty("skills", out var values)
                ? values.EnumerateArray().Select(v => v.GetProperty("skillId").GetGuid()).ToArray() : Array.Empty<Guid>();
            var own = x.PlayerId == viewer;
            return new MatchSelectionSideDto(x.Side, x.ConfirmedAt is not null, skills, own ? x.SourceLineupId : null, own ? x.SourceLineupRevision : null);
        }).ToArray();
        return new(match.Id, match.Mode, match.Status, sides);
    }
}
