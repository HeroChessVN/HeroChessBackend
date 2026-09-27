// Vai trò file: Strategy pattern cho special movement: key catalog chọn handler; RulesGeometry chia sẻ phép kiểm tra bàn cờ.
namespace HeroChess.Rules;

// IMovementHandler: Hợp đồng strategy: sinh đích và cập nhật state sau nước đi.
public interface IMovementHandler
{
    // GenerateDestinations: Sinh đích đặc biệt, chưa tự lọc self-check; HomeDiagonal đi 1–2, WildElephant đi 1–4 và tránh độ dài trước.
    IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece);
    // AfterMove: Hook sau nước thật: Dã Tượng lưu lastMoveDistance; HomeDiagonal không có trạng thái riêng.
    void AfterMove(PieceState piece, BoardPoint from, BoardPoint to);
}

// MovementHandlerRegistry: Tra handler theo implementation key trong catalog.
public sealed class MovementHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IMovementHandler> _handlers;

    // MovementHandlerRegistry: Đăng ký hai key mặc định và cho phép bổ sung/ghi đè handler qua constructor.
    public MovementHandlerRegistry(IEnumerable<KeyValuePair<string, IMovementHandler>>? additional = null)
    {
        var handlers = new Dictionary<string, IMovementHandler>(StringComparer.Ordinal)
        {
            ["elephant.diagonal_range"] = new HomeDiagonalHandler(2),
            ["elephant.alternating_distance"] = new WildElephantHandler()
        };
        if (additional is not null)
            foreach (var pair in additional) handlers[pair.Key] = pair.Value;
        _handlers = handlers;
    }

    // TryGet: Tra handler theo key; key không biết trả false, không tự fallback special move.
    public bool TryGet(string? key, out IMovementHandler handler)
    {
        if (key is not null && _handlers.TryGetValue(key, out var found))
        {
            handler = found;
            return true;
        }
        handler = null!;
        return false;
    }
}

// HomeDiagonalHandler: Tượng chéo trong nửa sân nhà, khoảng cách tối đa cấu hình qua constructor.
internal sealed class HomeDiagonalHandler : IMovementHandler
{
    private readonly int _maxDistance;
    // HomeDiagonalHandler: Nhận khoảng cách chéo tối đa; registry mặc định truyền 2.
    public HomeDiagonalHandler(int maxDistance) => _maxDistance = maxDistance;

    // GenerateDestinations: Sinh đích đặc biệt, chưa tự lọc self-check; HomeDiagonal đi 1–2, WildElephant đi 1–4 và tránh độ dài trước.
    public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
    {
        if (piece.Position is not { } from) yield break;
        foreach (var (dx, dy) in Diagonals())
        for (var distance = 1; distance <= _maxDistance; distance++)
        {
            var to = new BoardPoint(from.X + dx * distance, from.Y + dy * distance);
            if (!to.IsOnBoard || !RulesGeometry.IsHomeSide(piece.Side, to.Y)) break;
            var obstacle = state.Obstacles.Any(x => x.Position == to);
            if (obstacle) break;
            var occupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
            if (occupant is null) yield return to;
            else { yield return to; break; }
        }
    }

    // AfterMove: Hook sau nước thật: Dã Tượng lưu lastMoveDistance; HomeDiagonal không có trạng thái riêng.
    public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }
    // Diagonals: Trả bốn hướng chéo để duyệt đích Tượng.
    private static IEnumerable<(int dx, int dy)> Diagonals() => new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) };
}

// WildElephantHandler: Dã Tượng đi chéo 1–4 ô, không lặp độ dài nước trước.
internal sealed class WildElephantHandler : IMovementHandler
{
    // GenerateDestinations: Sinh đích đặc biệt, chưa tự lọc self-check; HomeDiagonal đi 1–2, WildElephant đi 1–4 và tránh độ dài trước.
    public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
    {
        if (piece.Position is not { } from) yield break;
        piece.TraitState.TryGetValue("lastMoveDistance", out var lastDistance);
        foreach (var (dx, dy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        for (var distance = 1; distance <= 4; distance++)
        {
            var to = new BoardPoint(from.X + dx * distance, from.Y + dy * distance);
            if (!to.IsOnBoard || !RulesGeometry.IsHomeSide(piece.Side, to.Y)) break;
            var obstacle = state.Obstacles.Any(x => x.Position == to);
            if (obstacle) break;
            var occupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
            if (lastDistance != distance) yield return to;
            if (occupant is not null) break;
        }
    }

    // AfterMove: Hook sau nước thật: Dã Tượng lưu lastMoveDistance; HomeDiagonal không có trạng thái riêng.
    public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) =>
        piece.TraitState["lastMoveDistance"] = Math.Abs(to.X - from.X);
}

// RulesGeometry: Các phép kiểm tra hình học dùng chung.
internal static class RulesGeometry
{
    // IsHomeSide: Red ở y <= 4, Black ở y >= 5.
    public static bool IsHomeSide(Side side, int y) => side == Side.Red ? y <= 4 : y >= 5;
    // IsBlocked: Ô có quân sống hoặc obstacle thì bị cản.
    public static bool IsBlocked(GameState state, BoardPoint point) =>
        state.Pieces.Any(x => x.Status == PieceStatus.Alive && x.Position == point) ||
        state.Obstacles.Any(x => x.Position == point);
}
