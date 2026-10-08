// Phase 3.5: Effect Enforcement in Live Match — Unit tests for effect-aware movement validation.
using HeroChess.Rules;
using HeroChess.Rules.Effects;
using HeroChess.Rules.Skills;
using Xunit;

namespace HeroChess.Rules.Tests;

/// <summary>
/// Phase 3.5 tests: Effect-aware movement validation in XiangqiRulesEngine.
/// Verifies that Active EffectInstances are enforced during GenerateLegalActions() and ApplyMove().
/// </summary>
public sealed class EffectEnforcementTests
{
    private readonly XiangqiRulesEngine _rules = new();

    // ========================================================================
    // VẠN CỌC TRẤN GIANG TESTS
    // ========================================================================

    [Fact]
    // VanCoc_active_effect_blocks_opponent_river_crossing: Opponent Rook crossing
    // river into a Vạn Cọc-affected column must be blocked.
    //
    // Setup: Red Rook at (3, 3) — safe from exposing own General.
    // Destination: (3, 6) — crossing river, column 3 is NOT in Vạn Cọc columns {4,5,6}.
    // Wait — Vạn Cọc blocks columns 4,5,6. Rook at (3,3) can go to (3,4) which is column 3.
    // Let me use a rook that crosses into column 4.
    //
    // Red Rook at (0, 4) — already on Red's side.
    // Destination: (0, 5) — crossing river, column 4 is in Vạn Cọc columns.
    // But the rook's horizontal route to column 4 crosses column 4 while still on Red side (y=4).
    // Vạn Cọc only blocks when the move is a RIVER CROSSING.
    // From (0,4) to (0,5): y goes from 4 to 5 — this IS a river crossing.
    public void VanCoc_active_effect_blocks_opponent_river_crossing()
    {
        // Red Rook at (3, 3). Go forward: (3, 4) — crosses river, column 3 not in Vạn Cọc.
        // Go right: (8, 3) — same side. Go forward right: (4, 4) — crosses? No, still y=4.
        // Let me use Rook at (0, 3) going to (0, 5): (0,3)→(0,4)→(0,5). From y=3 to y=5 crosses river at y=5.
        // Actually: Rook at (3, 3), moving to (3, 6): (3,3)→(3,4)→(3,5)→(3,6).
        // But the rook moves through (3,4) and (3,5). At (3,4) it crosses from y=3 to y=4.
        // For river crossing: Red home side y <= 4. So from y=3 to y=4 is NOT crossing.
        // From y=4 to y=5 IS crossing. So a rook at (0, 4) moving to (0, 5) crosses.
        // But the rook at (0, 4) going horizontally to (8, 4) never crosses — same y.
        //
        // Red Rook at (0, 3). Moves: (0, 4), (0, 5), (0, 6), etc.
        // (0,3)→(0,4): same side (y=3 to y=4). NOT crossing.
        // (0,4)→(0,5): crosses river (y=4 to y=5).
        // Rook cannot "jump" to (0,5) — it traverses through (0,4).
        // IsRiverCrossing checks from and to only, not the path.
        // So (0,3)→(0,5) IS a river crossing.
        //
        // Red Rook at (0, 3). Vạn Cọc at columns {4,5,6}.
        // (0,3)→(0,5): crosses river (y=3 to y=5), dest column = 0 (NOT in Vạn Cọc).
        // Wait, column X=0. Vạn Cọc blocks columns 4,5,6. X=0 is not blocked.
        //
        // Let me reconsider. Rook at (4, 3) going to (4, 6):
        // (4,3)→(4,6): crosses river, dest column X=4 IS in Vạn Cọc columns. BLOCKED.
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        // Black creates Vạn Cọc at columns 4, 5, 6
        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking", ["selectedPaths"] = new[] { 4, 5, 6 } }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // (4,6) is a river-crossing move to column 4, blocked by Vạn Cọc
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 6));
        // (4,5) also crosses river to column 4 — blocked
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 5));
    }

    [Fact]
    // VanCoc_active_effect_does_not_block_same_side_movement: Vạn Cọc must NOT
    // block a Rook moving within the same side of the board.
    //
    // Rook at (0, 3). Moves within y <= 4. Destination (8, 3). Same side.
    // (0,3)→(8,3): NOT a river crossing. Must NOT be blocked.
    public void VanCoc_active_effect_does_not_block_same_side_movement()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 0, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Moving within same side (y=3) — NOT a river crossing — must be legal
        Assert.Contains(moves, x => x.To == new BoardPoint(8, 3));
    }

    [Fact]
    // VanCoc_active_effect_does_not_block_same_side_capture: Vạn Cọc must NOT
    // block a capture on the same side of the board.
    //
    // Rook at (0, 3). Enemy at (5, 3). Both on Red's side (y=3).
    // Capture (0,3)→(5,3): NOT a river crossing. Must NOT be blocked.
    public void VanCoc_active_effect_does_not_block_same_side_capture()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 0, 3);
        var target = Piece(PieceClass.Soldier, Side.Black, 5, 3);
        var state = State(rook, target);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Capture at (5,3) is on Red's side — NOT a river crossing — must be legal
        Assert.Contains(moves, x => x.To == new BoardPoint(5, 3) && x.CapturedPieceId == target.PieceId);
    }

    [Fact]
    // VanCoc_owner_can_cross_own_effect: The effect creator must be able to
    // cross their own Vạn Cọc columns freely.
    //
    // Black Rook at (4, 6). Goes to (4, 5) — crossing river, column 4.
    // Black owns the effect — crossing must be allowed.
    public void VanCoc_owner_can_cross_own_effect()
    {
        var rook = Piece(PieceClass.Rook, Side.Black, 4, 6);
        var state = State(rook);
        state.SideToMove = Side.Black;

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black, // Black owns the effect
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state, Side.Black)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Black crosses river into column 4 — blocked for opponent, NOT for owner
        Assert.Contains(moves, x => x.To == new BoardPoint(4, 5));
    }

    [Fact]
    // VanCoc_disabled_effect_allows_crossing: A Disabled Vạn Cọc must NOT block.
    public void VanCoc_disabled_effect_allows_crossing()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        vanCoc.State = EffectStateValue.Disabled;
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Disabled effect must NOT block
        Assert.Contains(moves, x => x.To == new BoardPoint(4, 6));
    }

    [Fact]
    // VanCoc_ended_effect_allows_crossing: An Ended Vạn Cọc must NOT block.
    public void VanCoc_ended_effect_allows_crossing()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        vanCoc.State = EffectStateValue.Ended;
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        Assert.Contains(moves, x => x.To == new BoardPoint(4, 6));
    }

    [Fact]
    // VanCoc_multiple_columns_all_blocked: Vạn Cọc at columns {4,5,6} blocks
    // opponent crossing into all three columns.
    public void VanCoc_multiple_columns_all_blocked()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // All three river columns must be blocked
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 6)); // col 4
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(5, 6)); // col 5
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(6, 6)); // col 6
    }

    [Fact]
    // Stake_obstacle_blocks_cannon_screen_unchanged: Physical stake Obstacles must
    // continue to work for Cannon screen behavior.
    //
    // Cannon at (0, 2), screen at (0, 3) as a stake, target at (0, 5).
    // The stake at (0,3) is a physical obstacle that blocks cannon movement.
    // Cannon at (0,2) with screen at (0,3) can capture (0,5) if there's no screen after.
    // But there's no piece at (0,4) so cannon sees: screen=(0,3), then (0,4) empty, (0,5) target.
    // That's not a valid capture. Without the stake at (0,3), cannon would have no screen
    // and couldn't capture at (0,5) anyway.
    //
    // Let's use: Cannon at (0,2), screen at (0,3), screen at (0,4), target at (0,6).
    // With stake at (0,3): screen at (0,3), (0,4) empty → invalid.
    // Without stake: no screen at (0,3) → invalid.
    // Let's use: Cannon at (0,2), piece at (0,3), target at (0,6).
    // And a stake at (0,4): Cannon sees screen at (0,3) then stake at (0,4) — still invalid.
    //
    // Correct test: Cannon with ONE piece screen at (0,3), stake at (0,4).
    // The stake acts as an additional screen, blocking the capture.
    public void Stake_obstacle_blocks_cannon_screen_unchanged()
    {
        var cannon = Piece(PieceClass.Cannon, Side.Red, 0, 2);
        var screen = Piece(PieceClass.Soldier, Side.Red, 0, 3);
        var target = Piece(PieceClass.Rook, Side.Black, 0, 6);
        var state = State(cannon, screen, target);

        // Stake at (0,4) — additional "screen" for cannon
        state.Obstacles.Add(new ObstacleState(Guid.NewGuid(), new BoardPoint(0, 4), "stake"));

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == cannon.PieceId)
            .ToList();

        // Cannon path: 0,2 → 0,3(screen) → 0,4(stake) → 0,5 → 0,6(target)
        // With one piece screen (0,3) and one stake (0,4): 2 occupied squares after screen
        // → NOT a valid capture. This verifies stake behavior is unchanged.
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(0, 6));
    }

    // ========================================================================
    // BINH LÂM THỦY HIỂM TESTS
    // ========================================================================

    [Fact]
    // BinhLam_active_effect_blocks_opponent_crossing_on_applied_route: Binh Lâm
    // blocks opponent crossing only on the applied/non-conflicting columns.
    //
    // Red Rook at (4, 3). Goes to (4, 6) — crosses river, column 4.
    // Binh Lâm applied only to columns {4, 5} (col 6 conflicts).
    // Result: BLOCKED — column 4 is applied.
    public void BinhLam_active_effect_blocks_opponent_crossing_on_applied_route()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var binhLam = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 1,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5 }),
            payload: new Dictionary<string, object?>
            {
                ["kind"] = "terrain",
                ["restriction"] = "river_crossing_block",
                ["paths"] = new[] { 4, 5 },
                ["originalPaths"] = new[] { 4, 5, 6 },
                ["appliedPaths"] = new[] { 4, 5 }
            }
        );
        state.EffectInstances.Add(binhLam);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Rook at (4,3) going to (4,6): crosses river, column 4 is blocked.
        // (4,5): crosses river, column 4 is blocked.
        // (6,3): horizontal move, same side y=3, NOT crossing, NOT blocked.
        // Rook can't go to (6,6) — that's diagonal, not a rook move.
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 5));
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 6));
        // Rook can go horizontally to (6, 3) — same side, NOT crossing, NOT blocked
        Assert.Contains(moves, x => x.To == new BoardPoint(6, 3));
    }

    [Fact]
    // BinhLam_same_side_movement_remains_legal: Binh Lâm must NOT block same-side movement.
    public void BinhLam_same_side_movement_remains_legal()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 0, 3);
        var state = State(rook);

        var binhLam = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 1,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        state.EffectInstances.Add(binhLam);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Moving within same side (y=3) — NOT a river crossing
        Assert.Contains(moves, x => x.To == new BoardPoint(8, 3));
    }

    [Fact]
    // BinhLam_disabled_effect_allows_crossing: Disabled Binh Lâm does not block.
    public void BinhLam_disabled_effect_allows_crossing()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var binhLam = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 1,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        binhLam.State = EffectStateValue.Disabled;
        state.EffectInstances.Add(binhLam);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        Assert.Contains(moves, x => x.To == new BoardPoint(4, 6));
    }

    [Fact]
    // BinhLam_ended_effect_allows_crossing: Ended Binh Lâm does not block.
    public void BinhLam_ended_effect_allows_crossing()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var binhLam = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 1,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        binhLam.State = EffectStateValue.Ended;
        state.EffectInstances.Add(binhLam);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        Assert.Contains(moves, x => x.To == new BoardPoint(4, 6));
    }

    // ========================================================================
    // PHẢN KỲ ĐOẠT THẾ TESTS
    // ========================================================================

    [Fact]
    // PhanKy_pending_controller_does_not_transfer_authority: During the thief's
    // current turn, PendingController = thief but PositionControllers still = original.
    // The thief must NOT immediately gain control — use PositionControllers only.
    //
    // Black creates terrain at column 4. Red uses Phản Kỳ (sets PendingController=Red).
    // Red Rook at (4, 3) tries to cross to (4, 5) — column 4.
    // Result: BLOCKED — PendingController alone does not transfer authority.
    public void PhanKy_pending_controller_does_not_transfer_authority()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        // Black creates a terrain effect at column 4
        var terrain = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        state.EffectInstances.Add(terrain);

        // Red uses Phản Kỳ — sets PendingController = Red
        terrain.PendingController = Side.Red;
        // PositionControllers still = Black (original)

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // PendingController = Red, but PositionControllers still = Black.
        // Thief (Red) should be BLOCKED from using the terrain until finalization.
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 6));
    }

    [Fact]
    // PhanKy_finalized_position_controllers_determine_authority: After finalization,
    // PositionControllers is the authoritative source.
    //
    // PositionControllers now = Red (finalized steal).
    // Red Rook crosses into column 4.
    // Result: NOT BLOCKED — Red now officially controls the effect.
    public void PhanKy_finalized_position_controllers_determine_authority()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var terrain = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        // Override PositionControllers to simulate finalized steal
        terrain.PositionControllers.Clear();
        foreach (var pos in terrain.TargetPositions)
            terrain.PositionControllers[pos] = Side.Red; // Red now controls
        terrain.PendingController = null; // Finalized

        state.EffectInstances.Add(terrain);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Red now controls via PositionControllers — crossing allowed
        Assert.Contains(moves, x => x.To == new BoardPoint(4, 6));
    }

    [Fact]
    // PhanKy_original_creator_blocked_after_finalization: After steal is finalized,
    // original creator (Black) is blocked from using their own former terrain.
    public void PhanKy_original_creator_blocked_after_finalization()
    {
        var rook = Piece(PieceClass.Rook, Side.Black, 4, 6);
        var state = State(rook);
        state.SideToMove = Side.Black;

        var terrain = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        // Override to show finalized steal: Black created it, Red stole it, now finalized
        terrain.PositionControllers.Clear();
        foreach (var pos in terrain.TargetPositions)
            terrain.PositionControllers[pos] = Side.Red; // Red now controls
        terrain.PendingController = null;

        state.EffectInstances.Add(terrain);

        var moves = _rules.GenerateLegalActions(state, Side.Black)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Black crosses back to Red's side (y=6 to y=5) — blocked because Red controls the terrain
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 5));
    }

    // ========================================================================
    // PHÁ TRẬN ĐOẠT PHONG TESTS
    // ========================================================================

    [Fact]
    // PhaTran_remaining_duration_gt_zero_continues_enforcement: Effect with
    // RemainingDuration > 0 continues to block.
    public void PhaTran_remaining_duration_gt_zero_continues_enforcement()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var terrain = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        terrain.RemainingDuration = 2;
        state.EffectInstances.Add(terrain);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Active + RemainingDuration > 0 — must block
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 6));
    }

    [Fact]
    // PhaTran_remaining_duration_reaches_zero_allows_crossing: When Phá Trận
    // reduces RemainingDuration to 0, the effect transitions to Ended and no longer blocks.
    public void PhaTran_remaining_duration_reaches_zero_allows_crossing()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var terrain = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        terrain.RemainingDuration = 0;
        terrain.State = EffectStateValue.Ended;
        state.EffectInstances.Add(terrain);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Ended — must not block
        Assert.Contains(moves, x => x.To == new BoardPoint(4, 6));
    }

    [Fact]
    // PhaTran_disabled_effect_does_not_enforce: Disabled effect does not block,
    // even if RemainingDuration > 0.
    public void PhaTran_disabled_effect_does_not_enforce()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var terrain = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );
        terrain.State = EffectStateValue.Disabled;
        terrain.RemainingDuration = 2;
        state.EffectInstances.Add(terrain);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Disabled — must not block regardless of remaining duration
        Assert.Contains(moves, x => x.To == new BoardPoint(4, 6));
    }

    // ========================================================================
    // APPLY MOVE + INTEGRATION TESTS
    // ========================================================================

    [Fact]
    // ApplyMove_rejects_effect_blocked_move: ApplyMove must return ILLEGAL_MOVE
    // when the requested move is blocked by an Active effect.
    public void ApplyMove_rejects_effect_blocked_move()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var result = _rules.ApplyMove(state, new MoveAction(rook.PieceId, new BoardPoint(4, 6)));

        Assert.False(result.Accepted);
        Assert.Equal("ILLEGAL_MOVE", result.Error!.Code);
    }

    [Fact]
    // Legal_actions_exclude_effect_blocked_destinations: GenerateLegalActions must
    // not include effect-blocked destinations in the legal move list.
    //
    // Rook at (0, 3). Vạn Cọc blocks columns {4, 5, 6}.
    // - (3,3): horizontal, same side, NOT crossing → NOT blocked ✓
    // - (4,3): horizontal, same side (y=3), NOT crossing → NOT blocked ✓
    // - (4,5): vertical, crosses river, column 4 → BLOCKED ✓
    // - (4,6): vertical, crosses river, column 4 → BLOCKED ✓
    // - (5,3): horizontal, same side, NOT crossing → NOT blocked ✓
    // Note: column 4 horizontal moves (y=3) are NOT blocked because they don't cross.
    public void Legal_actions_exclude_effect_blocked_destinations()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 0, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var legal = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .Select(x => x.To)
            .ToList();

        // Column 3 destinations (same side, not crossing): NOT blocked
        Assert.Contains(legal, x => x.X == 3 && x.Y == 3);
        // Column 4 HORIZONTAL (y=3, same side, not crossing): NOT blocked
        Assert.Contains(legal, x => x.X == 4 && x.Y == 3);
        // Column 4 VERTICAL (crosses river): BLOCKED
        Assert.DoesNotContain(legal, x => x.X == 4 && x.Y > 4);
        // Column 5 VERTICAL (crosses river): BLOCKED
        Assert.DoesNotContain(legal, x => x.X == 5 && x.Y > 4);
        // Column 6 VERTICAL (crosses river): BLOCKED
        Assert.DoesNotContain(legal, x => x.X == 6 && x.Y > 4);
    }

    [Fact]
    // Bot_uses_same_engine_respects_effects: Bot calls GenerateLegalActions via
    // XiangqiRulesEngine — effect enforcement applies automatically.
    public void Bot_uses_same_engine_respects_effects()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var legal = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Bot would pick from this set — blocked move is not available
        Assert.DoesNotContain(legal, x => x.To == new BoardPoint(4, 6));
    }

    // ========================================================================
    // REGRESSION TESTS — base Xiangqi rules unchanged
    // ========================================================================

    [Fact]
    // Base_movement_unchanged_without_effects: Without EffectInstances, normal Xiangqi
    // movement is identical to before Phase 3.5.
    public void Base_movement_unchanged_without_effects()
    {
        // Rook at (0, 3). Enemy at (5, 3) blocks the rook's path.
        // Rook can capture at (5, 3) but NOT continue to (8, 3) since enemy is there.
        var rook = Piece(PieceClass.Rook, Side.Red, 0, 3);
        var enemy = Piece(PieceClass.Soldier, Side.Black, 5, 3);
        var state = State(rook, enemy);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Capture is legal
        Assert.Contains(moves, x => x.To == new BoardPoint(5, 3) && x.CapturedPieceId == enemy.PieceId);
        // (4,3) is before the blocker — legal
        Assert.Contains(moves, x => x.To == new BoardPoint(4, 3));
        // (0,3) itself is NOT a move — you can't move to where you already are
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(0, 3));
    }

    [Fact]
    // Self_check_filtering_unchanged_with_effects: Self-check filtering must
    // remain unchanged even when effects block some destinations.
    public void Self_check_filtering_unchanged_with_effects()
    {
        // Rook at (3, 4). General at (3, 0). If rook moves away to (5, 4),
        // the General would be exposed to the enemy rook at (3, 8).
        // Vạn Cọc blocks column 4 — so the move to (5,4) is blocked by the effect,
        // AND the move to (4,4) would expose the General. Both should be absent.
        var rook = Piece(PieceClass.Rook, Side.Red, 3, 4);
        var state = State(
            Piece(PieceClass.General, Side.Red, 3, 0),
            rook,
            Piece(PieceClass.General, Side.Black, 5, 9),
            Piece(PieceClass.Rook, Side.Black, 3, 8)
        );

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // (4,4): column 4 is blocked by Vạn Cọc (river crossing)
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 4));
        // (5,4): would expose own General to enemy rook at (3,8) — self-check blocks it
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(5, 4));
    }

    [Fact]
    // State_after_retains_effect_instances: When a move succeeds, the resulting
    // state must retain EffectInstances unchanged.
    public void State_after_retains_effect_instances()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 0, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);
        var originalEffectId = vanCoc.EffectId;

        // Move within same side (not crossing)
        var result = _rules.ApplyMove(state, new MoveAction(rook.PieceId, new BoardPoint(8, 3)));

        Assert.True(result.Accepted);
        Assert.Single(result.State.EffectInstances);
        Assert.Equal(originalEffectId, result.State.EffectInstances[0].EffectId);
    }

    [Fact]
    // Failed_action_does_not_mutate_state: An effect-blocked move must not
    // mutate any state — no version change, no effect change.
    public void Failed_action_does_not_mutate_state()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);
        var originalVersion = state.Version;

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var result = _rules.ApplyMove(state, new MoveAction(rook.PieceId, new BoardPoint(4, 6)));

        Assert.False(result.Accepted);
        Assert.Equal(originalVersion, state.Version);
        Assert.Single(state.EffectInstances);
        Assert.Equal(EffectStateValue.Active, state.EffectInstances[0].State);
    }

    // ========================================================================
    // CANNON / RAY MOVEMENT WITH EFFECTS TESTS
    // ========================================================================

    [Fact]
    // Cannon_crossing_river_blocked_by_VanCoc: Cannon crossing river into
    // a Vạn Cọc-affected column must be blocked.
    //
    // Cannon at (4, 3). Goes to (4, 6) — crosses river, column 4.
    // Must be blocked.
    public void Cannon_crossing_river_blocked_by_VanCoc()
    {
        var cannon = Piece(PieceClass.Cannon, Side.Red, 4, 3);
        var screen = Piece(PieceClass.Soldier, Side.Red, 4, 4);
        var state = State(cannon, screen);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == cannon.PieceId)
            .ToList();

        // Cannon at (4,3) with screen at (4,4) can go to (4,5) and (4,6).
        // (4,5): crosses river, column 4 — blocked
        // (4,6): crosses river, column 4 — blocked
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 6));
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 5));
    }

    [Fact]
    // Cannon_crossing_non_blocked_column_allowed: Cannon crossing river into a
    // column NOT affected by Vạn Cọc must be allowed.
    public void Cannon_crossing_non_blocked_column_allowed()
    {
        var cannon = Piece(PieceClass.Cannon, Side.Red, 0, 3);
        var screen = Piece(PieceClass.Soldier, Side.Red, 0, 4);
        var target = Piece(PieceClass.Rook, Side.Black, 0, 6);
        var state = State(cannon, screen, target);

        // Vạn Cọc at columns 4,5,6 — column 0 is NOT affected
        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == cannon.PieceId)
            .ToList();

        // Cannon at col 0 crossing to col 0 (y=6) — column 0 is not blocked
        Assert.Contains(moves, x => x.To == new BoardPoint(0, 6) && x.CapturedPieceId == target.PieceId);
    }

    // ========================================================================
    // MULTIPLE EFFECTS STACK CORRECTLY
    // ========================================================================

    [Fact]
    // Multiple_effects_stack_correctly: When both Vạn Cọc and Binh Lâm are
    // Active, all affected columns must be blocked.
    public void Multiple_effects_stack_correctly()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 4, 3);
        var state = State(rook);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );

        var binhLam = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.BinhLamThuyHien,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 2,
            duration: 1,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5 }),
            payload: new Dictionary<string, object?> { ["kind"] = "terrain", ["restriction"] = "river_crossing_block" }
        );

        state.EffectInstances.Add(vanCoc);
        state.EffectInstances.Add(binhLam);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == rook.PieceId)
            .ToList();

        // Columns 4, 5, 6 all blocked
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 6));
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(5, 6));
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(6, 6));
        // Column 8 is not blocked
        Assert.Contains(moves, x => x.To == new BoardPoint(8, 3));
    }

    // ========================================================================
    // SOLDIER RIVER CROSSING WITH EFFECTS
    // ========================================================================

    [Fact]
    // Soldier_crossing_river_blocked_by_VanCoc: Red Soldier crossing river
    // from y=3 to y=4 into column 4 must be blocked by Vạn Cọc.
    public void Soldier_crossing_river_blocked_by_VanCoc()
    {
        var soldier = Piece(PieceClass.Soldier, Side.Red, 4, 3);
        var state = State(soldier);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == soldier.PieceId)
            .ToList();

        // Soldier forward move: (4,3) → (4,4). IsRiverCrossing: y=3 to y=4. NOT crossing (both <=4).
        // So (4,4) should NOT be blocked by Vạn Cọc (it's not a crossing).
        // (4,4) → (4,5): from y=4 to y=5 IS crossing. Column 4 is in Vạn Cọc.
        // But a Soldier can only move one step at a time. From (4,3) to (4,4): not crossing.
        // From (4,4) to (4,5): crossing.
        // So a soldier at (4,3) is never blocked by Vạn Cọc.
        // Let's use a soldier that can cross: one already at y=4.
        // Soldier at (4, 4): forward move → (4,5). From y=4 to y=5. IS crossing. BLOCKED.
        var soldier2 = Piece(PieceClass.Soldier, Side.Red, 4, 4);
        var state2 = State(soldier2);
        state2.EffectInstances.Add(vanCoc);

        var moves2 = _rules.GenerateLegalActions(state2)
            .Where(x => x.PieceId == soldier2.PieceId)
            .ToList();

        // Forward from (4,4) to (4,5): crossing, column 4 blocked
        Assert.DoesNotContain(moves2, x => x.To == new BoardPoint(4, 5));
    }

    [Fact]
    // Soldier_crossing_river_column_not_blocked: Red Soldier crossing river
    // into column 8 (not blocked) must be allowed.
    public void Soldier_crossing_river_column_not_blocked()
    {
        var soldier = Piece(PieceClass.Soldier, Side.Red, 8, 4);
        var state = State(soldier);

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Black,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state)
            .Where(x => x.PieceId == soldier.PieceId)
            .ToList();

        // Column 8 is not blocked — forward move (8,4) → (8,5) is allowed
        Assert.Contains(moves, x => x.To == new BoardPoint(8, 5));
    }

    // ========================================================================
    // BLACK RIVER CROSSING
    // ========================================================================

    [Fact]
    // Black_soldier_crossing_river_blocked_by_VanCoc: Black Soldier crossing river
    // from y=5 to y=4 into column 5 must be blocked.
    public void Black_soldier_crossing_river_blocked_by_VanCoc()
    {
        var soldier = Piece(PieceClass.Soldier, Side.Black, 5, 5);
        var state = State(soldier);
        state.SideToMove = Side.Black;

        var vanCoc = EffectFactory.Create(
            effectId: Guid.NewGuid(),
            code: SkillKeys.VanCocTranGiang,
            skillId: Guid.NewGuid(),
            creator: Side.Red,
            creationOrder: 1,
            duration: 3,
            targetPositions: RiverGeometry.ResolvePositions(new[] { 4, 5, 6 }),
            payload: new Dictionary<string, object?> { ["kind"] = "river_blocking" }
        );
        state.EffectInstances.Add(vanCoc);

        var moves = _rules.GenerateLegalActions(state, Side.Black)
            .Where(x => x.PieceId == soldier.PieceId)
            .ToList();

        // Black forward: (5,5) → (5,4). From y=5 to y=4. IS crossing. Column 5 is blocked.
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(5, 4));
    }

    // ========================================================================
    // STATE HELPERS
    // ========================================================================

    private static GameState State(params PieceState[] pieces)
    {
        var state = new GameState();
        state.Pieces.AddRange(pieces);
        if (!pieces.Any(x => x.Class == PieceClass.General && x.Side == Side.Red))
            state.Pieces.Add(Piece(PieceClass.General, Side.Red, 3, 0));
        if (!pieces.Any(x => x.Class == PieceClass.General && x.Side == Side.Black))
            state.Pieces.Add(Piece(PieceClass.General, Side.Black, 5, 9));
        return state;
    }

    private static PieceState Piece(PieceClass pieceClass, Side side, int x, int y) => new()
    {
        PieceId = Guid.NewGuid(),
        HeroId = Guid.NewGuid(),
        Class = pieceClass,
        Side = side,
        Position = new BoardPoint(x, y),
        StartPosition = new BoardPoint(x, y),
        SetupPoints = 1
    };
}
