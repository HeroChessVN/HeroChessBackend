using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class BoardCosmeticService(AppDbContext db)
{
    public const string DefaultCode = "mac-dinh";

    public async Task<BoardCosmeticCatalogDto> CatalogAsync(Guid playerId, CancellationToken ct)
    {
        var owned = await db.PlayerBoardCosmetics.AsNoTracking().Where(x => x.PlayerId == playerId)
            .Select(x => x.BoardCosmeticId).ToHashSetAsync(ct);
        var selected = await db.PlayerBoardLoadouts.AsNoTracking().Where(x => x.PlayerId == playerId)
            .Select(x => (Guid?)x.BoardCosmeticId).SingleOrDefaultAsync(ct);
        var boards = await db.BoardCosmetics.AsNoTracking().Where(x => x.IsEnabled || x.Code == DefaultCode)
            .OrderBy(x => x.Code).ToListAsync(ct);
        var items = boards.Select(x => ToDto(x, x.Code == DefaultCode || owned.Contains(x.Id))).ToArray();
        return new(items, items.FirstOrDefault(x => x.Id == selected && x.IsOwned)?.Code ?? DefaultCode);
    }

    public async Task<BoardCosmeticDto> EquipAsync(Guid playerId, EquipBoardCosmeticRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 60)
            throw new ApiException(400, "INVALID_BOARD_CODE", "A board code is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Use the wallet lock to serialize first-time loadout inserts and concurrent purchases.
        _ = await db.Wallets.FromSqlInterpolated($"SELECT * FROM hero_chess.player_wallet WHERE player_id={playerId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player wallet was not found.");
        var board = await db.BoardCosmetics.FromSqlInterpolated($"SELECT * FROM hero_chess.board_cosmetic WHERE code={request.Code} FOR SHARE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "BOARD_NOT_FOUND", "The board was not found.");
        if (board.Code != DefaultCode)
        {
            if (!board.IsEnabled) throw new ApiException(422, "BOARD_DISABLED", "The board is unavailable.");
            if (!await db.PlayerBoardCosmetics.AnyAsync(x => x.PlayerId == playerId && x.BoardCosmeticId == board.Id, ct))
                throw new ApiException(422, "BOARD_NOT_OWNED", "Purchase this board before equipping it.");
        }
        var loadout = await db.PlayerBoardLoadouts.SingleOrDefaultAsync(x => x.PlayerId == playerId, ct);
        if (loadout is null) db.PlayerBoardLoadouts.Add(new() { PlayerId = playerId, BoardCosmeticId = board.Id });
        else loadout.BoardCosmeticId = board.Id;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(board, true);
    }

    private static BoardCosmeticDto ToDto(BoardCosmetic board, bool owned) =>
        new(board.Id, board.Code, board.Name, board.CoinPrice.ToString(), board.AssetKey, board.PreviewKey, owned);
}
