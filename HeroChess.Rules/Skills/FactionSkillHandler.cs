using System.Text.Json;

namespace HeroChess.Rules.Skills;

public sealed record RiverSnare(Side Owner, BoardPoint Position, int ExpiresAt, int? RootDurationTurns = null);

public sealed class FactionSkillHandler(string key) : ICommandSkillHandler
{
    public const string Trap = "bach_dang_tran_phap";
    public const string Forecast = "lam_son_quan_luoc";
    public const string Rally = "hich_xuat_quan";
    public const string Rooted = "bach_dang_rooted";
    public string ImplementationKey => key;

    public static bool IsNewFactionSkill(string key) => key is Trap or Forecast or Rally;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        var next = ctx.State.Clone();
        var duration = SkillTiming.Duration(ctx, 6);
        if (key is Trap or Rally && !SkillTiming.FitsTurn(next.TurnIndex, duration))
            return CommandSkillResult.Failure(ctx.State, "INVALID_SKILL_CONFIG", "The faction skill requires a positive durationTurns.");
        var rootDuration = 4;
        if (key == Trap && next.SkillTimingVersion >= 2 &&
            (!SkillTiming.Positive(ctx.Parameters, "rootDurationTurns", out rootDuration) || !SkillTiming.FitsTurn(next.TurnIndex, rootDuration)))
            return CommandSkillResult.Failure(ctx.State, "INVALID_SKILL_CONFIG", "The trap requires a positive rootDurationTurns.");
        if (key == Trap)
        {
            if (ctx.Target.ValueKind != JsonValueKind.Object ||
                !ctx.Target.TryGetProperty("positions", out var positions) ||
                positions.ValueKind != JsonValueKind.Array || positions.GetArrayLength() != 3)
                return Invalid();
            var points = new HashSet<BoardPoint>();
            foreach (var value in positions.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object ||
                    !value.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number || !x.TryGetInt32(out var col) ||
                    !value.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Number || !y.TryGetInt32(out var row) ||
                    col is < 0 or > 8 || row is not (4 or 5) || !points.Add(new BoardPoint(col, row)))
                    return Invalid();
            }
            if (points.Any(p => !SkillPlacement.IsCellFree(next, p, ctx.ActorSide)))
                return CommandSkillResult.Failure(ctx.State, "CELL_OCCUPIED", "Select three distinct empty river cells.");
            next.RiverSnares.AddRange(points.Select(p => new RiverSnare(ctx.ActorSide, p,
                next.TurnIndex + duration + (next.SkillTimingVersion >= 2 ? 1 : 0), rootDuration)));
        }
        else if (key == Rally)
        {
            foreach (var soldier in next.Pieces.Where(p => p.Side == ctx.ActorSide && p.Class == PieceClass.Soldier && p.Status == PieceStatus.Alive))
                soldier.Effects.Add(new EffectState(Rally, null, duration, ctx.State.TurnIndex));
        }
        return CommandSkillResult.Success(next, Array.Empty<object>());

        CommandSkillResult Invalid() => CommandSkillResult.Failure(ctx.State, "INVALID_TARGET", "Select three distinct empty river cells.");
    }

    public static IEnumerable<object> Targets(GameState state)
    {
        var points = Enumerable.Range(0, 9).OrderBy(x => Math.Abs(x - 4))
            .SelectMany(x => new[] { new BoardPoint(x, 4), new BoardPoint(x, 5) })
            .Where(p => SkillPlacement.IsCellFree(state, p, state.SideToMove)).Take(3).ToArray();
        if (points.Length == 3) yield return new { positions = points };
    }
}
