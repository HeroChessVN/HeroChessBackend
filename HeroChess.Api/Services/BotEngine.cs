using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeroChess.Api.Infrastructure;
using HeroChess.Rules;
using HeroChess.Rules.Skills;

namespace HeroChess.Api.Services;

public sealed record BotDecision(JsonElement? Action, int Nodes, int CompletedDepth, bool BudgetExhausted);

public sealed class BotEngine(CommandSkillDispatcher dispatcher, TimeProvider? time = null)
{
    private readonly TimeProvider _clock = time ?? TimeProvider.System;
    private readonly XiangqiRulesEngine _rules = new();

    public object Forecast(GameState state, RulesetSnapshot rules, IReadOnlyDictionary<Side, FrozenLineup> lineups, CancellationToken ct)
    {
        var started = _clock.GetTimestamp();
        var variation = new List<object>(3);
        object[] threats = [];
        for (var ply = 0; ply < 3 && state.Result is null && !OutOfTime(); ply++)
        {
            var actor = state.SideToMove;
            var ranked = new List<(JsonElement Action, GameState Next, int Score, bool Threat)>();
            foreach (var action in Candidates(state, OrderedMoves(state), 6))
            {
                ct.ThrowIfCancellationRequested();
                if (OutOfTime() || ranked.Count == 6) break;
                if (Simulate(state, action, rules, lineups) is not { } next) continue;
                var threat = next.Result == (actor == Side.Red ? "red_win" : "black_win") ||
                    _rules.IsInCheck(next, next.SideToMove) ||
                    next.Pieces.Count(p => p.Side != actor && p.Status == PieceStatus.Alive) <
                    state.Pieces.Count(p => p.Side != actor && p.Status == PieceStatus.Alive);
                ranked.Add((action, next, Evaluate(next, actor), threat));
            }
            var ordered = ranked.OrderByDescending(option => option.Score).ToArray();
            if (ordered.Length == 0) break;
            if (ply == 0) threats = ordered.Where(option => option.Threat).Take(2).Select(option => Describe(option.Action, state)).ToArray();
            variation.Add(Describe(ordered[0].Action, state));
            state = ordered[0].Next;
        }
        return new { variation, threats };

        bool OutOfTime() => _clock.GetElapsedTime(started).TotalMilliseconds >= 50;

        static object Describe(JsonElement action, GameState board)
        {
            var piece = action.TryGetProperty("pieceId", out var id) ? board.Pieces.FirstOrDefault(p => p.PieceId == id.GetGuid()) : null;
            var skill = action.TryGetProperty("slot", out var slot)
                ? board.SkillStates[board.SideToMove].FirstOrDefault(s => s.SlotNo == slot.GetInt32()) : null;
            return new { side = board.SideToMove, action, from = piece?.Position, pieceClass = piece?.Class,
                pieceName = piece?.HeroName, skillName = skill?.Name ?? (action.GetProperty("type").GetString() == "hero_active" ? piece?.TraitName : null) };
        }
    }

    public static bool Supports(GameState state) => state.PublicRiverStakes || (state.ThdTuongCocStakes.Count == 0 &&
        !state.Obstacles.Any(o => o.Kind == SkillKeys.ObstacleKindThDTuongCoc) &&
        !state.Pieces.Any(p => p.MovementImplementationKey is not null || p.TraitImplementationKey is not null));

    public BotDecision Choose(Guid matchId, GameState original, BotConfiguration config, RulesetSnapshot rules,
        IReadOnlyDictionary<Side, FrozenLineup> lineups, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Hidden legacy states cannot be searched using information the player cannot see.
        if (!Supports(original))
            throw new InvalidDataException("Tactical bot cannot search legacy hidden or trait states.");
        var state = MatchTurnRules.Prepare(original.Clone());
        state.RiverSnares.RemoveAll(trap => trap.Owner != state.SideToMove);
        var limits = config.Limits;
        var scoreExchanges = config.StrategyVersion == BotConfiguration.ExchangeStrategy;
        var guardRecaptures = scoreExchanges || config.StrategyVersion == BotConfiguration.RecaptureStrategy;
        var started = _clock.GetTimestamp();
        var nodes = 0;
        bool Exhausted() => nodes >= limits.Nodes || _clock.GetElapsedTime(started).TotalMilliseconds >= limits.Milliseconds;
        var moves = OrderedMoves(state);
        JsonElement? fallback = moves.Count > 0 ? Move(moves[0]) : MatchCommandService.FindLegalSkillAction(state, _rules, dispatcher);
        if (fallback is null || state.Result is not null) return new(null, nodes, 0, false);
        var actor = state.SideToMove;
        if (limits.Depth == 0)
        {
            var actions = moves.Where(move => !move.InterceptedByStake).Select(Move).ToList();
            foreach (var action in ActionSkillCandidates(state).Take(16))
            {
                ct.ThrowIfCancellationRequested();
                if (Exhausted()) break;
                nodes++;
                if (Simulate(state, action, rules, lineups) is { } next &&
                    next.Result != (actor == Side.Red ? "black_win" : "red_win")) actions.Add(action);
            }
            if (actions.Count == 0) actions.Add(fallback.Value);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{matchId:N}/{state.Version}/{config.StrategyVersion}"));
            return new(actions[(int)(BinaryPrimitives.ReadUInt32LittleEndian(hash) % (uint)actions.Count)], nodes, 0, Exhausted());
        }

        // Reserve quiet options for distinct pieces, starting with pieces the opponent can take.
        var quietMoves = moves.Where(m => m.CapturedPieceId is null && !m.InterceptedByStake);
        if (scoreExchanges && moves.Count > 0 && !Exhausted())
        {
            var opponent = actor == Side.Red ? Side.Black : Side.Red;
            var threatened = _rules.GenerateLegalActions(state, opponent)
                .Where(m => m.CapturedPieceId is not null).Select(m => m.CapturedPieceId!.Value).ToHashSet();
            quietMoves = quietMoves.OrderByDescending(m => threatened.Contains(m.PieceId));
        }
        var quiet = guardRecaptures ? quietMoves.DistinctBy(m => m.PieceId).Take(2).ToArray() : [];
        if (quiet.Length > 0)
            moves = quiet.Concat(moves.Except(quiet)).ToArray();

        var roots = new List<(JsonElement Action, GameState State, int Score, int CaptureLoss, IReadOnlyList<LegalMove> Replies)>();
        var rootAttempts = 0;
        foreach (var action in Candidates(state, moves, limits.Width))
        {
            ct.ThrowIfCancellationRequested();
            // Allow a bounded fallback check even if move generation used up the soft budget.
            var needsFallback = guardRecaptures && (rootAttempts == 0 && moves.Count > 0 ||
                rootAttempts == 1 && quiet.Length == 2 && roots.Count == 1 && roots[0].CaptureLoss > 0);
            if (Exhausted() && !needsFallback) break;
            rootAttempts++;
            nodes++;
            var next = Simulate(state, action, rules, lineups, out var replies);
            if (next is not null)
            {
                var score = Evaluate(next, actor);
                var loss = scoreExchanges ? ExchangeLoss(next, replies, score) :
                    guardRecaptures ? ImmediateCaptureLoss(next, replies) : 0;
                roots.Add((action, next, score, loss, replies));
            }
        }
        if (roots.Count == 0) return new(fallback, nodes, 0, Exhausted());
        var onePly = roots.OrderByDescending(r => r.Score).First();
        var scores = roots.Select(r => r.Score - r.CaptureLoss).ToArray();
        JsonElement BestSoFar() => guardRecaptures ? roots[Array.IndexOf(scores, scores.Max())].Action : onePly.Action;
        if (limits.Depth == 1 || Exhausted()) return new(BestSoFar(), nodes, 1, Exhausted());

        var best = fallback.Value;
        var bestScore = int.MinValue;
        foreach (var index in Enumerable.Range(0, roots.Count).OrderByDescending(i => scores[i]))
        {
            var root = roots[index];
            var worst = root.Score;
            if (root.State.Result is null)
            {
                worst = scoreExchanges ? scores[index] : int.MaxValue;
                var replies = guardRecaptures ? OrderMoves(root.State, root.Replies) : OrderedMoves(root.State);
                // Captures already contribute to the exchange score, including unexamined captures at face value.
                if (scoreExchanges)
                    replies = replies.Take(limits.Width).Where(m => m.CapturedPieceId is null).ToArray();
                foreach (var response in Candidates(root.State, replies, limits.Width))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Exhausted()) return new(BestSoFar(), nodes, 1, true);
                    nodes++;
                    var next = Simulate(root.State, response, rules, lineups);
                    if (next is null) continue;
                    worst = Math.Min(worst, Evaluate(next, actor));
                    if (guardRecaptures) scores[index] = Math.Min(scores[index], worst);
                    if (worst <= bestScore) break; // minimizing opponent cannot improve this root
                }
                // An empty selective list is not proof of mate.
                if (worst == int.MaxValue) worst = root.Score;
            }
            if (worst > bestScore) { bestScore = worst; best = root.Action; }
        }
        return new(best, nodes, 2, false);

        int ExchangeLoss(GameState position, IReadOnlyList<LegalMove> replies, int score)
        {
            var captures = OrderMoves(position, replies.Where(m => m.CapturedPieceId is not null).ToArray());
            // A clean defense wins over a small positional gain that relies on a countercapture.
            var loss = captures.Count == 0 ? 0 : 5;
            // ponytail: probe two captures per candidate, then use face value for the rest; widen only after measuring load.
            for (var i = 0; i < captures.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var capture = captures[i];
                if (i == 2 || Exhausted())
                    return Math.Max(loss, position.Pieces.Single(p => p.PieceId == capture.CapturedPieceId).SetupPoints * 100);

                nodes++;
                var after = Simulate(position, Move(capture), rules, lineups, out var counters);
                if (after is null) continue;
                var replyScore = Evaluate(after, actor);
                if (after.Result is null && counters.Any(m => m.CapturedPieceId == capture.PieceId))
                    replyScore += after.Pieces.Single(p => p.PieceId == capture.PieceId).SetupPoints * 100;
                loss = Math.Max(loss, score - replyScore);
            }
            return loss;
        }
    }

    public GameState? Simulate(GameState input, JsonElement action, RulesetSnapshot rules, IReadOnlyDictionary<Side, FrozenLineup> lineups) =>
        Simulate(input, action, rules, lineups, out _);

    private GameState? Simulate(GameState input, JsonElement action, RulesetSnapshot rules,
        IReadOnlyDictionary<Side, FrozenLineup> lineups, out IReadOnlyList<LegalMove> replies)
    {
        replies = [];
        var state = MatchTurnRules.Prepare(input.Clone());
        GameState next;
        var type = action.GetProperty("type").GetString();
        if (type == "move")
        {
            var to = action.GetProperty("to");
            var result = _rules.ApplyMove(state, new(action.GetProperty("pieceId").GetGuid(), new(to.GetProperty("x").GetInt32(), to.GetProperty("y").GetInt32())));
            if (!result.Accepted) return null;
            next = result.State;
        }
        else if (type == "hero_active")
        {
            var result = MatchTurnRules.ApplyHeroSkill(state, action, _rules);
            if (!result.Accepted) return null;
            next = result.State;
        }
        else
        {
            var slot = action.GetProperty("slot").GetInt32();
            var skill = state.SkillStates[state.SideToMove].FirstOrDefault(s => s.SlotNo == slot);
            if (skill is null || !lineups.TryGetValue(state.SideToMove, out var lineup)) return null;
            var frozen = lineup.Skills.FirstOrDefault(s => s.SlotNo == slot && s.SkillId == skill.SkillId);
            if (frozen is null) return null;
            var result = MatchTurnRules.ApplySkill(state, state.SideToMove,
                FrozenSkillSnapshotFactory.FromSkillState(skill, frozen.CooldownTurns), action, dispatcher);
            if (!result.Accepted) return null;
            next = result.State;
        }
        var resultAfterTurn = MatchTurnRules.AfterAction(next, rules, dispatcher);
        replies = resultAfterTurn.Replies;
        return resultAfterTurn.State;
    }

    // One opponent action can capture one piece. This is a short-horizon material estimate for cutoff, not a full exchange search.
    private static int ImmediateCaptureLoss(GameState state, IReadOnlyList<LegalMove> replies) => replies
        .Where(move => move.CapturedPieceId is not null)
        .Select(move => state.Pieces.Single(p => p.PieceId == move.CapturedPieceId).SetupPoints * 100)
        .DefaultIfEmpty(0).Max();

    private IReadOnlyList<LegalMove> OrderedMoves(GameState state) => OrderMoves(state, _rules.GenerateLegalActions(state));

    private static IReadOnlyList<LegalMove> OrderMoves(GameState state, IReadOnlyList<LegalMove> moves)
    {
        return moves.OrderBy(m => m.InterceptedByStake)
            .ThenByDescending(m => m.CapturedPieceId is null ? -1 : state.Pieces.Single(p => p.PieceId == m.CapturedPieceId).SetupPoints)
            .ThenBy(m => m.From.X).ThenBy(m => m.From.Y).ThenBy(m => m.To.X).ThenBy(m => m.To.Y).ToArray();
    }

    private IEnumerable<JsonElement> Candidates(GameState state, IReadOnlyList<LegalMove> moves, int width)
    {
        // shortcut: bounded beam, not exhaustive minimax; increase width only after measured server capacity.
        foreach (var move in moves.Take(4)) yield return Move(move);
        foreach (var action in ActionSkillCandidates(state).Take(8)) yield return action;
        foreach (var move in moves.Skip(4).Take(Math.Max(0, width - 4))) yield return Move(move);
    }

    private IEnumerable<JsonElement> ActionSkillCandidates(GameState state)
    {
        using var heroes = MatchTurnRules.HeroCandidates(state, _rules).GetEnumerator();
        using var team = SkillCandidates(state).GetEnumerator();
        while (true)
        {
            var hasHero = heroes.MoveNext();
            if (hasHero) yield return heroes.Current;
            var hasTeam = team.MoveNext();
            if (hasTeam) yield return team.Current;
            if (!hasHero && !hasTeam) yield break;
        }
    }

    public static IEnumerable<JsonElement> SkillCandidates(GameState state)
    {
        // Round-robin slots: a structure's 90 targets must not hide later ready skills.
        var streams = state.SkillStates[state.SideToMove]
            .Where(s => s.CooldownRemaining == 0 && s.UsesRemaining != 0).OrderBy(s => s.SlotNo)
            .Select(skill => ForSkill(skill).GetEnumerator()).ToArray();
        try
        {
            while (true)
            {
                var any = false;
                foreach (var stream in streams)
                    if (stream.MoveNext()) { any = true; yield return stream.Current; }
                if (!any) yield break;
            }
        }
        finally { foreach (var stream in streams) stream.Dispose(); }

        IEnumerable<JsonElement> ForSkill(SkillState skill)
        {
            IEnumerable<object> targets = skill.ImplementationKey switch
            {
                SkillKeys.Khien => state.Pieces.Where(p => p.Side == state.SideToMove && p.Status == PieceStatus.Alive)
                    .OrderByDescending(p => p.SetupPoints).ThenBy(p => p.Position?.X).ThenBy(p => p.Position?.Y).Select(p => (object)new { pieceId = p.PieceId }),
                SkillKeys.Thanh or SkillKeys.Rao => Enumerable.Range(0, 9).SelectMany(x => Enumerable.Range(0, 10).Select(y => new BoardPoint(x, y)))
                    .Where(pos => !state.Pieces.Any(p => p.Status == PieceStatus.Alive && p.Position == pos) &&
                        !state.Obstacles.Any(o => o.Position == pos && (o.Kind != SkillKeys.ObstacleKindThDTuongCoc || !state.PublicRiverStakes)))
                    .OrderBy(pos => Math.Abs(pos.Y - (state.SideToMove == Side.Red ? 2 : 7))).ThenBy(pos => Math.Abs(pos.X - 4))
                    .Select(pos => (object)new { position = new { x = pos.X, y = pos.Y } }),
                SkillKeys.VanCocTranGiang or SkillKeys.BinhLamThuyHien => new object[] { new { paths = new[] { 4, 5, 6 } } },
                FactionSkillHandler.Trap => FactionSkillHandler.Targets(state),
                FactionSkillHandler.Rally => new object[] { new { } },
                SkillKeys.PhanKyDoatThe or SkillKeys.PhaTranDoatPhong => state.EffectInstances.OrderBy(e => e.CreationOrder).Select(e => (object)new { effectId = e.EffectId }),
                _ => []
            };
            foreach (var target in targets) yield return JsonSerializer.SerializeToElement(new { type = "team_skill", slot = skill.SlotNo, target }, GameJson.Options);
        }
    }

    private static JsonElement Move(LegalMove move) => JsonSerializer.SerializeToElement(new
        { type = "move", pieceId = move.PieceId, to = new { x = move.To.X, y = move.To.Y } }, GameJson.Options);

    private int Evaluate(GameState state, Side actor)
    {
        if (state.Result is not null) return state.Result == "draw" ? 0 : state.Result == (actor == Side.Red ? "red_win" : "black_win") ? 1_000_000 : -1_000_000;
        var score = 0;
        foreach (var p in state.Pieces.Where(p => p.Status == PieceStatus.Alive && p.Position is not null))
        {
            var value = p.SetupPoints * 100;
            // Small positional terms never outweigh a normal piece's material value.
            if (p.Class == PieceClass.Soldier) value += (p.Side == Side.Red ? p.Position!.Value.Y : 9 - p.Position!.Value.Y) * 2;
            score += p.Side == actor ? value : -value;
        }
        if (_rules.IsInCheck(state, actor)) score -= 25;
        if (_rules.IsInCheck(state, actor == Side.Red ? Side.Black : Side.Red)) score += 25;
        return score;
    }
}
