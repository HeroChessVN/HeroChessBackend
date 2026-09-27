// Vai trò file: CRUD đội hình riêng của người chơi; revision chống ghi đè thay đổi từ request cũ.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class LineupService(AppDbContext db, LineupValidator validator)
{
    // ListAsync: Đọc đội hình của actor và tính lại validation hiện tại.
    public async Task<IReadOnlyList<LineupDto>> ListAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var lineups = await db.Lineups.AsNoTracking().Include(x => x.Entries).Include(x => x.Skills)
            .Where(x => x.PlayerId == playerId).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var output = new List<LineupDto>();
        foreach (var lineup in lineups) output.Add(await ToDto(lineup, playerId, cancellationToken));
        return output;
    }

    // CreateAsync: Validate rồi tạo lineup cùng entry/skill; trả DTO đã tính SP.
    public async Task<LineupDto> CreateAsync(Guid playerId, SaveLineupRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateOrThrow(playerId, request, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var lineup = new SavedLineup { Id = Guid.NewGuid(), PlayerId = playerId, RulesetId = request.RulesetId, Name = request.Name.Trim(), Revision = 1, CreatedAt = now, UpdatedAt = now };
        AddChildren(lineup, request, validation.SlotClasses);
        db.Lineups.Add(lineup);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDto(lineup, playerId, cancellationToken);
    }

    // UpdateAsync: Kiểm tra expectedRevision, thay toàn bộ entry/skill trong transaction và tăng revision.
    public async Task<LineupDto> UpdateAsync(Guid playerId, Guid id, SaveLineupRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision is null) throw new ApiException(400, "EXPECTED_REVISION_REQUIRED", "expectedRevision is required when updating a lineup.");
        var validation = await ValidateOrThrow(playerId, request, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var affected = await db.Lineups.Where(x => x.Id == id && x.PlayerId == playerId && x.Revision == request.ExpectedRevision)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, request.Name.Trim()).SetProperty(x => x.RulesetId, request.RulesetId)
                .SetProperty(x => x.Revision, x => x.Revision + 1).SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (affected == 0)
        {
            var exists = await db.Lineups.AsNoTracking().AnyAsync(x => x.Id == id && x.PlayerId == playerId, cancellationToken);
            throw new ApiException(exists ? 409 : 404, exists ? "STALE_REVISION" : "LINEUP_NOT_FOUND", exists ? "The lineup revision has changed." : "The lineup was not found.");
        }
        await db.LineupEntries.Where(x => x.LineupId == id).ExecuteDeleteAsync(cancellationToken);
        await db.LineupSkills.Where(x => x.LineupId == id).ExecuteDeleteAsync(cancellationToken);
        var shell = new SavedLineup { Id = id };
        AddChildren(shell, request, validation.SlotClasses);
        db.LineupEntries.AddRange(shell.Entries);
        db.LineupSkills.AddRange(shell.Skills);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        var saved = await db.Lineups.AsNoTracking().Include(x => x.Entries).Include(x => x.Skills).SingleAsync(x => x.Id == id && x.PlayerId == playerId, cancellationToken);
        return await ToDto(saved, playerId, cancellationToken);
    }

    // DeleteAsync: Xóa đúng owner/revision; phân biệt không tồn tại với revision cũ.
    public async Task DeleteAsync(Guid playerId, Guid id, int expectedRevision, CancellationToken cancellationToken)
    {
        var affected = await db.Lineups.Where(x => x.Id == id && x.PlayerId == playerId && x.Revision == expectedRevision).ExecuteDeleteAsync(cancellationToken);
        if (affected != 0) return;
        var exists = await db.Lineups.AsNoTracking().AnyAsync(x => x.Id == id && x.PlayerId == playerId, cancellationToken);
        throw new ApiException(exists ? 409 : 404, exists ? "STALE_REVISION" : "LINEUP_NOT_FOUND", exists ? "The lineup revision has changed." : "The lineup was not found.");
    }

    // ValidateOrThrow: Chuyển danh sách lỗi validator thành INVALID_LINEUP 422.
    private async Task<LineupValidationResult> ValidateOrThrow(Guid playerId, SaveLineupRequest request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(playerId, request, cancellationToken);
        if (validation.Errors.Count != 0) throw new ApiException(422, "INVALID_LINEUP", "The lineup is invalid.", validation.Errors);
        return validation;
    }

    // AddChildren: Chuyển request entry/skill sang entity con với slot/class đã được validator xác định.
    private static void AddChildren(SavedLineup lineup, SaveLineupRequest request, IReadOnlyDictionary<int, string> slots)
    {
        lineup.Entries.AddRange(request.Entries.Select(x => new SavedLineupEntry { LineupId = lineup.Id, SlotNo = (short)x.SlotNo, ClassCode = slots[x.SlotNo], HeroId = x.HeroId, CosmeticId = x.CosmeticId }));
        lineup.Skills.AddRange(request.Skills.Select(x => new SavedLineupSkill { LineupId = lineup.Id, SlotNo = (short)x.SlotNo, TeamSkillId = x.SkillId }));
    }

    // ToDto: Tính lại validation và chuyển entity sang DTO; không tin cờ hợp lệ do client gửi.
    private async Task<LineupDto> ToDto(SavedLineup lineup, Guid playerId, CancellationToken cancellationToken)
    {
        var request = new SaveLineupRequest(lineup.Name, lineup.RulesetId,
            lineup.Entries.OrderBy(x => x.SlotNo).Select(x => new LineupEntryInput(x.SlotNo, x.HeroId, x.CosmeticId)).ToArray(),
            lineup.Skills.OrderBy(x => x.SlotNo).Select(x => new LineupSkillInput(x.SlotNo, x.TeamSkillId)).ToArray(), lineup.Revision);
        var validation = await validator.ValidateAsync(playerId, request, cancellationToken);
        return new(lineup.Id, lineup.Name, lineup.Revision, lineup.RulesetId, request.Entries, request.Skills,
            validation.TotalSp, validation.Budget, validation.Errors.Count == 0, validation.Errors);
    }
}
