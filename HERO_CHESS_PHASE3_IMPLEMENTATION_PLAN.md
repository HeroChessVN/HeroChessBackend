# Hero Chess — Phase 3 Implementation Plan (Corrected)

> Phase 3 deliverable: **Analysis + Implementation Plan only.** No source code is modified
> in this phase.

---

## 1. Phase 3 Objective

Wire the Phase 2 Effect/Command Skill infrastructure into the live game pipeline so that
effects actually function during gameplay — cooldowns decrement, durations expire, 
Phản Kỳ pending steals finalize, Vạn Cọc stakes disappear, and the WebSocket 
broadcasts lifecycle events.

**Phase 3 does NOT add new gameplay mechanics, new Command Skills, or hero skills.**

---

## 2. Confirmed Gameplay Requirements

| # | Requirement | Source | Classification |
|---|------------|--------|----------------|
| 2.1 | TurnLifecycle processes exactly once per authoritative Player Turn | Phase 2 §8.2 | [CONFIRMED] |
| 2.2 | Cooldown decrements at turn-start | Phase 2 §13.1 | [CONFIRMED] |
| 2.3 | Effect duration decrements at Creator's turn-start (U-DUR = A) | Phase 2 §6.5 | [CONFIRMED] |
| 2.4 | Effects expire when RemainingDuration reaches 0 | Phase 2 §9.1 | [CONFIRMED] |
| 2.5 | Physical stakes expire when RemainingLifetime reaches 0 | Phase 2 §13.3 | [CONFIRMED] |
| 2.6 | Phản Kỳ pending steals finalize at Creator's turn-start | Phase 2.4 accepted | [CONFIRMED] |
| 2.7 | Creator cancellation is internal Phản Kỳ resolution — handled by TurnLifecycle at Creator's turn-start | Phase 2.4 accepted | [CONFIRMED] |
| 2.8 | Lifecycle events are broadcast to WebSocket clients | Phase 2 §14.1 | [CONFIRMED] |
| 2.9 | Bot can activate Command Skills | Phase 2 §17.2 | [UNRESOLVED — strategy not confirmed] |
| 2.10 | Effect blocking/restriction affects movement | Phase 2 §12.1, §12.4 | [CONFIRMED gameplay; PROPOSED implementation] |

---

## 3. Creator Cancellation — ALREADY RESOLVED

**This is NOT an unresolved decision.**

From accepted Phase 2.4:

> "Creator cancellation is an INTERNAL RESOLUTION of Phản Kỳ Đoạt Thế."
>
> "It is NOT: a fifth Command Skill, a separate 'phan_ky_cancel' action, RemoveStolenEffectHandler, a new cooldown, a new Effect."

**Correct classification:**

| Aspect | Classification |
|--------|---------------|
| Creator cancellation mechanism | [CONFIRMED] — `TurnLifecycle.Apply(..., resolveCreatorCancellation: true)` |
| Trigger timing | [CONFIRMED] — At the start of Creator's turn |
| What it does | [CONFIRMED] — Marks stolen effect as Ended |
| What it does NOT do | [CONFIRMED] — No new Effect, no cooldown, no new steal |
| Implementation location | [CONFIRMED] — Step 5 in `TurnLifecycle.Apply` |

**Phase 3.3 task:** Wire the cancellation trigger into the command pipeline. The logic already exists in `TurnLifecycle.cs`. The decision of HOW the player invokes cancellation is still needed (see §6.4).

---

## 4. Effect Blocking — Correct Classification

### Confirmed Gameplay Intent

| Skill | Confirmed Behavior | Source | Classification |
|--------|-------------------|--------|----------------|
| Vạn Cọc Trấn Giang | Blocks opponent river-crossing through affected columns | Phase 2 §12.1 | [CONFIRMED] |
| Vạn Cọc | Physical stakes remain separate from blocking Effect | Phase 2 §12.1 | [CONFIRMED] |
| Vạn Cọc | Does not modify general Xiangqi movement rules | Phase 2 §12.1 | [CONFIRMED] |
| Binh Lâm Thủy Hiểm | Restricts selected river-crossing routes for its duration | Phase 2 §12.4 | [CONFIRMED] |
| Binh Lâm | Does not modify general Xiangqi movement rules | Phase 2 §12.4 | [CONFIRMED] |

### Proposed Implementation

The **exact architecture** for integrating effects into `XiangqiRulesEngine` has NOT been approved.

| Implementation Aspect | Classification |
|----------------------|----------------|
| Effect blocking location | [PROPOSED] — `GenerateLegalActions`, `ApplyMove`, or new handler |
| River crossing detection | [PROPOSED] — y-coordinate check or position-based |
| Effect kind filtering | [PROPOSED] — Check `EffectInstance.Code` and `EffectInstance.Payload["kind"]` |
| Own-side exception | [PROPOSED] — Check `PositionControllers[pos] != opponent` |

---

## 5. Exactly-Once Turn Semantics — Correct Architecture

### Turn Transition Paths in Current Codebase

**[CODEBASE FACT]** There are **TWO authoritative turn transition paths**:

| Path | Location | Current Behavior | Who calls it |
|------|----------|-----------------|--------------|
| Move action | `XiangqiRulesEngine.ApplyMove()` | Increments `TurnIndex`, flips `SideToMove` | `MatchCommandService.ExecuteActorAsync` (line 70) |
| Timeout action | `MatchCommandService.TimeoutAsync()` | Increments `TurnIndex`, flips `SideToMove`, handles AFK | `MatchTimeoutHostedService` |

### Current Turn Transition Logic

**In `XiangqiRulesEngine.ApplyMove()` (line 46-48):**
```csharp
next.Version++;
next.TurnIndex++;
next.SideToMove = Opposite(state.SideToMove);
```

**In `MatchCommandService.TimeoutAsync()` (line 154-155):**
```csharp
state.Version++; state.TurnIndex++;
state.SideToMove = timedOut == Side.Red ? Side.Black : Side.Red;
```

### Key Insight: TurnIndex Increment = Turn Boundary

**[CODEBASE FACT]** `TurnIndex` is incremented at the END of each turn (after the action is applied, before the next player's turn begins). This is the authoritative marker for "this turn is complete, next turn is starting."

### Lifecycle Timing

According to Phase 2 §8.2:
> "Turn-start expiration is processed BEFORE the player can act."
>
> "If RemainingDuration reaches 0 → Effect ends immediately."

This means **lifecycle runs at the START of a player's turn**, before they can act.

### Proposed Integration Architecture

The Phase 2 §8.2 two-phase pattern:

```
BEFORE ACTION (at turn START):
  if not IsTurnProcessed(state, sideToMove, state.TurnIndex):
      lifecycle = TurnLifecycle.Apply(state, sideToMove, resolveCreatorCancellation)
      MarkTurnProcessed(state, sideToMove, state.TurnIndex)
      state = lifecycle.State
  // Then process action on post-lifecycle state

ACTION PROCESSING:
  ApplyMove / ExecuteSkill / Timeout
  // SideToMove unchanged during action processing
  // TurnIndex incremented AFTER action (at turn END)

TURN END:
  TurnIndex++
  SideToMove = Opposite(SideToMove)
  // Next command will trigger lifecycle for the NEW sideToMove
```

### Lifecycle Trigger Points

| Command Type | Lifecycle Trigger | Implementation |
|--------------|-----------------|---------------|
| Move | Before `XiangqiRulesEngine.ApplyMove` | Call `TurnLifecycle.Apply(state, actorSide)` in `ExecuteActorAsync` |
| Command Skill | Before `CommandSkillDispatcher.Dispatch` | Same call in `ExecuteActorAsync` |
| Timeout | Before timeout turn transition | Call `TurnLifecycle.Apply(state, timedOutSide)` in `TimeoutAsync` BEFORE incrementing TurnIndex |

### Critical Constraint

**Lifecycle must run for the player whose turn is STARTING, not ending.**

- Move: lifecycle runs for `actorSide` (the player who just acted, but their NEXT turn)
- Timeout: lifecycle runs for `timedOutSide` (the player who just timed out, but their NEXT turn)

### ProcessedTurns Tracking

The `ProcessedTurns` field uses `(side, TurnIndex)` as the key. This correctly identifies each unique player turn.

---

## 6. Bot Command Skills — Correct Status

### Current Bot Architecture

**[CODEBASE FACT]** `BotTurnScheduler.ExecuteAsync()`:
- Reads current state
- Generates all legal moves via `XiangqiRulesEngine.GenerateLegalActions(state)`
- Selects move with highest captured piece SetupPoints
- Executes via `ExecuteBotAsync()`

### Missing for Bot Skill Activation

| Aspect | Status | Evidence |
|--------|--------|----------|
| Bot can call `ExecuteBotAsync` with `type: "team_skill"` | ✅ Already wired | `ExecuteBotAsync` uses same pipeline as `ExecuteActorAsync` |
| Bot knows which skills are available | ✅ Already wired | Skills in `state.SkillStates[botSide]` |
| Bot knows skill cooldowns | ✅ Already wired | `SkillState.CooldownRemaining` |
| Bot **chooses** when to use a skill | ❌ [UNRESOLVED] | No evaluation strategy confirmed |
| Bot evaluates skill vs. move | ❌ [UNRESOLVED] | No scoring function confirmed |

### Decision Required Before Phase 3.6

**Bot skill activation requires a confirmed evaluation strategy.** The Phase 2 plan says "Bot uses same pipeline and undo appends without deleting history" but does not specify how the bot decides to use a skill.

Possible approaches:
1. **Random**: Bot activates skills randomly when cooldown is ready (simplest)
2. **Rule-based**: Bot activates specific skills based on game state conditions
3. **Heuristic**: Bot scores each option and picks the highest

**Phase 3.6 is BLOCKED until a bot evaluation strategy is confirmed.**

---

## 7. Current Codebase Facts

### 7.1 What EXISTS (implemented in Phase 2)

| File | Status | Evidence |
|------|--------|----------|
| `TurnLifecycle.cs` | ✅ Implemented | 4 steps + creator cancellation |
| `EffectInstance.cs` | ✅ Implemented | Full domain model |
| `EffectFactory.cs`, `EffectQueries.cs`, `EffectStateValue.cs` | ✅ Implemented | Effect system |
| `CommandSkillDispatcher.cs` | ✅ Implemented | Wired in `ExecuteActorAsync` |
| `CommandSkillRegistry.cs` | ✅ Implemented | 4 skill handlers registered |
| All 4 Command Skill handlers | ✅ Implemented | Vạn Cọc, Phản Kỳ, Phá Trận, Binh Lâm |
| `RiverGeometry.cs`, `StakeMetadata.cs` | ✅ Implemented | Geometry and stake tracking |
| `GameState.Clone()` | ✅ Updated | Clones all Phase 2 fields |
| Unit tests | ✅ 179 passing | Full test coverage |
| `ProcessedTurns` field | ✅ Implemented | Not used yet |

### 7.2 What is MISSING (Phase 3 scope)

| # | Missing | Classification | Impact |
|---|---------|---------------|--------|
| M1 | TurnLifecycle wired into `ExecuteActorAsync` | [CODEBASE FACT] | Lifecycle never runs |
| M2 | TurnLifecycle wired into `TimeoutAsync` | [CODEBASE FACT] | Timeout skips lifecycle |
| M3 | Lifecycle events broadcast | [CODEBASE FACT] | Clients don't see events |
| M4 | Effect blocking in movement engine | [PROPOSED] | Vạn Cọc/Binh Lâm don't affect movement |
| M5 | Bot skill evaluation | [UNRESOLVED] | Bot cannot choose skills |
| M6 | `hero_active` stub | Not Phase 3 | Stub remains |

---

## 8. Phase 3 Scope — Corrected

### IN SCOPE

| # | Item | Classification |
|---|------|----------------|
| 3.1 | Wire TurnLifecycle into `ExecuteActorAsync` | [CONFIRMED] |
| 3.2 | Wire TurnLifecycle into `TimeoutAsync` | [CONFIRMED] |
| 3.3 | Wire creator cancellation trigger | [CONFIRMED] — already in TurnLifecycle |
| 3.4 | Broadcast lifecycle events | [CONFIRMED] |
| 3.5 | Effect-based movement restriction | [CONFIRMED gameplay; PROPOSED implementation] |
| 3.6 | Bot Command Skills | [UNRESOLVED strategy — BLOCKED] |

### OUT OF SCOPE

| # | Item | Reason |
|---|------|--------|
| O1 | Hero active skills (`hero_active`) | Separate feature |
| O2 | New Command Skills | 4 confirmed only |
| O3 | Database schema changes | All in jsonb |
| O4 | API contract changes | `JsonElement`-based |

---

## 9. Proposed Implementation Architecture

### 9.1 Lifecycle Integration Pattern

```csharp
// In ExecuteActorAsync — BEFORE action dispatch:
var lifecycle = TurnLifecycle.Apply(state, actorSide, resolveCreatorCancellation: false);
if (lifecycle.ExpiredEffects.Count > 0 || lifecycle.RemovedStakes.Count > 0 || 
    lifecycle.FinalizedSteals.Count > 0 || lifecycle.DecrementedCooldowns.Count > 0)
{
    // Lifecycle made changes — emit events
}
state = lifecycle.State;
TurnLifecycle.MarkTurnProcessed(state, actorSide, state.TurnIndex);

// Then proceed with action dispatch on post-lifecycle state
```

### 9.2 Lifecycle Event Broadcasting

```csharp
// Merge lifecycle events with action events:
var allEvents = new List<object>();
allEvents.Add(new { type = "turn.started", side = actorSide.ToString().ToLowerInvariant() });
foreach (var e in lifecycle.ExpiredEffects) 
    allEvents.Add(new { type = "effect.expired", effectId = e.EffectId });
foreach (var s in lifecycle.RemovedStakes) 
    allEvents.Add(new { type = "stake.removed", obstacleId = s.ObstacleId });
foreach (var c in lifecycle.DecrementedCooldowns) 
    allEvents.Add(new { type = "skill.cooldown_decremented", ... });
// ... etc for FinalizedSteals, RemovedEffects
allEvents.AddRange(actionEvents);
```

### 9.3 Effect Blocking (PROPOSED — needs design approval)

**Option A: In `GenerateLegalActions`**
```csharp
// Filter out moves that cross blocked river columns
foreach (var destination in GeneratePseudoDestinations(state, piece, attacksOnly: false))
{
    // ... existing checks ...
    
    // NEW: Check effect blocking
    if (IsBlockedByEffect(state, from, destination, actor))
        continue;
}
```

**Option B: In `ApplyMove` validation**
```csharp
// Add effect blocking check after move legality
if (IsBlockedByEffect(next, movedPiece, destination, actor))
    return ApplyMoveResult.Failure(state, "MOVE_BLOCKED_BY_EFFECT", ...);
```

---

## 10. Files Expected to Change

| File | Change | Sub-phase |
|------|--------|-----------|
| `HeroChess.Api/Services/MatchCommandService.cs` | Wire TurnLifecycle into ExecuteActorAsync and TimeoutAsync | 3.1, 3.2, 3.3, 3.4 |
| `HeroChess.Rules/XiangqiRulesEngine.cs` | Add effect blocking check | 3.5 |
| `HeroChess.Api/Services/BotTurnScheduler.cs` | Add skill evaluation (BLOCKED) | 3.6 |
| `tests/HeroChess.IntegrationTests/MatchFlowTests.cs` | Add lifecycle integration tests | 3.7 |

---

## 11. Database/API Impact

| Aspect | Impact | Classification |
|--------|--------|----------------|
| Database schema | **NONE** | [CODEBASE FACT] |
| API contracts | **NONE** | [CODEBASE FACT] |
| New endpoints | **NONE** | |

---

## 12. Ordered Implementation Sub-phases

### Phase 3.1 — ExecuteActorAsync Lifecycle Integration

**Objective:** Wire `TurnLifecycle.Apply` into player command processing so lifecycle runs at turn-start.

**Files:**
- `HeroChess.Api/Services/MatchCommandService.cs`

**Implementation:**
1. Before action dispatch (after state read, before switch):
   ```csharp
   if (!TurnLifecycle.IsTurnProcessed(state, actorSide, state.TurnIndex))
   {
       var lifecycle = TurnLifecycle.Apply(state, actorSide);
       state = lifecycle.State;
       TurnLifecycle.MarkTurnProcessed(state, actorSide, state.TurnIndex);
       // Accumulate lifecycle events
   }
   ```
2. Merge lifecycle events with action events for broadcast

**Tests:**
- `Lifecycle_runs_for_move_command`
- `Lifecycle_runs_for_team_skill_command`
- `Lifecycle_skipped_if_already_processed`
- `Lifecycle_events_included_in_broadcast`

**Acceptance criteria:**
- Cooldown decrements visible in next snapshot after player acts
- Effect duration decrements at Creator's turn
- Lifecycle events appear in WebSocket broadcast
- Second command in same turn does NOT re-run lifecycle

**Dependencies:** None

---

### Phase 3.2 — TimeoutAsync Lifecycle Integration

**Objective:** Wire `TurnLifecycle.Apply` into timeout processing so lifecycle runs for timed-out player's next turn.

**Files:**
- `HeroChess.Api/Services/MatchCommandService.cs` (TimeoutAsync method)

**Implementation:**
1. At start of TimeoutAsync (before incrementing TurnIndex):
   ```csharp
   if (!TurnLifecycle.IsTurnProcessed(state, timedOutSide, state.TurnIndex))
   {
       var lifecycle = TurnLifecycle.Apply(state, timedOutSide);
       state = lifecycle.State;
       TurnLifecycle.MarkTurnProcessed(state, timedOutSide, state.TurnIndex);
       // Accumulate lifecycle events
   }
   // Then proceed with timeout logic...
   ```

**Tests:**
- `Lifecycle_runs_for_timeout`
- `Stakes_expire_after_timeout`

**Acceptance criteria:**
- Lifecycle runs for timed-out player
- Stake lifetime decrements correctly
- Lifecycle events in timeout broadcast

**Dependencies:** Phase 3.1

---

### Phase 3.3 — Creator Cancellation Trigger

**Objective:** Wire the creator cancellation mechanism so players can cancel stolen effects.

**Status:** [CONFIRMED] — Logic already in `TurnLifecycle.Apply(..., resolveCreatorCancellation: true)`

**Decision Required:** How does the player invoke cancellation?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| A | Separate button/action that triggers `resolveCreatorCancellation: true` in same lifecycle call | Clean, explicit | Requires client UI |
| B | Automatic — if Creator has stolen effect at turn start, cancel automatically | Simple | Player may not want to cancel |
| C | Part of move validation — if Creator tries to move piece in stolen effect area, prompt | Natural flow | Complex UX |

**Recommendation:** Option A for Phase 3.3 — explicit cancellation action that is processed as part of the lifecycle call.

**Files:**
- `HeroChess.Api/Services/MatchCommandService.cs`

**Tests:**
- `Creator_cancellation_ends_stolen_effect`
- `Cancellation_requires_creator_turn`

**Acceptance criteria:**
- Creator can cancel stolen effect during their turn
- Cancellation ends effect (State = Ended)
- No new EffectId created
- Creator unchanged

**Dependencies:** Phase 3.1

---

### Phase 3.4 — Lifecycle Event Broadcasting

**Objective:** Ensure all lifecycle events appear in WebSocket broadcast.

**Files:**
- `HeroChess.Api/Services/MatchCommandService.cs`
- `HeroChess.Api/Services/MatchConnectionHub.cs`

**Implementation:**
1. Convert typed `LifecycleResult` events to flat object arrays
2. Include in `match.command_accepted` broadcast payload

**Tests:**
- `Expired_effect_appears_in_broadcast`
- `Removed_stake_appears_in_broadcast`
- `Finalized_steal_appears_in_broadcast`

**Acceptance criteria:**
- WebSocket clients receive all lifecycle event types
- Clients can update board state accordingly

**Dependencies:** Phase 3.1

---

### Phase 3.5 — Effect-Based Movement Restriction

**Objective:** Enforce that Vạn Cọc and Binh Lâm effects actually block/restrict movement.

**Status:** [CONFIRMED gameplay; PROPOSED implementation]

**Design Decision Required:** Where in the engine should effect blocking be checked?

**Files:**
- `HeroChess.Rules/XiangqiRulesEngine.cs`
- `HeroChess.Rules/MovementHandlers.cs` (if blocking is handler-specific)

**Tests:**
- `VanCoc_blocks_opponent_river_crossing`
- `VanCoc_does_not_block_own_side`
- `BinhLam_restricts_opponent_river_crossing`
- `Effect_does_not_affect_pieces_not_crossing_river`

**Acceptance criteria:**
- Vạn Cọc blocks opponent pieces from crossing affected river columns
- Binh Lâm restricts (not blocks) opponent pieces
- Own-side pieces unaffected
- Pieces already on river unaffected

**Dependencies:** Phase 3.1

---

### Phase 3.6 — Bot Command Skill Activation

**Objective:** Enable bot to evaluate and activate Command Skills.

**Status:** [BLOCKED] — No confirmed evaluation strategy

**Decision Required:** How does the bot decide to use a skill?

**Possible Strategies:**

| Strategy | Description | Complexity |
|----------|-------------|------------|
| Random | Activate randomly when cooldown ready | Minimal |
| Rule-based | Activate based on fixed conditions | Low |
| Heuristic scoring | Score each option (skill vs. move) | Medium |
| ML-based | Learn from games | High |

**Recommendation for Phase 3:** Start with **random** or **rule-based** strategy. This is sufficient to demonstrate the capability without over-engineering.

**Files:**
- `HeroChess.Api/Services/BotTurnScheduler.cs`

**Tests:**
- `Bot_can_activate_command_skill`
- `Bot_respects_cooldown`

**Acceptance criteria:**
- Bot successfully activates a Command Skill
- Bot does not activate when on cooldown

**Dependencies:** Phase 3.1, **Bot strategy decision**

---

### Phase 3.7 — Integration Test Coverage

**Objective:** Add integration tests for the full lifecycle pipeline.

**Files:**
- `tests/HeroChess.IntegrationTests/MatchFlowTests.cs`

**Tests:**
- `Command_skill_lifecycle_combined_in_single_command`
- `Replay_preserves_effect_state`
- `Two_skills_same_turn_both_processed`

**Acceptance criteria:**
- All new integration tests pass
- All existing tests continue to pass

**Dependencies:** Phase 3.1, 3.2, 3.3, 3.4, 3.5, 3.6

---

## 13. Test Plan

### Unit Tests (HeroChess.Rules.Tests)

| Test | Sub-phase | Classification |
|------|-----------|----------------|
| `Lifecycle_runs_for_move_command` | 3.1 | [PROPOSED] |
| `Lifecycle_runs_for_team_skill_command` | 3.1 | [PROPOSED] |
| `Lifecycle_skipped_if_already_processed` | 3.1 | [PROPOSED] |
| `Lifecycle_runs_for_timeout` | 3.2 | [PROPOSED] |
| `Creator_cancellation_ends_stolen_effect` | 3.3 | [PROPOSED] |
| `VanCoc_blocks_opponent_river_crossing` | 3.5 | [PROPOSED] |
| `VanCoc_does_not_block_own_side` | 3.5 | [PROPOSED] |
| `BinhLam_restricts_opponent_river_crossing` | 3.5 | [PROPOSED] |

### Integration Tests (HeroChess.IntegrationTests)

| Test | Sub-phase | Classification |
|------|-----------|----------------|
| `Lifecycle_runs_exactly_once_for_turn_in_execute_actor` | 3.1 | [PROPOSED] |
| `Lifecycle_events_included_in_broadcast` | 3.4 | [PROPOSED] |
| `Expired_effect_appears_in_broadcast` | 3.4 | [PROPOSED] |
| `Bot_can_activate_command_skill` | 3.6 | [PROPOSED] |

---

## 14. Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Exactly-once lifecycle breaks existing tests | Medium | Run all existing tests first; verify TurnIndex is authoritative key |
| Effect blocking changes legal move count | Medium | Add unit tests before changing engine |
| Bot skill strategy not confirmed | High | Request decision before Phase 3.6 |
| Concurrent commands during same turn | Low | MatchLockRegistry already serializes |

---

## 15. Acceptance Criteria

| Criterion | Verification |
|-----------|--------------|
| TurnLifecycle runs exactly once per authoritative Player Turn | Unit test + integration test |
| Cooldown decrements visible in next snapshot | Integration test |
| Effect duration decrements at Creator's turn | Unit test |
| Effects expire at RemainingDuration = 0 | Unit test |
| Physical stakes expire after 2 placer turns | Unit test |
| Phản Kỳ pending steals finalize at Creator's turn | Unit test |
| Creator cancellation ends stolen effect | Unit test |
| Lifecycle events broadcast via WebSocket | Integration test |
| Vạn Cọc blocks opponent river crossing | Unit test |
| Binh Lâm restricts opponent river crossing | Unit test |
| Bot can activate Command Skills | Integration test (BLOCKED) |
| All 179+ Rules unit tests pass | `dotnet test HeroChess.Rules.Tests` |
| No API/DB changes | Code review |

---

## 16. Summary Classification

| Classification | Items |
|----------------|-------|
| [CONFIRMED] gameplay requirements | 8 (requirements 2.1–2.8) |
| [CODEBASE FACT] | Current implementation status, missing items |
| [PROPOSED] implementation | Effect blocking architecture, bot strategy |
| [UNRESOLVED] decisions | Bot evaluation strategy, cancellation trigger mechanism |

---

## 17. Final Status

| Item | Status |
|------|--------|
| Phase 3 analysis | ✅ Complete |
| Phase 3 objective | ✅ Wire Phase 2 infrastructure into live pipeline |
| Confirmed requirements | ✅ 8 confirmed |
| Creator cancellation | ✅ Already resolved — internal Phản Kỳ resolution |
| Effect blocking | ✅ CONFIRMED gameplay; PROPOSED implementation |
| Exactly-once architecture | ✅ Analyzed — TurnIndex is authoritative key |
| Bot skills | ⚠️ BLOCKED — evaluation strategy not confirmed |
| Ready for Phase 3.1 | ✅ YES |
| Ready for Phase 3.6 | ❌ NO — requires bot strategy decision |

---

## Appendix A — Classification Legend

| Classification | Meaning |
|----------------|---------|
| [CONFIRMED] | Explicitly confirmed gameplay requirement; cannot be changed |
| [CODEBASE FACT] | Verified by reading actual file; reflects current state |
| [PROPOSED] | Technical approach proposed but not approved |
| [UNRESOLVED] | Decision needed before implementation; blocks sub-phase |
