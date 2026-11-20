using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class PieceCosmeticService(AppDbContext db)
{
    public const string DefaultCode = "mac-dinh";

    public async Task<PieceCosmeticCatalogDto> CatalogAsync(Guid playerId, CancellationToken ct)
    {
        var owned = await db.PlayerPieceCosmetics.AsNoTracking().Where(x => x.PlayerId == playerId)
            .Select(x => x.PieceCosmeticId).ToHashSetAsync(ct);
        var selected = await db.PlayerPieceLoadouts.AsNoTracking().Where(x => x.PlayerId == playerId)
            .Select(x => (Guid?)x.PieceCosmeticId).SingleOrDefaultAsync(ct);
        var pieces = await db.PieceCosmetics.AsNoTracking().Where(x => x.IsEnabled || x.Code == DefaultCode)
            .OrderBy(x => x.Code).ToListAsync(ct);
        var items = pieces.Select(x => ToDto(x, x.Code == DefaultCode || owned.Contains(x.Id))).ToArray();
        return new(items, items.FirstOrDefault(x => x.Id == selected && x.IsOwned)?.Code ?? DefaultCode);
    }

    public async Task<PieceCosmeticDto> EquipAsync(Guid playerId, EquipPieceCosmeticRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 60)
            throw new ApiException(400, "INVALID_PIECE_CODE", "A piece code is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Use the wallet lock to serialize first-time loadout inserts and concurrent purchases.
        _ = await db.Wallets.FromSqlInterpolated($"SELECT * FROM hero_chess.player_wallet WHERE player_id={playerId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player wallet was not found.");
        var piece = await db.PieceCosmetics.FromSqlInterpolated($"SELECT * FROM hero_chess.piece_cosmetic WHERE code={request.Code} FOR SHARE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "PIECE_NOT_FOUND", "The piece was not found.");
        if (piece.Code != DefaultCode)
        {
            if (!piece.IsEnabled) throw new ApiException(422, "PIECE_DISABLED", "The piece is unavailable.");
            if (!await db.PlayerPieceCosmetics.AnyAsync(x => x.PlayerId == playerId && x.PieceCosmeticId == piece.Id, ct))
                throw new ApiException(422, "PIECE_NOT_OWNED", "Purchase this piece before equipping it.");
        }
        var loadout = await db.PlayerPieceLoadouts.SingleOrDefaultAsync(x => x.PlayerId == playerId, ct);
        if (loadout is null) db.PlayerPieceLoadouts.Add(new() { PlayerId = playerId, PieceCosmeticId = piece.Id });
        else loadout.PieceCosmeticId = piece.Id;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(piece, true);
    }

    private static PieceCosmeticDto ToDto(PieceCosmetic piece, bool owned) =>
        new(piece.Id, piece.Code, piece.Name, piece.CoinPrice.ToString(), piece.AssetKey, piece.PreviewKey, owned);
}
