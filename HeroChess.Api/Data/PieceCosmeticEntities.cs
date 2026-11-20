namespace HeroChess.Api.Data;

public sealed class PieceCosmetic
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long CoinPrice { get; set; }
    public string AssetKey { get; set; } = "";
    public string PreviewKey { get; set; } = "";
    public bool IsEnabled { get; set; }
}

public sealed class PlayerPieceCosmetic
{
    public Guid PlayerId { get; set; }
    public Guid PieceCosmeticId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public string IdempotencyKey { get; set; } = "";
}

public sealed class PlayerPieceLoadout
{
    public Guid PlayerId { get; set; }
    public Guid PieceCosmeticId { get; set; }
}
