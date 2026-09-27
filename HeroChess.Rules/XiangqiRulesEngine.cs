// Vai trò file: Luật di chuyển/chiếu của 7 class và special movement; cùng nguồn legal moves cho player, bot và web.
namespace HeroChess.Rules;

public sealed class XiangqiRulesEngine
{
    private static readonly (int dx, int dy)[] Orthogonal = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int dx, int dy)[] Diagonal = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    private readonly MovementHandlerRegistry _handlers;

    // XiangqiRulesEngine: Nhận registry tùy biến hoặc dùng registry mặc định.
    public XiangqiRulesEngine(MovementHandlerRegistry? handlers = null) => _handlers = handlers ?? new();

    // GenerateLegalActions: Sinh đích giả định rồi mô phỏng để lọc tự chiếu, ăn quân mình và ăn Tướng.
    public IReadOnlyList<LegalMove> GenerateLegalActions(GameState state, Side? side = null)
    {
        if (state.Result is not null) return Array.Empty<LegalMove>();
        var actor = side ?? state.SideToMove;
        var legal = new List<LegalMove>();
        foreach (var piece in state.Pieces.Where(x => x.Side == actor && IsAlive(x)))
        {
            foreach (var destination in GeneratePseudoDestinations(state, piece, attacksOnly: false))
            {
                var target = PieceAt(state, destination);
                if (target?.Side == actor || target?.Class == PieceClass.General) continue;
                var candidate = MoveUnchecked(state, piece.PieceId, destination, runHandler: false);
                if (!IsInCheck(candidate, actor))
                    legal.Add(new(piece.PieceId, piece.Position!.Value, destination, target?.PieceId));
            }
        }
        return legal;
    }

    // ApplyMove: Chỉ nhận nước trong legal list; clone, apply, tăng turn/version/counter, reset AFK và xét đối thủ hết legal moves.
    public ApplyMoveResult ApplyMove(GameState state, MoveAction action)
    {
        if (state.Result is not null)
            return ApplyMoveResult.Failure(state, "MATCH_ENDED", "The match has already ended.");

        var move = GenerateLegalActions(state).FirstOrDefault(x => x.PieceId == action.PieceId && x.To == action.To);
        if (move is null)
            return ApplyMoveResult.Failure(state, "ILLEGAL_MOVE", "The requested move is not legal in the current state.");

        var next = MoveUnchecked(state, action.PieceId, action.To, runHandler: true);
        next.Version++;
        next.TurnIndex++;
        next.CountedActions++;
        next.ConsecutiveTimeouts[state.SideToMove] = 0;
        next.SideToMove = Opposite(state.SideToMove);

        if (GenerateLegalActions(next).Count == 0)
        {
            next.Result = state.SideToMove == Side.Red ? "red_win" : "black_win";
            next.EndReason = IsInCheck(next, next.SideToMove) ? "checkmate" : "no_legal_actions";
        }
        return ApplyMoveResult.Success(next);
    }

    // ApplyTeamSkill: Stub trả SKILL_NOT_IMPLEMENTED; hiện chưa có dispatcher skill.
    public ApplyMoveResult ApplyTeamSkill(GameState state, int skillSlot) =>
        ApplyMoveResult.Failure(state, "SKILL_NOT_IMPLEMENTED", $"Team skill slot {skillSlot} has no implemented handler.");

    // IsInCheck: Xét Tướng thiếu, lộ mặt hai Tướng và các quân địch đang khống chế ô Tướng.
    public bool IsInCheck(GameState state, Side side)
    {
        var general = state.Pieces.FirstOrDefault(x => x.Side == side && x.Class == PieceClass.General && IsAlive(x));
        if (general?.Position is not { } generalPosition) return true;

        var otherGeneral = state.Pieces.FirstOrDefault(x => x.Side != side && x.Class == PieceClass.General && IsAlive(x));
        if (otherGeneral?.Position is { } otherPosition && otherPosition.X == generalPosition.X &&
            CountBlockers(state, generalPosition, otherPosition) == 0) return true;

        return state.Pieces
            .Where(x => x.Side != side && IsAlive(x))
            .Any(x => GeneratePseudoDestinations(state, x, attacksOnly: true).Contains(generalPosition));
    }

    // GeneratePseudoDestinations: Sinh đích theo class/handler trước khi lọc an toàn Tướng; tham số attacksOnly hiện chưa được tách xử lý trong thân hàm.
    private IEnumerable<BoardPoint> GeneratePseudoDestinations(GameState state, PieceState piece, bool attacksOnly)
    {
        if (piece.Position is not { } from) yield break;
        if (piece.MovementImplementationKey is not null)
        {
            if (!_handlers.TryGet(piece.MovementImplementationKey, out var custom)) yield break;
            foreach (var point in custom.GenerateDestinations(state, piece)) yield return point;
            yield break;
        }

        switch (piece.Class)
        {
            case PieceClass.General:
                foreach (var (dx, dy) in Orthogonal)
                {
                    var to = new BoardPoint(from.X + dx, from.Y + dy);
                    if (InPalace(piece.Side, to)) yield return to;
                }
                break;
            case PieceClass.Advisor:
                foreach (var (dx, dy) in Diagonal)
                {
                    var to = new BoardPoint(from.X + dx, from.Y + dy);
                    if (InPalace(piece.Side, to)) yield return to;
                }
                break;
            case PieceClass.Elephant:
                foreach (var (dx, dy) in Diagonal)
                {
                    var eye = new BoardPoint(from.X + dx, from.Y + dy);
                    var to = new BoardPoint(from.X + 2 * dx, from.Y + 2 * dy);
                    if (to.IsOnBoard && RulesGeometry.IsHomeSide(piece.Side, to.Y) && !RulesGeometry.IsBlocked(state, eye))
                        yield return to;
                }
                break;
            case PieceClass.Rook:
                foreach (var point in RayMoves(state, piece, cannon: false)) yield return point;
                break;
            case PieceClass.Cannon:
                foreach (var point in RayMoves(state, piece, cannon: true)) yield return point;
                break;
            case PieceClass.Horse:
                foreach (var (dx, dy) in new[] { (2, 1), (2, -1), (-2, 1), (-2, -1), (1, 2), (-1, 2), (1, -2), (-1, -2) })
                {
                    var leg = Math.Abs(dx) == 2
                        ? new BoardPoint(from.X + Math.Sign(dx), from.Y)
                        : new BoardPoint(from.X, from.Y + Math.Sign(dy));
                    var to = new BoardPoint(from.X + dx, from.Y + dy);
                    if (to.IsOnBoard && !RulesGeometry.IsBlocked(state, leg)) yield return to;
                }
                break;
            case PieceClass.Soldier:
                var direction = piece.Side == Side.Red ? 1 : -1;
                var forward = new BoardPoint(from.X, from.Y + direction);
                if (forward.IsOnBoard) yield return forward;
                var crossed = piece.Side == Side.Red ? from.Y >= 5 : from.Y <= 4;
                if (crossed)
                {
                    if (from.X > 0) yield return new(from.X - 1, from.Y);
                    if (from.X < 8) yield return new(from.X + 1, from.Y);
                }
                break;
        }
    }

    // RayMoves: Duyệt từng tia cho Xe/Pháo; Pháo cần một vật cản làm ngòi trước khi ăn.
    private static IEnumerable<BoardPoint> RayMoves(GameState state, PieceState piece, bool cannon)
    {
        var from = piece.Position!.Value;
        foreach (var (dx, dy) in Orthogonal)
        {
            var screened = false;
            for (var distance = 1; distance <= 9; distance++)
            {
                var to = new BoardPoint(from.X + dx * distance, from.Y + dy * distance);
                if (!to.IsOnBoard) break;
                var target = PieceAt(state, to);
                var obstacle = state.Obstacles.Any(x => x.Position == to);
                var occupied = target is not null || obstacle;

                if (!cannon)
                {
                    if (obstacle) break;
                    if (target is null) yield return to;
                    else { yield return to; break; }
                    continue;
                }

                if (!screened)
                {
                    if (!occupied) yield return to;
                    else screened = true;
                }
                else if (occupied)
                {
                    if (target is not null) yield return to;
                    break;
                }
            }
        }
    }

    // MoveUnchecked: Clone rồi di chuyển/đánh dấu quân bị ăn; chỉ gọi AfterMove khi thực sự apply, không khi mô phỏng legal.
    private GameState MoveUnchecked(GameState state, Guid pieceId, BoardPoint to, bool runHandler)
    {
        var next = state.Clone();
        var piece = next.Pieces.Single(x => x.PieceId == pieceId);
        var from = piece.Position!.Value;
        var captured = PieceAt(next, to);
        if (captured is not null)
        {
            captured.Position = null;
            captured.Status = PieceStatus.Captured;
        }
        piece.Position = to;
        if (runHandler && _handlers.TryGet(piece.MovementImplementationKey, out var handler))
            handler.AfterMove(piece, from, to);
        return next;
    }

    // CountBlockers: Đếm vật cản giữa hai tọa độ thẳng hàng; dùng kiểm tra hai Tướng đối mặt.
    private static int CountBlockers(GameState state, BoardPoint from, BoardPoint to)
    {
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);
        var current = new BoardPoint(from.X + dx, from.Y + dy);
        var count = 0;
        while (current != to)
        {
            if (RulesGeometry.IsBlocked(state, current)) count++;
            current = new(current.X + dx, current.Y + dy);
        }
        return count;
    }

    // PieceAt: Tìm quân còn sống ở một tọa độ.
    private static PieceState? PieceAt(GameState state, BoardPoint point) =>
        state.Pieces.FirstOrDefault(x => IsAlive(x) && x.Position == point);
    // IsAlive: Quân được tính còn sống khi status Alive và có position.
    private static bool IsAlive(PieceState piece) => piece.Status == PieceStatus.Alive && piece.Position is not null;
    // Opposite: Đổi Red sang Black hoặc ngược lại.
    private static Side Opposite(Side side) => side == Side.Red ? Side.Black : Side.Red;
    // InPalace: Kiểm tra ô nằm trên bàn và trong cung của đúng bên.
    private static bool InPalace(Side side, BoardPoint point) => point.IsOnBoard && point.X is >= 3 and <= 5 &&
        (side == Side.Red ? point.Y is >= 0 and <= 2 : point.Y is >= 7 and <= 9);
}
