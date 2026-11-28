using System.Text.Json;
using HeroChess.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace HeroChess.Api.Services;

// Only legacy snapshots need live names. Never cache boards or player-specific projections here.
public sealed class HeroNameLookup(TimeProvider? clock = null) : IDisposable
{
    private sealed class CacheClock(TimeProvider time) : ISystemClock { public DateTimeOffset UtcNow => time.GetUtcNow(); }
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 256, Clock = new CacheClock(clock ?? TimeProvider.System) });

    public async Task<Dictionary<Guid, string>> MissingAsync(AppDbContext db, IEnumerable<JsonElement> snapshots, CancellationToken ct)
    {
        var ids = snapshots.SelectMany(state => state.TryGetProperty("pieces", out var pieces) && pieces.ValueKind == JsonValueKind.Array
            ? pieces.EnumerateArray().Where(p => (!p.TryGetProperty("heroName", out var name) || name.ValueKind == JsonValueKind.Null) &&
                p.TryGetProperty("heroId", out _)).Select(p => p.GetProperty("heroId").GetGuid())
            : []).Distinct();
        var result = new Dictionary<Guid, string>();
        var missing = new List<Guid>();
        foreach (var id in ids)
        {
            if (_cache.TryGetValue<string>(id, out var name)) result[id] = name!;
            else missing.Add(id);
        }
        if (missing.Count == 0) return result;
        var names = await db.Heroes.AsNoTracking().Where(h => missing.Contains(h.Id))
            .Select(h => new { h.Id, h.Name }).ToListAsync(ct);
        foreach (var name in names)
        {
            result[name.Id] = name.Name;
            _cache.Set(name.Id, name.Name, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) });
        }
        return result;
    }

    public void Dispose() => _cache.Dispose();
}
