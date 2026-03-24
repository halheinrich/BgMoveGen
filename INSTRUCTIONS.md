# BgMoveGen — Project Instructions

Part of the Backgammon tools ecosystem: https://github.com/halheinrich/backgammon
**After committing here, return to the Backgammon Umbrella project to update hashes and instructions doc.**

## Repo
https://github.com/halheinrich/BgMoveGen
**Branch:** main
**Current commit:** `ab52cd3` — Add EnumerateStates lazy iterator API (50 tests pass)

## Stack
C# / .NET 10 / Visual Studio 2026 / xUnit

## Status
All move generation complete and optimized. Board representation: int[26] (0=opponent bar, 1–24=points, 25=player bar). Move type: (FrPt, ToPt) with sign-encoded hits. Doubles use ordered generation with no dedup needed. Non-doubles use ordered generation with avoidance-based dedup (no HashSet in hot path). Legacy code removed. Reference implementation (`Reference_GeneratePlays`) provides brute-force ground truth for testing. All 50 tests passing in both Debug and Release.

## Purpose
High-performance backgammon move generation library. Pure game logic — no AI, no UI. Produces all legal plays for a given board state and dice roll, enforcing standard backgammon rules. Designed to be consumed by:
- **BgRLEngine** (Python) via native interop for training speedup
- Future C# game client / analysis tools
- Any project in the ecosystem that needs legal move enumeration

## Public API
```csharp
// Move sequences — for game clients that need to animate or record moves
List<Play> plays = MoveGenerator.GeneratePlays(state, die1, die2);

// Resulting positions — for RL evaluation, just need successor states
List<BoardState> states = MoveGenerator.GenerateStates(state, die1, die2);

// Lazy iterator — for early termination (alpha-beta, first-legal-move)
foreach (var successor in MoveGenerator.EnumerateStates(state, die1, die2))
{
    float value = Evaluate(successor);
    if (value > bestValue) { bestValue = value; bestState = successor.Copy(); }
}
```

## Key files
- BgMoveGen.csproj: https://raw.githubusercontent.com/halheinrich/BgMoveGen/ab52cd3/BgMoveGen/BgMoveGen.csproj
- BoardState.cs: https://raw.githubusercontent.com/halheinrich/BgMoveGen/ab52cd3/BgMoveGen/BoardState.cs
- Move.cs: https://raw.githubusercontent.com/halheinrich/BgMoveGen/ab52cd3/BgMoveGen/Move.cs
- MoveGenerator.cs: https://raw.githubusercontent.com/halheinrich/BgMoveGen/ab52cd3/BgMoveGen/MoveGenerator.cs
- Play.cs: https://raw.githubusercontent.com/halheinrich/BgMoveGen/ab52cd3/BgMoveGen/Play.cs
- Tests.csproj: https://raw.githubusercontent.com/halheinrich/BgMoveGen/ab52cd3/BgMoveGen.Tests/BgMoveGen.Tests.csproj
- Tests/MoveGeneratorTests.cs: https://raw.githubusercontent.com/halheinrich/BgMoveGen/ab52cd3/BgMoveGen.Tests/MoveGeneratorTests.cs

## Scope

### In scope
- Board state representation (mutable, optimized for apply/undo)
- Single-checker move generation for a given die value
- Complete play generation for a dice roll (all legal combinations)
- Apply/undo move (mutate in place, no allocation in inner loop)
- Bar entry, hitting, bearing off (exact and overshoot)
- Rule enforcement: must use both dice if possible, must use larger die if only one usable
- Deduplication of equivalent plays (by avoidance for both doubles and non-doubles)
- Bear-off eligibility tracking (incremental via HighPointOccupied)
- Race detection (zero contact)
- Pip count computation
- Dice rolling
- Starting position generation (standard, Nackgammon, Bg960)
- Native interop surface for Python consumption (C-style exports or shared memory)

### Out of scope
- Neural network evaluation
- Training logic
- Cube decisions
- Match equity tables
- UI / display

---

## Architecture

### Board representation
- **`int[26]` array.** `Points[0]` = opponent's bar. `Points[1]`–`Points[24]` = playing surface. `Points[25]` = on-roll player's bar.
- **Perspective: always on-roll.** Positive values = on-roll player's checkers. Negative = opponent's. Board is flipped between turns by the caller.
- **`HighPointOccupied`**: highest point (1–25) with a player checker, 0 if none. Updated incrementally by ApplyMove/UndoMove. Bear-off legal when `HighPointOccupied <= 6`.
- Borne-off checkers are not tracked — they simply leave the board.
- `BoardState` is mutable. No heap allocations during move generation.

### Core types
```
BoardState          — int[26] + HighPointOccupied. Mutable.
Move                — readonly record struct (FrPt, ToPt).
                      FrPt: 1–24 (board) or 25 (bar).
                      ToPt: positive = regular, 0 = bear off, negative = hit (land on |ToPt|).
Play                — fixed 4-slot buffer of Moves, value type.
MoveGenerator       — static methods: GeneratePlays, GenerateStates, EnumerateStates,
                      GenerateDoubles, GenerateNonDoubles, NextMove, ApplyMove,
                      UndoMove, Reference_GeneratePlays.
```

### Move encoding
The `Move(FrPt, ToPt)` encoding stores everything needed to apply and undo:
- **Regular move:** `Move(13, 7)` — move from point 13 to point 7
- **Bear off:** `Move(4, 0)` — bear off from point 4
- **Hit:** `Move(13, -12)` — land on point 12, sending opponent blot to bar (`Points[0]`)
- **ToPt calculation:** `ToPt = FrPt <= die ? 0 : FrPt - die`
- **Undo:** reverse the apply. For hits, restore the blot. For bear-off, put checker back. FrPt > HighPointOccupied triggers update.

### Key design principles
- **Zero allocation in the hot path**: apply/undo mutates in place, no BoardState.Copy()
- **Incremental state tracking**: `HighPointOccupied` updated on apply/undo, never rescanned (except when emptying the highest point)
- **Result-state deduplication by avoidance (doubles)**: `NextMove` iterator with non-increasing FrPt constraint produces canonical ordering — no duplicates generated, no HashSet needed
- **Avoidance-based dedup (non-doubles)**: pass 1 (smallDie first) keeps all plays; pass 2 (bigDie first) skips same-checker plays where smallDie-first path is also legal and produces the same board state
- **Correctness validated against reference implementation**: `Reference_GeneratePlays` brute-force generates all plays and deduplicates by board state

### NextMove iterator pattern
```csharp
// NextMove finds one legal move scanning from prevFrPt - 1 downward.
// First call: prevFrPt = 26 (starts from bar at 25).
// Subsequent calls: prevFrPt = lastMove.FrPt (scan from FrPt - 1 down).
// Same-point continuation: pass FrPt + 1 to allow the same point again.

bool NextMove(BoardState state, int die, int prevFrPt, out Move move)
```

### Doubles generation (ordered, no dedup)
Four nested while loops using NextMove. Each level passes `prevMove.FrPt + 1` to allow same-point moves, then advances to `move.FrPt` after each iteration. If a deeper level finds nothing, the partial result is recorded only if no full-depth results exist yet ("only one way to get fewer than 4").

### Non-doubles generation (avoidance-based dedup)
Two passes iterating FrPt from rearmost down:
- **Pass 1 (smallDie first):** canonical ordering, keep all plays. At each FrPt, use smallDie for first move, bigDie for second move (FrPt2 ≤ FrPt1).
- **Pass 2 (bigDie first):** at each FrPt, use bigDie for first move, smallDie for second. Skip same-checker plays where (a) both intermediates are on-board, (b) the smallDie intermediate is not blocked, and (c) neither intermediate has an opponent blot. These are exact duplicates of pass 1 plays.

Two-different-checker plays are never duplicated because the FrPt ordering constraint (FrPt2 ≤ FrPt1) is symmetric — the same pair appears the same way in both passes.

Enforces must-use-both-dice and must-use-larger-die rules.

### Apply/undo pattern
```csharp
MoveGenerator.ApplyMove(state, move);    // mutate forward
// ... recurse or continue ...
MoveGenerator.UndoMove(state, move);     // reverse the mutation
```

### HighPointOccupied tracking
- **Apply:** if `FrPt == HighPointOccupied` and `Points[FrPt] == 0` after decrement, scan down from `FrPt - 1` to find new high.
- **Undo:** if `FrPt > HighPointOccupied`, set `HighPointOccupied = FrPt`.
- Player can never move backward, so ToPt never raises HighPointOccupied during apply.

---

## Validation strategy
- **Primary correctness target**: `Reference_GeneratePlays` — brute-force implementation that generates all possible plays via recursive enumeration of both die orderings, then deduplicates by final board state (FNV-1a hash). Guaranteed correct. Used as ground truth.
- **Master test harness**: `ReferenceCorrectnessTests.Optimized_MatchesReference` — parameterized by (position, die1, die2). Compares optimized `GeneratePlays` against `Reference_GeneratePlays` by board-state set equality. Add new test cases by adding `[InlineData]` rows.
- **xUnit test project** (`BgMoveGen.Tests`) with 50 tests currently passing (Debug and Release).
- Test categories:
  - BoardState setup (checker counts, HighPointOccupied, bear-off eligibility)
  - Apply/undo correctness (round-trip identity, HighPointOccupied tracking)
  - Single move generation (bar entry, regular, bearing off, overshoot, ordering)
  - Reference correctness (optimized vs brute-force for all 21 opening rolls — extensible via InlineData)
  - GenerateStates / EnumerateStates API tests
  - Performance benchmarks (doubles-only and all-rolls)

---

## Performance

| Metric | Value (Release) | Notes |
|---|---|---|
| All 21 opening rolls | 3.4 μs/call | Avoidance-based dedup, no HashSet |
| Doubles only | 3.9 μs/call | Ordered generation, no dedup |
| Target | < 10 μs/call | ✅ Met — 3× under target |

---

## Bg960 setup constraints (future)
- Symmetrical (opponent mirrors player)
- No checkers on bar or borne off at start
- At least 2 checkers on every occupied point (no blots)
- At least one occupied point per quadrant
- No mirror conflicts (point i and point 23-i never both occupied)
- Minimum pip count: 100
- Made-point distribution weighted toward 4–5 made points (configurable)

---

## Python interop (deferred)
Three approaches, in order of preference:
1. **Native DLL + ctypes/cffi**: NativeAOT or C-style exports. Lowest overhead.
2. **Named pipe / socket protocol**: Persistent subprocess. Simple, cross-platform.
3. **pythonnet**: Load .NET assembly directly. Simplest, heaviest dependency.

Decision deferred until training bottleneck is benchmarked.

---

## Known pitfalls
- **int overflow in pip counts**: checker_count × point_index can exceed byte range. Use int or short.
- **Mirror conflicts in Bg960**: point i and point (23-i) must never both be selected for player checkers. Track a blocked set during generation.
- **Bearing off overshoot**: only legal from the highest occupied point in the home board (`HighPointOccupied`). Easy to get wrong.
- **Same-checker dedup**: different die orderings for same-checker moves produce the same board state when neither intermediate has a blot and both intermediates are reachable. Handled by avoidance in pass 2.

---

## Next steps
1. Profile and optimize remaining allocations (List<Play> results, Play structs)
2. Add more test positions (bar entry, bearing off, blocked, edge cases)
3. Add SetupGenerator (standard position first, then Nackgammon, Bg960)
4. Add pip count, race detection, flip perspective to BoardState as needed by consumers
5. Python interop (when training bottleneck is benchmarked)

---

## Shared rules

See `AGENTS.md` in the umbrella repo — applies to all sub-projects.
`https://raw.githubusercontent.com/halheinrich/backgammon/main/AGENTS.md`

---

## Session handoff
After committing:
1. `git rev-parse HEAD` — note the short hash
2. Update commit hash in this doc and in every raw URL
3. Return to Backgammon Umbrella project — update umbrella instructions doc
