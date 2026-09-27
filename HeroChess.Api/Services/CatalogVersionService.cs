// Vai trò file: Tạo fingerprint nội dung để phát hiện catalog thay đổi giữa lúc ghép trận và confirm.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeroChess.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class CatalogVersionService
{
    // ComputeAsync: Đọc các trường hero/trait/skill được chọn trong code, sắp xếp ổn định, serialize và hash SHA-256; không phải version của toàn database.
    public async Task<string> ComputeAsync(AppDbContext db, CancellationToken ct)
    {
        var heroes = await db.Heroes.AsNoTracking().Where(x => x.IsEnabled).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ClassCode, x.SetupPoints, x.TraitId, x.AssetKey }).ToArrayAsync(ct);
        var traits = await db.HeroTraits.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ImplementationKey, x.Parameters }).ToArrayAsync(ct);
        var skills = await db.TeamSkills.AsNoTracking().Where(x => x.IsEnabled).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ImplementationKey, x.Parameters, x.Eligibility, x.MaxUses, x.CooldownTurns }).ToArrayAsync(ct);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { heroes, traits, skills }, Infrastructure.GameJson.Options));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
