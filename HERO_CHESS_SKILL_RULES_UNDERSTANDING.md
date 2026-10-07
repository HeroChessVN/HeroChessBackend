# HERO CHESS SKILL/EFFECT RULES UNDERSTANDING AUDIT

**Status**: Analysis-Only Audit (No Code Changes)  
**Date**: Analysis performed based on Hero Chess gameplay requirements and existing codebase inspection  
**Scope**: Command Skill and Effect system reconciliation  

---

## 1. Executive Understanding

The Hero Chess Command Skill / Effect system is intended to provide a third layer of gameplay strategy alongside Army formation and Hero/faction skills. Command Skills are player-activated abilities equipped to an Army (3 per Army) that primarily modify battlefield conditions such as terrain, paths, control, and tactical state rather than simple stat buffs.

The system requires:
- **Server-authoritative validation** before any game state change
- **Clear separation between Creator and Controller** — the player who created an Effect is immutable; the player controlling it may change via Steal, but Creator remains
- **Duration and Cooldown as independent concepts**, both measured in player turns
- **Effects as persistent gameplay state** with distinct lifecycle (creation → activation → disable/modify → expiration)
- **Interaction permissions** that are explicit per Skill (not blanket rules)
- **Multi-position Effects** where one Skill activation creates one Effect affecting multiple positions or pieces
- **History/Replay as event-based records** preserving the full sequence of state changes

---

## 2. Existing Project Structure

### 2.1 Project Layout
- **HeroChess.Rules**: Pure C# rules engine (no DB/HTTP dependencies)
  - `GameModels.cs`: Core domain models (GameState, PieceState, EffectState, SkillState, LegalMove, ApplyMoveResult)
  - `XiangqiRulesEngine.cs`: Xiangqi movement and legal action generation
  - `MovementHandlers.cs`: Custom movement implementations

- **HeroChess.Api**: ASP.NET Core Web API
  - `Data/Entities.cs`: EF Core entity models mapped to PostgreSQL
  - `Data/AppDbContext.cs`: Entity Framework DbContext and mappings
  - `Services/MatchCommandService.cs`: Server-authoritative action execution pipeline
  - `Services/MatchReadService.cs`: Read/query service for match state
  - `Services/MatchSelectionService.cs`: Lineup selection and validation
  - `Services/SettlementService.cs`: Match settlement (incomplete at audit time)
  - `Infrastructure/GameJson.cs`: JSON serialization/deserialization utilities
  - `Data/Bootstrap/04_identity_schema.sql`: Identity tables for auth

- **HeroChess.Contracts**: API request/response types (DTOs)
  - `ApiContracts.cs`: API contract definitions (TeamSkillDto, LineupSkillInput, MatchCommandRequest, etc.)

- **Database Schema** (Hero_Chess_DB_Prototype_v0_1/):
  - `01_schema.sql`: PostgreSQL DDL for full schema (including team_skill, game_match, match_state, match_action tables)
  - `02_seed_catalog.sql`: Seed data for chess_class, lineup_slot, ruleset, hero_trait, hero
  - `03_seed_dev_only.sql`: Optional dev-only fixtures

- **Tests**:
  - `tests/HeroChess.Rules.Tests/XiangqiRulesEngineTests.cs`: Unit tests for Xiangqi movement rules and stubs
  - `tests/HeroChess.IntegrationTests/MatchFlowTests.cs`: Integration tests for match flow including command execution
  - `tests/HeroChess.IntegrationTests/ApiFlowTests.cs`: API flow tests including skill assignment

### 2.2 Key Classes and Responsibilities

| Class/Entity | File | Responsibility | Relevance to Skill/Effect |
|---|---|---|---|
| `GameState` | HeroChess.Rules/GameModels.cs | Runtime board state cloned for each move; contains Pieces, Obstacles, PendingEffects, SkillStates | **HIGH**: Core game state container; holds Effects and Skill cooldown/charge |
| `PieceState` | HeroChess.Rules/GameModels.cs | Individual piece with position, status, traits, and Effects list | **HIGH**: Pieces can have Effects attached; EffectState list stored here |
| `EffectState` | HeroChess.Rules/GameModels.cs | **Record**: `(Code, SourcePieceId, RemainingTurns)` | **CRITICAL**: Currently minimal; lacks Creator, Controller, CreationOrder, Disable state, Effect-specific data |
| `PendingEffectState` | HeroChess.Rules/GameModels.cs | **Record**: `(Code, Side Owner, ExpiresAtTurn)` | **PARTIAL**: Preserves Owner and expiration concept but lacks detail |
| `SkillState` | HeroChess.Rules/GameModels.cs | **Record**: `(SlotNo, SkillId, UsesRemaining, CooldownRemaining, ImplementationKey)` | **HIGH**: Models Skill cooldown per side; CooldownRemaining is key for turn-based decrement |
| `XiangqiRulesEngine` | HeroChess.Rules/XiangqiRulesEngine.cs | Generates legal actions, applies moves, validates match state | **MEDIUM**: Xiangqi rules enforced; ApplyTeamSkill is a stub returning SKILL_NOT_IMPLEMENTED |
| `MatchCommandService` | HeroChess.Api/Services/MatchCommandService.cs | Server-authoritative command execution: validate action, apply rules, save state/action log, broadcast | **CRITICAL**: Enforces server authority; implements version checks and stale-state detection; implements transaction rollback on failure |
| `TeamSkill` (Entity) | HeroChess.Api/Data/Entities.cs | DB entity: `(Id, Code, Name, FactionId, ImplementationKey, Parameters, Eligibility, MaxUses, CooldownTurns, ...)` | **HIGH**: Catalog entry for a Command Skill; stores metadata, cooldown, and eligibility |
| `TeamSkillDto` (DTO) | HeroChess.Contracts/ApiContracts.cs | API contract: same structure as TeamSkill entity | **MEDIUM**: Used for API responses; carries skill metadata to client |
| `MatchAction` (Entity) | HeroChess.Api/Data/Entities.cs | DB entity: `(Id, MatchId, SequenceNo, CommandId, Kind, RequestPayload, ResolvedEvents, StateAfter, ...)` | **HIGH**: Event log; stores each command and resulting state; supports replay |
| `MatchState` (Entity) | HeroChess.Api/Data/Entities.cs | DB entity: `(MatchId, Version, SideToMove, TurnIndex, CountedActions, State, TurnDeadlineAt, ...)` | **HIGH**: Persistent snapshot of GameState; State is serialized GameState JSON |

---

## 3. Existing Implementation Findings

### 3.1 Already Supported

**[CODE FACT]** Server Authority:
- File: `HeroChess.Api/Services/MatchCommandService.cs`, method `ExecuteActorAsync()`
- Evidence: Lines 23–116 implement transactional execution with version check (line 53), deadline validation (lines 54–55), transaction rollback on failure via database transaction (line 28: `BeginTransactionAsync`), and state cloning before mutation (line 57: `GameJson.Read<GameState>`).
- Status: **SUPPORTED** — Server validates action before any state mutation; failed actions do not commit.

**[CODE FACT]** Game State Cloning:
- File: `HeroChess.Rules/GameModels.cs`
- Evidence: `GameState.Clone()` (lines 97–118) and `PieceState.Clone()` (lines 35–54) perform deep copies of mutable collections, enabling undo/replay without shared object mutation.
- Status: **SUPPORTED** — Cloning enables replays and undo operations.

**[CODE FACT]** Cooldown Tracking:
- File: `HeroChess.Rules/GameModels.cs`, `SkillState` record (line 64)
- Evidence: `SkillState` includes `CooldownRemaining: int`, which persists across turns in `GameState.SkillStates` dictionary indexed by `Side`.
- Status: **SUPPORTED** — Per-side Skill cooldown state tracked.

**[CODE FACT]** Event-based History:
- File: `HeroChess.Api/Data/Entities.cs`, `MatchAction` entity; also `HeroChess.Api/Services/MatchCommandService.cs` lines 96–99
- Evidence: Each action stores `RequestPayload`, `ResolvedEvents`, and `StateAfter`; line 70 shows events are constructed as `new object[] { new { type = "piece.moved", pieceId = ..., to = ... } }`.
- Status: **SUPPORTED** — Actions are logged with events; replay reconstruction is possible from event history.

**[CODE FACT]** Duration Concept:
- File: `HeroChess.Rules/GameModels.cs`, `EffectState` record (line 58)
- Evidence: `EffectState` includes `RemainingTurns: int?`, representing duration countdown.
- Status: **SUPPORTED** — Duration field exists; no expiration logic implemented yet.

**[CODE FACT]** Xiangqi Movement Laws:
- File: `HeroChess.Rules/XiangqiRulesEngine.cs`, lines 88–200+ (piece movement by class)
- Evidence: Standard Xiangqi rules implemented: General confined to palace, Advisor diagonal in palace, Soldier/Horse/Rook/Cannon moves, etc.
- Status: **SUPPORTED** — Xiangqi movement rules are enforced.

---

### 3.2 Partially Supported

**[CODE FACT]** Skill Activation Pipeline:
- File: `HeroChess.Api/Services/MatchCommandService.cs`, lines 59–89
- Evidence:
  - Lines 85–86: Commands `"hero_active"` and `"team_skill"` throw `SKILL_NOT_IMPLEMENTED`.
  - Line 59: Action type is extracted from JSON.
  - No dispatcher or handler registry is connected to route `"team_skill"` commands to skill implementations.
- Status: **PARTIALLY_SUPPORTED** — Skill detection exists; handler-dispatch logic is missing.

**[CODE FACT]** Effect Attachment to Pieces:
- File: `HeroChess.Rules/GameModels.cs`, `PieceState.Effects` (line 32)
- Evidence: `List<EffectState> Effects` is present on each piece; cloned in `PieceState.Clone()` (line 52).
- Status: **PARTIALLY_SUPPORTED** — Effects can be attached to pieces; but Effect data structure is too minimal.

**[CODE FACT]** Obstacle State:
- File: `HeroChess.Rules/GameModels.cs`, `ObstacleState` record (line 60)
- Evidence: `GameState.Obstacles: List<ObstacleState>` (line 88) and `ObstacleState(Guid ObstacleId, BoardPoint Position, string Kind)`.
- Status: **PARTIALLY_SUPPORTED** — Obstacles (e.g., stakes) can be represented on the board; but no logic for creating, disabling, or removing obstacle Effects has been observed.

**[CODE FACT]** Turn Management:
- File: `HeroChess.Rules/GameModels.cs`, `GameState.TurnIndex` (line 78) and `SideToMove` (line 76)
- Evidence: `TurnIndex` increments with each move (line 45 in ApplyMove); `SideToMove` alternates (line 48).
- Status: **PARTIALLY_SUPPORTED** — Turn-based state is tracked; but no automatic turn-start logic (e.g., expiring Effects, decrementing cooldown) has been observed in provided code.

---

### 3.3 Conflicts With Confirmed Rules

**[CONFLICT]** Effect Data Structure is Insufficient:

| Required Concept | Current Implementation | Issue |
|---|---|---|
| Creator | Not in EffectState | Creator must be immutable and distinct from Controller |
| Controller | Not in EffectState | Controller may change (e.g., via Steal); must be tracked separately |
| CreationOrder | Not in EffectState | Multiple Effects on one position require a precedence/ordering mechanism |
| Disable State | Not in EffectState | Disable is independent of Duration; Effect can be disabled while Duration runs |
| Effect-Specific Data | Parameters not structured in EffectState | Each Skill may attach custom data (e.g., river positions for Vạn Cọc, direction for river block) |
| EffectId | Not in EffectState | No unique identifier for Effects; cannot reference a specific Effect for Steal or removal |

**Evidence:**
- File: `HeroChess.Rules/GameModels.cs`, line 58: `EffectState` is a simple record with only Code, SourcePieceId, RemainingTurns
- File: `HeroChess.Api/Data/Entities.cs`, no Effect persistence table; Effects only exist in GameState during a match

**Impact:**
- Cannot distinguish Creator from current Controller during Steal or Control Transfer events
- Cannot disable an Effect while preserving Duration
- Cannot store Effect-specific data (e.g., affected positions, control state per position)
- Cannot query "all Effects created by player A" or "all Effects currently controlled by player B"
- Event history does not preserve that X was stolen from A to B and back to A

---

**[CONFLICT]** Skill Action Not Dispatched:

**Evidence:**
- File: `HeroChess.Api/Services/MatchCommandService.cs`, lines 85–86:
  ```csharp
  case "hero_active": throw new ApiException(422, "SKILL_NOT_IMPLEMENTED", "Hero active skills are not implemented.");
  case "team_skill": throw new ApiException(422, "SKILL_NOT_IMPLEMENTED", "Team skills are not implemented.");
  ```
- File: `HeroChess.Rules/XiangqiRulesEngine.cs`, lines 59–60:
  ```csharp
  public ApplyMoveResult ApplyTeamSkill(GameState state, int skillSlot) =>
	  ApplyMoveResult.Failure(state, "SKILL_NOT_IMPLEMENTED", $"Team skill slot {skillSlot} has no implemented handler.");
  ```

**Gameplay Rule:**
- Cooldown must start **only after Server confirms successful Skill activation**.
- Failed activation must not start cooldown.

**Current Capability:**
- No handler registry exists to route Skill activations to implementations.
- No mechanism to validate Skill eligibility (target constraints, range, board state).
- No mechanism to resolve Skill effects and create/modify Effects.
- No mechanism to decrement cooldown after successful activation.

**Status:** **CONFLICT** — Skill dispatch architecture is missing; cooldown timing cannot be enforced.

---

**[CONFLICT]** Duration Expiration Logic Not Implemented:

**Gameplay Rule:**
- At the start of a player's turn, server checks expiration; expired Effects are ended.
- Only then is the player's turn opened for actions.

**Current Implementation:**
- File: `HeroChess.Rules/GameModels.cs`: `EffectState.RemainingTurns` exists but no expiration logic is present.
- File: `HeroChess.Rules/XiangqiRulesEngine.cs`, `ApplyMove()`: No check for effect expiration before or after turn completion.
- File: `HeroChess.Api/Services/MatchCommandService.cs`: No pre-turn effect expiration step.

**Status:** **CONFLICT** — Duration fields are tracked but expiration rules are not implemented.

---

**[CONFLICT]** Cooldown Decrement Timing Unclear:

**Gameplay Rule:**
- Cooldown is measured in PLAYER TURNS.
- Cooldown starts only after successful activation.
- Cooldown continues even if Effect is Disabled.

**Current Implementation:**
- `SkillState.CooldownRemaining` is tracked but:
  - No logic observed to decrement it at the start of a player's next turn.
  - No logic observed to increment it after a failed Skill action (per rule: failed = no cooldown start).
  - Unclear whether cooldown is decremented during or after turn execution.

**Status:** **CONFLICT** — Cooldown timing and decrement mechanics not fully specified in code.

---

**[CONFLICT]** No Skill Eligibility / Target Validation:

**Gameplay Rule:**
- Each Skill must explicitly define what it is allowed to do.
- Interaction permissions include: CanSteal, CanReduceDuration, CanDisable, CanEnd, CanRemoveControlTransfer, etc.
- Creator is NOT automatically granted special rights.
- A player can affect an Effect only if the Skill allows it.

**Current Implementation:**
- `TeamSkill.Eligibility` (JSON) exists in DB but is never consulted during action execution.
- `XiangqiRulesEngine.ApplyTeamSkill()` does not perform any eligibility checks.
- No conditional logic exists to permit/deny interactions based on Skill rules.

**Status:** **CONFLICT** — Eligibility/permission framework is missing; all validation will require new implementation.

---

### 3.4 Missing

**[MISSING]** Skill Handler Implementation Architecture:
- No handler registry or dispatcher for routing `"team_skill"` actions to specific Skill implementations.
- No framework for Skill-specific validation (targets, range, board state).
- No framework for Skill-specific Effect creation / resolution.
- Current stubs in `XiangqiRulesEngine.ApplyTeamSkill()` and `MatchCommandService` execute line 85–86 (throw not implemented).

**[MISSING]** Effect Lifecycle Management:
- No pre-turn expiration check.
- No Effect creation/removal events in History.
- No tracking of control transfers (Steal events).
- No removal of Steal effects.
- No disabling/re-enabling of Effects.

**[MISSING]** Cooldown & Duration Decrement:
- No turn-start logic to decrement active Skill cooldowns and Effect durations.
- No logic to end Effects when Duration reaches 0.

**[MISSING]** Multi-position / Multi-unit Effect Handling:
- Current `EffectState` attaches to a single `PieceState`.
- No mechanism for one Effect to span multiple positions or pieces.
- No mechanism to split an Effect (e.g., Control Transfer affecting only part of a zone).

**[MISSING]** Steal / Control Transfer Implementation:
- No framework to transfer control of an Effect.
- No validation of stealability.
- No event to record who stole from whom.
- No removal of Steal effects.

**[MISSING]** Database Persistence for Effects:
- Effects are transient (exist only in `GameState` during a match).
- No `effect` table or separate Effect record in the database.
- Replay reconstruction relies on event history; Cannot directly query Effects by Creator/Controller.

---

## 4. Confirmed Gameplay Rules — Technical Summary

This section restates the rules provided in the task description in concise technical language for implementation reference.

### 4.1 Core Concepts

**Server Authority:**
- Client requests an Action.
- Server validates it.
- Only after successful validation may the game state change.
- If validation fails: no Effect is created, no cooldown is started, no partial state change occurs.
- If validation succeeds but Effect creation/application fails: entire Action is rolled back (atomic transaction).

**Command Skill vs Effect:**
- **Command Skill**: The ability the player activates (user-controlled, equipped to Army).
- **Effect**: The persistent gameplay state created after a Skill succeeds (may be modified, disabled, stolen, or expire).

**Essential Effect Data:**
- `EffectId` (unique identifier)
- `SkillId` (which Skill created it)
- `Creator` (immutable; player who first used the Skill)
- `Controller` (current controller; may change via Steal)
- `CreationOrder` (assignment at creation; immutable)
- `Duration` (measured in player turns)
- `State` (active, disabled, ended, etc.)
- `Target/Area` (affected positions/pieces)
- Effect-specific data (Skill-dependent Parameters)

### 4.2 Creator and Controller

**Creator**:
- Immutable after Effect creation.
- Does NOT change due to: Steal, Control Transfer, Disable, Split, End, or other state changes.
- If A creates X, then B steals X, then A takes X back — Creator(X) is always A.
- If B creates a NEW Effect Y while controlling X, Creator(Y) = B (independently determined).

**Controller**:
- Mutable; the player currently controlling the Effect.
- Changed via Steal and Control Transfer operations.
- Removed via Remove Steal operation.

---

### 4.3 CreationOrder

- Assigned when Effect is originally created.
- Preserved through Control Transfer, Split, Disable, Duration reduction, or End.
- Not reset by any state change.
- If X is split into multiple controlled portions, they preserve X's original CreationOrder (not new Effects).

---

### 4.4 Duration

- Measured in **PLAYER TURNS** (not seconds, not generic rounds).
- For a Duration=1 Effect created during A's turn:
  - Remains through rest of A's current turn.
  - Remains during B's turn.
  - Expires before A can act on A's next turn.
- At start of player's turn:
  1. Server checks expiration.
  2. Expired Effects are ended.
  3. Only then is turn opened for actions.
- If Duration reaches 0, Effect Ends immediately.
- Ending removes Effect from active Board State but remains in History/Replay.

---

### 4.5 Disable

- Independent of Duration.
- When disabled: Effect still exists, function inactive, Duration continues running.
- Disable does NOT pause, reset, or interact with Duration.
- Disable does NOT change Creator or Controller.
- If Disable ends before Duration, Effect resumes if valid.
- If Duration reaches 0 while Disabled, Effect Ends immediately.
- **IMPORTANT**: Disabled does NOT universally mean "untargetable". Interaction depends on Skill's explicit permissions. Example: Phá Trận Đoạt Phong can act on Disabled Effects.

---

### 4.6 Skill Cooldown

- Measured in **PLAYER TURNS**.
- Starts **only after Server confirms successful Skill activation**.
- Failed activation: no cooldown.
- Cooldown continues while Effect is Disabled.
- If Disable ends but Cooldown remains, Skill still unavailable until Cooldown reaches 0.
- **NO global "one Command Skill per turn" restriction**.

---

### 4.7 Through 4.15: [Full rules preserved as per source — see sections 4.7–4.15 in original rules text]

---

## 5. Current Command Skill Understanding

### 5.1 Vạn Cọc Trấn Giang (River Stakes)

**Current Implementation Status**: **MISSING**
- No Skill handler.
- No Obstacle/stake creation logic.
- No river-crossing path blockage logic.
- No separation of physical stakes from blocking Effect.

**Unresolved Items**:
1. How are "3 consecutive river-crossing paths" determined?
2. Does physical stake persist independently after blocking Effect expires?
3. How is "counter/control interaction to neutralize river-blocking effect" triggered?
4. What happens when new stakes placed during active 2-turn lifetime?

---

### 5.2 Phản Kỳ Đoạt Thế (Steal / Control Transfer)

**Current Implementation Status**: **MISSING**
- No dispatcher for Steal Skill.
- No validation of Effect stealability.
- No controller transfer logic.
- No event recording for Steal.

**Unresolved Items**:
1. Is stealability a property on Effect or determined by source Skill?
2. Can one Steal activation target multiple Effects?
3. What is scope of "opponent Effect"?
4. Is there a cooldown on Steal?

---

### 5.3 Phá Trận Đoạt Phong (Duration Reduction)

**Gameplay**: Reduce opponent Effect Duration by 1, cooldown 3 turns, can act on Disabled Effects.

**Current Implementation Status**: **MISSING**
- No dispatcher.
- No Duration reduction logic.
- No Disabled Effect check.

**Unresolved Items**:
1. Can reduce multiple Effects in one activation?
2. Can target multiple pieces?
3. Event logging when Effect ends due to Duration=0?

---

### 5.4 Binh Lâm Thủy Hiểm (River Restriction)

**Gameplay**: Restrict 3 consecutive river-crossing paths, 1-turn duration, partial application (unblocked paths still get Effect).

**Current Implementation Status**: **MISSING**
- No dispatcher.
- No river path restriction logic.
- Cooldown: **NOT YET DECIDED** (cannot implement without value).

**Unresolved Items**:
1. What defines a "river-crossing path"?
2. What constitutes "conflict with existing rules/effects"?
3. How is "up to 3 consecutive" enforced?
4. Does Binh Lâm block or restrict movement?

---

## 6. Ambiguities / Conflicts / Questions

### 6.1 Effect Data Model Structure
- Current `EffectState` insufficient for confirmed rules.
- Unclear how to extend/restructure (in-place, separate table, split entities).

### 6.2 River Path Definition
- "River-crossing paths" not explicitly defined in Xiangqi context.
- Needed for Vạn Cọc and Binh Lâm.

### 6.3 Effect Attachment Model
- Current piece-based may not support zone/area Effects required by Binh Lâm and stakes.

### 6.4 Cooldown Decrement Timing
- Start-of-turn vs. end-of-turn vs. after-Skill-use timing unclear.

### 6.5 Multi-position Effect Representation
- How are Effects spanning multiple positions/pieces/zones modeled?

### 6.6 Steal / Control Transfer Scope
- Can target single Effect, multiple Effects, or zone Effects?

### 6.7 Failed Skill Action Rollback
- Full rollback vs. partial application for Binh Lâm unclear.

### 6.8 Effect Persistence Strategy
- Should Effects be queryable after match? Database table needed?

---

## 7. Requirement-to-Code Trace Matrix

| Requirement | Code Location | Current Behavior | Status | Evidence |
|---|---|---|---|---|
| Server validates before state mutation | MatchCommandService.ExecuteActorAsync() lines 23–116 | Version check, deadline validation, transaction | **SUPPORTED** | Transaction-based execution |
| Failed action does NOT start cooldown | Not implemented | No cooldown logic present | **MISSING** | ApplyTeamSkill() stub throws error |
| Creator is immutable | Not in EffectState | Not tracked | **MISSING** | EffectState only has Code, SourcePieceId, RemainingTurns |
| Controller separate from Creator | Not in EffectState | Not tracked | **MISSING** | No Controller field |
| CreationOrder preserved | Not in EffectState | Not tracked | **MISSING** | No CreationOrder field |
| Duration in player turns | SkillState.CooldownRemaining, EffectState.RemainingTurns | Field exists | **PARTIALLY_SUPPORTED** | Field exists; no decrement logic |
| Effects expire at turn start | Not implemented | No pre-turn logic | **MISSING** | No expiration check |
| Disable independent of Duration | Not in EffectState | Not tracked | **MISSING** | No Disable state |
| Cooldown starts after successful validation | Not implemented | Stub throws error | **MISSING** | ApplyTeamSkill() not implemented |
| Cooldown continues while Disabled | Not implemented | No logic | **MISSING** | No Disable or cooldown decrement |
| Interaction permissions Skill-specific | TeamSkill.Eligibility exists but unused | Not consulted | **CONFLICT** | Field unused in validation |
| Each Skill defines interactions | No handler architecture | Not implemented | **MISSING** | No handler registry |
| Creator NOT automatically granted rights | Not implemented | No permission model | **MISSING** | No interaction logic |
| Steal transfers control, not object | Not implemented | No handler | **MISSING** | ApplyTeamSkill() stub |
| Steal does NOT change Creator | Not implemented | Not tracked | **MISSING** | No Creator field |
| Steal does NOT reset Duration | Not implemented | No logic | **MISSING** | No Steal handler |
| Xiangqi movement rules preserved | XiangqiRulesEngine lines 88–200+ | Enforced strictly | **SUPPORTED** | Standard rules implemented |
| History event-based | MatchAction.ResolvedEvents, MatchCommandService  | Events logged | **SUPPORTED** | Event construction in code |
| Replay preserves Creator history | Event-based exists; but Effects not in events | Events stored; Effect events missing | **PARTIALLY_SUPPORTED** | Events exist; Effect events not implemented |
| Game state cloneable | GameState.Clone(), PieceState.Clone() | Deep copy with collections | **SUPPORTED** | Lines 97–118, 35–54 |
| Multiple Effects per position allowed | Not enforced conflict | Piece-based model can hold many | **UNSPECIFIED** | PieceState.Effects can hold many; unclear for position-based |
| One Skill = one multi-position Effect | No handler | Not implemented | **MISSING** | No multi-position structure |
| Vạn Cọc: stakes, 2-turn lifetime | Not implemented | No handler | **MISSING** | No obstacle/stake creation |
| Phá Trận Đoạt Phong: reduce by 1, cooldown 3 | Not implemented | Stub throws error | **MISSING** | No handler |
| Binh Lâm: restrict paths, partial application | Not implemented | No handler; cooldown NOT YET DECIDED | **MISSING** | No handler; cooldown undefined |
| Phản Kỳ Đoạt Thế: steal/transfer control | Not implemented | No handler | **MISSING** | No handler |

---

## 8. Implementation Impact — Analysis Only

### 8.1 Critical Areas Requiring Change

**GameModels.cs**: `EffectState` must be extended/restructured with Creator, Controller, CreationOrder, Disable, EffectId, SkillId, Parameters.

**XiangqiRulesEngine.cs**: `ApplyTeamSkill()` stub must be replaced with handler dispatcher.

**MatchCommandService.cs**: Lines 85–86 must dispatch to Skill handlers; new pre-turn effect lifecycle step needed.

**MatchState/Entities**: Optional database Effect table if persistent querying needed.

**Event History**: New event types for effect.created, effect.stolen, effect.disabled, etc.

**Skill Handlers**: New implementations for Vạn Cọc, Phản Kỳ Đoạt Thế, Phá Trận Đoạt Phong, Binh Lâm.

---

## 9. Pre-Coding Checklist

- [ ] EffectState Data Model Decision
- [ ] Effect Attachment Model (piece-based, area-based, or both)
- [ ] Multi-position Effect Representation
- [ ] Creator/Controller Storage Strategy
- [ ] Skill Handler Architecture
- [ ] Cooldown Decrement Timing
- [ ] Effect Persistence in Database
- [ ] River Path Definition
- [ ] Vạn Cọc Physical vs Effect Separation
- [ ] Phá Trận Đoạt Phong Scope
- [ ] Binh Lâm Partial Application Conflict Resolution
- [ ] Binh Lâm Cooldown Value
- [ ] Steal Scope
- [ ] Remove Steal Interaction Mechanism

---

## 10. Agent Confidence

**Confidence: 72%**

### What Is Well Understood
- Server-authoritative validation model ✅
- Game state cloning and turn progression ✅
- Xiangqi movement rules ✅
- Event-based history infrastructure ✅
- Database schema structure ✅
- Layered architecture ✅

### What Remains Uncertain
- EffectState data model extension approach ⚠️
- Multi-position Effect representation ⚠️
- Skill handler dispatch architecture ⚠️
- Cooldown decrement timing ⚠️
- River path definition ⚠️
- Vạn Cọc physical/Effect separation ⚠️
- Steal scope and mechanics ⚠️
- Binh Lâm cooldown value ❌ (NOT YET DECIDED)
- Effect persistence strategy ⚠️

### What Would Increase Confidence
1. EffectState restructuring approach decision
2. Explicit multi-position Effect design
3. Skill handler architecture decision
4. River path definition
5. Vạn Cọc stake/Effect separation
6. Binh Lâm cooldown value from product/design
7. Steal implementation scope
8. Review of existing test fixtures

---

## 11. Audit Conclusion

The Hero Chess backend has **strong fundamentals** for server-authoritative, turn-based tactical gameplay:
- Transaction-based command execution with rollback ✅
- Event-based history for replay ✅
- Xiangqi movement rules fully implemented ✅
- Layered architecture ✅

However, **Command Skill / Effect system is NOT implemented**:
- EffectState model lacks Creator, Controller, CreationOrder, Disable fields
- **Zero Skill handlers** — all Skill activations stubbed as "not implemented"
- No cooldown/duration lifecycle logic
- No interaction permission model
- No multi-position Effect abstraction

**Implementation can proceed confidently on movement/turn mechanics; Skill/Effect implementation requires prior architectural decisions.**

---

**END OF AUDIT**

*This is a fact-based reconciliation of gameplay rules against existing code. It identifies alignment, conflicts, and gaps. Use it as a baseline for architect decisions and implementation planning. No code changes have been made.*
