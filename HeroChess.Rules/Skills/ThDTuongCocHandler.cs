// Step 6: Trần Hưng Đạo — Tượng Hero Skill.
// Places one visible Cọc on the river, directly in front of the hero.
// The hero can also recall (remove) the placed stake.
using HeroChess.Rules.Effects;

namespace HeroChess.Rules.Skills;

/// <summary>
/// Handler for Trần Hưng Đạo — Tượng's "Tạo Cọc An Trên Sông" Hero Skill.
///
/// Creates exactly ONE visible Cọc obstacle on the river, in one of the 3 forward
/// river cells in front of the hero.
///
/// Rules:
/// - Skill cooldown: 3 turns.
/// - One stake per hero instance at a time.
/// - Stake position: one of the 3 river cells directly in front of the hero.
/// - "In front" = forward toward the opponent (Red: y+1 toward Black; Black: y-1 toward Red).
/// - Stake is visible (not hidden).
/// - Stake duration: 3 turns counted from the turn it is created.
/// - Recall: removes the stake; does NOT teleport the hero.
/// - Choosing NOT to place (target with no valid position) does NOT consume the skill.
/// - If a stake already exists, placing a new one replaces it.
/// - Recall does NOT consume the skill cooldown.
///
/// Hero identification:
/// - Identified by the piece whose HeroId corresponds to the THD Tượng hero character.
/// - The handler locates the THD Tượng ELEPHANT piece on the actor's side.
/// </summary>
public sealed class ThDTuongCocHandler : ICommandSkillHandler
{
    public string ImplementationKey => SkillKeys.ThDTuongCoc;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        // --- Parse target ---
        if (!ctx.Target.TryGetProperty("action", out var actionProp))
        {
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET",
                "THD Tượng Cọc requires a target with an action field (place or recall).");
        }
        var action = actionProp.GetString();

        if (action == "recall")
            return ExecuteRecall(ctx);
        if (action == "place")
            return ExecutePlace(ctx);

        return CommandSkillResult.Failure(ctx.State, "INVALID_ACTION",
            "THD Tượng Cọc action must be 'place' or 'recall'.");
    }

    private CommandSkillResult ExecutePlace(CommandSkillContext ctx)
    {
        // --- Parse position ---
        if (!ctx.Target.TryGetProperty("position", out var posProp) ||
            !posProp.TryGetProperty("x", out var xProp) ||
            !posProp.TryGetProperty("y", out var yProp))
        {
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET",
                "THD Tượng Cọc placement requires a position {x, y}.");
        }
        var x = xProp.GetInt32();
        var y = yProp.GetInt32();
        var pos = new BoardPoint(x, y);

        // --- Locate the THD Tượng piece ---
        // THD Tượng is an ELEPHANT-class piece on the actor's side.
        // We identify it by side + class as a fallback.
        var thdPiece = FindThdTuongPiece(ctx.State, ctx.ActorSide);
        if (thdPiece == null)
            return CommandSkillResult.Failure(ctx.State, "NO_THUONG_TUONG",
                "THD Tượng piece not found on actor's side.");

        // --- Validate river position ---
        if (!IsRiver(pos))
            return CommandSkillResult.Failure(ctx.State, "NOT_RIVER",
                "THD Tượng Cọc must be placed on the river (y=4 or y=5).");

        // --- Validate directly in front of the hero ---
        var forward = GetForwardDirection(thdPiece.Side);
        var heroPos = thdPiece.Position!.Value;
        var expectedY = heroPos.Y + forward;
        if (pos.X != heroPos.X || pos.Y != expectedY)
            return CommandSkillResult.Failure(ctx.State, "NOT_IN_FRONT",
                $"THD Tượng Cọc must be placed directly in front of the hero on the river (x={heroPos.X}, y={expectedY}).");

        // --- Build state changes ---
        var next = ctx.State.Clone();
        var obstacleId = Guid.NewGuid();

        // Remove any existing stake for this hero.
        if (next.ThdTuongCocStakes.TryGetValue(thdPiece.PieceId, out var existingId))
        {
            next.Obstacles.RemoveAll(o => o.ObstacleId == existingId);
        }

        // Add new stake.
        next.Obstacles.Add(new ObstacleState(obstacleId, pos, SkillKeys.ObstacleKindThDTuongCoc)
        {
            RemainingLifetime = 3
        });

        // Track placer for TurnLifecycle lifetime decrement.
        next.ObstacleMetadata[obstacleId] = new ObstacleMetadata { Placer = ctx.ActorSide };

        // Record in ThdTuongCocStakes.
        next.ThdTuongCocStakes[thdPiece.PieceId] = obstacleId;

        var events = new object[]
        {
            new
            {
                type = "obstacle.created",
                obstacleId = obstacleId,
                kind = SkillKeys.ObstacleKindThDTuongCoc,
                position = new { x, y },
                creator = ctx.ActorSide.ToString()
            }
        };

        return CommandSkillResult.Success(next, events);
    }

    private CommandSkillResult ExecuteRecall(CommandSkillContext ctx)
    {
        var thdPiece = FindThdTuongPiece(ctx.State, ctx.ActorSide);
        if (thdPiece == null)
            return CommandSkillResult.Failure(ctx.State, "NO_THUONG_TUONG",
                "THD Tượng piece not found for recall.");

        // Check if there's a stake to recall.
        if (!ctx.State.ThdTuongCocStakes.TryGetValue(thdPiece.PieceId, out var stakeId))
        {
            return CommandSkillResult.Failure(ctx.State, "NO_STAKE",
                "THD Tượng Cọc recall: no stake exists to recall.");
        }

        var next = ctx.State.Clone();
        next.Obstacles.RemoveAll(o => o.ObstacleId == stakeId);
        next.ThdTuongCocStakes.Remove(thdPiece.PieceId);
        next.ObstacleMetadata.Remove(stakeId);

        var events = new object[]
        {
            new
            {
                type = "obstacle.removed",
                obstacleId = stakeId,
                kind = SkillKeys.ObstacleKindThDTuongCoc,
                reason = "recall",
                creator = ctx.ActorSide.ToString()
            }
        };

        // Recall does NOT consume the skill cooldown.
        return CommandSkillResult.Success(next, events, cooldownTurns: null);
    }

    /// <summary>
    /// Locates the THD Tượng ELEPHANT piece on the given side.
    /// Uses ThdTuongCocStakes as a hint when multiple ELEPHANT pieces exist.
    /// </summary>
    private static PieceState? FindThdTuongPiece(GameState state, Side side)
    {
        var candidates = state.Pieces
            .Where(p => p.Status == PieceStatus.Alive && p.Side == side && p.Class == PieceClass.Elephant)
            .ToList();

        if (candidates.Count == 0) return null;

        // Prefer the piece that already has a stake in ThdTuongCocStakes.
        var withStake = candidates.FirstOrDefault(p => state.ThdTuongCocStakes.ContainsKey(p.PieceId));
        if (withStake != null) return withStake;

        // If only one ELEPHANT, return it.
        if (candidates.Count == 1) return candidates[0];

        // Ambiguous: multiple ELEPHANT pieces, none with a stake.
        // Fall back to the first one (this should be rare in practice).
        return candidates[0];
    }

    private static bool IsRiver(BoardPoint pos) => pos.Y == 4 || pos.Y == 5;

    private static int GetForwardDirection(Side side) => side == Side.Red ? 1 : -1;
}
