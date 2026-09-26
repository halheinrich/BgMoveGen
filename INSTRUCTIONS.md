# BgMoveGen

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / xUnit / BenchmarkDotNet. NativeAOT-published DLL consumed from
Python via ctypes.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgMoveGen\BgMoveGen.slnx`

## Repo

https://github.com/halheinrich/BgMoveGen — branch `main`.

## Depends on

`BgDataTypes_Lib` — for `Move`, `Play`, `BoardState` and `BoardPosition`.
The shared-data layer owns the move primitives, the mutable board and the
immutable position value, "the same position" (`BoardPosition`'s equality),
play identity (`BoardState.IsSamePlay` and the list match
`IndexOfSamePlay`), and a play's notation (`Play.ToNotation`); BgMoveGen
contributes the move-generation algorithms over them. The split
keeps the data shape reusable from non-move-gen consumers (game substrate,
diagram rendering, filters) without dragging them through this library.

BgRLEngine is a downstream consumer via the NativeAOT interop surface, but
that arrow points outward — BgMoveGen knows nothing about it.

## Layout

Three projects under `BgMoveGen.slnx`, governed by repo-root
`Directory.Build.props` (TFM, nullable, implicit usings,
`TreatWarningsAsErrors`, XML doc generation) and `Directory.Packages.props`
(Central Package Management — no inline `Version=` anywhere).

**`BgMoveGen/`** — the library, and the one shipped surface: published to a
NativeAOT DLL, and declared `IsAotCompatible`, so the trim, AOT and
single-file analyzers run in its build. Three areas:

- **Generation** — `MoveGenerator`. Public: `GeneratePlays`, the
  resulting-position view `GenerateResultingStates`, the paired view
  `GenerateCandidatePlays` (each `Play` with its resulting board, as a
  `CandidatePlay`), and the validating
  turn-boundary pair `IsLegalPlay` / `ApplyPlay`. Internal: the single-move
  primitives `NextMove` and `SingleMoves`
  (`Span` and `List` overloads), the two optimized paths `GenerateDoubles` /
  `GenerateNonDoubles`, and `Reference_GeneratePlays`, the brute-force ground
  truth the tests hold them to.
- **Click entry** — `MoveEntryState`: stateful one-click `Play` assembly
  for board UIs, reporting each click as a `ClickOutcome` (`Illegal` /
  `MoveCommitted` / `PlayCompleted`).
- **Native interop** — `Interop`: the NativeAOT export surface, `internal`
  as a whole, and the blittable `BgBoardState` it marshals across the
  boundary. The only unsafe code in the library, and the reason the project
  grants `AllowUnsafeBlocks`.

**`BgMoveGen.Tests/`** — xUnit, one file per library area (generation,
click entry, interop), plus `SyntheticPositions`: the
deterministic, seed-generated board corpus the breadth sweeps share (see
Validation below), and `Boards`, the hand-built boards more than one test
class shares. A test builds a board from its counts (`BoardState.FromMop`);
it cannot write one.

**`BgMoveGen.Benchmarks/`** — a BenchmarkDotNet harness over
`GeneratePlays`: `Program.cs` is the `BenchmarkSwitcher` entry point, and
`MoveGenerationBenchmarks` measures the five play-assembly shapes plus the
load canary. Not a test project; run on demand (see Benchmarks below).

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
  `smallDie` second. Three duplicate shapes are skipped. *Same-checker* plays
  where (a) both intermediates are on-board, (b) the smallDie intermediate
  is unblocked, and (c) neither intermediate has an opponent blot — those
  are exact duplicates of Pass 1. *Same-source-point* plays (the smallDie
  move leaves the same point the bigDie move just left — including two bar
  entries) — Pass 1 already emitted that pair in the other order, and the
  orders are always interchangeable: the destinations differ, so neither
  move affects the other's legality. *Two bear-offs from different points* —
  see below.

Two-checker plays from *different* points are, with one exception, never
duplicated: the `FrPt` ordering constraint is symmetric, so a pair from
points `A > B` appears as (smallDie from `A`, bigDie from `B`) in Pass 1 and
as (bigDie from `A`, smallDie from `B`) in Pass 2 — four different moves,
because a move's destination is `FrPt - die` and the two dice differ.

The exception is the bear-off, which encodes as `(FrPt, 0)` whichever die
paid for it. When both moves bear off, the two passes build the *same two
moves*, and the ordering argument buys nothing. Pass 2 therefore also asks,
per candidate: is my first move one Pass 1 would have made with `smallDie`
(true only for a bear-off), and if so, would `bigDie` reproduce my second
move from the state that first move leaves? Both yes means Pass 1 already
emitted this pair — skip. The two legality questions go back through
`TryMakeMove`, so the passes cannot drift apart on what a legal move is.
This was the [halheinrich/backgammon#141] defect: every emitted play was
legal and the distinct set was right, but a two-die bear-off of two checkers
(checkers on the 5- and 4-point, roll 6-5, is the minimal case) came out
twice.

Both passes enforce must-use-both-dice and must-use-larger-die.

[halheinrich/backgammon#141]: https://github.com/halheinrich/backgammon/issues/141

### NextMove iterator

```
bool NextMove(BoardState state, int die, int prevFrPt, out Move move)
```

Finds one legal move scanning from `prevFrPt - 1` downward. First call:
`prevFrPt = 26` (starts from the bar at 25). Subsequent calls: pass
`lastMove.FrPt` to advance, or `lastMove.FrPt + 1` to allow the same point
again (same-checker continuation).

### Validating turn-boundary apply

`MoveGenerator.IsLegalPlay(state, play, die1, die2)` is the one legality
match: it re-runs `GeneratePlays` and asks BgDataTypes_Lib's list match,
`state.IndexOfSamePlay(play, legal)`, whether the caller's play is the same
play as a candidate. Play identity is not restated here — it is
`BoardState.IsSamePlay`'s, whose doc comment is its one statement — but
its consequence for this API is: a valid encoding of a legal play is legal
whatever its move order, decomposition into hops, pairing of combined
moves, or choice of checker to carry a hit mark, while an encoding that is
invalid from the position (a hop onto a point the opponent holds, a hit
mark that disagrees with the board) matches nothing.

`MoveGenerator.ApplyPlay(state, play, die1, die2)` is the validating wrapper
around `BoardState.ApplyPlay`: `IsLegalPlay`, throwing `ArgumentException`
on a mismatch, then `state.ApplyPlay(play)`. **The caller's play is what is
applied.** A match means it reaches the candidate's position, and
`BoardState.ApplyPlay` applies a play through that same rule rather than hop
by hop, so the board reached is the candidate's whatever encoding the caller
wrote — and the turn has one encoding, the caller's own, which is the one a
caller records. (Before identity was decided by position, a notation-level
match could accept a decomposition through a blocked point, and the
generator's encoding was applied in its place; such an encoding is now
invalid and refused.) The contract is **throw-before-mutate**: on an illegal
play, `state` is left unchanged so callers can recover without a defensive
clone.

Both are the simple-correct re-enumeration implementation. Not hot-path —
callers running tight loops should drive `GeneratePlays` directly. The
unvalidated turn-boundary primitive (`state.ApplyPlay(play)`) remains
available for callers that have already proven legality.

### MoveEntryState — state-based click legality

`MoveEntryState` assembles a `Play` one click at a time. The subtle part is
that `GeneratePlays` emits one play per resulting position, so of the
equivalent die orderings of a combined single-checker move it keeps one:
with a non-double 5-1 it emits `11/5` only as `11→10→5`, never the
equally-legal `11→6→5` (both intermediates open ⇒ same position ⇒ one
play). The same collapse happens for doubles permutations and for
bar-entry-then-hit (`bar/21 21/16*` is emitted; the equivalent
`bar/20 20/16*` is not).

So per-click legality is **not** anchored on the emitted move-lists. A click
is accepted iff:

1. it is a legal single move from the *current intermediate* state (using one
   of the dice still to be played — enumerated via `SingleMoves`, which already
   enforces bar-first, bear-off, and hit rules), **and**
2. after applying it, the position can still complete — using the dice that
   remain — to one of the positions `GeneratePlays`' plays reach
   (`CanReachTarget`, a small DFS over remaining-dice orderings against the
   precomputed target positions).

On completion the position reached identifies exactly one generated play,
and `CompletedPlay` is *that* play in the generator's own encoding — not the
literal clicked moves. Two intermediate paths to the same position therefore
yield the identical encoding (`Play.IsSameEncoding`), so a consumer matching
it among the candidates finds it whatever the path; paths that reach
genuinely different positions (one hits an intermediate blot, the other
doesn't) yield different plays.

Dice bookkeeping: `_turnDice` (length = play length) is the multiset played
this turn; `_remainingDice` tracks what's unconsumed, and each committed move
records the die it used so `UndoLast` can restore it. Target positions are
keyed by `BoardPosition`, BgDataTypes_Lib's one "same position", whose
equality decides (a hash only buckets), and each is computed by the
generator's `ResultingPositionOf` — see "One same position" below.

### One same position

Every question of "is this the same position" in this library is asked of
BgDataTypes_Lib's `BoardPosition` value, whose equality decides it
(halheinrich/backgammon#245): the reference's dedup, `MoveEntryState`'s
target positions and its landing and completion searches, and the tests'
board comparisons. There is no board hash of this library's own. The
position a generated play reaches is computed in one place,
`MoveGenerator.ResultingPositionOf`: the play's moves applied with the raw
pair, the position taken as a value (`ToPosition`), the moves undone —
allocation-free, and the input left as it was. The candidate views build
their boards from it, and the reference and `MoveEntryState` key on it.
The hot path's own dedup is unaffected: `GeneratePlays` avoids duplicates
by construction (ordered doubles, avoidance for non-doubles) and compares
no positions at all.

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

`Interop` is an `internal static` class; `BgBoardState` is an `internal`
nested struct of it (`Interop.BgBoardState`), existing only to marshal
across the native boundary and distinct from `BgDataTypes_Lib.BoardState`.
The marshaller (`TryReadPosition` / `ToExternalFlipped` / `ToExternal`)
translates between the two. `generate_successor_states` reads its input as
a `BoardPosition` through the value's non-throwing `TryCreate`, so an input
board that is not a well-formed position is refused with
`Status.InvalidPosition` before anything runs, and resets its reused board
with `BoardState.SetPosition`, the board's one whole-position write — it
never writes counts. The `[UnmanagedCallersOnly]` exports
(`GenerateSuccessorStates`, `GetStartingPosition`, `GetVersion`) are
`internal` too. None of this managed visibility touches the native
surface — NativeAOT's export discovery is attribute-based, so the C
exports are emitted identically whether the declaring class is public or
internal (verified — see Pitfalls). Own tests reach the class through
`InternalsVisibleTo`.

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
  orderings, one play kept per resulting position (`BoardPosition`). Guaranteed
  correct. Ground truth. It is two stages: `Reference_LegalSequences`, every
  legal play as a sequence of single-die moves with nothing deduplicated,
  then one per position.
- `ReferenceCorrectnessTests.Optimized_MatchesReference` — parameterized
  harness comparing optimized `GeneratePlays` to `Reference_GeneratePlays`
  by board-state set equality. Extended by adding `[InlineData]` rows; the
  default set covers all 21 opening rolls.
- `SyntheticPositions` — a deterministic, seed-generated corpus of board
  positions, half of them inside the home board. Nothing gating may read
  `TestData/`, so breadth comes from here. Two sweeps cross it with all 21
  rolls: `Optimized_MatchesReference_AcrossSyntheticPositions` (1,000
  positions against the reference — the guard on the avoidance dedup, whose
  failure mode is a *missing* play) and
  `GeneratePlays_CandidatesAreDistinctPlays_AcrossSyntheticPositions`
  (4,000 positions, 84,000 pairs — the guard on emitting one play twice,
  asked through the list match `IndexOfSamePlay`). The two are complements:
  the first only ever notices too few candidates, the second only ever
  notices too many. A third,
  `GenerateResultingStates_BoardsAreDistinct_AcrossSyntheticPositions`,
  compares the boards `GenerateResultingStates` returns: the reference sweep
  compares board *sets*, which a repeated board survives, so board
  distinctness is compared position for position, never by hash.
- `GeneratePlays_HoldsExactlyOnePlayPerLegalPosition_AcrossSyntheticPositions`
  — the pin on the generator's promise of one play per resulting position
  (halheinrich/backgammon#279, restated for position identity). On the
  corpus's first 1,000 positions and all 21 rolls it walks every legal
  single-die sequence (`Reference_LegalSequences`): no two listed plays reach
  one position, every sequence reaches a listed play's position and is the
  same play as it under `BoardState.IsSamePlay`, and every listed play's
  position is one a legal sequence reaches. Positions compare as
  `BoardPosition` values. The middle clause is what ties the producer's
  identity rule to where the moves physically lead, for every way of
  writing a legal play; it asks `IsSamePlay` of the one listed play, since
  asking the list match per sequence costs one rule evaluation per
  candidate — minutes in a Debug run — and adds nothing the distinct-plays
  sweep does not already pin.
- Test categories: apply/undo round-trip; single-move generation (bar
  entry, regular, bear-off exact and overshoot, ordering); reference
  correctness; `GenerateResultingStates` contract (final boards of the
  candidates, mover's frame, no-legal-move copy, input untouched,
  caller-owned copies, board distinctness); `GenerateCandidatePlays`
  association (each candidate is the generator's play `i` in raw encoding
  and `GenerateResultingStates`' board `i`, plus the same frame / pass /
  input / ownership pins, and `Play` returned by value); `IsLegalPlay` /
  `ApplyPlay` validation contract (legality round-trip, illegal-input
  throw, throw-before-mutate state preservation, dice-order invariance,
  closed-out empty-pass case, rejection of a hit mark disagreeing with the
  board, acceptance of every valid encoding — decomposed, paired
  differently, the hit marked on either checker — and the candidate's board
  reached from each, rejection of a decomposition through a blocked point,
  candidates distinct as plays on the opening board, on the two-die
  bear-off, and across the synthetic corpus); performance benchmarks;
  interop (successor count, flip correctness, off-count tracking, checker
  conservation, pass detection, the reused board's reset across successive
  calls, refusal of a malformed input board, Bg960 conservation and seed
  reproducibility); MoveEntryState click-by-click assembly.

### Benchmarks

`BgMoveGen.Benchmarks` is a BenchmarkDotNet harness over the public
`GeneratePlays` entry point — the surface BgRLEngine drives through interop,
and the one whose cost matters. Five cases cover the distinct play-assembly
shapes; the sixth row is not a generator measurement at all:

| Benchmark | Exercises |
|---|---|
| `DoublesFullDepth` | 3-3 from the opening — every branch reaches depth four |
| `DoublesPartialDepth` | 4-4 onto a five-point board with two on the bar — the reduced-depth fallbacks |
| `NonDoubles` | 6-4 from the opening — the two-pass avoidance-dedup path |
| `NonDoublesBearOff` | 6-5 on a home board with a lone checker on the highest point — that path where moves encode as `(point, 0)` |
| `AllOpeningRolls` | all 21 rolls from the opening — the aggregate signal |
| `SentinelNotationFormat` | notation of a fixed play set, through BgDataTypes_Lib's `Play.ToNotation` — the load canary, not a generator path |

`[MemoryDiagnoser]` is on because allocation, not nanoseconds, is the property
this generator is designed around: the documented invariant is zero allocation
in the recursion, with only the result `List<Play>` and its backing array on
the heap.

**`NonDoublesBearOff` closes a blind spot, and its position is load-bearing.**
Until [halheinrich/backgammon#141] the harness could not reach a bear-off at
all: the opening position has checkers on the 24-point, and the partial-depth
position has two on the bar. A generator change confined to bear-off encoding
therefore measured as pure parity on every row, which is indistinguishable
from a change that costs nothing. The row's position is not simply "a home
board" — it puts a **lone** checker on the highest occupied point, and the
skip it exists to measure is unreachable without that. Under 6-5 the lone
5-point checker bears off either way (exactly with the 5, as the highest point
with the 6), and taking it drops the highest point to the 4, which the
remaining die then bears off — the same two moves from both die assignments,
which is the pair Pass 2 has to recognise. Stack the 5-point instead and the
highest point never moves, the lower points can only bear off on an exact die,
and the branch is walked past rather than entered. When editing this fixture,
re-verify the same way the row was: the position must yield two candidates
against a generator without the skip and one with it. A row that no longer
enters the branch reports parity for the wrong reason.

`SentinelNotationFormat` is not a generator measurement. It is a load canary
for the case where the one-process sibling-benchmark form is unavailable —
when the two variants under test are two *spellings of the same method*, only
one of which can be compiled into a given binary. Then the only option is two
binaries run alternately, and the canary is what makes that sequential
comparison readable: it runs under the same load as the generator rows in its
own run, on a path no generator change can reach, so a canary that holds
across runs licenses reading the generator deltas and a canary that drifts
condemns the whole set. Alternate at least A, B, A; on drift, re-run rather
than average. See the contention Pitfall.

**The canary is necessary, not sufficient — and when it runs out, take the
minimum per row across runs.** A canary that drifts still condemns its set;
what [halheinrich/backgammon#141] added is that a canary that *holds* does not
by itself license the deltas. Measured there over eight alternating runs: two
runs of the **same unmodified binary** agreed on the canary to within 2% while
differing **1.6x** on `DoublesFullDepth` — 840 ns against 1,363 ns. External
CPU load on this machine is bursty and does not fall evenly on every row, so
the canary can sit in a quiet window while a generator row sits in a loud one.
Three of those eight runs were condemned by canary drift outright, and no
adjacent A/B pair survived both tests.

The fallback that does work, and the one that carried that gate: **read the
best (minimum) figure for each row across all runs of each variant, and
compare those.** It rests on the same property the canary does — contention
only ever adds time, so the smallest figure a row ever posted is its closest
approach to the uncontended one, and taking it per row costs nothing when the
runs happen to be clean. Two further reads make it safe. Keep at least one row
the change under test **cannot** reach (the doubles rows, for a non-doubles
change) as an in-run control: the spread those post is the noise floor, and a
touched row inside it is parity. And read `Allocated` first regardless — it is
immune to load, so a byte-identical allocation column is the one part of the
gate that contention cannot soften.

Run it in Release. `Program.cs` uses `BenchmarkSwitcher`, which *requires* a
selection — without `--filter` it stops and prompts, so the bare command hangs
a non-interactive shell:

```
dotnet run -c Release --project BgMoveGen.Benchmarks -- --filter '*'
```

Excluded from `dotnet test` via `IsTestProject=false` in its csproj — a run
takes minutes and asserts nothing, so it is measured on demand, never as part
of the suite.

## Public API

### Managed — `MoveGenerator`

```csharp
// Full play enumeration — for clients that need to animate or record moves.
List<Play> plays = MoveGenerator.GeneratePlays(state, die1, die2);

// Distinct positions after the mover's play, in the mover's frame (no flip).
IReadOnlyList<BoardState> boards = MoveGenerator.GenerateResultingStates(state, die1, die2);

// The same boards, each paired with the play that reaches it.
IReadOnlyList<CandidatePlay> candidates = MoveGenerator.GenerateCandidatePlays(state, die1, die2);
// candidates[i].Play, candidates[i].ResultingState

// Validating turn-boundary primitives.
bool legal = MoveGenerator.IsLegalPlay(state, play, die1, die2);
MoveGenerator.ApplyPlay(state, play, die1, die2);   // throws on illegal play
```

`GeneratePlays` enforces must-use-both-dice and must-use-larger-die. A
pass is represented as a single successor identical to the input board
(flipped by the interop layer).

`GenerateResultingStates` is the position-level view of the same candidate
list, for consumers that choose among resulting positions and need nothing
of the plays. One board per candidate,
in candidate order, each the input with that play applied and **not
flipped** — the mover's frame, unlike `BoardState.ApplyPlay` and the native
`generate_successor_states`. No two boards are equal position for position,
so no position is weighted twice; a pass yields one board equal to the
input. The list is complete before the method returns, the input is
untouched, and every board is an independent caller-owned copy — the list
interface is read-only, the `BoardState`s are ordinary mutable boards.

`GenerateCandidatePlays` returns the same candidates with the association
made explicit, for consumers that choose among resulting positions and must
report the play reaching the chosen one: candidate `i` is `GeneratePlays`'
play `i` (the generator's own encoding) and the board
`GenerateResultingStates` returns at `i`. Every guarantee above carries
over. Both methods build each board from the position the play reaches,
computed by one routine, `ResultingPositionOf` — the single source of the
play → position rule — so the two views cannot drift apart. `CandidatePlay` is a sealed class with an
internal constructor: only BgMoveGen pairs a play with a board, and there
is no `default` instance with a null board. It defines no value equality.
`Play` is returned by value, so its stored copy cannot be modified;
`ResultingState` is the caller's own mutable board, and mutating it breaks
the association. Choosing among candidates, and breaking ties, is the
consumer's decision.

The candidates `GeneratePlays` returns are distinct by resulting position:
exactly one play per position a legal play reaches, so no two are the same
play from the input (`BoardState.IsSamePlay`) and a consumer may treat
`Count == 1` as "no choice"; pinned by
`GeneratePlays_HoldsExactlyOnePlayPerLegalPosition_AcrossSyntheticPositions`,
with `GeneratePlays_CandidatesAreDistinctPlays` and its synthetic-corpus
sweep beside it.

`IsLegalPlay` matches with BgDataTypes_Lib's list match,
`BoardState.IndexOfSamePlay`, so identity is `BoardState.IsSamePlay`'s.
`ApplyPlay` is the validating wrapper around `BoardState.ApplyPlay`; on a
match it applies the caller's play, which reaches the candidate's board
(see Architecture); on rejection, the input state is unchanged. The
unvalidated form (`state.ApplyPlay(play)`) remains available.

Apply/undo at the move level are instance methods on `BoardState`
(defined in BgDataTypes_Lib): `state.ApplyMove(move)` /
`state.UndoMove(move)`. `MoveGenerator` does not expose move-level
apply/undo — the data type owns that surface.

A play's notation is BgDataTypes_Lib's `Play.ToNotation()`; this library
has no formatter of its own.

### Managed — `MoveEntryState`

Stateful one-click `Play` assembly. Anchored on
`MoveGenerator.GeneratePlays` as the legality reference, but
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
// Returns -2 (InvalidPosition), writing nothing, when the input board is
// not a well-formed position.

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

- **No-legal-move returns a pass, not an empty list.** For a dance /
  closed-out position, `GeneratePlays` returns a one-element list holding the
  empty pass `Play` (`Count == 0`) — never `Count == 0` on the list itself.
  `GenerateResultingStates` inherits this (one board, equal to the input). Consumers must handle a single "pass" candidate, not an
  empty collection; the C interop mirrors it (successor count always `>= 1`).
- **Bearing-off overshoot.** Legal only from the highest occupied point in
  the home board (`HighPointOccupied`). The die must exceed `FrPt` *and*
  `FrPt == HighPointOccupied`. The `NextMove` iterator and `TryMakeMove`
  helper enforce this — code that hand-builds bear-off moves outside the
  generator must respect the rule. (The data primitive `Move(FrPt, 0)`
  itself is encodable for any `FrPt`; legality is the generator's job.)
- **Same-checker dedup (non-doubles).** Different die orderings for the
  same checker produce the same board state when neither intermediate has
  a blot and both intermediates are reachable. Handled by the Pass 2
  avoidance check — three conditions, all three must hold to skip. Distinct
  from the *same-source-point* skip (two checkers leaving one point, or two
  bar entries), which is unconditional — those orderings are always
  interchangeable.
- **A bear-off's encoding does not name its die.** `Move(FrPt, 0)` is what
  both dice produce, so anything that reasons about die orderings by
  comparing *moves* is blind on bear-offs. That is why Pass 2 needs a third
  skip, and it is the general trap: two plays can be move-for-move identical
  while having been built from opposite die assignments. Distinctness pins on
  the opening board cannot see this shape at all — the opening position never
  reaches a bear-off — so the synthetic corpus that backs the sweeps
  (`SyntheticPositions`) draws half its positions inside the home board.
- **Mirror conflicts in Bg960 validation.** Point `i` and point `25 - i`
  can never both be made by the player. The constraint lives inside
  `BoardState.Bg960` (BgDataTypes_Lib); BgMoveGen consumes the result.
- **Interop `_state` is static and not thread-safe.** One OS process per
  caller is fine (BgRLEngine's current model). If multi-thread use ever
  becomes needed, change to `[ThreadStatic]`. Interop tests must run
  sequentially — enforced via `[Collection("Interop")]`.
- **NativeAOT exports survive an `internal` declaring type.** `Interop`,
  its `[UnmanagedCallersOnly]` exports, and the `BgBoardState` marshalling
  struct are all `internal`, yet `generate_successor_states`,
  `get_starting_position`, and `get_version` are still emitted into the
  published DLL and load fine from Python. Export discovery is
  attribute-based, not visibility-based — verified empirically by
  internalizing the class, republishing the NativeAOT DLL, and running
  BgRLEngine's pytest (green). Keep the surface `internal`; nothing about
  the native path needs it public.
- **`GenerateResultingStates` is not a successor generator.** Its boards
  are in the mover's frame; a consumer that needs the next mover's
  perspective must flip (`FlippedCopy`) — and the native
  `generate_successor_states`, despite the similar purpose, *does* flip.
  Don't "fix" either to match the other: they serve different frames.
- **Fixed-arity `Play.Create` is the spelling at the generator's
  play-assembly sites; the span-taking spellings are not.** BgDataTypes_Lib's
  intent-level construction surface (`Play.Create(m1, m2, m3, m4)`) reads
  better than `new Play(); play.Add(m1); play.Add(m2); …` — the doubles
  depth ladder collapses from 24 lines to four and becomes visible at a
  glance — and since BgDataTypes_Lib gained fixed-arity `Create` overloads it
  also costs nothing. Those overloads take one to four `Move`s and write the
  play's slots directly, with no argument buffer in between, so a call folds
  the way four separately-inlined `Add` calls did. Measured on this adoption,
  sentinel-validated A/B/A on an idle machine: `DoublesFullDepth` 0.90x
  (847 ns → 765 ns), `DoublesPartialDepth` 0.94x, `NonDoubles` 1.015x,
  `AllOpeningRolls` 0.9994x (7,032 ns → 7,028 ns), allocation byte-identical
  on every row. Parity, with the four-move site the one that gains.

  This reverses an earlier ruling that the incremental `Add` spelling was a
  deliberate performance choice. That ruling was right on its numbers —
  1.48x on `DoublesFullDepth` against the `Create` of the day, which looped
  its `params ReadOnlySpan<Move>` so `Count` was not statically known per
  element — and the fixed-arity overloads exist because of it. It is the
  grounds that went stale, not the measurement.

  Still excluded: the span-taking spellings, the collection expression
  `Play p = [m1, m2];` among them, since `[CollectionBuilder]` upstream
  points at `Create(params ReadOnlySpan<Move>)`. That overload is no longer
  the loop that made the first attempt slow — it is branch-unrolled too — but
  it still costs about 1.6x, and upstream's own measurement puts all of the
  residual in the caller's argument buffer. That cost is caller-side, so no
  upstream change retires it: hot-path sites take the fixed-arity form, and
  the collection expression stays in tests and other cold callers. Upstream
  keeps `[OverloadResolutionPriority(-1)]` on the span overload so a
  fixed-arity call cannot fall into it by accident — do not "simplify" a
  `Play.Create(m1, m2)` in the generator into `[m1, m2]`.

  (The recursion's `Add` / `RemoveLast` use is separate and correct on its
  own terms: those are the documented incremental primitives and the moves
  are not all in hand.)
- **Hot-path changes get benchmarked before merge.** This is a hot-path
  producer — BgRLEngine calls `generate_successor_states` millions of times
  per training run. Any change touching the generation paths runs
  `BgMoveGen.Benchmarks` before and after, *including* a refactor billed as
  behaviour-neutral, with both tables recorded in the commit body.
  Allocation is the tighter of the two constraints: a moved `Allocated`
  figure is a regression even when the clock does not notice.
- **Benchmark numbers off this machine are contended.** eXtremeGammon
  rollouts routinely run in the background here and eat ~5 cores. That
  inflates BenchmarkDotNet's *mean* by up to 1.8x and its StdDev by 10x —
  enough on its own to invent or mask a regression, and measured: the same
  unmodified binary reported 7.7 us and 14.1 us on `AllOpeningRolls` in two
  runs. Three defences. Compare the *minimum* per-iteration figure (contention
  only ever adds time, so the min barely moves across load regimes — 880 ns
  vs 895 ns for that same pair). For a real before/after decision, put both
  variants in one process as sibling `[Benchmark]` methods so identical load
  hits both. And when that is impossible because the two variants are two
  *spellings of the same method* — only one of which can be compiled into a
  given binary — run the two binaries alternately, A, B, A at minimum, and
  read `SentinelNotationFormat` in each: it sits on a path the change under
  test cannot reach and is measured under the same load as the rows that
  matter *in its own run*, so it reports whether the machine held still
  between them. Take the deltas only if the canary agrees across runs inside
  its own error bars; if it drifts, the set is contaminated — re-run, never
  average. But do not treat an agreeing canary as proof either: it has been
  measured holding to within 2% across two runs of the *same binary* whose
  `DoublesFullDepth` differed 1.6x, because the load here is bursty and does
  not fall evenly on every row. When re-running keeps producing sets that fail
  one test or the other, fall back to the minimum per row across runs — see
  **Benchmarks** for that ruling and the two reads that make it safe.
  Sequential "measure, edit, measure" with nothing watching the machine
  remains not a valid comparison here.
- **`IsLegalPlay` and `ApplyPlay` are not hot-path.** Both re-enumerate
  via `GeneratePlays`. Acceptable for turn-boundary validation; for
  inner-loop repeated checks, drive the generator directly.
- **`MoveEntryState` legality is state-based, not move-list-based.** Do not
  "fix" entry by making `GeneratePlays` emit both die orderings of a combined
  move — one play per resulting position is correct, and RL state
  enumeration, equity, and consumers matching a play among the candidates
  all depend on it. Entry instead accepts any click that is a legal single
  move from the current state *and* keeps a generated position reachable,
  then completes as the generated play reaching the position. A click can be
  legal even though its move appears in no emitted play (e.g. `8/5` then
  `5/4`, since `8/4` is emitted as `8/7/4`). See the MoveEntryState
  architecture section.
- **Plays are compared only from a position, and never here.** Identity is
  BgDataTypes_Lib's: `BoardState.IsSamePlay`, whose doc comment is its one
  statement, and `IndexOfSamePlay`, the list match `IsLegalPlay` uses. `Play`
  has no equality — `==` does not compile, and `Equals`, hashing, and
  anything built on them (`HashSet<Play>`, `Distinct`, xUnit's
  `Assert.Equal` on plays) throw. Do not reintroduce a board-less
  comparison or a notation key here; compare exact encodings with
  `Play.IsSameEncoding` only where the encoding itself is the contract (a
  `CandidatePlay` or `CompletedPlay` is the generator's own).

## Subproject-internal next steps

- Profile and shrink remaining allocations (`List<Play>` /
  `List<BoardState>` results, `Play` struct handling on the boundary).
  `BgMoveGen.Benchmarks` now provides the `[MemoryDiagnoser]` baseline to
  measure against — 9,248 B for a full-depth doubles roll, 65,856 B for all
  21 opening rolls.
- Extend the `Optimized_MatchesReference` harness with more positions: bar
  entry with and without blockers, late-bear-off edge cases, near-blocked
  positions, contact/race transitions.
- Wording polish in this doc: the Pitfalls bullet "**`IsLegalPlay` and
  `ApplyPlay` are not hot-path**" uses "hot-path" as a predicate adjective —
  a minor predicate/prenominal distinction. Future polish candidate.
