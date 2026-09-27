// Vai trò file: Mua hero bằng coin server-side; khóa wallet để tránh hai request cùng tiêu một số dư.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class ShopService(AppDbContext db, TimeProvider clock)
{
    // PurchaseHeroAsync: Validate UUID key, dedup, kiểm tra hero/ownership/số dư; ghi ví + ownership + ledger trong transaction; mua miễn phí không tạo ledger amount 0.
    public async Task<PurchaseHeroDto> PurchaseHeroAsync(Guid playerId, Guid heroId, string? idempotencyHeader, CancellationToken ct)
    {
        if (!Guid.TryParse(idempotencyHeader, out var keyId))
            throw new ApiException(400, "INVALID_IDEMPOTENCY_KEY", "Idempotency-Key must be a UUID.");
        var key = keyId.ToString("D");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var wallet = await db.Wallets.FromSqlInterpolated($"SELECT * FROM hero_chess.player_wallet WHERE player_id={playerId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player wallet was not found.");
        var previous = await db.PlayerHeroes.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == playerId && x.IdempotencyKey == key, ct);
        if (previous is not null)
        {
            if (previous.HeroId != heroId) throw new ApiException(409, "IDEMPOTENCY_KEY_CONFLICT", "The idempotency key was already used for another hero.");
            var previousLedger = await db.CoinTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == playerId && x.IdempotencyKey == key, ct);
            await transaction.CommitAsync(ct);
            return new(heroId, true, wallet.Balance.ToString(), Math.Abs(previousLedger?.Amount ?? 0).ToString());
        }
        var hero = await db.Heroes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == heroId, ct)
            ?? throw new ApiException(404, "HERO_NOT_FOUND", "The hero was not found.");
        if (!hero.IsEnabled) throw new ApiException(422, "HERO_DISABLED", "The hero is not available for purchase.");
        if (await db.PlayerHeroes.AsNoTracking().AnyAsync(x => x.PlayerId == playerId && x.HeroId == heroId, ct))
            throw new ApiException(409, "HERO_ALREADY_OWNED", "The hero is already owned.");
        if (wallet.Balance < hero.CoinPrice)
            throw new ApiException(422, "INSUFFICIENT_COINS", "The wallet does not have enough coins.", new { balance = wallet.Balance.ToString(), price = hero.CoinPrice.ToString() });
        wallet.Balance -= hero.CoinPrice;
        var now = clock.GetUtcNow();
        db.PlayerHeroes.Add(new PlayerHero { PlayerId = playerId, HeroId = heroId, AcquiredVia = "purchase", AcquiredAt = now, IdempotencyKey = key });
        if (hero.CoinPrice > 0)
            db.CoinTransactions.Add(new CoinTransaction { Id = Guid.NewGuid(), PlayerId = playerId, IdempotencyKey = key, Kind = "hero_purchase",
                Amount = -hero.CoinPrice, BalanceAfter = wallet.Balance, HeroId = heroId, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(heroId, true, wallet.Balance.ToString(), hero.CoinPrice.ToString());
    }
}
