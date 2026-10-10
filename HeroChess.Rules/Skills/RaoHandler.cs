// Step 6: Rào — Command Skill.
// Creates a temporary barrier obstacle on the owning side's half of the board.
// Duration: 4 shared board turns. Cooldown: 6 shared board turns.
// Rào behaves like a Soldier for destruction interaction.
// Rào blocks normal movement.
using HeroChess.Rules.Effects;

namespace HeroChess.Rules.Skills;

/// <summary>
/// Handler for Rào.
///
/// Creates a single Rào barrier obstacle at the target position.
/// Rào behaves like a Soldier for destruction: any piece that could capture a Soldier
/// can "destroy" the Rào (i.e., the Rào occupies a cell that pieces move through).
/// Since Rào is a barrier (not a piece), "destroying" it means the piece captures/moves
/// onto its square and the Rào is removed.
///
/// Rules:
/// - Duration: 4 shared board turns.
/// - Cooldown: 6 shared board turns.
/// - Target: a single valid cell on the owner's half of the board.
/// - Rào is a movement obstacle.
/// - When a piece moves to/through the Rào cell, the Rào is removed (like being captured).
/// - No specific piece is required to "capture" it — any move onto its cell removes it.
/// - If Rào is placed on an occupied cell, activation fails.
/// </summary>
public sealed class RaoHandler : ICommandSkillHandler
{
    public string ImplementationKey => SkillKeys.Rao;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        // --- Parse target: { "position": { "x": 0, "y": 0 } } ---
        if (ctx.Target.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !ctx.Target.TryGetProperty("position", out var posProp) || posProp.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !posProp.TryGetProperty("x", out var xProp) || xProp.ValueKind != System.Text.Json.JsonValueKind.Number || !xProp.TryGetInt32(out var x) ||
            !posProp.TryGetProperty("y", out var yProp) || yProp.ValueKind != System.Text.Json.JsonValueKind.Number || !yProp.TryGetInt32(out var y))
        {
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET",
                "Rào requires a target with a position {x, y}.");
        }
        var pos = new BoardPoint(x, y);

        if (!pos.IsOnBoard)
            return CommandSkillResult.Failure(ctx.State, "INVALID_POSITION",
                "Rào position is off the board.");

        // --- Must be on the owner's half ---
        if (!IsOnOwnHalf(ctx.ActorSide, pos))
            return CommandSkillResult.Failure(ctx.State, "WRONG_HALF",
                "Rào must be placed on the owner's half of the board.");

        // --- Cell must not be occupied by a living piece ---
        if (ctx.State.Pieces.Any(p => p.Status == PieceStatus.Alive && p.Position == pos))
            return CommandSkillResult.Failure(ctx.State, "CELL_OCCUPIED",
                "Cannot place Rào on a cell occupied by a piece.");
        if (ctx.State.Obstacles.Any(o => o.Position == pos && o.Kind != SkillKeys.ObstacleKindThDTuongCoc))
            return CommandSkillResult.Failure(ctx.State, "CELL_OCCUPIED", "Cannot place Rào on an occupied cell.");

        // --- Build state changes ---
        var next = ctx.State.Clone();
        var obstacleId = Guid.NewGuid();
        // The turn that creates Rào counts as the first of four turns.
        next.Obstacles.Add(new ObstacleState(obstacleId, pos, SkillKeys.ObstacleKindRao)
        {
            RemainingLifetime = 4
        });
        // Track placer for TurnLifecycle lifetime decrement.
        next.ObstacleMetadata[obstacleId] = new ObstacleMetadata { Placer = ctx.ActorSide };

        var events = new object[]
        {
            new
            {
                type = "obstacle.created",
                obstacleId = obstacleId,
                kind = SkillKeys.ObstacleKindRao,
                position = new { x, y },
                creator = ctx.ActorSide.ToString()
            }
        };

        return CommandSkillResult.Success(next, events);
    }

    private static bool IsOnOwnHalf(Side side, BoardPoint pos) =>
        side == Side.Red ? pos.Y <= 4 : pos.Y >= 5;
}
