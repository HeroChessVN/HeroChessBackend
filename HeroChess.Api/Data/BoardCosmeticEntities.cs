namespace HeroChess.Api.Data;

public sealed class BoardCosmetic
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long CoinPrice { get; set; }
    public string AssetKey { get; set; } = "";
    public string PreviewKey { get; set; } = "";
    public bool IsEnabled { get; set; }
}

public sealed class PlayerBoardCosmetic
{
    public Guid PlayerId { get; set; }
    public Guid BoardCosmeticId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public string IdempotencyKey { get; set; } = "";
}

public sealed class PlayerBoardLoadout
{
    public Guid PlayerId { get; set; }
    public Guid BoardCosmeticId { get; set; }
}
