namespace HeroChess.Rules.Skills;

public sealed class KhienHandler : ICommandSkillHandler
{
    public string ImplementationKey => SkillKeys.Khien;

    public CommandSkillResult Execute(CommandSkillContext ctx)
    {
        if (ctx.Target.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !ctx.Target.TryGetProperty("pieceId", out var id) || id.ValueKind != System.Text.Json.JsonValueKind.String ||
            !id.TryGetGuid(out var pieceId))
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET", "Khiên requires target.pieceId.");

        var target = ctx.State.Pieces.FirstOrDefault(p => p.PieceId == pieceId && p.Side == ctx.ActorSide &&
            p.Status == PieceStatus.Alive && p.Position is not null);
        if (target is null)
            return CommandSkillResult.Failure(ctx.State, "INVALID_TARGET", "Khiên requires a living allied piece.");
        if (target.Effects.Any(e => e.Code == SkillKeys.ShieldEffect && e.RemainingTurns > 0))
            return CommandSkillResult.Failure(ctx.State, "ALREADY_SHIELDED", "The piece already has Khiên.");

        var next = ctx.State.Clone();
        next.Pieces.Single(p => p.PieceId == pieceId).Effects.Add(new EffectState(SkillKeys.ShieldEffect, null, 4));
        return CommandSkillResult.Success(next, new object[] { new { type = "shield.applied", pieceId } });
    }
}
