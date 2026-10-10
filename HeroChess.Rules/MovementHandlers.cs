// Vai trò file: Strategy pattern cho special movement: key catalog chọn handler; RulesGeometry chia sẻ phép kiểm tra bàn cờ.
namespace HeroChess.Rules;
using HeroChess.Rules.Skills;

// IMovementHandler: Hợp đồng strategy: sinh đích và cập nhật state sau nước đi.
public interface IMovementHandler
{
    // GenerateDestinations: Sinh đích đặc biệt, chưa tự lọc self-check; HomeDiagonal đi 1–2, WildElephant đi 1–4 và tránh độ dài trước.
    IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece);
    // AfterMove: Hook sau nước thật: Dã Tượng lưu lastMoveDistance; HomeDiagonal không có trạng thái riêng.
    void AfterMove(PieceState piece, BoardPoint from, BoardPoint to);

    /// <summary>
    /// Returns true when this handler's pass-through mechanic was used.
    /// </summary>
    bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to);
}

// MovementHandlerRegistry: Tra handler theo implementation key trong catalog.
public sealed class MovementHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IMovementHandler> _handlers;

    // MovementHandlerRegistry: Đăng ký bốn key mặc định và cho phép bổ sung/ghi đè handler qua constructor.
    public MovementHandlerRegistry(IEnumerable<KeyValuePair<string, IMovementHandler>>? additional = null)
    {
        var handlers = new Dictionary<string, IMovementHandler>(StringComparer.Ordinal)
        {
            ["elephant.diagonal_range"] = new HomeDiagonalHandler(2),
            ["elephant.alternating_distance"] = new WildElephantHandler(),
            ["elephant.river_crossing"] = new RiverCrossingDiagonalHandler(),
            ["general.king_move"] = new LeLoiHandler(),
            ["general.orthogonal_range_3"] = new OrthogonalRangeHandler(1), // legacy Quang Trung snapshots
            ["general.orthogonal_range_1_no_palace"] = new TranHungDaoGeneralHandler(),
            ["rook.hoanh_soc"] = new HoanhSocHandler(),
            [SkillKeys.QuangTrungSpecialMove] = new OrthogonalRangeHandler(3),
            // Lý Thường Kiệt Xe can pass or break Thành/Rào.
            [SkillKeys.LyThuongKietXe] = new LyThuongKietXeHandler(),
            // Lý Thường Kiệt Pháo can use Thành/Rào as a screen or break Thành.
            [SkillKeys.LyThuongKietPhao] = new LyThuongKietPhaoHandler(),
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
            var obstacle = state.Obstacles.Any(x => x.Position == to && RulesGeometry.IsBlockingObstacle(x));
            if (obstacle) break;
            var occupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
            if (occupant is null) yield return to;
            else { yield return to; break; }
        }
    }

    // AfterMove: Hook sau nước thật: Dã Tượng lưu lastMoveDistance; HomeDiagonal không có trạng thái riêng.
    public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }
    public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to) => false;
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
            var obstacle = state.Obstacles.Any(x => x.Position == to && RulesGeometry.IsBlockingObstacle(x));
            if (obstacle) break;
            var occupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
            if (lastDistance != distance) yield return to;
            if (occupant is not null) break;
        }
    }

    // AfterMove: Hook sau nước thật: Dã Tượng lưu lastMoveDistance; HomeDiagonal không có trạng thái riêng.
    public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) =>
        piece.TraitState["lastMoveDistance"] = Math.Abs(to.X - from.X);
    public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to) => false;
}

// RiverCrossingDiagonalHandler: Tượng đi chéo 1–2 ô, được qua sông (không giới hạn nửa sân).
// Dùng chung cho Trưng Trắc và Trưng Nhị.
// Ràng buộc: chỉ đi chéo, không nhảy mắt, không đi tổng cộng quá 2 bước, không vượt bàn cờ.
internal sealed class RiverCrossingDiagonalHandler : IMovementHandler
{
    // GenerateDestinations: Sinh đích chéo 1–2 ô không giới hạn nửa sân, không nhảy.
    public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
    {
        if (piece.Position is not { } from) yield break;
        foreach (var (dx, dy) in Diagonals())
        {
            // 1-step diagonal
            var to1 = new BoardPoint(from.X + dx, from.Y + dy);
            if (to1.IsOnBoard)
            {
                var eyeObstacle = state.Obstacles.Any(x => x.Position == to1 && RulesGeometry.IsBlockingObstacle(x));
                var occupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to1);
                if (eyeObstacle) continue;
                if (occupant is null) yield return to1;
                else if (occupant.Side != piece.Side) yield return to1; // capture enemy
            }
            // 2-step diagonal
            var to2 = new BoardPoint(from.X + 2 * dx, from.Y + 2 * dy);
            if (to2.IsOnBoard)
            {
                var eye = new BoardPoint(from.X + dx, from.Y + dy);
                var eyeObstacle = state.Obstacles.Any(x => x.Position == eye && RulesGeometry.IsBlockingObstacle(x));
                var eyeOccupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == eye);
                var destOccupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to2);
                if (eyeObstacle || eyeOccupant is not null) continue;
                if (destOccupant is null) yield return to2;
                else if (destOccupant.Side != piece.Side) yield return to2; // capture enemy
            }
        }
    }

    // AfterMove: Không có trạng thái riêng.
    public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }
    public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to) => false;

    private static IEnumerable<(int dx, int dy)> Diagonals() => new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) };
}

    // LeLoiHandler: Lê Lợi — Tướng đi tối đa 1 ô theo 8 hướng, giới hạn trong cung.
    // Tương đương King nhưng class_code = GENERAL và base movement bị ghi đè bởi handler này.
    // Ràng buộc: 1 bước duy nhất, 8 hướng, trong bàn, trong cung của mình.
    internal sealed class LeLoiHandler : IMovementHandler
    {
        private static readonly (int dx, int dy)[] EightDirections = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

        // GenerateDestinations: Sinh đích 1 bước theo 8 hướng, trong bàn và trong cung.
        public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
        {
            if (piece.Position is not { } from) yield break;
            foreach (var (dx, dy) in EightDirections)
            {
                var to = new BoardPoint(from.X + dx, from.Y + dy);
                if (to.IsOnBoard && InPalace(piece.Side, to))
                    yield return to;
            }
        }

        // AfterMove: Không có trạng thái riêng.
        public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }
        public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to) => false;

        // InPalace: Red cung y=0–2, x=3–5; Black cung y=7–9, x=3–5.
        private static bool InPalace(Side side, BoardPoint point) =>
            point.X is >= 3 and <= 5 &&
            (side == Side.Red ? point.Y is >= 0 and <= 2 : point.Y is >= 7 and <= 9);
    }

    // OrthogonalRangeHandler: Quang Trung — đi thẳng 1–N ô (N từ 1–3), bị chặn như Xe.
    // Không nhảy, không chéo, không qua sông.
    // Cơ chế: duyệt từng bước từ 1 đến maxDistance; dừng khi gặp vật cản.
    // Quân địch đầu tiên trên tia có thể ăn; quân ta dừng trước nó.
    internal sealed class OrthogonalRangeHandler : IMovementHandler
    {
        private readonly int _maxDistance;

        public OrthogonalRangeHandler(int maxDistance) => _maxDistance = maxDistance;

        // GenerateDestinations: Sinh đích thẳng 1–maxDistance, bị chặn như Xe, không qua sông.
        public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
        {
            if (piece.Position is not { } from) yield break;
            foreach (var (dx, dy) in Orthogonal())
            {
                for (var distance = 1; distance <= _maxDistance; distance++)
                {
                    var to = new BoardPoint(from.X + dx * distance, from.Y + dy * distance);
                    if (!to.IsOnBoard) break;
                    if (IsRiverCrossing(piece.Side, from, to)) break;
                    var obstacle = state.Obstacles.Any(x => x.Position == to && RulesGeometry.IsBlockingObstacle(x));
                    if (obstacle) break;
                    var occupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
                    if (occupant is null) yield return to;
                    else { yield return to; break; }
                }
            }
        }

        // AfterMove: Không có trạng thái riêng.
        public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }
        public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to) => false;

        private static (int dx, int dy)[] Orthogonal() => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        // IsRiverCrossing: Red home side y<=4 crosses INTO y>=5; Black home side y>=5 crosses INTO y<=4.
        private static bool IsRiverCrossing(Side side, BoardPoint from, BoardPoint to) =>
            side == Side.Red
                ? from.Y <= 4 && to.Y >= 5
                : from.Y >= 5 && to.Y <= 4;
    }

    // TranHungDaoGeneralHandler: Trần Hưng Đạo — Tướng đi 1 ô thẳng, được ra khỏi cung, không qua sông.
    // Khác Tướng chuẩn: KHÔNG giới hạn trong cung.
    // Khác Lê Lợi: CHỈ thẳng (4 hướng), không chéo.
    // Ràng buộc: 1 bước duy nhất, 4 hướng thẳng, không ra bàn, không qua sông.
    internal sealed class TranHungDaoGeneralHandler : IMovementHandler
    {
        private static readonly (int dx, int dy)[] FourDirections = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        // GenerateDestinations: Sinh đích 1 bước thẳng, không trong cung nhưng không qua sông.
        public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
        {
            if (piece.Position is not { } from) yield break;
            foreach (var (dx, dy) in FourDirections)
            {
                var to = new BoardPoint(from.X + dx, from.Y + dy);
                if (to.IsOnBoard && !IsRiverCrossing(piece.Side, from, to))
                    yield return to;
            }
        }

        // AfterMove: Không có trạng thái riêng.
        public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }
        public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to) => false;

        // IsRiverCrossing: Red home side y<=4 crosses INTO y>=5; Black home side y>=5 crosses INTO y<=4.
        private static bool IsRiverCrossing(Side side, BoardPoint from, BoardPoint to) =>
            side == Side.Red
                ? from.Y <= 4 && to.Y >= 5
                : from.Y >= 5 && to.Y <= 4;
    }

// HoanhSocHandler: Phạm Ngũ Lão — Rook with "hoành sóc giang sơn".
// After a successful capture, the next movement may pass through at most one allied piece.
// Generates standard Rook destinations, plus pass-through destinations when hoanhSocCharged == 1.
internal sealed class HoanhSocHandler : IMovementHandler
{
    public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
    {
        if (piece.Position is not { } from) yield break;

        // Generate standard Rook destinations (blocked by first friendly piece, capture first enemy).
        foreach (var dest in StandardRookDestinations(state, piece, from))
            yield return dest;

        // Generate pass-through destinations if hoanhSocCharged == 1.
        var charged = piece.TraitState.TryGetValue("hoanhSocCharged", out var v) && v == 1;
        if (charged)
        {
            foreach (var dest in PassThroughDestinations(state, piece, from))
                yield return dest;
        }
    }

    // The engine consumes the charge after this piece's next move.
    public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }

    /// <summary>
    /// Returns true if the movement from→to used the hoành sóc pass-through mechanic.
    /// A pass-through move is one where 'to' is reachable by going past exactly one allied
    /// piece and continuing on the same orthogonal line (not stopping at the ally).
    /// Requires access to GameState to check piece occupancy along the path.
    /// </summary>
    public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to)
    {
        if (!piece.TraitState.TryGetValue("hoanhSocCharged", out var v) || v != 1)
            return false;
        return IsPassThroughDestination(state, from, to, piece.Side);
    }

    private static IEnumerable<BoardPoint> StandardRookDestinations(GameState state, PieceState piece, BoardPoint from)
    {
        foreach (var (dx, dy) in Orthogonal())
        {
            for (var distance = 1; distance <= 9; distance++)
            {
                var to = new BoardPoint(from.X + dx * distance, from.Y + dy * distance);
                if (!to.IsOnBoard) break;
                var obstacle = state.Obstacles.Any(x => x.Position == to && RulesGeometry.IsBlockingObstacle(x));
                if (obstacle) break;
                var occupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
                if (occupant is null) yield return to;
                else { yield return to; break; }
            }
        }
    }

    private static IEnumerable<BoardPoint> PassThroughDestinations(GameState state, PieceState piece, BoardPoint from)
    {
        foreach (var (dx, dy) in Orthogonal())
        {
            var passedAlly = false;
            for (var distance = 1; distance <= 9; distance++)
            {
                var to = new BoardPoint(from.X + dx * distance, from.Y + dy * distance);
                if (!to.IsOnBoard) break;
                if (state.Obstacles.Any(x => x.Position == to && RulesGeometry.IsBlockingObstacle(x))) break;

                var occupant = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
                if (occupant is null)
                {
                    if (passedAlly) yield return to;
                }
                else if (occupant.Side == piece.Side)
                {
                    if (passedAlly) break;
                    passedAlly = true;
                }
                else
                {
                    if (passedAlly) yield return to;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Determines whether 'to' is a pass-through destination from 'from'.
    /// A pass-through destination is reached by going past exactly one allied piece
    /// on the same orthogonal line (the ally's cell is not the destination).
    /// Requires GameState to check actual piece occupancy along the path.
    /// </summary>
    private static bool IsPassThroughDestination(GameState state, BoardPoint from, BoardPoint to, Side side)
    {
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);
        // Must be orthogonal
        if (dx != 0 && dy != 0) return false;
        if (dx == 0 && dy == 0) return false;

        var stepX = dx != 0 ? dx : 0;
        var stepY = dy != 0 ? dy : 0;
        var distance = Math.Abs(dx != 0 ? to.X - from.X : to.Y - from.Y);

        var allyCount = 0;
        for (var i = 1; i < distance; i++)
        {
            var intermediate = new BoardPoint(from.X + stepX * i, from.Y + stepY * i);
            if (state.Obstacles.Any(x => x.Position == intermediate && RulesGeometry.IsBlockingObstacle(x))) return false;
            var occupant = state.Pieces.FirstOrDefault(p => p.Status == PieceStatus.Alive && p.Position == intermediate);
            if (occupant is null) continue;
            if (occupant.Side != side || ++allyCount > 1) return false;
        }
        return allyCount == 1;
    }

    private static (int dx, int dy)[] Orthogonal() => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
}

// =============================================================================
// Step 6 — Lý Thường Kiệt Movement Handlers
// =============================================================================

// LyThuongKietXeHandler: Lý Thường Kiệt — Xe.
// Standard Rook movement PLUS passive: Thành/Rào are transparent to this Xe.
// It may land on an obstacle to destroy it or move past without destroying it.
// Normal Xiangqi pieces and Rook movement rules are otherwise unchanged.
internal sealed class LyThuongKietXeHandler : IMovementHandler
{
    public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
    {
        if (piece.Position is not { } from) yield break;

        foreach (var (dx, dy) in Orthogonal())
        {
            for (var distance = 1; distance <= 9; distance++)
            {
                var to = new BoardPoint(from.X + dx * distance, from.Y + dy * distance);
                if (!to.IsOnBoard) break;

                var obstacle = state.Obstacles.FirstOrDefault(x => x.Position == to && RulesGeometry.IsBlockingObstacle(x));
                var target = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
                if (obstacle is not null)
                {
                    if (obstacle.Kind is not (SkillKeys.ObstacleKindThanh or SkillKeys.ObstacleKindRao)) break;
                    yield return to;
                    continue;
                }
                if (target == null)
                {
                    yield return to;
                }
                else
                {
                    if (target.Side != piece.Side) yield return to;
                    break;
                }
            }
        }
    }

    public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }
    public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to) => false;

    private static (int dx, int dy)[] Orthogonal() => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
}

// LyThuongKietPhaoHandler: Lý Thường Kiệt — Pháo.
// Standard Cannon movement PLUS passive: Thành and Rào can serve as cannon screens.
// LKT Pháo destroys a Thành by moving into its square (consumes the turn).
// Rào can act as a screen; hidden Cọc is not a physical screen.
// Normal Xiangqi Cannon rules are otherwise unchanged.
internal sealed class LyThuongKietPhaoHandler : IMovementHandler
{
    public IEnumerable<BoardPoint> GenerateDestinations(GameState state, PieceState piece)
    {
        if (piece.Position is not { } from) yield break;

        foreach (var (dx, dy) in Orthogonal())
        {
            var screened = false;
            for (var distance = 1; distance <= 9; distance++)
            {
                var to = new BoardPoint(from.X + dx * distance, from.Y + dy * distance);
                if (!to.IsOnBoard) break;

                var obstacle = state.Obstacles.FirstOrDefault(x => x.Position == to && RulesGeometry.IsBlockingObstacle(x));
                var target = state.Pieces.FirstOrDefault(x => x.Status == PieceStatus.Alive && x.Position == to);
                var occupied = target != null || obstacle != null;

                if (!screened)
                {
                    if (!occupied) yield return to;
                    else if (obstacle?.Kind == SkillKeys.ObstacleKindThanh) { yield return to; screened = true; }
                    else screened = true;
                }
                else if (occupied)
                {
                    if (target?.Side != piece.Side && target is not null ||
                        obstacle?.Kind is SkillKeys.ObstacleKindThanh or SkillKeys.ObstacleKindRao)
                        yield return to;
                    break;
                }
            }
        }
    }

    public void AfterMove(PieceState piece, BoardPoint from, BoardPoint to) { }
    public bool DidUsePassThrough(GameState state, PieceState piece, BoardPoint from, BoardPoint to) => false;

    private static (int dx, int dy)[] Orthogonal() => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
}
// =============================================================================

// RulesGeometry: Các phép kiểm tra hình học dùng chung.
internal static class RulesGeometry
{
    public static bool IsBlockingObstacle(ObstacleState obstacle) => obstacle.Kind != SkillKeys.ObstacleKindThDTuongCoc;
    // IsHomeSide: Red ở y <= 4, Black ở y >= 5.
    public static bool IsHomeSide(Side side, int y) => side == Side.Red ? y <= 4 : y >= 5;
    // IsBlocked: Ô có quân sống hoặc obstacle thì bị cản.
    public static bool IsBlocked(GameState state, BoardPoint point) =>
        state.Pieces.Any(x => x.Status == PieceStatus.Alive && x.Position == point) ||
        state.Obstacles.Any(x => x.Position == point && IsBlockingObstacle(x));
}
