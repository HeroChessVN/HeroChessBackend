// Vai trò file: Quản trị nội dung catalog (hero, trait, phe, skill, bộ luật). Mọi thay đổi ghi audit trong cùng transaction.
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminCatalogService(AppDbContext db, AdminAuditWriter audit)
{
    // ListHeroesAsync: Danh sách hero cho admin, bao gồm cả hero đang tắt; lọc theo lớp/phe/trạng thái và tìm theo tên hoặc mã.
    public async Task<AdminHeroPageDto> ListHeroesAsync(string? query, string? classCode, string? factionCode, bool? enabled,
        bool? inShop, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var heroes = db.Heroes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            heroes = heroes.Where(x => x.Name.Contains(text) || x.Code.Contains(text));
        }
        if (!string.IsNullOrWhiteSpace(classCode)) heroes = heroes.Where(x => x.ClassCode == classCode);
        if (enabled.HasValue) heroes = heroes.Where(x => x.IsEnabled == enabled.Value);
        if (inShop.HasValue) heroes = heroes.Where(x => x.IsInShop == inShop.Value);
        if (!string.IsNullOrWhiteSpace(factionCode))
            heroes = heroes.Where(x => db.HeroFactions.Any(f => f.HeroId == x.Id &&
                db.Factions.Any(k => k.Id == f.FactionId && k.Code == factionCode)));
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            // Hero không có created_at nên phân trang keyset theo mã (duy nhất và tăng dần).
            var afterCode = CursorCodec.ParseText(cursor);
            // CompareTo dịch được thành `code > @p` trong PostgreSQL; cùng thứ tự với ORDER BY nên cursor khớp.
            heroes = heroes.Where(x => x.Code.CompareTo(afterCode) > 0);
        }
        var total = await heroes.CountAsync(ct);
        var rows = await heroes.OrderBy(x => x.Code).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var dtos = await BuildHeroesAsync(page, ct);
        string? next = null;
        if (rows.Count > limit) next = CursorCodec.FormatText(page[^1].Code);
        return new(dtos, next, total);
    }

    // HeroDetailAsync: Chi tiết hero kèm skin, số chủ sở hữu, doanh thu và lịch sử đổi giá.
    public async Task<AdminHeroDetailDto> HeroDetailAsync(Guid heroId, CancellationToken ct)
    {
        var hero = await db.Heroes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == heroId, ct)
            ?? throw new ApiException(404, "HERO_NOT_FOUND", "The hero was not found.");
        var dtos = await BuildHeroesAsync([hero], ct);
        var cosmetics = await db.Cosmetics.AsNoTracking().Where(x => x.HeroId == heroId).OrderBy(x => x.Code)
            .Select(x => new AdminCosmeticDto(x.Id, x.Code, x.Name, x.HeroId, x.CoinPrice.ToString(), x.IsEnabled, x.AssetKey)).ToListAsync(ct);
        var skills = await SkillDtosAsync(await db.TeamSkills.AsNoTracking().Where(x => x.IsEnabled).ToListAsync(ct), ct);
        var ownerCount = await db.PlayerHeroes.AsNoTracking().CountAsync(x => x.HeroId == heroId, ct);
        var revenue = await db.CoinTransactions.AsNoTracking().Where(x => x.HeroId == heroId)
            .SumAsync(x => (long?)(-x.Amount), ct) ?? 0;
        var auditRows = await db.AdminAuditLogs.AsNoTracking()
            .Where(x => x.EntityType == "hero" && x.EntityId == heroId.ToString("D") && x.Action == "hero.price.update")
            .OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
        var history = auditRows.Select(x => new AdminPriceChangeDto(x.Id, x.ActorUserId,
            PriceOf(x.BeforeData) ?? "", PriceOf(x.AfterData) ?? "", x.Reason, x.CreatedAt)).ToArray();
        return new(dtos[0], cosmetics, skills, ownerCount, revenue, history);
    }

    // CreateHeroAsync: Tạo hero mới; mã là duy nhất, giá và lớp được kiểm tra server-side.
    public async Task<AdminHeroDto> CreateHeroAsync(Guid actorId, AdminHeroUpsertRequest request, CancellationToken ct)
    {
        var name = AdminValidation.RequireName(request.Name);
        var code = request.Code is null ? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()
            : AdminValidation.RequireCode(request.Code);
        var price = request.CoinPrice is null ? 0 : AdminValidation.ParseCoinPrice(request.CoinPrice);
        var classCode = request.ClassCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(classCode)) throw new ApiException(400, "CLASS_REQUIRED", "classCode is required.");
        if (!await db.ChessClasses.AsNoTracking().AnyAsync(x => x.Code == classCode, ct))
            throw new ApiException(422, "CLASS_NOT_FOUND", "The chess class does not exist.");
        if (await db.Heroes.AsNoTracking().AnyAsync(x => x.Code == code, ct))
            throw new ApiException(409, "HERO_CODE_EXISTS", "A hero with this code already exists.");
        var setupPoints = AdminValidation.RequireSetupPoints(classCode, request.SetupPoints);
        var hero = new Hero
        {
            Id = Guid.NewGuid(), Code = code, Name = name, ClassCode = classCode,
            SetupPoints = setupPoints,
            CoinPrice = price, IsEnabled = request.IsEnabled ?? true, IsInShop = request.IsInShop ?? true,
            IsStarter = request.IsStarter ?? false, IsTestFixture = false,
            AssetKey = request.AssetKey, Description = request.Description, TraitId = request.TraitId
        };
        if (hero.TraitId is not null && !await db.HeroTraits.AsNoTracking().AnyAsync(x => x.Id == hero.TraitId, ct))
            throw new ApiException(422, "TRAIT_NOT_FOUND", "The trait does not exist.");
        db.Heroes.Add(hero);
        audit.Write(actorId, "hero.create", "hero", hero.Id.ToString("D"), null, new { hero.Code, hero.Name, hero.CoinPrice }, request.Reason);
        await db.SaveChangesAsync(ct);
        return (await BuildHeroesAsync([hero], ct))[0];
    }

    // UpdateHeroAsync: Sửa thông tin hero; chỉ trường được gửi mới thay đổi, giá nằm ở endpoint riêng.
    public async Task<AdminHeroDto> UpdateHeroAsync(Guid actorId, Guid heroId, AdminHeroUpdateRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hero = await db.Heroes.FromSqlInterpolated($"SELECT * FROM hero_chess.hero WHERE id={heroId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "HERO_NOT_FOUND", "The hero was not found.");
        var before = new { hero.Name, hero.SetupPoints, hero.IsEnabled, hero.AssetKey, hero.Description, hero.TraitId };
        if (request.Name is not null) hero.Name = AdminValidation.RequireName(request.Name);
        if (request.SetupPoints.HasValue)
            hero.SetupPoints = AdminValidation.RequireSetupPoints(hero.ClassCode, request.SetupPoints.Value);
        if (request.IsEnabled.HasValue) hero.IsEnabled = request.IsEnabled.Value;
        if (request.AssetKey is not null) hero.AssetKey = request.AssetKey;
        if (request.Description is not null) hero.Description = request.Description;
        if (request.TraitId.HasValue)
        {
            if (!await db.HeroTraits.AsNoTracking().AnyAsync(x => x.Id == request.TraitId, ct))
                throw new ApiException(422, "TRAIT_NOT_FOUND", "The trait does not exist.");
            hero.TraitId = request.TraitId;
        }
        var after = new { hero.Name, hero.SetupPoints, hero.IsEnabled, hero.AssetKey, hero.Description, hero.TraitId };
        audit.Write(actorId, "hero.update", "hero", heroId.ToString("D"), before, after, request.Reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await BuildHeroesAsync([hero], ct))[0];
    }

    // SetHeroFactionAsync: Gán phe cho hero; thiết kế chốt tối đa một phe mỗi hero nên gán mới thay thế phe cũ.
    public async Task<AdminHeroDto> SetHeroFactionAsync(Guid actorId, Guid heroId, AdminHeroFactionRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hero = await db.Heroes.FromSqlInterpolated($"SELECT * FROM hero_chess.hero WHERE id={heroId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "HERO_NOT_FOUND", "The hero was not found.");
        var current = await db.HeroFactions.AsNoTracking().Where(x => x.HeroId == heroId).ToListAsync(ct);
        var before = current.Select(x => x.FactionId.ToString("D")).ToArray();
        if (request.FactionId is not null && !await db.Factions.AsNoTracking().AnyAsync(x => x.Id == request.FactionId, ct))
            throw new ApiException(422, "FACTION_NOT_FOUND", "The faction does not exist.");
        db.HeroFactions.RemoveRange(current);
        if (request.FactionId is not null) db.HeroFactions.Add(new HeroFaction { HeroId = heroId, FactionId = request.FactionId.Value });
        var after = request.FactionId is null ? Array.Empty<string>() : new[] { request.FactionId.Value.ToString("D") };
        audit.Write(actorId, "hero.faction.set", "hero", heroId.ToString("D"), before, after, request.Reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await BuildHeroesAsync([hero], ct))[0];
    }

    // ListSkillsAsync: Danh sách command skill kèm phe và trạng thái draft/live.
    public async Task<IReadOnlyList<AdminSkillDto>> ListSkillsAsync(CancellationToken ct)
    {
        var skills = await db.TeamSkills.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
        return await SkillDtosAsync(skills, ct);
    }

    // UpdateSkillAsync: Sửa metadata skill; không cho sửa hiệu ứng/cooldown khi luật thiết kế chưa chốt.
    public async Task<AdminSkillDto> UpdateSkillAsync(Guid actorId, Guid skillId, AdminSkillUpdateRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var skill = await db.TeamSkills.FromSqlInterpolated($"SELECT * FROM hero_chess.team_skill WHERE id={skillId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "SKILL_NOT_FOUND", "The team skill was not found.");
        var before = new { skill.Name, skill.Description, skill.IsEnabled, skill.MaxUses, skill.CooldownTurns };
        if (request.Name is not null) skill.Name = AdminValidation.RequireName(request.Name);
        if (request.Description is not null) skill.Description = request.Description;
        if (request.IsEnabled.HasValue) skill.IsEnabled = request.IsEnabled.Value;
        if (request.MaxUses.HasValue)
        {
            if (request.MaxUses.Value < 0) throw new ApiException(400, "INVALID_MAX_USES", "maxUses must not be negative.");
            skill.MaxUses = (short?)Math.Clamp(request.MaxUses.Value, 0, short.MaxValue);
        }
        if (request.CooldownTurns.HasValue)
        {
            if (request.CooldownTurns.Value < 0) throw new ApiException(400, "INVALID_COOLDOWN", "cooldownTurns must not be negative.");
            skill.CooldownTurns = (short?)Math.Clamp(request.CooldownTurns.Value, 0, short.MaxValue);
        }
        var after = new { skill.Name, skill.Description, skill.IsEnabled, skill.MaxUses, skill.CooldownTurns };
        audit.Write(actorId, "skill.update", "team_skill", skillId.ToString("D"), before, after, request.Reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await SkillDtosAsync([skill], ct))[0];
    }

    // ListChessClassesAsync: Danh sách lớp quân lấy nguyên từ bảng hero_chess.chess_class; nguồn duy nhất cho form tạo hero.
    public async Task<IReadOnlyList<ChessClassDto>> ListChessClassesAsync(CancellationToken ct) =>
        await db.ChessClasses.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new ChessClassDto(x.Code, x.NameVi, x.BaseSp, x.RequiredCount, x.BaseMovementCode)).ToListAsync(ct);

    // ListFactionsAsync: Danh sách phe kèm số hero và số skill thuộc phe.
    public async Task<IReadOnlyList<AdminFactionDto>> ListFactionsAsync(CancellationToken ct)
    {
        var factions = await db.Factions.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
        var heroCounts = await db.HeroFactions.AsNoTracking().GroupBy(x => x.FactionId)
            .Select(g => new { FactionId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.FactionId, x => x.Count, ct);
        var skillCounts = await db.TeamSkills.AsNoTracking().Where(x => x.FactionId != null).GroupBy(x => x.FactionId!.Value)
            .Select(g => new { FactionId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.FactionId, x => x.Count, ct);
        return factions.Select(x => new AdminFactionDto(x.Id, x.Code, x.Name, x.Description,
            heroCounts.GetValueOrDefault(x.Id), skillCounts.GetValueOrDefault(x.Id))).ToArray();
    }

    // CreateFactionAsync: Tạo phe mới; mã là duy nhất.
    public async Task<AdminFactionDto> CreateFactionAsync(Guid actorId, AdminFactionUpsertRequest request, CancellationToken ct)
    {
        var name = AdminValidation.RequireName(request.Name);
        var code = request.Code is null ? Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() : AdminValidation.RequireCode(request.Code);
        if (await db.Factions.AsNoTracking().AnyAsync(x => x.Code == code, ct))
            throw new ApiException(409, "FACTION_CODE_EXISTS", "A faction with this code already exists.");
        var faction = new Faction { Id = Guid.NewGuid(), Code = code, Name = name, Description = request.Description };
        db.Factions.Add(faction);
        audit.Write(actorId, "faction.create", "faction", faction.Id.ToString("D"), null, new { faction.Code, faction.Name }, request.Reason);
        await db.SaveChangesAsync(ct);
        return new(faction.Id, faction.Code, faction.Name, faction.Description, 0, 0);
    }

    // UpdateFactionAsync: Sửa tên/mô tả phe.
    public async Task<AdminFactionDto> UpdateFactionAsync(Guid actorId, Guid factionId, AdminFactionUpdateRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var faction = await db.Factions.FromSqlInterpolated($"SELECT * FROM hero_chess.faction WHERE id={factionId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "FACTION_NOT_FOUND", "The faction was not found.");
        var before = new { faction.Name, faction.Description };
        if (request.Name is not null) faction.Name = AdminValidation.RequireName(request.Name);
        if (request.Description is not null) faction.Description = request.Description;
        var after = new { faction.Name, faction.Description };
        audit.Write(actorId, "faction.update", "faction", factionId.ToString("D"), before, after, request.Reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var heroCount = await db.HeroFactions.AsNoTracking().CountAsync(x => x.FactionId == factionId, ct);
        var skillCount = await db.TeamSkills.AsNoTracking().CountAsync(x => x.FactionId == factionId, ct);
        return new(faction.Id, faction.Code, faction.Name, faction.Description, heroCount, skillCount);
    }

    // ListRulesetsAsync: Bộ luật kèm số trận đã dùng; chỉ đọc, không cho sửa luật thiết kế.
    public async Task<IReadOnlyList<AdminRulesetDto>> ListRulesetsAsync(CancellationToken ct)
    {
        var usage = await db.Matches.AsNoTracking().GroupBy(x => x.RulesetId)
            .Select(g => new { RulesetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RulesetId, x => x.Count, ct);
        var lastUsed = await db.Matches.AsNoTracking().GroupBy(x => x.RulesetId)
            .Select(g => new { RulesetId = g.Key, Last = g.Max(x => x.CreatedAt) })
            .ToDictionaryAsync(x => x.RulesetId, x => x.Last, ct);
        var rulesets = await db.Rulesets.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
        return rulesets.Select(x => new AdminRulesetDto(x.Id, x.Code, x.Name,
            x.SetupBudget, x.TurnSeconds, x.ActionLimit, x.IsActive, x.Config.RootElement.Clone(),
            usage.GetValueOrDefault(x.Id),
            lastUsed.TryGetValue(x.Id, out var last) ? last : null)).ToArray();
    }

    // PriceOf: Đọc giá coin từ jsonb audit; dữ liệu cũ không có trường thì trả null.
    private static string? PriceOf(JsonDocument? data) =>
        data is not null && data.RootElement.TryGetProperty("coinPrice", out var value) ? value.GetString() : null;

    // BuildHeroesAsync: Dựng DTO hero kèm mã phe và trait; tải theo lô để tránh N+1.
    public async Task<IReadOnlyList<AdminHeroDto>> BuildHeroesAsync(IReadOnlyList<Hero> heroes, CancellationToken ct)
    {
        if (heroes.Count == 0) return [];
        var ids = heroes.Select(x => x.Id).ToArray();
        var links = await db.HeroFactions.AsNoTracking().Where(x => ids.Contains(x.HeroId)).ToListAsync(ct);
        var factionCodes = await db.Factions.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var traitCodes = await db.HeroTraits.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        return heroes.Select(hero => new AdminHeroDto(hero.Id, hero.Code, hero.Name, hero.ClassCode, hero.CharacterId,
            hero.SetupPoints, hero.CoinPrice.ToString(), hero.IsEnabled, hero.IsInShop, hero.IsStarter, hero.IsTestFixture,
            hero.AssetKey, hero.Description,
            links.Where(l => l.HeroId == hero.Id).Select(l => factionCodes.GetValueOrDefault(l.FactionId, "")).Where(c => c.Length > 0).ToArray(),
            hero.TraitId, hero.TraitId is not null ? traitCodes.GetValueOrDefault(hero.TraitId.Value) : null)).ToArray();
    }

    // SkillDtosAsync: Dựng DTO skill; skill chưa có handler thực thi được đánh dấu DRAFT.
    private async Task<IReadOnlyList<AdminSkillDto>> SkillDtosAsync(IReadOnlyList<TeamSkill> skills, CancellationToken ct)
    {
        if (skills.Count == 0) return [];
        var factionIds = skills.Where(x => x.FactionId is not null).Select(x => x.FactionId!.Value).Distinct().ToArray();
        var factionCodes = await db.Factions.AsNoTracking().Where(x => factionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var live = new HashSet<string>(StringComparer.Ordinal)
        {
            "van_coc_tran_giang", "phan_ky_doat_the", "pha_tran_doat_phong", "binh_lam_thuy_hien", "thanh", "rao", "khien"
        };
        return skills.Select(skill => new AdminSkillDto(skill.Id, skill.Code, skill.Name, skill.FactionId,
            skill.FactionId is not null ? factionCodes.GetValueOrDefault(skill.FactionId.Value) : null,
            skill.ImplementationKey, skill.IsEnabled, skill.MaxUses, skill.CooldownTurns, skill.Description,
            skill.IsEnabled && live.Contains(skill.ImplementationKey) ? "LIVE" : "DRAFT")).ToArray();
    }
}
