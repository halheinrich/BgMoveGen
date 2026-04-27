# BgMoveGen

> Session conventions: [`../CLAUDE.md`](../CLAUDE.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / xUnit. NativeAOT-published DLL consumed from Python via ctypes.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgMoveGen\BgMoveGen.slnx`

## Repo

https://github.com/halheinrich/BgMoveGen — branch `main`.

## Depends on

Standalone. No C# dependencies. BgRLEngine is a downstream consumer via the
NativeAOT interop surface, but that arrow points the other way — BgMoveGen
knows nothing about it.

## Directory tree

```
BgMoveGen.slnx
BgMoveGen/
  BgMoveGen.csproj
  BoardState.cs        — int[26] board, HighPointOccupied, starting positions
  Move.cs              — (FrPt, ToPt) record struct
  Play.cs              — fixed 4-slot Move buffer
  MoveGenerator.cs     — GeneratePlays / GenerateStates / EnumerateStates /
                         NextMove / ApplyMove / UndoMove / Reference_GeneratePlays
  MoveNotationFormatter.cs — Play → standard notation ("8/5(2)", "24/18*")
  Interop.cs           — NativeAOT exports + blittable BgBoardState
BgMoveGen.Tests/
  BgMoveGen.Tests.csproj
  MoveGeneratorTests.cs
  MoveNotationFormatterTests.cs
  InteropTests.cs
```

## Architecture

### Board representation

- `int[26]` array. `Points[0]` = opponent bar. `Points[1..24]` = playing
  surface. `Points[25]` = on-roll player's bar.
- **Perspective is always on-roll.** Positive values = on-roll player's
  checkers, negative = opponent's. Board is flipped between turns by the
  caller (`generate_successor_states` returns pre-flipped successors).
- `HighPointOccupied`: highest point (1–25) with a player checker, 0 if none.
  Updated incrementally by `ApplyMove` / `UndoMove`. Bear-off legal iff
  `HighPointOccupied <= 6`.
- Borne-off checkers are not tracked inside `BoardState` — they simply leave
  the board. Off counts live in `BgBoardState` for interop only.
- `BoardState` is mutable by design. **No heap allocations in the hot path.**

### Core types

```
BoardState     — int[26] + HighPointOccupied. Mutable.
                 Static factories: Standard(), Nackgammon(), Bg960(seed?).
Move           — readonly record struct (FrPt, ToPt).
                 FrPt: 1–24 (board) or 25 (bar).
                 ToPt: >0 regular, 0 bear off, <0 hit (land on |ToPt|).
Play           — fixed 4-slot buffer of Moves, value type.
MoveGenerator  — static: GeneratePlays, GenerateStates, EnumerateStates,
                 GenerateDoubles, GenerateNonDoubles, NextMove,
                 ApplyMove, UndoMove, Reference_GeneratePlays.
MoveNotationFormatter — static: Format(Play) → standard notation string.
                 Collapses same-checker chains (bidirectional), groups
                 identical adjacent chains with "(n)" count suffix.
Interop        — NativeAOT exports + BgBoardState (blittable layout below).
                 MaxSuccessors = 100 (4 × 25 theoretical max for doubles).
```

### Move encoding

`Move(FrPt, ToPt)` stores everything `ApplyMove` / `UndoMove` need:

- Regular: `Move(13, 7)` — 13 → 7.
- Bear off: `Move(4, 0)`.
- Hit: `Move(13, -12)` — land on 12, send opponent blot to `Points[0]`.
- Formula: `ToPt = FrPt <= die ? 0 : FrPt - die`.
- Undo reverses apply. For hits, restore the blot. For bear-off, put the
  checker back. `FrPt > HighPointOccupied` after undo triggers update.

### Apply/undo pattern

```
MoveGenerator.ApplyMove(state, move);
// ... recurse or continue ...
MoveGenerator.UndoMove(state, move);
```

`HighPointOccupied` tracking:

- **Apply**: if `FrPt == HighPointOccupied` and `Points[FrPt] == 0` after
  decrement, scan down from `FrPt - 1` to find the new high.
- **Undo**: if `FrPt > HighPointOccupied`, set it to `FrPt`. Player can never
  move backward, so `ToPt` never raises `HighPointOccupied` during apply.

### NextMove iterator

```
bool NextMove(BoardState state, int die, int prevFrPt, out Move move)
```

Finds one legal move scanning from `prevFrPt - 1` downward. First call:
`prevFrPt = 26` (starts from the bar at 25). Subsequent calls: pass
`lastMove.FrPt` to advance, or `lastMove.FrPt + 1` to allow the same point
again (same-checker continuation).

### Doubles generation — ordered, no dedup

Four nested `while` loops over `NextMove`. Each level passes
`prevMove.FrPt + 1` to allow same-point moves, then advances to `move.FrPt`
after each iteration. If a deeper level finds nothing, the partial result is
recorded only if no full-depth results exist yet ("only one way to get fewer
than 4"). The non-increasing `FrPt` constraint produces canonical ordering —
no duplicates generated, no `HashSet` needed.

### Non-doubles generation — avoidance-based dedup

Two passes iterating `FrPt` from rearmost down:

- **Pass 1 (smallDie first):** canonical ordering, keep all plays. At each
  `FrPt`, use `smallDie` for the first move and `bigDie` for the second
  (with `FrPt2 <= FrPt1`).
- **Pass 2 (bigDie first):** at each `FrPt`, use `bigDie` first and
  `smallDie` second. Skip same-checker plays where (a) both intermediates
  are on-board, (b) the smallDie intermediate is not blocked, and (c)
  neither intermediate has an opponent blot — those are exact duplicates
  of pass 1.

Two-different-checker plays are never duplicated because the `FrPt` ordering
constraint is symmetric — the same pair appears the same way in both passes.
Both passes enforce must-use-both-dice and must-use-larger-die.

### Interop layout

BgRLEngine hands in and expects back:

```
points[0..23]  int16   positive = on-roll player's checkers
                       negative = opponent's checkers
                       points[0]  = 1-point
                       points[23] = 24-point
                       on-roll player moves high → low
bar_player     int32   on-roll player's checkers on bar
bar_opponent   int32   opponent's checkers on bar
off_player     int32   on-roll player's checkers borne off
off_opponent   int32   opponent's checkers borne off
```

`generate_successor_states` flips every successor before return (negate and
reverse `points`, swap bars, swap off counts) so the next call is already
oriented correctly. `get_starting_position` does **not** flip — output is
from the on-roll player's perspective.

### Bg960 random starting position

`BoardState.Bg960(seed?)` generates a symmetric random opening satisfying:

- **Symmetry:** each made point on the player side mirrors to `25 - pt` on
  the opponent side.
- **Quadrant coverage:** at least one made point in every quadrant (1–6,
  7–12, 13–18, 19–24). Mirror points are blocked at selection time so the
  constraint cannot conflict with itself.
- **Made-point count:** sampled from a weighted distribution skewed toward
  4–5 points (weights: 2→1, 3→3, 4→10, 5→10, 6→5, 7→2). Capped at 7 since
  every made point needs ≥ 2 checkers and each side has 15.
- **Per-point checker count:** stars-and-bars over the made points with a
  min-2 floor.
- **Pip floor:** total pip count must be ≥ 100. Failing positions are
  rejected and the outer loop retries, up to 1000 attempts before throwing.

### Design principles

- Zero allocation in the hot path: apply/undo mutates in place, no
  `BoardState.Copy()`.
- Incremental state tracking: `HighPointOccupied` is updated on apply/undo,
  never rescanned except when emptying the highest point.
- Dedup without collections: canonical ordering for doubles, avoidance for
  non-doubles — no `HashSet` in the inner loop.
- Correctness is validated against a reference implementation rather than
  asserted structurally (see Validation).

### Validation

- `Reference_GeneratePlays` — brute-force recursive enumeration of both die
  orderings, deduplicated by final board state (FNV-1a hash). Guaranteed
  correct. Ground truth.
- `ReferenceCorrectnessTests.Optimized_MatchesReference` — parameterized
  harness comparing optimized `GeneratePlays` to `Reference_GeneratePlays`
  by board-state set equality. Extended by adding `[InlineData]` rows; the
  default set covers all 21 opening rolls.
- Test categories: `BoardState` setup (checker counts, `HighPointOccupied`,
  bear-off eligibility); apply/undo round-trip; single-move generation (bar
  entry, regular, bear-off exact and overshoot, ordering); reference
  correctness; `GenerateStates` / `EnumerateStates` API; performance
  benchmarks; interop (successor count, flip correctness, off-count
  tracking, checker conservation, pass detection, Bg960 conservation and
  seed reproducibility).

## Public API

### Managed — `MoveGenerator`

```csharp
// Full play enumeration — for clients that need to animate or record moves.
List<Play> plays = MoveGenerator.GeneratePlays(state, die1, die2);

// Successor states only — for RL evaluation.
List<BoardState> states = MoveGenerator.GenerateStates(state, die1, die2);

// Lazy iterator — for early termination (alpha-beta, first-legal-move).
// Yielded BoardState is a shared mutable buffer; clone via Copy() if kept.
foreach (var successor in MoveGenerator.EnumerateStates(state, die1, die2))
{
    float value = Evaluate(successor);
    if (value > bestValue) { bestValue = value; bestState = successor.Copy(); }
}
```

All three enforce must-use-both-dice and must-use-larger-die. A pass is
represented as a single successor identical to the input board (flipped by
the interop layer).

### Managed — `MoveNotationFormatter`

```csharp
// Play → standard backgammon notation. No board argument needed —
// Move.ToPt already encodes hits (negative) and bear-offs (zero).
string notation = MoveNotationFormatter.Format(play);
// Examples: "8/5(2)", "bar/22", "24/18*", "6/off", "21/14", "6/2(2)*".
```

Handles bar entry (`FrPt == 25` → "bar"), bear off (`ToPt == 0` → "off"),
hits (`ToPt < 0` → "*" suffix), doubles (chains sharing `(from, to)`
collapse to "(n)" — the "*" follows the count, "(n)*", and is applied if
**any** constituent chain hit), and same-checker chain collapse across
multiple legs. Chain matching is bidirectional — legs emitted in either
time order collapse the same way.

### Native — NativeAOT exports

```c
int generate_successor_states(
    BgBoardState* input,
    int die1, int die2,
    BgBoardState* outputBuffer,
    int bufferCapacity);
// Returns successor count (always >= 1; a pass returns one flipped state
// with no moves applied). Each successor is flipped to the opponent's
// perspective. MaxSuccessors = 100.

int get_starting_position(int variant, int seed, BgBoardState* output);
// variant: 0 = standard, 1 = nackgammon, 2 = bg960
// seed:    -1 = no seed; ignored for standard and nackgammon
// Returns: 0 on success, -1 on unknown variant.
// Output is from the on-roll player's perspective (NOT flipped).

int get_version();
// Returns the DLL version integer. BgRLEngine checks this against
// REQUIRED_MOVEGEN_VERSION on load and hard-fails on mismatch.
```

## Pitfalls

- **Bearing-off overshoot.** Legal only from the highest occupied point in
  the home board (`HighPointOccupied`). The die must exceed `FrPt` *and*
  `FrPt == HighPointOccupied`. Easy to get wrong.
- **Same-checker dedup (non-doubles).** Different die orderings for the
  same checker produce the same board state when neither intermediate has
  a blot and both intermediates are reachable. Handled by the pass-2
  avoidance check — three conditions, all three must hold to skip.
- **Mirror conflicts in Bg960.** Point `i` and point `25 - i` can never
  both be made by the player (they'd collide under symmetry). The
  generator tracks a blocked set as it picks quadrant representatives.
- **Interop `_state` is static and not thread-safe.** One OS process per
  caller is fine (BgRLEngine's current model). If multi-thread use ever
  becomes needed, change to `[ThreadStatic]`. Interop tests must run
  sequentially — enforced via `[Collection("Interop")]`.
- **Pip-count integer width.** `checker_count * point_index` stays well
  inside `int` range but overflows `byte`. Use `int` or `short`.
- **`EnumerateStates` yields a shared buffer.** Consumers that retain
  successors across iterations must call `.Copy()`.

## Next steps

- Profile and shrink remaining allocations (`List<Play>` / `List<BoardState>`
  results, `Play` struct handling on the boundary).
- Extend the `Optimized_MatchesReference` harness with more positions: bar
  entry with and without blockers, late-bearoff edge cases, near-blocked
  positions, contact/race transitions.
- Consider exposing pip count, race detection, and perspective flip on
  `BoardState` as first-class methods if consumers grow beyond the current
  interop surface.
