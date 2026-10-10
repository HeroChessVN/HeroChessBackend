// Step 6: Thành — Command Skill.
// Creates a temporary fortification obstacle on the owning side's half of the board.
// Duration: 6 shared board turns. Cooldown: 8 shared board turns.
// Only Cannon (Pháo) can destroy Thành.
// Thành CANNOT act as a Cannon screen.
// Thành blocks normal movement.
using HeroChess.Rules.Effects;

namespace HeroChess.Rules.Skills;

/// <summary>
/// Handler for Thành.
///
/// Creates a single Thành fortification obstacle at the target position.
/// Thành is a movement obstacle but CANNOT serve as a cannon screen.
///
/// Rules:
/// - Duration: 6 shared board turns.
/// - Cooldown: 8 shared board turns.
/// - Target: a single valid cell on the owner's half of the board.
/// - Only Cannon can destroy Thành (handled by MoveUnchecked).
/// - Thành is NOT a valid Cannon screen.
/// - If Thành is placed on an occupied cell, activation fails.
/// </summary>
public sealed class ThanhHandler : ICommandSkillHandler
{
    public string ImplementationKey => SkillKeys.Thanh;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        // --- Parse target: { "position": { "x": 0, "y": 0 } } ---
        if (ctx.Target.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !ctx.Target.TryGetProperty("position", out var posProp) || posProp.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !posProp.TryGetProperty("x", out var xProp) || xProp.ValueKind != System.Text.Json.JsonValueKind.Number || !xProp.TryGetInt32(out var x) ||
            !posProp.TryGetProperty("y", out var yProp) || yProp.ValueKind != System.Text.Json.JsonValueKind.Number || !yProp.TryGetInt32(out var y))
        {
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET",
                "Thành requires a target with a position {x, y}.");
        }
        var pos = new BoardPoint(x, y);

        if (!pos.IsOnBoard)
            return CommandSkillResult.Failure(ctx.State, "INVALID_POSITION",
                "Thành position is off the board.");

        // --- Must be on the owner's half ---
        if (!IsOnOwnHalf(ctx.ActorSide, pos))
            return CommandSkillResult.Failure(ctx.State, "WRONG_HALF",
                "Thành must be placed on the owner's half of the board.");

        // --- Cell must not be occupied by a living piece ---
        if (ctx.State.Pieces.Any(p => p.Status == PieceStatus.Alive && p.Position == pos))
            return CommandSkillResult.Failure(ctx.State, "CELL_OCCUPIED",
                "Cannot place Thành on a cell occupied by a piece.");
        if (ctx.State.Obstacles.Any(o => o.Position == pos && o.Kind != SkillKeys.ObstacleKindThDTuongCoc))
            return CommandSkillResult.Failure(ctx.State, "CELL_OCCUPIED", "Cannot place Thành on an occupied cell.");

        // --- Build state changes ---
        var next = ctx.State.Clone();
        var obstacleId = Guid.NewGuid();
        // The turn that creates Thành counts as the first of six turns.
        next.Obstacles.Add(new ObstacleState(obstacleId, pos, SkillKeys.ObstacleKindThanh)
        {
            RemainingLifetime = 6
        });
        // Track placer for TurnLifecycle lifetime decrement.
        next.ObstacleMetadata[obstacleId] = new ObstacleMetadata { Placer = ctx.ActorSide };

        var events = new object[]
        {
            new
            {
                type = "obstacle.created",
                obstacleId = obstacleId,
                kind = SkillKeys.ObstacleKindThanh,
                position = new { x, y },
                creator = ctx.ActorSide.ToString()
            }
        };

        return CommandSkillResult.Success(next, events);
    }

    /// <summary>Red owns y=0..4, Black owns y=5..9.</summary>
    private static bool IsOnOwnHalf(Side side, BoardPoint pos) =>
        side == Side.Red ? pos.Y <= 4 : pos.Y >= 5;
}
