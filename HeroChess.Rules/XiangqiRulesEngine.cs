// Vai trò file: Luật di chuyển/chiếu của 7 class và special movement; cùng nguồn legal moves cho player, bot và web.
using HeroChess.Rules.Effects;
using HeroChess.Rules.Skills;

namespace HeroChess.Rules;

public sealed class XiangqiRulesEngine
{
    private static readonly (int dx, int dy)[] Orthogonal = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int dx, int dy)[] Diagonal = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    private static readonly int[] RiverColumns = { 4, 5, 6 };
    private readonly MovementHandlerRegistry _handlers;

    // XiangqiRulesEngine: Nhận registry tùy biến hoặc dùng registry mặc định.
    public XiangqiRulesEngine(MovementHandlerRegistry? handlers = null) => _handlers = handlers ?? new();

    // ============================================================
    // Phase 3.5: Effect-aware movement validation
    // ============================================================
    // River boundary: Red home side y <= 4, Black home side y >= 5.
    // A river crossing occurs when from and to straddle this boundary.
    // Red crosses INTO y >= 5; Black crosses INTO y <= 4.
    //
    // River-blocking effects (Vạn Cọc, Binh Lâm):
    // - Only Active effects block.
    // - Only the OPPONENT's crossing is blocked.
    // - The EFFECT OWNER (creator) can cross freely through their own effect.
    // - Moving within the same side (not crossing) is NEVER blocked.
    //
    // Terrain effects with PositionControllers:
    // - A piece may only occupy a position controlled by an effect if the
    //   PositionControllers[position] == piece.Side (or no effect covers it).
    // - PendingController is NOT authoritative — use PositionControllers only.

    /// <summary>
    /// Returns true when moving from → to crosses the river boundary.
    /// Red crosses into y >= 5; Black crosses into y <= 4.
    /// Returns false when moving entirely within one side of the river.
    /// </summary>
    private static bool IsRiverCrossing(BoardPoint from, BoardPoint to)
    {
        // Red: home side y <= 4; crosses into y >= 5
        if (from.Y <= 4 && to.Y >= 5) return true;
        // Black: home side y >= 5; crosses into y <= 4
        if (from.Y >= 5 && to.Y <= 4) return true;
        return false;
    }

    /// <summary>
    /// Returns true when the given destination column is a river-crossing column (4, 5, 6).
    /// </summary>
    private static bool IsRiverColumn(int columnX) =>
        columnX == 4 || columnX == 5 || columnX == 6;

    /// <summary>
    /// Resolves the effective movement handler key for hero-specific skills.
    ///
    /// Routing rules:
    /// - Quang Trung (MovementKey = "general.orthogonal_range_3"):
    ///     cooldown ready (cooldownReady == 1) → "quang_trung.hoanh_soc" (up to 9 ortho)
    ///     otherwise → "general.orthogonal_range_3" (up to 3 ortho, no river crossing)
    /// - Phạm Ngũ Lão (MovementKey = "rook.hoanh_soc"):
    ///     always uses "rook.hoanh_soc" (standard + pass-through via TraitState)
    /// - All other handlers: use the piece's MovementImplementationKey unchanged.
    /// </summary>
    private static string ResolveEffectiveHandlerKey(GameState state, PieceState piece)
    {
        var key = piece.MovementImplementationKey;
        if (key == "general.orthogonal_range_3")
        {
            // Quang Trung hero: check cooldown-ready state.
            var ready = piece.TraitState.TryGetValue(SkillKeys.QuangTrungCooldownKey, out var v)
                && v == 1;
            return ready ? SkillKeys.QuangTrungHoanhSoc : key;
        }
        return key!;
    }

    /// <summary>
    /// Checks whether a river-crossing move is blocked by an Active river-blocking effect
    /// created by the opponent of the given piece.
    ///
    /// Blocking rules:
    /// - Only Active effects block (Disabled/Ended do not).
    /// - Moving within the same side (not crossing) is NEVER blocked by river effects.
    /// - Physical stake obstacles are handled separately by RulesGeometry.IsBlocked().
    ///
    /// Ownership resolution for river-blocking effects:
    /// - For "terrain" kind effects (e.g., Binh Lâm, stolen terrain via Phản Kỳ):
    ///   Check PositionControllers for the crossing column. The controller controls the column.
    ///   (Phản Kỳ transfer updates PositionControllers.)
    /// - For "river_blocking" kind effects (e.g., Vạn Cọc): Creator controls the column.
    ///
    /// River-crossing column detection:
    /// - For straight-line moves (Rook, Cannon — dx=0 or dy=0):
    ///   Scan every column along the path. The first affected column blocks.
    ///   This correctly handles vertical moves where the crossing column != destination.
    ///   e.g., Rook from (4,3) to (4,6): crosses at column 4 (intermediate position).
    /// - For jump moves (Horse, Soldier, Advisor):
    ///   Check the DESTINATION position's column (and for Horse, the intermediate leg).
    /// </summary>
    private static bool IsRiverCrossingBlockedByEffect(GameState state, PieceState piece, BoardPoint from, BoardPoint to)
    {
        if (!IsRiverCrossing(from, to)) return false;

        // For straight-line moves (rook, cannon — same column or same row):
        // scan every column/row along the path to find the first crossing column.
        // For jump moves (horse, soldier): check destination (and intermediate for horse).
        bool isStraightLine = from.X == to.X || from.Y == to.Y;

        IEnumerable<int> columnsToCheck;
        if (isStraightLine && from.X == to.X)
        {
            // Vertical straight-line: the crossing column is the source column (where the piece stands).
            columnsToCheck = new[] { from.X };
        }
        else if (isStraightLine && from.Y == to.Y)
        {
            // Horizontal straight-line: the crossing column is the destination column
            // (the piece enters this column at the final step of the move).
            columnsToCheck = new[] { to.X };
        }
        else
        {
            // Jump move (horse, soldier, advisor, elephant): check destination column.
            columnsToCheck = new[] { to.X };
        }

        foreach (var crossingColumn in columnsToCheck)
        {
            if (!IsRiverColumn(crossingColumn)) continue;

            foreach (var effect in state.EffectInstances)
            {
                if (effect.State != EffectStateValue.Active) continue;

                // Is this a river-blocking skill?
                if (!string.Equals(effect.Code, SkillKeys.VanCocTranGiang, StringComparison.Ordinal) &&
                    !string.Equals(effect.Code, SkillKeys.BinhLamThuyHien, StringComparison.Ordinal))
                    continue;

                // Does this effect cover the crossing column?
                var isInEffect = effect.TargetPositions.Any(p => p.X == crossingColumn);
                if (!isInEffect) continue;

                // Determine whether this piece can cross.
                // For "terrain" kind: use PositionControllers for the crossing column.
                // For "river_blocking" kind: only the creator can cross.
                bool canCross;
                Side? controllerInColumn = null;
                if (effect.Payload.TryGetValue("kind", out var kindObj) &&
                    string.Equals(kindObj as string, "terrain", StringComparison.Ordinal))
                {
                    foreach (var kvp in effect.PositionControllers)
                    {
                        if (kvp.Key.X == crossingColumn)
                        { controllerInColumn = kvp.Value; break; }
                    }
                    canCross = controllerInColumn.HasValue
                        ? piece.Side == controllerInColumn.Value
                        : piece.Side == effect.Creator;
                }
                else
                {
                    canCross = piece.Side == effect.Creator;
                }

                if (!canCross) return true; // Blocked by this effect.
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a destination is controlled by an Active terrain effect that does
    /// not belong to the moving piece's side.
    ///
    /// A piece may only occupy a position covered by a terrain effect if:
    /// - PositionControllers[position] == piece.Side, OR
    /// - No Active effect covers that position.
    ///
    /// NOTE: This only applies to "terrain" kind effects (e.g., Binh Lâm).
    /// "river_blocking" kind effects (e.g., Vạn Cọc) are handled separately by
    /// IsRiverCrossingBlockedByEffect — they do NOT restrict same-side movement
    /// via PositionControllers. Vạn Cọc's PositionControllers are for cannon-screen
    /// purposes only.
    ///
    /// PendingController is NOT authoritative — use PositionControllers only.
    /// </summary>
    private static bool IsBlockedByTerrainEffect(GameState state, PieceState piece, BoardPoint destination)
    {
        // Short-circuit: if no Active effect covers this position, it's always free.
        // Only "terrain" kind effects restrict movement via PositionControllers.
        // "river_blocking" effects (Vạn Cọc) are handled by IsRiverCrossingBlockedByEffect.
        IEnumerable<EffectInstance> activeEffectsAtPos = state.EffectInstances
            .Where(e => e.State == EffectStateValue.Active && e.TargetPositions.Contains(destination));

        // Filter to only "terrain" kind effects (skip river_blocking)
        var terrainEffects = new List<EffectInstance>();
        foreach (var e in activeEffectsAtPos)
        {
            if (e.Payload.TryGetValue("kind", out var kindObj) &&
                string.Equals(kindObj as string, "terrain", StringComparison.Ordinal))
                terrainEffects.Add(e);
        }

        if (terrainEffects.Count == 0) return false;

        // For each Active terrain effect covering this position, check PositionControllers.
        foreach (var effect in terrainEffects)
        {
            if (!effect.PositionControllers.TryGetValue(destination, out var controller))
                continue; // No controller entry at this exact position.

            // The position is controlled by this terrain effect. Block if it's not this piece's side.
            if (controller != piece.Side) return true;
        }

        return false;
    }

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
    // Phase 3.5: Each destination is validated against Active EffectInstances before being yielded.
    // Step 5 (Hero Skills): Quang Trung cooldown-gated movement; Phạm Ngũ Lão hoành sóc pass-through.
    private IEnumerable<BoardPoint> GeneratePseudoDestinations(GameState state, PieceState piece, bool attacksOnly)
    {
        if (piece.Position is not { } from) yield break;
        if (piece.MovementImplementationKey is not null)
        {
            // Step 5 Hero Skill routing: resolve the effective handler key for hero-specific skills.
            var effectiveKey = ResolveEffectiveHandlerKey(state, piece);
            if (_handlers.TryGet(effectiveKey, out var custom))
            {
                foreach (var point in custom.GenerateDestinations(state, piece))
                    if (!IsBlockedByActiveEffect(state, piece, from, point, attacksOnly))
                        yield return point;
            }
            yield break;
        }

        switch (piece.Class)
        {
            case PieceClass.General:
                foreach (var (dx, dy) in Orthogonal)
                {
                    var to = new BoardPoint(from.X + dx, from.Y + dy);
                    if (InPalace(piece.Side, to) && !IsBlockedByActiveEffect(state, piece, from, to, attacksOnly))
                        yield return to;
                }
                break;
            case PieceClass.Advisor:
                foreach (var (dx, dy) in Diagonal)
                {
                    var to = new BoardPoint(from.X + dx, from.Y + dy);
                    if (InPalace(piece.Side, to) && !IsBlockedByActiveEffect(state, piece, from, to, attacksOnly))
                        yield return to;
                }
                break;
            case PieceClass.Elephant:
                foreach (var (dx, dy) in Diagonal)
                {
                    var eye = new BoardPoint(from.X + dx, from.Y + dy);
                    var to = new BoardPoint(from.X + 2 * dx, from.Y + 2 * dy);
                    if (to.IsOnBoard && RulesGeometry.IsHomeSide(piece.Side, to.Y) && !RulesGeometry.IsBlocked(state, eye)
                        && !IsBlockedByActiveEffect(state, piece, from, to, attacksOnly))
                        yield return to;
                }
                break;
            case PieceClass.Rook:
                foreach (var point in RayMoves(state, piece, cannon: false))
                    if (!IsBlockedByActiveEffect(state, piece, from, point, attacksOnly))
                        yield return point;
                break;
            case PieceClass.Cannon:
                foreach (var point in RayMoves(state, piece, cannon: true))
                    if (!IsBlockedByActiveEffect(state, piece, from, point, attacksOnly))
                        yield return point;
                break;
            case PieceClass.Horse:
                foreach (var (dx, dy) in new[] { (2, 1), (2, -1), (-2, 1), (-2, -1), (1, 2), (-1, 2), (1, -2), (-1, -2) })
                {
                    var leg = Math.Abs(dx) == 2
                        ? new BoardPoint(from.X + Math.Sign(dx), from.Y)
                        : new BoardPoint(from.X, from.Y + Math.Sign(dy));
                    var to = new BoardPoint(from.X + dx, from.Y + dy);
                    if (to.IsOnBoard && !RulesGeometry.IsBlocked(state, leg)
                        && !IsBlockedByActiveEffect(state, piece, from, to, attacksOnly))
                        yield return to;
                }
                break;
            case PieceClass.Soldier:
                var direction = piece.Side == Side.Red ? 1 : -1;
                var forward = new BoardPoint(from.X, from.Y + direction);
                if (forward.IsOnBoard && !IsBlockedByActiveEffect(state, piece, from, forward, attacksOnly))
                    yield return forward;
                var crossed = piece.Side == Side.Red ? from.Y >= 5 : from.Y <= 4;
                if (crossed)
                {
                    if (from.X > 0)
                    {
                        var left = new BoardPoint(from.X - 1, from.Y);
                        if (!IsBlockedByActiveEffect(state, piece, from, left, attacksOnly))
                            yield return left;
                    }
                    if (from.X < 8)
                    {
                        var right = new BoardPoint(from.X + 1, from.Y);
                        if (!IsBlockedByActiveEffect(state, piece, from, right, attacksOnly))
                            yield return right;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Phase 3.5: Unified effect-aware destination filter.
    ///
    /// Checks all active EffectInstances for restrictions on the given move.
    /// Returns true if the destination is BLOCKED by an active effect.
    /// Returns false if the destination is LEGAL (no active effect blocks it).
    ///
    /// Checks in order:
    /// 1. River-crossing block (Vạn Cọc / Binh Lâm): opponent crossing river through an affected column.
    /// 2. Terrain control block (Phản Kỳ): destination controlled by opponent via PositionControllers.
    ///
    /// Does NOT check:
    /// - Physical stake Obstacles (handled by RulesGeometry.IsBlocked / RayMoves)
    /// - Disabled or Ended effects (only Active effects block)
    /// - Effect duration (handled by TurnLifecycle — enforcement is by Effect.State)
    /// </summary>
    private static bool IsBlockedByActiveEffect(GameState state, PieceState piece, BoardPoint from, BoardPoint to, bool attacksOnly)
    {
        // 1. River-crossing block: opponent crossing through Vạn Cọc / Binh Lâm columns.
        // Only blocks when actually crossing the river (from != to side).
        if (IsRiverCrossingBlockedByEffect(state, piece, from, to))
            return true;

        // 2. Terrain control: destination occupied by a terrain effect owned by opponent.
        // This is checked regardless of river crossing — any destination covered by
        // an Active terrain effect whose PositionControllers denies access.
        if (IsBlockedByTerrainEffect(state, piece, to))
            return true;

        return false;
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

        // Resolve effective handler key for all checks below.
        var effectiveKey = piece.MovementImplementationKey;

        // Step 6 — Lý Thường Kiệt Pháo destroys Thành without moving onto it.
        // Two cases:
        // 1. LKT Pháo captures a piece or moves orthogonally — any Thành along the path is destroyed.
        //    (Thành is not a screen, so it can be in the middle of the path or at the destination.)
        // 2. LKT Pháo targets a Thành directly (to=Thành position) — destroy it in place, no move.
        var lktPhaoDestroyedThanh = false;
        if (effectiveKey == SkillKeys.LyThuongKietPhao)
        {
            var dx = Math.Sign(to.X - from.X);
            var dy = Math.Sign(to.Y - from.Y);
            // Only orthogonal moves
            if ((dx == 0) != (dy == 0))
            {
                // Case 1: destroy any Thành along the path between from and to (intermediate cells).
                // This fires whether or not we are capturing a piece.
                var x = from.X + dx;
                var y = from.Y + dy;
                while (x != to.X || y != to.Y)
                {
                    var pos = new BoardPoint(x, y);
                    var thanhIdx = next.Obstacles.FindIndex(o =>
                        o.Position == pos && o.Kind == SkillKeys.ObstacleKindThanh);
                    if (thanhIdx >= 0)
                    {
                        next.Obstacles.RemoveAt(thanhIdx);
                        lktPhaoDestroyedThanh = true;
                    }
                    x += dx;
                    y += dy;
                }

                // Case 2: if the target cell itself is a Thành obstacle, destroy it in place.
                // The piece does NOT move onto the Thành's square.
                var targetThanhIdx = next.Obstacles.FindIndex(o =>
                    o.Position == to && o.Kind == SkillKeys.ObstacleKindThanh);
                if (targetThanhIdx >= 0)
                {
                    next.Obstacles.RemoveAt(targetThanhIdx);
                    lktPhaoDestroyedThanh = true;
                    // Do NOT update piece.Position — cannon stays at 'from'.
                    // Skip normal capture logic and the piece-position update below.
                    if (runHandler)
                    {
                        if (_handlers.TryGet(effectiveKey, out var handler))
                            handler.AfterMove(piece, from, to);
                        var didUsePassThrough = handler?.DidUsePassThrough(state, piece, from, to) == true;
                        if (didUsePassThrough)
                            piece.TraitState.Remove(SkillKeys.HoanhSocChargedKey);
                    }
                    return next;
                }
            }
        }

        // Apply the move: update piece position and handle normal piece capture.
        // (LKT Pháo Thành destruction is NOT a piece capture.)
        if (captured is not null && !lktPhaoDestroyedThanh)
        {
            captured.Position = null;
            captured.Status = PieceStatus.Captured;
        }
        piece.Position = to;

        if (runHandler)
        {
            // Run the standard handler's AfterMove (e.g., WildElephant lastMoveDistance).
            // Use the already-resolved effectiveKey from above.
            if (_handlers.TryGet(effectiveKey, out var handler))
                handler.AfterMove(piece, from, to);

            // Step 6 — Phạm Ngũ Lão hoành sóc charge consumption:
            // Consume charge ONLY if the handler confirms this move used pass-through.
            // AfterMove no longer clears charge unconditionally.
            // Call DidUsePassThrough ONCE and cache the result to avoid double-evaluation.
            var didUsePassThrough = handler?.DidUsePassThrough(state, piece, from, to) == true;
            if (didUsePassThrough)
                piece.TraitState.Remove(SkillKeys.HoanhSocChargedKey);

            // Step 5 Hero Skill — grant hoành sóc charge after successful BASIC capture by Phạm Ngũ Lão.
            // "Basic/Normal capture" = NOT a pass-through capture.
            // If the capture used pass-through (DidUsePassThrough above), the charge was consumed
            // and we skip the recharge so the hero doesn't double-dip.
            if (captured is not null && !didUsePassThrough)
                GrantHoanhSocChargeAfterCapture(next, piece);

            // Step 5 Hero Skill — Quang Trung cooldown consumption.
            ConsumeQuangTrungCooldownIfUsed(next, piece);
        }

        return next;
    }

    /// <summary>
    /// After a successful BASIC capture by Phạm Ngũ Lão, grants the hoành sóc charge.
    /// The charge is stored in TraitState["hoanhSocCharged"] = 1.
    /// This is a ONE-TIME charge: the charge is granted on basic capture and consumed on
    /// the next pass-through movement.
    /// Charge does NOT stack — if already charged, do not overwrite with another grant.
    /// </summary>
    private static void GrantHoanhSocChargeAfterCapture(GameState state, PieceState piece)
    {
        // Only Phạm Ngũ Lão (rook.hoanh_soc) gets the charge.
        if (piece.MovementImplementationKey != SkillKeys.HoanhSoc) return;
        // Do NOT stack — if already charged, leave the existing charge.
        if (piece.TraitState.ContainsKey(SkillKeys.HoanhSocChargedKey)) return;
        piece.TraitState[SkillKeys.HoanhSocChargedKey] = 1;
    }

    /// <summary>
    /// After a successful move by Quang Trung (general.orthogonal_range_3), if the move
    /// used the cooldown-ready handler (quang_trung.hoanh_soc), the cooldown is consumed.
    ///
    /// Consumption rule:
    /// - If cooldownReady == 1 AND the effective handler was quang_trung.hoanh_soc,
    ///   set cooldownReady = 0 and start cooldown on Quang Trung's team skill.
    /// - A failed/illegal move never calls MoveUnchecked with runHandler=true,
    ///   so no cooldown is consumed for a rejected move.
    /// </summary>
    private static void ConsumeQuangTrungCooldownIfUsed(GameState state, PieceState piece)
    {
        if (piece.MovementImplementationKey != "general.orthogonal_range_3") return;
        if (!piece.TraitState.TryGetValue(SkillKeys.QuangTrungCooldownKey, out var v) || v != 1) return;

        // The move used the cooldown-ready handler — consume it.
        piece.TraitState[SkillKeys.QuangTrungCooldownKey] = 0;

        // Start cooldown on all team skills that belong to this hero.
        // We rebuild the list with the new cooldown value.
        if (state.SkillStates.TryGetValue(piece.Side, out var skills))
        {
            state.SkillStates[piece.Side] = skills
                .Select(s => s with { CooldownRemaining = SkillKeys.QuangTrungCooldownTurns })
                .ToList();
        }
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
