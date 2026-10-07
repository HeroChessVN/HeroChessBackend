# Hero Chess — Command Skill & Effect System Implementation Plan

> Phase 1 deliverable: **Analysis + Implementation Plan only.** No source code is modified
> in this phase. The repository has been inspected end-to-end (Rules project, Api project,
> Contracts project, SQL prototype, and tests). Every statement in this plan is grounded
> in a concrete file path under `c:\Users\ptien\Herochess\Code\HeroChessBackend`.

---

## 1. Executive Summary

This plan defines how the four confirmed Command Skills (Vạn Cọc Trấn Giang, Phản Kỳ
Đoạt Thế, Phá Trận Đoạt Phong, Binh Lâm Thủy Hiểm) will be layered on top of the
existing Hero Chess backend, which today supports Xiangqi movement, transactional
command execution, event-based replay, and a deliberately **stub-only** skill layer.

The plan is intentionally incremental and additive. It does **not** require database
schema changes, API contract changes, or rewrites of working Xiangqi rules. It does
require an upgrade of the runtime `GameState` JSON model (`StateSchemaVersion` 3 → 4)
plus a new "Command Skill dispatch and Effect lifecycle" subsystem.

The codebase already has the four cross-cutting primitives we need:

1. **Server-authoritative transaction pipeline** (`MatchCommandService.ExecuteActorAsync`,
   `HeroChess.Api/Services/MatchCommandService.cs`, lines 27–117). Version check,
   deadline check, transaction commit, append-only `MatchAction` log.
2. **Cloneable game state** (`GameState.Clone`, `PieceState.Clone`,
   `HeroChess.Rules/GameModels.cs`, lines 35–54 and 97–118).
3. **Per-side Skill state** with `CooldownRemaining`
   (`HeroChess.Rules/GameModels.cs` line 64, `SkillState` record).
4. **Event log** that already accepts `kind = 'team_skill'` in the SQL CHECK constraint
   (`Hero_Chess_DB_Prototype_v0_1/01_schema.sql` line 243).

What is missing — and what this plan builds — is:

- A proper Effect domain model (`EffectId`, `Creator`, `CreationOrder`,
  `Disabled`, per-position controller state).
- A Command Skill dispatcher that replaces the existing
  `throw new ApiException(422, "SKILL_NOT_IMPLEMENTED", ...)` cases in
  `MatchCommandService` (lines 85–86) and `XiangqiRulesEngine.ApplyTeamSkill`
  (lines 59–60).
- A turn-start Effect lifecycle step that decrements cooldowns and ends Effects
  before the player can act, processed exactly once per authoritative Player Turn.
- Cooldown lifecycle that decrements per Skill only after successful execution.
- Four handler classes implementing the four confirmed Command Skills.
- Multi-position Effect representation that supports both Vạn Cọc (3 consecutive
  river-crossing paths) and Binh Lâm (3 consecutive river-crossing paths).

---

## 2. Current Codebase Baseline

### 2.1 Solution layout (confirmed)

```
HeroChessBackend.slnx
├── HeroChess.Rules/                Pure C# rules engine; netstandard2.1; no DB / HTTP
│   ├── GameModels.cs               GameState, PieceState, EffectState, SkillState, …
│   ├── XiangqiRulesEngine.cs       Legal-action generation, ApplyMove, ApplyTeamSkill stub
│   └── MovementHandlers.cs          IMovementHandler + HomeDiagonal/WildElephant
├── HeroChess.Contracts/            DTO records shared between server and client
│   ├── ApiContracts.cs              MatchCommandRequest, MatchStateDto, ReplayEntryDto, …
│   └── IsExternalInit.cs            C# 8 records init-only shim
├── HeroChess.Api/                  ASP.NET Core 10 Web API
│   ├── Controllers/                MatchesController, CatalogController, …
│   ├── Services/                  MatchCommandService, MatchSelectionService, …
│   ├── Data/                       AppDbContext.cs, Entities.cs, Bootstrap/
│   └── Infrastructure/             ApiExceptionMiddleware, GameJson
├── Hero_Chess_DB_Prototype_v0_1/  SQL prototype (schema + seed + constraints + mock)
│   ├── 01_schema.sql               25 gameplay tables; CHECK includes 'team_skill'
│   ├── 02_seed_catalog.sql         Catalog seed
│   ├── 03_seed_dev_only.sql        DEV-only fixture; skills use 'dev.not_implemented'
│   ├── 09_constraint_tests.sql     23 SQL invariant tests
│   └── 06_mock_data.json          Mock GameState shape (effects/obstacles empty)
└── tests/
    ├── HeroChess.Rules.Tests/       16 unit tests including SKILL_NOT_IMPLEMENTED test
    └── HeroChess.IntegrationTests/   Web-flow / socket / settlement tests
```

Workspace path: `c:\Users\ptien\Herochess\Code\HeroChessBackend`.

### 2.2 Baseline inventory (confirmed by direct file inspection)

| Concept | File | Current state |
|---|---|---|
| `GameState` | `HeroChess.Rules/GameModels.cs` lines 73–120 | In-memory mutable POCO with `Clone`. Schema v3. Carries `Pieces`, `Obstacles`, `PendingEffects`, `SkillStates`, `ConsecutiveTimeouts`. |
| `PieceState` | `HeroChess.Rules/GameModels.cs` lines 17–54 | Holds position, status, trait, and a `List<EffectState> Effects` (per-piece). |
| `EffectState` | `HeroChess.Rules/GameModels.cs` line 58 | Record `(string Code, Guid? SourcePieceId, int? RemainingTurns)`. **Insufficient** — no `EffectId`, `Creator`, `CreationOrder`, `Disabled`, or payload. |
| `ObstacleState` | `HeroChess.Rules/GameModels.cs` line 60 | Record `(Guid ObstacleId, BoardPoint Position, string Kind)`. Already in use by Xiangqi rules for Cannon screen semantics. |
| `PendingEffectState` | `HeroChess.Rules/GameModels.cs` line 62 | Record `(string Code, Side Owner, int? ExpiresAtTurn)`. Not currently read by any code path. |
| `SkillState` | `HeroChess.Rules/GameModels.cs` line 64 | Record `(int SlotNo, Guid SkillId, int? UsesRemaining, int CooldownRemaining, string ImplementationKey)`. Per side, per match. |
| `XiangqiRulesEngine.ApplyMove` | `HeroChess.Rules/XiangqiRulesEngine.cs` lines 35–56 | Hard-coded Xiangqi move pipeline, increments `Version/TurnIndex/CountedActions`, flips `SideToMove`, sets `Result/EndReason` on terminal. |
| `XiangqiRulesEngine.ApplyTeamSkill` | `HeroChess.Rules/XiangqiRulesEngine.cs` lines 59–60 | **Stub** returning `SKILL_NOT_IMPLEMENTED`. |
| `MatchCommandService.ExecuteActorAsync` | `HeroChess.Api/Services/MatchCommandService.cs` lines 27–117 | Acquires `MatchLockRegistry` gate; opens DB transaction; dedup by `CommandId`; reads `MatchState.State`; switch on `"type"`; **cases `hero_active` and `team_skill` throw 422 SKILL_NOT_IMPLEMENTED** (lines 85–86); commits DB; broadcasts `match.command_accepted`. |
| `MatchCommandService.TimeoutAsync` | `HeroChess.Api/Services/MatchCommandService.cs` lines 120–168 | Applies AFK / timeout-in-check using `XiangqiRulesEngine` only; emits `turn.timeout` event. |
| `MatchSelectionService.Start` | `HeroChess.Api/Services/MatchSelectionService.cs` lines 130–156 | Creates 32-piece `GameState` from two `FrozenLineup`s; rotates Black; seeds `state.SkillStates[side]` from `FrozenSkill` rows. |
| `FrozenSkill` | `HeroChess.Api/Services/MatchModels.cs` lines 13 | Record `(int SlotNo, Guid SkillId, string ImplementationKey, int? MaxUses, int CooldownTurns)`. Stored in `match_participant.lineup_snapshot` jsonb. |
| `MatchAction` | `HeroChess.Api/Data/Entities.cs` lines 85 | `Kind` column allows `'team_skill'` (SQL CHECK at `01_schema.sql` line 243). |
| `MatchTimeoutHostedService` | `HeroChess.Api/Services/MatchTimeoutHostedService.cs` | Polls DB every 500 ms and routes through `MatchCommandService.TimeoutAsync`. |
| `MatchRecoveryHostedService` | `HeroChess.Api/Services/MatchRecoveryHostedService.cs` | On startup, cancels `selecting`/`active` matches with `server_restart` reason. |
| `LineupValidator` | `HeroChess.Api/Services/LineupValidator.cs` | Validates 16 entries, 3 skills, ≤ 1 faction skill. |
| `TeamSkill` (DB) | `Hero_Chess_DB_Prototype_v0_1/01_schema.sql` lines 115–129 | Holds `code`, `faction_id`, `implementation_key`, `parameters jsonb`, `eligibility jsonb`, `max_uses`, `cooldown_turns`. |
| `ReplayEntryDto` | `HeroChess.Contracts/ApiContracts.cs` lines 100 | `(sequenceNo, kind, actorSide, resolvedEvents, stateAfter, CommittedAt)` — already a fit for Effect events. |

### 2.3 Test inventory (relevant existing tests)

- `tests/HeroChess.Rules.Tests/XiangqiRulesEngineTests.cs`
  - `Unimplemented_skill_does_not_mutate_state` (lines 215–225): asserts the stub still throws and the input state is not mutated.
  - `Apply_move_is_deterministic_and_does_not_mutate_input` (lines 240–253): fixture for the immutable-input contract.
- `tests/HeroChess.IntegrationTests/MatchFlowTests.cs`
  - `Ranked_match_serializes_competing_commands_broadcasts_and_replays` (lines 26–83): full command/race/replay flow.
  - `Bot_uses_same_pipeline_and_undo_appends_without_deleting_history` (lines 88–132): bot path through `ExecuteBotAsync`.
  - `Deadline_boundary_and_settlement_retries_are_idempotent` (lines 138–170): timeout + Version interaction.

---

## 3. Current Architecture

The current runtime architecture is:

- **Single authoritative writer per match** (`MatchLockRegistry`). FIFO async gate so REST, WebSocket, bot, and timeout are serialized.
- **One `GameState` per match**, persisted in `match_state.state` jsonb. `version` equals `match_action.sequence_no` of the last committed action.
- **Append-only `match_action` log** as the source of truth for replay.
- **Xiangqi rules engine** in `HeroChess.Rules` is a pure C# library. It owns `ApplyMove` and the stub `ApplyTeamSkill`.
- **Bot scheduler** (`BotTurnScheduler.cs`) generates legal moves via `XiangqiRulesEngine.GenerateLegalActions` and feeds them through `MatchCommandService.ExecuteBotAsync` (line 46). Skill activations from bot are not implemented yet.

---

## 4. Requirement → Code Gap Analysis

| Gameplay requirement | Current code | Gap | Plan impact |
|---|---|---|---|
| Per-player-turn cooldown | `SkillState.CooldownRemaining` exists; no decrement logic | Turn-start cooldown step is missing | Add to `TurnLifecycle` |
| Effect `Creator` immutable | Not represented | New field needed | Extend `GameState` (`StateSchemaVersion` 3 → 4) |
| Per-position Controller (split control) | Not represented | Multi-position Effects need per-portion controller | Add `PositionControllers: Dictionary<BoardPoint, Side>` |
| Effect `CreationOrder` immutable | Not represented | New field needed | Same |
| Effect `Disabled` independent of duration | Not represented | New field needed | Same |
| Effect `EffectId` for targeting | `EffectState` has no id | New field needed | Same |
| Multi-position Effect | `PieceState.Effects` is per-piece | No multi-position Effect | New `EffectInstance` with `TargetPositions` |
| Vạn Cọc: two separate timers | One `RemainingDuration` field | Cannot represent both timers | Separate Effect duration and physical stake lifetime |
| Server-authoritative, atomic | Already present | n/a | None — reuse pipeline |
| No partial state on failure | Already enforced via DB transaction | n/a | None — reuse pipeline |
| Multiple Command Skills per turn allowed | Current stub does not constrain | Already not constrained | None |
| Cooldown only after success | `CooldownRemaining` never set | Must set after successful execution only | `SetCooldown(SkillState, duration)` |
| Disabled Effects still targetable by Phá Trận | No Disable concept | Cannot implement | Add `Disabled` state |
| Turn-start lifecycle EXACTLY ONCE per player turn | Not represented | Need authoritative turn-processing marker | New field + separate commit strategy — see §8.2 |
| Split Effect duration semantics | Not represented | Multiple Controllers share one timer | **RESOLVED — U-DUR = A: shared RemainingDuration, Creator is timer owner** |
| River-crossing geometry | Not represented | Handler cannot map paths to positions | **RESOLVED — U-GEO = A: 1 path = 1 board column; column-based IRiverPathDefinition** |
| History preserves Creator | No Effect events | No Effect events | Add new event kinds |

---

## 5. Proposed Target Architecture

```
client ──HTTP/WS──▶ MatchesController / WebSocketEndpoint
                          │
                          ▼
              MatchCommandService.ExecuteActorAsync  (existing)
                          │
                          ├─▶ 1. Lock gate (MatchLockRegistry)            (existing)
                          ├─▶ 2. Authorize participant                     (existing)
                          ├─▶ 3. Dedup commandId                           (existing)
                          ├─▶ 4. Read & version/deadline check             (existing)
                          ├─▶ 5. TurnLifecycle processing
                          │     See §8.2 for the exactly-once design.
                          │     Conceptually:
                          │       if turn has not been processed:
                          │           process turn-start lifecycle
                          │           commit lifecycle atomically
                          │       else:
                          │           skip (already processed this turn)
                          ├─▶ 6. Branch on action.type                    (existing, extended)
                          │     ├─ "move"        → XiangqiRulesEngine.ApplyMove
                          │     ├─ "resign"      → (existing)
                          │     ├─ "undo"        → (existing)
                          │     ├─ "team_skill"  → CommandSkillDispatcher
                          │     ├─ "hero_active" → (not-implemented)
                          │     └─ "start"/"cancel"/"timeout" → (existing)
                          ├─▶ 7. (existing) ApplyActionLimit, deadlineAt
                          ├─▶ 8. (existing) Write MatchState, append MatchAction
                          └─▶ 9. (existing) Broadcast match.command_accepted
```

### 5.1 New modules (file-level, not implemented in this phase)

- `HeroChess.Rules/Skills/CommandSkillContext.cs`
- `HeroChess.Rules/Skills/ICommandSkillHandler.cs`
- `HeroChess.Rules/Skills/CommandSkillDispatcher.cs`
- `HeroChess.Rules/Skills/TurnLifecycle.cs`
- `HeroChess.Rules/Effects/EffectInstance.cs`
- `HeroChess.Rules/Effects/EffectCatalog.cs`
- `HeroChess.Rules/Effects/EffectQueries.cs`
- `HeroChess.Rules/Skills/Handlers/VanCocTranGiangHandler.cs`
- `HeroChess.Rules/Skills/Handlers/PhanKyDoatTheHandler.cs`
- `HeroChess.Rules/Skills/Handlers/PhaTranDoatPhongHandler.cs`
- `HeroChess.Rules/Skills/Handlers/BinhLamThuyHienHandler.cs`

### 5.2 `StateSchemaVersion` upgrade

- v3 (current): existing fields unchanged.
- v4 (new): adds `EffectInstances`, `NextCreationOrder`, `ProcessedTurns` (see §8.2).
- The reader is wrapped with a `StateSchemaUpgrade` helper that maps v3 → v4.

---

## 6. Effect Domain Model

### 6.1 Conceptual fields (confirmed gameplay)

- `EffectId` — unique identifier within a match. Assigned once; never changes.
- `SkillId` — the Skill whose activation produced this Effect.
- `Creator` — immutable Side (Red/Black). **Creator NEVER changes.**
- `PositionControllers` — `Dictionary<BoardPoint, Side>`. Maps each affected position
  to the Side currently controlling that portion.
  - When an Effect affects P1/P2/P3 and P2 is stolen:
    `{P1: A, P2: B, P3: A}`
- `CreationOrder` — immutable monotonic counter. Assigned once; never changes.
- `State` — `Active | Disabled | Ended`.
- `Duration` — measured in player turns; set at creation per Skill rules.
  When the Effect is created, `RemainingDuration` is initialized to `Duration`.
  `RemainingDuration` counts down at turn-start (per Option A below).
- `RemainingDuration` — the remaining shared duration of the Effect, measured in
  the **creator's** player turns. Counts down at the start of the creator's each
  subsequent player turn. When it reaches 0, the Effect transitions to `Ended`.
  The creator is the `TimerOwner`. This is the **selected duration model (Option A)**.
  Transfer/split does NOT reset `RemainingDuration`.
- `TargetPositions` — `IReadOnlyList<BoardPoint>` of all positions this Effect covers.
  U-GEO = A: populated via column-based `IRiverPathDefinition` (see §10.1).
- `EffectSpecificData` — Skill-dependent payload.

### 6.2 Root-level `Controller` field — NOT in model

**The model has no root-level `Controller` field.** The confirmed gameplay rules do
not define a meaningful single root Controller after a split. The only defined controller
concept is **per-position**: each position in `PositionControllers` has its own controller.

### 6.3 Proposed `EffectInstance` C# shape (SELECTED ARCHITECTURE — U-DUR = A)

```csharp
public sealed class EffectInstance
{
    public Guid EffectId { get; init; }
    public string Code { get; init; }           // stable kind identifier
    public Guid SkillId { get; init; }
    public Side Creator { get; init; }            // immutable
    // No root-level Controller field.
    public Dictionary<BoardPoint, Side> PositionControllers { get; set; }
        // key = position, value = Side currently controlling this portion
        // Every position in TargetPositions has an entry here.
    public int CreationOrder { get; init; }      // immutable
    public EffectStateValue State { get; set; } // Active | Disabled | Ended
    public int Duration { get; init; }          // set at creation; never changes
    public int RemainingDuration { get; set; }  // counts down at creator's turn-start
                                                 // TimerOwner = Creator (Option A)
    public IReadOnlyList<BoardPoint> TargetPositions { get; init; }
        // BLOCKING: requires geometry abstraction — see §10.1 (U-GEO = A selected)
    public IReadOnlyDictionary<string, object?> Payload { get; init; }
}
```

`EffectStateValue` is a 3-value enum: `Active`, `Disabled`, `Ended`.

### 6.4 Split control transfer (confirmed gameplay)

The confirmed rule says: **one Skill activation creates ONE root Effect.** When a
position of that Effect is stolen:

```
Effect X
- Creator: A
- PositionControllers: {P1: A, P2: A, P3: A}
- TargetPositions: [P1, P2, P3]
- CreationOrder: 1
- EffectId: GUID-1

B steals P2:
Effect X (still ONE EffectId = GUID-1)
- Creator: A  (unchanged)
- PositionControllers: {P1: A, P2: B, P3: A}
- CreationOrder: 1  (unchanged)
- EffectId: GUID-1  (NO new EffectId)
```

**What does NOT change:**
- `Creator` — unchanged.
- `CreationOrder` — unchanged.
- `State` — unchanged.
- `EffectId` — unchanged.

### 6.5 SPLIT EFFECT DURATION — RESOLVED (U-DUR = A)

**DECISION: Option A — One Root Effect = One Shared Duration**

The confirmed rules are mutually consistent under Option A:
- "Duration is measured in turns of the designated Controller/player."
- "Stealing does NOT reset duration."
- "Split control can exist inside one root Effect."

**Option A — Root-designated timer owner (SELECTED):**

The Effect tracks a `TimerOwner: Side` field (initially `Creator`). Only the
`TimerOwner`'s turn decrements `RemainingDuration`. `TimerOwner` never changes after
creation — it stays as the original `Creator` throughout the Effect's lifetime.

The `RemainingDuration` field is a **shared, single integer** on the root Effect.
It is:
- Initialized to `Duration` at creation.
- Decremented by 1 at the start of the **Creator's** each subsequent player turn.
- When it reaches 0, the root Effect transitions to `Ended`.
- **Not reset** by control transfer (split).
- **Not reset** by disabling.

**Key implications:**
- If the Creator's portion is stolen, the Creator's turn still drives the timer.
- If a non-Creator is the timer owner (for effects they created), they control the pace.
- The timer is independent of PositionControllers — all positions share the same timer.
- All positions expire together when the shared `RemainingDuration` reaches 0.

**Why Option A is consistent:**
- "Duration is measured in turns of the designated Controller/player" → The Creator is
  the designated Controller/TimerOwner for duration counting purposes.
- "Stealing does NOT reset duration" → The Creator's timer keeps counting regardless of
  who controls individual positions.
- "Split control can exist inside one root Effect" → PositionControllers splits; the
  shared duration does not.

### 6.6 Invariants (confirmed gameplay)

1. `EffectInstance.Creator` is set once at creation and **never mutated**.
2. `CreationOrder` is set once at creation and **never mutated**.
3. `EffectId` is assigned once at creation and **never changed**.
4. `PositionControllers` is the only mutable controller representation. Each entry
   maps one position to one Side.
5. `RemainingDuration` is **initialized** to `Duration` at creation. It counts down
   at the Creator's turn-start. It is **never reset** by control transfer, disabling,
   or any other in-game event.
6. `Duration` is set once at creation and **never mutated**.

### 6.7 Persistence

Effects live in `match_state.state` jsonb. Replay uses `match_action.state_after`
(`MatchHistoryService.ReplayAsync`) — snapshot deserialization, not event reconstruction.

---

## 7. Creator / CreationOrder

### 7.1 Invariants (confirmed gameplay)

All invariants are listed in §6.6. Key reminders:

### 7.2 Control transfer operation (confirmed gameplay)

```
ControlTransferPortion(effect, position, newController, cause):
    if effect.State == Ended: reject
    from = effect.PositionControllers[position]
    effect.PositionControllers[position] = newController
    emit effect.controller_changed { effectId, position, from, to: newController, cause }
```

**What does NOT change:**
- `Creator`
- `CreationOrder`
- `RemainingDuration` (U-DUR = A: never resets on transfer)
- `State`
- `EffectId`

---

## 8. Command Skill Execution Flow

### 8.1 Pipeline contract (existing)

```
ExecuteActorAsync (MatchCommandService.cs lines 27–117)
```

Provides: lock acquisition, authorization, idempotency, version check, deadline check,
transaction commit, `match_action` append, WebSocket broadcast.

### 8.2 TURN-START LIFECYCLE — EXACTLY ONCE (CORRECTED)

**TURN-START LIFECYCLE INVARIANT:**
> For each authoritative Player Turn identity, TurnLifecycle is committed exactly once.

An invalid command attempt MUST NOT cause the already-processed turn-start lifecycle
to execute again.

**Why the naive approach fails:**

The following design is NOT sufficient:

```
BeginTurn()  # mutates cooldowns/durations
  -> action validation
  -> action fails → rollback
  -> next action calls BeginTurn() again → double decrement
```

This design couples the turn-start lifecycle mutation with the individual command
transaction. If the command transaction rolls back, the lifecycle mutation rolls back
too. The next valid command then re-executes the lifecycle — violating exactly-once.

**Proposed Architecture (PROPOSED — NOT SELECTED):**

Two concepts are separated:

**A. Turn-start lifecycle transition** — an authoritative state change that advances
all per-turn timers. This is its own atomic commit.

**B. Individual player action transaction** — a specific command (move, Skill, etc.)
that is validated against the current state and applied if valid.

These are not interchangeable. An invalid action does not roll back the turn-start
lifecycle.

**Concrete pattern (PROPOSED ARCHITECTURE):**

```
# At the start of every Player Turn, the authoritative game state
# must commit exactly one turn-start lifecycle before any player action.

# Two-phase approach:

PHASE 1 — Authoritative turn-start commit:
  Open transaction T1.
  Read current state.
  if not IsCurrentTurnProcessed(state):
      ApplyTurnLifecycle(state)
      MarkCurrentTurnProcessed(state)
      Commit T1.  # Turn lifecycle is now committed.
  else:
      Rollback T1.  # Turn already processed — no-op.

PHASE 2 — Player action (separate transaction):
  Open transaction T2.
  Read state (includes committed turn lifecycle).
  Validate action.
  if valid: ApplyAction(state).
  Commit T2.
```

**Why this satisfies exactly-once:**

| Scenario | Behavior |
|---|---|
| Invalid command first | Turn lifecycle committed in Phase 1. Command validation fails in Phase 2. Command rolled back. Turn lifecycle NOT re-executed. |
| Valid command next | Turn lifecycle already committed. Phase 1 is a no-op. Command proceeds. |
| Multiple valid Skills | Turn lifecycle committed once in Phase 1. Each Skill in separate Phase 2 transaction. |
| Failed Skill attempt | Phase 2 of that attempt rolls back. Phase 1 turn lifecycle persists. |
| Timeout | Timeout handler performs Phase 1 for the new turn. Then Phase 2 processes the timeout action. |
| Client retry | Phase 1 is idempotent (turn already processed). Phase 2 is idempotent via `CommandId` dedup. |
| Reconnect | Same state. Same behavior. |

**Implementation note:** The exact mechanism (two transactions, conditional commit,
separate commit of turn-processing marker) is a **PROPOSED ARCHITECTURE**. The
invariant ("exactly once per authoritative Player Turn") is the confirmed gameplay
requirement. The implementation must satisfy the invariant; the pattern above is
one way to do so that is compatible with the existing repository architecture.

**What the turn-start lifecycle contains:**
- Decrement `CooldownRemaining` for each Skill owned by the current player (if > 0).
- Apply the Effect duration policy (U-DUR = A: decrement `RemainingDuration` by 1 at the start of the Creator's player turn).
- Expire Effects whose duration has reached 0.
- Emit `turn.started` event.

**What it does NOT contain:**
- Any specific `RemainingDuration` decrement algorithm (pending §6.5 resolution).
- Any specific geometry mapping (pending §10.1 resolution).

### 8.3 TurnLifecycle content (U-DUR = A — resolved)

```
TurnLifecycle.Apply(state, sideToMove)
  next = state.Clone()
  # Cooldowns: concrete — confirmed gameplay
  for skill in next.SkillStates[sideToMove]:
      if skill.CooldownRemaining > 0:
          skill.CooldownRemaining -= 1
  events = []
  # U-DUR = A: Decrement RemainingDuration for all Effects whose Creator == currentSide.
  # RemainingDuration is a shared integer on each Effect. TimerOwner = Creator.
  for effect in next.EffectInstances:
      if effect.State == Active and effect.Creator == currentSide:
          effect.RemainingDuration -= 1
          if effect.RemainingDuration == 0:
              effect.State = Ended
              events.append(turn.started { expiredEffects: [effect.EffectId] })
  emit events
  return (next, events)
```

### 8.4 Step 6 — `"team_skill"` branch

```
case "team_skill":
    await skills.DispatchAsync(playerId, matchId, state, request.Action, ct)
    next = applied.State
    events = applied.Events
    break
```

`skills.DispatchAsync`:
1. Resolve `FrozenSkill` for `(matchId, playerId, slotNo)`.
2. Build `CommandSkillContext`.
3. `handler = dispatcher.For(implementationKey)`. If absent → 422.
4. `handler.Validate(ctx)`. Failure → 422 with no state change.
5. `handler.Execute(ctx)` → `(newState, events, cooldownTurns)`.
6. On success: `state.SkillStates[actorSide][slot].CooldownRemaining = cooldownTurns`.
7. Append `team_skill.activated` event.
8. Return `(newState, mergedEvents)`.

### 8.5 Error semantics

All errors throw `ApiException`. Transaction rolls back. No `match_action` appended.
This is consistent with the existing pipeline.

---

## 9. Effect Lifecycle

### 9.1 State diagram (confirmed gameplay)

```
                  handler.Execute()
                         │
                         ▼
                  ┌──────────────┐
       create ──▶ │   Active     │ ◀── re-enable (future Skill)
                  └──────────────┘
                       │  │
        Skill-disable  │  │  Turn-start expiration
        (cause only)  │  │  or duration_reduced_to_zero
                       ▼  ▼
                  ┌──────────────┐
                  │   Disabled   │
                  └──────┬───────┘
                         │
                         ▼
                  ┌──────────────┐
                  │    Ended     │ (terminal; still in History/Replay)
                  └──────────────┘
```

### 9.2 Disabled ≠ Ended (confirmed)

Disabled is a separate `EffectStateValue`. When `RemainingDuration` reaches 0 while
Disabled, the Effect transitions to Ended (per U-DUR = A: shared `RemainingDuration`
decrements at Creator's turn-start).

### 9.3 Targeting Disabled Effects

**Interaction with Disabled Effects is Skill-specific.**

- Phá Trận: **confirmed** — can target Disabled Effects.
- Vạn Cọc: irrelevant (creates new Effects).
- Phản Kỳ: **Confirmed — cannot target Disabled Effects** (U6 = A: only Active Effects are stealable).
- Binh Lâm: irrelevant (creates new Effects).

---

## 10. Multi-Position Effect Model

### 10.1 Geometry — RESOLVED (U-GEO = A)

**DECISION: 1 River-Crossing Path = 1 Board Column. 3 Consecutive Paths = 3 Consecutive Columns.**

River-crossing geometry is **resolved**. U-GEO = A has been selected:

- A river-crossing path is represented as **one full board column** (all 10 rows).
- "3 consecutive paths" = any run of 3 consecutive column indices.
- The river occupies columns 4, 5, 6 (indices from the left edge of the 9-column board).
- Each path/column contains all positions from row 0 to row 9 within that column.
- `TargetPositions` for a path = all 10 `BoardPoint(row, column)` entries in that column.
- Conflict: two paths conflict if they share any column. A new activation conflicts with
  an existing Effect if the selected path's column is already covered by that Effect's
  `TargetPositions`.

The implementation uses a concrete geometry abstraction (`IRiverPathDefinition`) whose
concrete column-based model is now confirmed. The `IRiverPathDefinition` interface remains
for testability but its concrete implementation is column-based.

**PROPOSED ARCHITECTURE (U-GEO = A — SELECTED):**

```
IRiverPathDefinition
  - ValidatePathSelection(selectedPaths): bool
      // selectedPaths: list of column indices (int[]), must be exactly 3 consecutive.
  - ResolvePositions(selectedPaths): IReadOnlyList<BoardPoint>
      // Returns all 10 board positions per column × 3 columns = 30 positions.
  - IsConflicting(selectedPath, existingEffect): bool
      // selectedPath column overlaps any of existingEffect.TargetPositions columns.
  - IsConsecutive(pathSet): bool
      // All 3 column indices are consecutive (e.g., {4,5,6} or {5,6,7}).
  - GetPathIdentifier(pathIndex): string
      // e.g., "col_4", "col_5"
```

### 10.2 Multi-position Effect fields (SELECTED ARCHITECTURE — U-GEO = A)

```csharp
EffectInstance.TargetPositions: IReadOnlyList<BoardPoint>
    // All positions this Effect covers.
    // U-GEO = A: column-based. Each path = one column (10 rows × 1 column = 10 BoardPoints).
    // 3 paths = 3 columns = 30 BoardPoints total.
    // Confirmed: geometry abstraction populated with column-based implementation.
```

### 10.3 Partial application and conflict (Binh Lâm — RESOLVED U8 = A)

Partial application is confirmed: one activation creates one Effect, but only the
non-conflicting paths are included in `TargetPositions`.

**U8 = A: Conflict definition is RESOLVED.** Two Effects conflict if they impose
mutually exclusive states/interactions on the same path (same board column). A new
activation's selected path conflicts with an existing Effect if the path's column is
already covered by that Effect's `TargetPositions`. Non-conflicting paths receive
Binh Lâm. One activation = one root Effect.

### 10.4 Position-level endings (confirmed gameplay)

If one position of an Effect is control-transferred, the other positions continue.
`PositionControllers` is updated at the position level. All positions share the
same `RemainingDuration` (U-DUR = A). The root Effect transitions to `Ended` when
`RemainingDuration` reaches 0. Transfer does not create per-position duration.

---

## 11. Control Transfer / Steal Model

### 11.1 Operation (confirmed gameplay)

```
ControlTransferPortion(effect, position, newController, cause):
    if effect.State == Ended: reject
    from = effect.PositionControllers[position]
    effect.PositionControllers[position] = newController
    emit effect.controller_changed { effectId, position, from, to, cause }
```

### 11.2 What does NOT change (confirmed gameplay)

- `Creator` — unchanged.
- `CreationOrder` — unchanged.
- `RemainingDuration` — unchanged (U-DUR = A: Creator's timer; never resets on transfer).
- `Duration` — unchanged.
- `State` — unchanged.
- `EffectId` — unchanged.

### 11.3 PositionControllers semantics

- Every position in `TargetPositions` must have an entry in `PositionControllers`.
- When unsplit: all entries = `Creator`.
- When a position is stolen: only that entry is updated.
- There is **no fallback to a root `Controller`** — per-position entries are authoritative.
- When all positions are transferred to one player, the dict contains only that player's
  Side for each entry — but the `EffectInstance` and `EffectId` are still the same record.

---

## 12. Command Skill Implementation Plans

### 12.1 Vạn Cọc Trấn Giang

**Trigger**
```
{
  "type": "team_skill",
  "slot": 1..3,
  "skillCode": "van_coc_tran_gian",
  "target": { /* per confirmed geometry */ }
}
```

**Validation**
1. Actor is `side-to-move` (enforced upstream).
2. Skill slot cooldown == 0 (confirmed: U1 = 2 player turns).
3. Target is a valid 3-consecutive-column path selection per the geometry definition (U-GEO = A).
4. Skill is not currently Disabled.

**State change — two independent concepts**

**A. Blocking Effect:**
```
EffectInstance ec = new(
    effectId: NewId(),
    code: "van_coc_tran_giang",
    skillId: frozenSkill.SkillId,
    creator: actorSide,
    positionControllers: {},  # all positions → actorSide
    creationOrder: state.NextCreationOrder++,
    duration: 2,               # 2 player turns of creator (U4 = A)
    remainingDuration: 2,     # initialized to Duration; decrements at creator's turn-start
    state: EffectStateValue.Active,
    targetPositions: geometry.ResolvePositions(selectedPaths),
        # U-GEO = A: 3 consecutive columns, each column = 10 board positions
    payload: {
        "kind": "river_blocking",
        "placer": actorSide,    # immutable: original placer
        "selectedPaths": selectedPaths  # column indices per geometry definition (U-GEO = A)
    }
);
state.EffectInstances.Add(ec);
```

**B. Physical stakes (separate from blocking Effect):**
```
state.Obstacles.Add(new ObstacleState(
    obstacleId: Guid.NewGuid(),
    position: geometry.ResolvePositions(selectedPaths)[i],  # U-GEO = A
    kind: "stake",
    metadata: {
        "creator": actorSide,         # immutable: original placer
        "effectId": ec.EffectId,     # link back to parent Effect
        "lifetime": 2,               # 2 turns of original placer (CONFIRMED)
        "remainingLifetime": 2       # counts down at original placer's turn-start
    }
));
```

**Physical stake rules (CONFIRMED — not affected by U-DUR):**
- Physical stakes normally disappear after 2 turns of the player who placed them.
- Once they disappear, the player can place new stakes.
- The stakes cannot be destroyed by ordinary means.
- A counter Skill can remove/neutralize the **blocking Effect** without immediately
  removing the physical stakes.
- Physical stakes continue to function as Cannon screen while physically present.
- Countering does NOT reset the original lifetime.
- Lifetime remains tied to the original placer.
- Blocking Effect and Physical Stakes are separate state concepts.

**Cooldown:** Confirmed — 2 player turns (U1 = 2).

### 12.2 Phản Kỳ Đoạt Thế

**Trigger**
```
{
  "type": "team_skill",
  "slot": 1..3,
  "skillCode": "phan_ky_doat_the",
  "target": {
    "effectId": "<guid>",
    "position": /* BoardPoint within a column, per U-GEO = A */
  }
}
```

**Validation**
1. Cooldown == 0 (confirmed: U2 = 2 player turns).
2. Target `effectId` exists.
3. `EffectInstance.Creator != actorSide` (opponent-created).
4. `EffectInstance.State != Ended`.
5. Target position is in `EffectInstance.TargetPositions`.
6. Phản Kỳ **cannot target Disabled Effects** (confirmed: U6 = disabled Effects are not stealable).
7. Only terrain-affecting Effects are stealable (confirmed: U7 = A).

**State change**

```
ControlTransferPortion(targetEffect, position, newController: actorSide,
    cause: "phan_ky_doat_the")
```

**Cooldown:** Confirmed — 2 player turns (U2 = 2).

### 12.3 Phá Trận Đoạt Phong (confirmed)

**Trigger**
```
{
  "type": "team_skill",
  "slot": 1..3,
  "skillCode": "pha_tran_doat_phong",
  "target": { "effectId": "<guid>" }
}
```

**Confirmed gameplay requirements:**
- Targets an opponent-created Effect.
- The Effect must be assigned to a selected user piece.
- Reduces remaining duration by 1 player turn.
- Target must have at least 1 turn remaining.
- Can affect Disabled Effects (confirmed).
- Cooldown = 3 player turns.

**Validation**
1. Cooldown == 0.
2. Target `effectId` exists.
3. `EffectInstance.Creator != actorSide`.
4. `EffectInstance.State != Ended`.
5. The target Effect has at least 1 remaining player turn (`EffectInstance.RemainingDuration > 0`).
6. **UNRESOLVED ARCHITECTURE DECISION:** The "assigned to a selected user piece"
   rule must be represented in the technical model. Gameplay is confirmed; the
   representation is not yet chosen. Possible options:
   - The Effect's `Payload` carries `assignedPieceId`.
   - The Effect is linked via a `TargetPieces` collection on the Effect or the piece.
   - The assignment is implicit in the skill activation event (e.g., `effect.assigned`
     event with `pieceId`).
   The plan does NOT select a representation. The confirmed gameplay requirement
   is: "the Effect is assigned to a selected user piece."

**State change:**

**CONFIRMED GAMEPLAY:** Phá Trận reduces the `RemainingDuration` of the target Effect
by exactly 1 player turn.

**CONCRETE FIELD MUTATION (U-DUR = A):**

```
eff.RemainingDuration -= 1
if eff.RemainingDuration == 0:
    eff.State = EffectStateValue.Ended
    emit effect.ended { effectId: eff.EffectId, cause: "duration_reduced_to_zero",
        positions: eff.TargetPositions }
```

- Decrement: single integer subtraction on the root `RemainingDuration` field.
- End condition: when `RemainingDuration` reaches 0, the root Effect transitions to `Ended`.
- All positions expire together (shared duration, U-DUR = A).

**Cooldown:** Confirmed — 3 player turns, applied only after successful execution.

### 12.4 Binh Lâm Thủy Hiểm

**Trigger**
```
{
  "type": "team_skill",
  "slot": 1..3,
  "skillCode": "binh_lam_thuy_hien",
  "target": { "paths": [ /* 3 consecutive column indices, per U-GEO = A */ ] }
}
```

**Confirmed gameplay:**
- Duration = 1 turn of the user.
- Partial application is allowed.
- Conflicting paths do not receive Binh Lâm.
- Non-conflicting paths receive Binh Lâm.
- One activation = one root Effect.

**Validation**
1. Cooldown == 0 (confirmed: U3 = 2 player turns).
2. `target.paths` is a valid 3-element consecutive-column sequence per U-GEO = A.
3. Partial application applies (U8 = A): conflicting paths are excluded; non-conflicting paths receive Binh Lâm. One activation = one root Effect.

**State change — partial application**

```
selectedPaths = target.paths  # per confirmed geometry
nonConflictingPaths = []
for path in selectedPaths:
    if not IsConflicting(path, existingEffects):  # U8 = A
        nonConflictingPaths.add(path)

EffectInstance ec = new(
    effectId: NewId(),
    code: "binh_lam_thuy_hien",
    skillId: frozenSkill.SkillId,
    creator: actorSide,
    positionControllers: {},  # all positions → actorSide
    creationOrder: state.NextCreationOrder++,
    duration: 1,               # 1 player turn (confirmed gameplay)
    remainingDuration: 1,    # initialized to Duration
    state: EffectStateValue.Active,
    targetPositions: geometry.ResolvePositions(nonConflictingPaths),
        # U-GEO = A: positions from 3 consecutive columns
    payload: {
        "paths": selectedPaths,
        "appliedPaths": nonConflictingPaths
    }
);
state.EffectInstances.Add(ec);
```

**Cooldown:** Confirmed — 2 player turns (U3 = 2).

---

## 13. Turn / Cooldown / Duration Timing

### 13.1 Confirmed timing rules

- Cooldown is measured in **PLAYER TURNS**. Decrementing happens at the start of the
  owner's next player turn.
- Turn-start expiration is processed **BEFORE** the player can act.
- If `RemainingDuration` reaches 0 → Effect ends immediately (U-DUR = A: shared timer, Creator's turns).
- Successful creation starts the Effect's duration per Skill rules.
- Cooldown starts only after successful server confirmation.
- Failed activation = no cooldown consumed and no successful Skill usage.

### 13.2 Multiple Command Skills per turn (confirmed gameplay)

Hero Chess allows multiple Command Skills during the same Player Turn if each is
otherwise valid. There is **no global "one Skill per turn" restriction.** The
turn identity system (§8.2) ensures that `TurnLifecycle` runs exactly once per
authoritative Player Turn, regardless of how many Skill commands are submitted.

### 13.3 Physical stake lifetime (Vạn Cọc — CONFIRMED)

Physical stakes created by Vạn Cọc have a separate lifetime tracked in obstacle
metadata. This counter:
- Is measured in the **original placer's turns** (confirmed: "2 turns of the player
  who placed them").
- Is **not reset** by control transfer of the blocking Effect.
- Is **not reset** by the blocking Effect being disabled.
- Continues even when the blocking Effect ends.
- When stake lifetime expires, the `ObstacleState` is removed from the board.
- The player who placed the stakes can place new stakes after the old ones disappear.

**Architecture proposal (NOT SELECTED):** `ObstacleState.metadata.lifetime` carries
`RemainingStakeTurns`. The exact decrement mechanism (per original-placer turn) is
an architecture decision that must align with the turn identity system (§8.2).

---

## 14. History / Event / Replay

### 14.1 New event types

| Event type | Producer | Payload |
|---|---|---|
| `team_skill.activated` | `CommandSkillDispatcher` | `{ code, slot, side, success }` |
| `effect.created` | each handler | `{ effectId, code, creator, creationOrder, targetPositions[] }` |
| `effect.controller_changed` | Phản Kỳ | `{ effectId, position, from, to, cause }` |
| `effect.duration_reduced` | Phá Trận | `{ effectId, from, to }` |
| `effect.disabled` / `effect.re_enabled` | future | `{ effectId, cause }` |
| `effect.ended` | lifecycle / Phá Trận | `{ effectId, cause, positions[] }` |
| `obstacle.created` | Vạn Cọc | `{ obstacleId, position, kind, creator, effectId }` |
| `obstacle.removed` | stake expiry | `{ obstacleId, cause }` |
| `turn.started` | `TurnLifecycle` | `{ side, decrementedCooldowns[], expiredEffects[] }` |

### 14.2 Replay semantics (confirmed architecture)

The system uses **snapshot replay**. `MatchHistoryService.ReplayAsync` reads
`match_action.state_after` jsonb. No event re-execution occurs. Events are
descriptive metadata only.

`Guid.NewGuid()` is used only at Effect creation time. The generated GUID is stored
in `EffectInstance.EffectId` which is persisted in `state_after`. No GUID generation
occurs during replay.

---

## 15. API / DTO / Contract Impact

**None** at the typed DTO/schema level. No new DTO classes, no new API endpoints, no
modifications to controller files, and no changes to `HeroChess.Contracts/ApiContracts.cs`.

New state fields (v4 `GameState`) and new event types travel through the existing
`JsonElement`-based state contract (`MatchStateDto.State` and
`MatchCommandResultDto.ResolvedEvents`). The serialized JSON shape changes from v3 to
v4 but the type-level contract is unchanged.

---

## 16. Persistence / Database Impact

**Nothing.** All new data lives in `match_state.state` jsonb.
No new tables, columns, constraints, or migrations in Phase 2.

---

## 17. Test Strategy

### 17.1 Unit tests (`HeroChess.Rules.Tests/`)

**Effect model:**
- `Effect_clone_independence`
- `Effect_creation_order_monotonic_per_match`
- `Effect_single_id_unchanged_after_transfer`
- `Effect_position_controllers_mutable_creator_immutable`

**Vạn Cọc:**
- `VanCoc_creates_one_effect_and_stakes`
- `VanCoc_stake_lifetime_tied_to_original_placer`
- `VanCoc_stake_lifetime_not_reset_by_counter_or_control_transfer`
- `VanCoc_counter_disables_effect_but_keeps_stakes`

**Phản Kỳ:**
- `PhanKy_transfers_position_controller_unchanged_creator`
- `PhanKy_effect_id_unchanged_after_transfer`
- `PhanKy_position_controllers_updated_atomically`

**Phá Trận:**
- `PhaTran_reduces_duration_by_exactly_one`
- `PhaTran_ends_effect_at_zero`
- `PhaTran_can_target_disabled_effect`
- `PhaTran_cooldown_is_three_player_turns`

**Binh Lâm:**
- `BinhLam_creates_one_effect_with_subset`
- `BinhLam_duration_is_one_turn`

**Turn lifecycle:**
- `TurnLifecycle_processes_turn_exactly_once`
- `TurnLifecycle_invalid_command_does_not_repeat_lifecycle`
- `TurnLifecycle_multiple_skills_same_turn_single_lifecycle`
- `TurnLifecycle_timeout_transition_single_lifecycle`
- `TurnLifecycle_retry_same_command_single_lifecycle`

### 17.2 Integration tests (`HeroChess.IntegrationTests/`)

- `Team_skill_succeeds_and_records_effect_created_event`
- `Team_skill_failure_does_not_advance_state`
- `Two_team_skills_in_one_turn_both_succeed`
- `Failed_team_skill_followed_by_valid_team_skill_same_turn`
- `Bot_uses_same_pipeline_for_team_skill`
- `Replay_preserves_effect_created_event`

---

## 18. Implementation Order

1. **Phase 2.1 — Effect domain model** (without duration timer logic)
   - Add `EffectInstance`, `EffectStateValue`, `NextCreationOrder`, `PositionControllers`.
   - No root-level `Controller` field.
   - Bump `StateSchemaVersion` to 4.
   - Add `StateSchemaUpgrade` reader wrapper.
   - Update `GameState.Clone()`.

2. **Phase 2.2 — Turn identity system**
   - Add turn-processing marker to `GameState`.
   - Wire exactly-once turn lifecycle into `ExecuteActorAsync` and `TimeoutAsync`.
   - Two-phase commit pattern (PROPOSED — see §8.2).

3. **Phase 2.3 — TurnLifecycle skeleton**
   - Add `TurnLifecycle` with cooldown logic concrete; duration logic concrete (U-DUR = A).
   - Wire from `ExecuteActorAsync` and `TimeoutAsync`.
   - Test exactly-once-per-turn invariant.

4. **Phase 2.4 — Command Skill dispatcher**
   - Add `ICommandSkillHandler`, `CommandSkillContext`, `CommandSkillDispatcher`,
     `CommandSkillService`.
   - Wire `case "team_skill":` branch.

5. **Phase 2.5–2.8 — Individual Skill handlers**
   - All blocking decisions resolved (U-DUR = A, U-GEO = A, U1–U4, U6–U8).
   - Remaining architecture items (A1, A3, A4) do not block implementation.

---

## 19. Risk Register

| Risk | Severity | Mitigation |
|---|---|---|
| Turn identity double-execution | Medium | Test exactly-once invariant across all retry/reconnect scenarios. |
| v3 snapshot replay incompatible with v4 | Low | `StateSchemaUpgrade` maps v3 → v4 with empty `EffectInstances`. |
| A1: physical stake `remainingLifetime` storage location | Low | Unresolved architecture; does not block Phase 2.1–2.4. |
| A3: turn identity field name/format | Low | Unresolved architecture; §8.2 design is confirmed. |
| A4: Phá Trận "assigned piece" representation | Low | Unresolved architecture; gameplay is confirmed. |

---

## 20. File-by-File Change Plan

### 20.1 `HeroChess.Rules/`

| Path | Action | Why |
|---|---|---|
| `GameModels.cs` | **Modify** | v4: add `EffectInstances`, `NextCreationOrder`, turn-processing marker; remove root `Controller`; update `Clone()` |
| `XiangqiRulesEngine.cs` | **No change** | `ApplyMove` unchanged; `ApplyTeamSkill` stub kept |
| `MovementHandlers.cs` | **No change** | Xiangqi handlers unchanged |
| `Skills/CommandSkillContext.cs` | **Create** | In-domain carrier |
| `Skills/ICommandSkillHandler.cs` | **Create** | Interface |
| `Skills/CommandSkillDispatcher.cs` | **Create** | In-process dict lookup |
| `Skills/TurnLifecycle.cs` | **Create** | §8.2 logic |
| `Skills/Handlers/VanCocTranGiangHandler.cs` | **Create** | §12.1 (BLOCKED by geometry + U1 + U4) |
| `Skills/Handlers/PhanKyDoatTheHandler.cs` | **Create** | §12.2 (BLOCKED by U2 + U6 + U7) |
| `Skills/Handlers/PhaTranDoatPhongHandler.cs` | **Create** | §12.3 (only confirmed handler; U9 architecture) |
| `Skills/Handlers/BinhLamThuyHienHandler.cs` | **Create** | §12.4 (BLOCKED by geometry + U3 + U8) |
| `Effects/EffectInstance.cs` | **Create** | §6.3 |
| `Effects/EffectStateValue.cs` | **Create** | enum |
| `Effects/EffectCatalog.cs` | **Create** | Skill-specific factories |
| `Effects/EffectQueries.cs` | **Create** | Read-only helpers |
| `Geometry/IRiverPathDefinition.cs` | **Create** | Geometry abstraction (BLOCKED pending geometry decision) |

### 20.2 `HeroChess.Api/`

| Path | Action | Why |
|---|---|---|
| `Program.cs` | **Modify** | Register `CommandSkillService` in DI |
| `Services/MatchCommandService.cs` | **Modify** | Turn identity check + TurnLifecycle + dispatcher wiring |
| `Services/CommandSkillService.cs` | **Create** | Thin DI wrapper |

### 20.3 `tests/`

| Path | Action | Why |
|---|---|---|
| `HeroChess.Rules.Tests/CommandSkillHandlerTests.cs` | **Create** | §17.1 |
| `HeroChess.Rules.Tests/XiangqiRulesEngineTests.cs` | **Modify** | Replace `Unimplemented_skill_does_not_mutate_state` |
| `HeroChess.IntegrationTests/MatchFlowTests.cs` | **Modify** | Add §17.2 tests |

---

## 21. Decisions Required Before Phase 2

### 21.1 Confirmed gameplay values

| Skill | Value | Status |
|---|---|---|
| Phá Trận cooldown | 3 player turns | **Confirmed** |
| Binh Lâm duration | 1 player turn | **Confirmed** |
| Vạn Cọc physical stake lifetime | 2 turns of original placer | **Confirmed** |
| Vạn Cọc cooldown | 2 player turns | **Confirmed** (U1 = A) |
| Vạn Cọc blocking Effect duration | 2 player turns of creator | **Confirmed** (U4 = A) |
| Phản Kỳ cooldown | 2 player turns | **Confirmed** (U2 = A) |
| Binh Lâm cooldown | 2 player turns | **Confirmed** (U3 = A) |
| Phản Kỳ: cannot target Disabled Effects | Confirmed | **Confirmed** (U6 = A) |
| Phản Kỳ: only terrain-affecting Effects are stealable | Confirmed | **Confirmed** (U7 = A) |
| Binh Lâm conflict definition | Same path (column) overlap | **Confirmed** (U8 = A) |
| U-DUR = A | One root Effect = one shared `RemainingDuration` | **Resolved** |
| U-GEO = A | 1 path = 1 board column; 3 consecutive paths | **Resolved** |

### 21.2 Previously blocking decisions — all resolved

All items in this section have been resolved. The table is retained for historical reference.

| ID | Decision | Resolution | Classification |
|---|---|---|---|
| **U1** | Vạn Cọc cooldown | **A** — 2 player turns | **Resolved** |
| **U2** | Phản Kỳ cooldown | **A** — 2 player turns | **Resolved** |
| **U3** | Binh Lâm cooldown | **A** — 2 player turns | **Resolved** |
| **U4** | Vạn Cọc blocking Effect duration | **A** — 2 player turns of creator | **Resolved** |
| **U6** | Phản Kỳ: can target Disabled Effects? | **A** — Cannot; only Active Effects are stealable | **Resolved** |
| **U7** | Phản Kỳ: which Effects are stealable? | **A** — Only terrain-affecting Effects | **Resolved** |
| **U8** | Binh Lâm: exact definition of conflict | **A** — Same path (column) overlap | **Resolved** |
| **U-DUR** | **SPLIT EFFECT DURATION SEMANTICS** | **A** — One root Effect = one shared `RemainingDuration`; Creator's turn is timer owner | **Resolved** |
| **U-GEO** | River-crossing path geometry | **A** — 1 path = 1 board column; 3 consecutive paths = 3 consecutive columns | **Resolved** |

### 21.3 Remaining unresolved architecture decisions

| ID | Decision | Impact | Classification |
|---|---|---|---|
| **A1** | Where to store physical stake `remainingLifetime` | Stake persistence | **UNRESOLVED ARCHITECTURE** |
| **A3** | Turn identity field name and format | Turn lifecycle | **UNRESOLVED ARCHITECTURE** |
| **A4** | Phá Trận: representation of "assigned to selected user piece" | Phá Trận handler | **UNRESOLVED ARCHITECTURE** (gameplay confirmed; representation not) |

**Note:** A2 (per-position duration representation) is resolved — U-DUR = A means the Effect uses a shared root `RemainingDuration` field, not per-position timers. Option B is not selected.

---

## Appendix A — Classification of Claims

| Category | Description | Examples |
|---|---|---|
| **Confirmed Gameplay Requirement** | Explicitly stated in task brief; cannot be changed | Creator immutability, Controller mutability, Phá Trận cooldown = 3, Binh Lâm duration = 1, Physical stakes lifetime = 2 turns of original placer, partial application, Phá Trận can affect Disabled Effects, Vạn Cọc stakes + blocking Effect are separate concepts, U-DUR = A, U-GEO = A, U1 = A, U2 = A, U3 = A, U4 = A, U6 = A, U7 = A, U8 = A |
| **Current Codebase Fact** | Verified by reading the actual file | `MatchCommandService.ExecuteActorAsync` lines 27–117, `SkillState.CooldownRemaining` exists |
| **Selected Architecture** | Technical design selected after resolving decisions | `EffectInstance` with shared `RemainingDuration` (U-DUR = A), column-based `IRiverPathDefinition` (U-GEO = A), `PositionControllers` dict, two-phase turn lifecycle pattern |
| **Unresolved Architecture Decision** | Must be decided by technical lead | A1 (stake lifetime storage), A3 (turn identity field), A4 (Phá Trận assigned-piece representation) |

---

## Appendix B — Files Explicitly Inspected

- `HeroChess.Rules/GameModels.cs`
- `HeroChess.Rules/MovementHandlers.cs`
- `HeroChess.Rules/XiangqiRulesEngine.cs`
- `HeroChess.Contracts/ApiContracts.cs`
- `HeroChess.Api/Program.cs`
- `HeroChess.Api/Services/MatchCommandService.cs`
- `HeroChess.Api/Services/MatchModels.cs`
- `HeroChess.Api/Services/MatchSelectionService.cs`
- `HeroChess.Api/Services/MatchReadService.cs`
- `HeroChess.Api/Services/MatchHistoryService.cs`
- `HeroChess.Api/Services/MatchLockRegistry.cs`
- `HeroChess.Api/Services/MatchConnectionHub.cs`
- `HeroChess.Api/Services/WebSocketEndpoint.cs`
- `HeroChess.Api/Services/BotTurnScheduler.cs`
- `HeroChess.Api/Services/MatchTimeoutHostedService.cs`
- `HeroChess.Api/Services/MatchRecoveryHostedService.cs`
- `HeroChess.Api/Services/MatchMaintenanceHostedService.cs`
- `HeroChess.Api/Services/SettlementService.cs`
- `HeroChess.Api/Services/SettlementPolicies.cs`
- `HeroChess.Api/Services/LineupValidator.cs`
- `HeroChess.Api/Services/LineupService.cs`
- `HeroChess.Api/Services/MatchmakingService.cs`
- `HeroChess.Api/Data/Entities.cs`
- `HeroChess.Api/Data/AppDbContext.cs`
- `HeroChess.Api/Controllers/MatchesController.cs`
- `HeroChess.Api/Controllers/CatalogController.cs`
- `HeroChess.Api/Infrastructure/GameJson.cs`
- `tests/HeroChess.Rules.Tests/XiangqiRulesEngineTests.cs`
- `tests/HeroChess.IntegrationTests/MatchFlowTests.cs`
- `Hero_Chess_DB_Prototype_v0_1/01_schema.sql` (relevant sections)
- `Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql` (relevant sections)
- `Hero_Chess_DB_Prototype_v0_1/03_seed_dev_only.sql` (relevant sections)
- `docs/ImplementationStatus.md`
- `HERO_CHESS_SKILL_RULES_UNDERSTANDING.md`
