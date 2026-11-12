// Vai trò file: HTTP cho quản trị nội dung (hero, phe, skill, bộ luật). Quyền kiểm tra server-side theo policy.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Admin)]
[Route("api/v1/admin")]
public sealed class AdminCatalogController(AdminCatalogService catalog, ICurrentUser current) : ControllerBase
{
    // Heroes: Danh sách hero cho admin, gồm cả hero đang tắt.
    [HttpGet("heroes")]
    [Authorize(Policy = AccountPolicies.HeroRead)]
    public async Task<ActionResult<AdminHeroPageDto>> Heroes([FromQuery] string? query, [FromQuery] string? classCode,
        [FromQuery] string? factionCode, [FromQuery] bool? enabled, [FromQuery] bool? inShop,
        [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await catalog.ListHeroesAsync(query, classCode, factionCode, enabled, inShop, cursor, limit, ct));

    // HeroDetail: Chi tiết hero kèm skin, số chủ sở hữu, doanh thu và lịch sử đổi giá.
    [HttpGet("heroes/{id:guid}")]
    [Authorize(Policy = AccountPolicies.HeroRead)]
    public async Task<ActionResult<AdminHeroDetailDto>> HeroDetail(Guid id, CancellationToken ct = default) =>
        Ok(await catalog.HeroDetailAsync(id, ct));

    // CreateHero: Tạo hero mới.
    [HttpPost("heroes")]
    [Authorize(Policy = AccountPolicies.HeroWrite)]
    public async Task<ActionResult<AdminHeroDto>> CreateHero(AdminHeroUpsertRequest request, CancellationToken ct = default) =>
        Ok(await catalog.CreateHeroAsync(current.UserId, request, ct));

    // UpdateHero: Sửa thông tin hero (không gồm giá; giá dùng endpoint shop).
    [HttpPatch("heroes/{id:guid}")]
    [Authorize(Policy = AccountPolicies.HeroWrite)]
    public async Task<ActionResult<AdminHeroDto>> UpdateHero(Guid id, AdminHeroUpdateRequest request, CancellationToken ct = default) =>
        Ok(await catalog.UpdateHeroAsync(current.UserId, id, request, ct));

    // SetHeroFaction: Gán phe cho hero; tối đa một phe mỗi hero.
    [HttpPut("heroes/{id:guid}/faction")]
    [Authorize(Policy = AccountPolicies.HeroWrite)]
    public async Task<ActionResult<AdminHeroDto>> SetHeroFaction(Guid id, AdminHeroFactionRequest request, CancellationToken ct = default) =>
        Ok(await catalog.SetHeroFactionAsync(current.UserId, id, request, ct));

    // Skills: Danh sách command skill kèm trạng thái DRAFT/LIVE.
    [HttpGet("skills")]
    [Authorize(Policy = AccountPolicies.HeroRead)]
    public async Task<ActionResult<IReadOnlyList<AdminSkillDto>>> Skills(CancellationToken ct = default) =>
        Ok(await catalog.ListSkillsAsync(ct));

    // UpdateSkill: Sửa metadata của skill.
    [HttpPatch("skills/{id:guid}")]
    [Authorize(Policy = AccountPolicies.HeroWrite)]
    public async Task<ActionResult<AdminSkillDto>> UpdateSkill(Guid id, AdminSkillUpdateRequest request, CancellationToken ct = default) =>
        Ok(await catalog.UpdateSkillAsync(current.UserId, id, request, ct));

    // Factions: Danh sách phe kèm số hero và số skill.
    [HttpGet("factions")]
    [Authorize(Policy = AccountPolicies.HeroRead)]
    public async Task<ActionResult<IReadOnlyList<AdminFactionDto>>> Factions(CancellationToken ct = default) =>
        Ok(await catalog.ListFactionsAsync(ct));

    // CreateFaction: Tạo phe mới.
    [HttpPost("factions")]
    [Authorize(Policy = AccountPolicies.HeroWrite)]
    public async Task<ActionResult<AdminFactionDto>> CreateFaction(AdminFactionUpsertRequest request, CancellationToken ct = default) =>
        Ok(await catalog.CreateFactionAsync(current.UserId, request, ct));

    // UpdateFaction: Sửa phe.
    [HttpPatch("factions/{id:guid}")]
    [Authorize(Policy = AccountPolicies.HeroWrite)]
    public async Task<ActionResult<AdminFactionDto>> UpdateFaction(Guid id, AdminFactionUpdateRequest request, CancellationToken ct = default) =>
        Ok(await catalog.UpdateFactionAsync(current.UserId, id, request, ct));

    // Rulesets: Bộ luật và số trận đã dùng; chỉ đọc.
    [HttpGet("rulesets")]
    [Authorize(Policy = AccountPolicies.RulesetRead)]
    public async Task<ActionResult<IReadOnlyList<AdminRulesetDto>>> Rulesets(CancellationToken ct = default) =>
        Ok(await catalog.ListRulesetsAsync(ct));
}