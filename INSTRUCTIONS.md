# BgMoveGen

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / xUnit. NativeAOT-published DLL consumed from Python via ctypes.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgMoveGen\BgMoveGen.slnx`

## Repo

https://github.com/halheinrich/BgMoveGen — branch `main`.

## Depends on

`BgDataTypes_Lib` — for `Move`, `Play`, and `BoardState`. The shared-data
layer owns the move primitives and the mutable board representation;
BgMoveGen contributes the move-generation algorithms over them. The split
keeps the data shape reusable from non-move-gen consumers (game substrate,
diagram rendering, filters) without dragging them through this library.

BgRLEngine is a downstream consumer via the NativeAOT interop surface, but
that arrow points outward — BgMoveGen knows nothing about it.

## Directory tree

```
BgMoveGen.slnx
BgMoveGen/
  BgMoveGen.csproj
  MoveGenerator.cs       — public: GeneratePlays / GenerateStates /
                           EnumerateStates / IsLegalPlay / ApplyPlay.
                           Internal helpers: NextMove / SingleMoves
                           (Span and List overloads) / GenerateDoubles /
                           GenerateNonDoubles / Reference_GeneratePlays
  MoveNotationFormatter.cs — Play → standard notation ("8/5(2)", "24/18*")
  MoveEntryState.cs      — stateful one-click Play assembly;
                           ClickOutcome enum (Illegal / MoveCommitted /
                           PlayCompleted)
  Interop.cs             — NativeAOT exports + internal blittable
                           BgBoardState
BgMoveGen.Tests/
  BgMoveGen.Tests.csproj
  MoveGeneratorTests.cs
  MoveNotationFormatterTests.cs
  MoveEntryStateTests.cs
  InteropTests.cs
```

## Architecture

Two optimized generation paths over `BgDataTypes_Lib.BoardState`'s mutable
apply/undo primitives, plus a brute-force reference implementation used as
ground truth for tests.

### Apply/undo pattern

Hot-path consumption uses `BoardState.ApplyMove(Move)` /
`BoardState.UndoMove(Move)` instance methods (defined in BgDataTypes_Lib).
These maintain `HighPointOccupied` incrementally with no allocation; the
generator recurses by mutating the input state in place and undoing on the
way out. See [BgDataTypes_Lib's `INSTRUCTIONS.md`](../BgDataTypes_Lib/INSTRUCTIONS.md)
for the data-side semantics (point layout, `HighPointOccupied` invariant,
hit / bear-off encoding).

### Doubles generation — ordered, no dedup

Four nested `while` loops over `NextMove`. Level 1 starts from `26` (the
sentinel above the bar). Levels 2–4 pass `prevMove.FrPt + 1` to allow
same-point moves, then advance to `move.FrPt` after each iteration. If a
deeper level finds nothing, the partial result is recorded only if no
full-depth results exist yet ("only one way to get fewer than 4"). The
non-increasing `FrPt` constraint produces canonical ordering — no
duplicates generated, no `HashSet` needed.

### Non-doubles generation — avoidance-based dedup

Two passes iterating `FrPt` from rearmost down:

- **Pass 1 (smallDie first):** canonical ordering, keep all plays. At each
  `FrPt`, use `smallDie` for the first move and `bigDie` for the second
  (with `FrPt2 <= FrPt1`).
- **Pass 2 (bigDie first):** at each `FrPt`, use `bigDie` first and
  `smallDie` second. Skip same-checker plays where (a) both intermediates
  are on-board, (b) the smallDie intermediate is unblocked, and (c) neither
  intermediate has an opponent blot — those are exact duplicates of Pass 1.

Two-different-checker plays are never duplicated because the `FrPt` ordering
constraint is symmetric — the same pair appears the same way in both passes.
Both passes enforce must-use-both-dice and must-use-larger-die.

### NextMove iterator

```
bool NextMove(BoardState state, int die, int prevFrPt, out Move move)
```

Finds one legal move scanning from `prevFrPt - 1` downward. First call:
`prevFrPt = 26` (starts from the bar at 25). Subsequent calls: pass
`lastMove.FrPt` to advance, or `lastMove.FrPt + 1` to allow the same point
again (same-checker continuation).

### Validating turn-boundary apply

`MoveGenerator.ApplyPlay(state, play, die1, die2)` is the validating wrapper
around `BoardState.ApplyPlay`: it re-runs `GeneratePlays` and checks the
input play's `DeduplicationKey` against the legal set, throwing
`ArgumentException` on mismatch. The contract is **throw-before-mutate**:
on an illegal play, `state` is left byte-for-byte unchanged so callers can
recover without a defensive clone.

`MoveGenerator.IsLegalPlay(state, play, die1, die2)` is the standalone
predicate used by the wrapper; both are the simple-correct re-enumeration
implementation. Not hot-path — callers running tight loops should drive
`GeneratePlays` directly. The unvalidated turn-boundary primitive
(`state.ApplyPlay(play)`) remains available for callers that have already
proven legality.

### MoveEntryState — state-based click legality

`MoveEntryState` assembles a `Play` one click at a time. The subtle part is
that `GeneratePlays` board-state-dedups equivalent die orderings of a
combined single-checker move: with a non-double 5-1 it emits `11/5` only as
`11→10→5`, never the equally-legal `11→6→5` (both intermediates open ⇒ same
final state ⇒ one canonical play). The same collapse happens for doubles
permutations and for bar-entry-then-hit (`bar/21 21/16*` is emitted; the
equivalent `bar/20 20/16*` is not).

So per-click legality is **not** anchored on the emitted move-lists. A click
is accepted iff:

1. it is a legal single move from the *current intermediate* state (using one
   of the dice still to be played — enumerated via `SingleMoves`, which already
   enforces bar-first, bear-off, and hit rules), **and**
2. after applying it, the position can still complete — using the dice that
   remain — to one of the final board states `GeneratePlays` produced
   (`CanReachTarget`, a small DFS over remaining-dice orderings against the
   precomputed target-state signature set).

On completion the resulting board state identifies a unique generated play
(the generator dedups by final state), and `CompletedPlay` is set to *that*
canonical play — not the literal clicked moves. Two intermediate paths to the
same final state therefore yield a `CompletedPlay` equal under
`Play.Equals` / `DeduplicationKey` (so quiz scoring and `allPlays.Contains`
match regardless of path); paths that reach genuinely different states (one
hits an intermediate blot, the other doesn't) stay distinct.

Dice bookkeeping: `_turnDice` (length = play length) is the multiset played
this turn; `_remainingDice` tracks what's unconsumed, and each committed move
records the die it used so `UndoLast` can restore it. Target states are keyed
by an FNV-1a signature matching the generator's dedup hash.

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

`BgBoardState` is an `internal` nested struct of the public `Interop`
class (`Interop.BgBoardState`); it exists only to marshal across the
native boundary and is distinct from `BgDataTypes_Lib.BoardState`. The
marshaller (`FromExternal` / `ToExternalFlipped` / `ToExternal`)
translates between the two. The `[UnmanagedCallersOnly]` exports
(`GenerateSuccessorStates`, `GetStartingPosition`, `GetVersion`) are
also `internal` — they're inaccessible to managed callers regardless,
and NativeAOT's export discovery is attribute-based.

### Bg960 random starting position

Generated by `BoardState.Bg960(seed?)` in BgDataTypes_Lib — see
[that subproject's `INSTRUCTIONS.md`](../BgDataTypes_Lib/INSTRUCTIONS.md)
for the constraints (symmetry, quadrant coverage, mirror conflicts,
pip-floor retry loop). BgMoveGen exposes it through the
`get_starting_position` interop export (variant 2).

### Design principles

- Zero allocation in the hot path: apply/undo mutates in place, no
  `BoardState.Copy()`.
- Dedup without collections: canonical ordering for doubles, avoidance for
  non-doubles — no `HashSet` in the inner loop.
- Correctness validated against a reference implementation rather than
  asserted structurally (see Validation).

### Validation

- `Reference_GeneratePlays` — brute-force recursive enumeration of both die
  orderings, deduplicated by final board state (FNV-1a hash). Guaranteed
  correct. Ground truth.
- `ReferenceCorrectnessTests.Optimized_MatchesReference` — parameterized
  harness comparing optimized `GeneratePlays` to `Reference_GeneratePlays`
  by board-state set equality. Extended by adding `[InlineData]` rows; the
  default set covers all 21 opening rolls.
- Test categories: apply/undo round-trip; single-move generation (bar
  entry, regular, bear-off exact and overshoot, ordering); reference
  correctness; `GenerateStates` / `EnumerateStates` API; `IsLegalPlay` /
  `ApplyPlay` validation contract (legality round-trip, illegal-input
  throw, throw-before-mutate state preservation, dice-order invariance,
  closed-out empty-pass case); performance benchmarks; interop (successor
  count, flip correctness, off-count tracking, checker conservation, pass
  detection, Bg960 conservation and seed reproducibility); MoveEntryState
  click-by-click assembly.

## Public API

### Managed — `MoveGenerator`

```csharp
// Full play enumeration — for clients that need to animate or record moves.
List<Play> plays = MoveGenerator.GeneratePlays(state, die1, die2);

// Successor states only — for RL evaluation.
List<BoardState> states = MoveGenerator.GenerateStates(state, die1, die2);

// Lazy iterator — for early termination (alpha-beta, first-legal-move).
foreach (var successor in MoveGenerator.EnumerateStates(state, die1, die2))
{
    float value = Evaluate(successor);
    if (value > bestValue) { bestValue = value; bestState = successor.Copy(); }
}

// Validating turn-boundary primitives.
bool legal = MoveGenerator.IsLegalPlay(state, play, die1, die2);
MoveGenerator.ApplyPlay(state, play, die1, die2);   // throws on illegal play
```

`GeneratePlays` / `GenerateStates` / `EnumerateStates` enforce
must-use-both-dice and must-use-larger-die. A pass is represented as a
single successor identical to the input board (flipped by the interop
layer).

`IsLegalPlay` matches by `Play.DeduplicationKey()` — order- and
hit-invariant. `ApplyPlay` is the validating wrapper around
`BoardState.ApplyPlay`; on rejection, the input state is unchanged.
The unvalidated form (`state.ApplyPlay(play)`) remains available.

Apply/undo at the move level are instance methods on `BoardState`
(defined in BgDataTypes_Lib): `state.ApplyMove(move)` /
`state.UndoMove(move)`. `MoveGenerator` does not expose move-level
apply/undo — the data type owns that surface.

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

### Managed — `MoveEntryState`

Stateful one-click `Play` assembly. Anchored on
`MoveGenerator.GeneratePlays` as the canonical legality reference, but
**by reachable board state, not by literal move-lists** — see
Architecture and Pitfalls below. Public surface:
`TryAdvanceFrom(int, IReadOnlyList<int>)` (advance the clicked point by
one legal move, the caller's `diePreference` resolving which die) and
`TryBearOffMax()` (tray click — bear off the most checkers when a unique
completion achieves it), both → `ClickOutcome`, plus `LegalNextClicks`,
`CompletedPlay`, `Current`, `IsComplete`, `AppliedMoves`, `UndoLast()`,
`UndoAll()`. Consumed by BgDiag_Razor's `BackgammonPlayEntry`.

### Native — NativeAOT exports

```c
int generate_successor_states(
    BgBoardState* input,
    int die1, int die2,
    BgBoardState* outputBuffer,
    int bufferCapacity);
// Returns successor count (always >= 1; a pass returns one flipped state
// with no moves applied). Each successor is flipped to the opponent's
// perspective. See Interop.cs for the buffer-capacity cap.

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
  `FrPt == HighPointOccupied`. The `NextMove` iterator and `TryMakeMove`
  helper enforce this — code that hand-builds bear-off moves outside the
  generator must respect the rule. (The data primitive `Move(FrPt, 0)`
  itself is encodable for any `FrPt`; legality is the generator's job.)
- **Same-checker dedup (non-doubles).** Different die orderings for the
  same checker produce the same board state when neither intermediate has
  a blot and both intermediates are reachable. Handled by the Pass 2
  avoidance check — three conditions, all three must hold to skip.
- **Mirror conflicts in Bg960 validation.** Point `i` and point `25 - i`
  can never both be made by the player. The constraint lives inside
  `BoardState.Bg960` (BgDataTypes_Lib); BgMoveGen consumes the result.
- **Interop `_state` is static and not thread-safe.** One OS process per
  caller is fine (BgRLEngine's current model). If multi-thread use ever
  becomes needed, change to `[ThreadStatic]`. Interop tests must run
  sequentially — enforced via `[Collection("Interop")]`.
- **`EnumerateStates` yields fresh copies, not a shared buffer.** Every
  yielded `BoardState` is an independent `Copy()` of the input; consumers
  are free to retain or discard without further cloning.
- **`IsLegalPlay` and `ApplyPlay` are not hot-path.** Both re-enumerate
  via `GeneratePlays`. Acceptable for turn-boundary validation; for
  inner-loop repeated checks, drive the generator directly.
- **`MoveEntryState` legality is state-based, not move-list-based.** Do not
  "fix" entry by making `GeneratePlays` emit both die orderings of a combined
  move — the distinct-outcome dedup is correct and RL state enumeration,
  equity, and quiz `DeduplicationKey` scoring all depend on it. Entry instead
  accepts any click that is a legal single move from the current state *and*
  keeps a generated final state reachable, then canonicalizes the completed
  `Play` by resulting board state. A click can be legal even though its move
  appears in no emitted play (e.g. `8/5` then `5/4`, since `8/4` is emitted as
  `8/7/4`). See the MoveEntryState architecture section.
- **`Play` equivalence is hit-invariant.** `IsLegalPlay` matches by
  `DeduplicationKey`, which collapses hit and non-hit forms of the same
  `(FrPt, |ToPt|)` multiset. A play that lists `Move(24, 18)` will round-
  trip true even when the only legal form is the hit `Move(24, -18)`.
  This is intentional — the dedup contract is order- and hit-invariant —
  but consumers wanting hit-sensitive equivalence must compare moves
  directly, not via `IsLegalPlay`.

## Subproject-internal next steps

- Profile and shrink remaining allocations (`List<Play>` /
  `List<BoardState>` results, `Play` struct handling on the boundary).
- Extend the `Optimized_MatchesReference` harness with more positions: bar
  entry with and without blockers, late-bear-off edge cases, near-blocked
  positions, contact/race transitions.
