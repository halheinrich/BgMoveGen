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

`BgDataTypes_Lib` — for `Move`, `Play` (and its canonical chain form,
`CanonicalPlay` / `PlayChain`), and `BoardState`. The shared-data
layer owns the move primitives, play equivalence, and the mutable board
representation; BgMoveGen contributes the move-generation algorithms over
them. The split
keeps the data shape reusable from non-move-gen consumers (game substrate,
diagram rendering, filters) without dragging them through this library.

BgRLEngine is a downstream consumer via the NativeAOT interop surface, but
that arrow points outward — BgMoveGen knows nothing about it.

## Directory tree

```
BgMoveGen.slnx
Directory.Packages.props
BgMoveGen/
  BgMoveGen.csproj
  MoveGenerator.cs       — public: GeneratePlays / IsLegalPlay / ApplyPlay.
                           Internal: GenerateStates / EnumerateStates
                           (RL-successor wrappers, own-tests-only) /
                           NextMove / SingleMoves (Span and List overloads) /
                           GenerateDoubles / GenerateNonDoubles /
                           Reference_GeneratePlays
  MoveNotationFormatter.cs — Play → standard notation ("8/5(2)", "24/18*")
  MoveEntryState.cs      — stateful one-click Play assembly;
                           ClickOutcome enum (Illegal / MoveCommitted /
                           PlayCompleted)
  Interop.cs             — internal NativeAOT export surface (whole class
                           is internal) + blittable BgBoardState
BgMoveGen.Tests/
  BgMoveGen.Tests.csproj
  MoveGeneratorTests.cs
  MoveNotationFormatterTests.cs
  MoveEntryStateTests.cs
  InteropTests.cs
BgMoveGen.Benchmarks/
  BgMoveGen.Benchmarks.csproj
  Program.cs                  — BenchmarkSwitcher entry point
  MoveGenerationBenchmarks.cs — GeneratePlays across the four play-assembly
                                shapes, plus the load canary; see Benchmarks
                                below
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
  `smallDie` second. Two duplicate shapes are skipped. *Same-checker* plays
  where (a) both intermediates are on-board, (b) the smallDie intermediate
  is unblocked, and (c) neither intermediate has an opponent blot — those
  are exact duplicates of Pass 1. *Same-source-point* plays (the smallDie
  move leaves the same point the bigDie move just left — including two bar
  entries) — Pass 1 already emitted that pair in the other order, and the
  orders are always interchangeable: the destinations differ, so neither
  move affects the other's legality.

Two-checker plays from *different* points are never duplicated because the
`FrPt` ordering constraint is symmetric — the same pair appears the same way
in both passes. Both passes enforce must-use-both-dice and
must-use-larger-die.

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
around `BoardState.ApplyPlay`: it re-runs `GeneratePlays` and matches the
input against the legal set by canonical `Play` equality (order- and
decomposition-insensitive, hit-sensitive — see BgDataTypes_Lib's
`CanonicalPlay`), throwing `ArgumentException` on mismatch. On a match it
applies the **generator's encoding of the matched play, not the caller's
move sequence** — canonical equality deliberately ignores which intermediate
points a trajectory touches, so a caller's decomposition of a legal play may
route through a blocked point or an unacknowledged blot; the generator's
encoding is mechanically sound by construction and reaches the identical
final state. The contract is **throw-before-mutate**: on an illegal play,
`state` is left byte-for-byte unchanged so callers can recover without a
defensive clone.

`MoveGenerator.IsLegalPlay(state, play, die1, die2)` is the standalone
predicate over the same match rule (a shared `TryFindLegal` helper); both
are the simple-correct re-enumeration implementation. Not hot-path —
callers running tight loops should drive `GeneratePlays` directly. The
unvalidated turn-boundary primitive (`state.ApplyPlay(play)`) remains
available for callers that have already proven legality.

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
same final state therefore yield a `CompletedPlay` equal under `Play.Equals`
(so quiz scoring and `allPlays.Contains` match regardless of path); paths
that reach genuinely different states (one hits an intermediate blot, the
other doesn't) stay distinct.

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

`Interop` is an `internal static` class; `BgBoardState` is an `internal`
nested struct of it (`Interop.BgBoardState`), existing only to marshal
across the native boundary and distinct from `BgDataTypes_Lib.BoardState`.
The marshaller (`FromExternal` / `ToExternalFlipped` / `ToExternal`)
translates between the two. The `[UnmanagedCallersOnly]` exports
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
  closed-out empty-pass case, hit-sensitive rejection of mis-encoded hits,
  decomposed-encoding acceptance, canonicalize-then-apply, candidate-list
  canonical distinctness); performance benchmarks; interop (successor
  count, flip correctness, off-count tracking, checker conservation, pass
  detection, Bg960 conservation and seed reproducibility); MoveEntryState
  click-by-click assembly.

### Benchmarks

`BgMoveGen.Benchmarks` is a BenchmarkDotNet harness over the public
`GeneratePlays` entry point — the surface BgRLEngine drives through interop,
and the one whose cost matters. Four cases cover the distinct play-assembly
shapes; the fifth row is not a generator measurement at all:

| Benchmark | Exercises |
|---|---|
| `DoublesFullDepth` | 3-3 from the opening — every branch reaches depth four |
| `DoublesPartialDepth` | 4-4 onto a five-point board with two on the bar — the reduced-depth fallbacks |
| `NonDoubles` | 6-4 from the opening — the two-pass avoidance-dedup path |
| `AllOpeningRolls` | all 21 rolls from the opening — the aggregate signal |
| `SentinelNotationFormat` | notation formatting of a fixed play set — the load canary, not a generator path |

`[MemoryDiagnoser]` is on because allocation, not nanoseconds, is the property
this generator is designed around: the documented invariant is zero allocation
in the recursion, with only the result `List<Play>` and its backing array on
the heap.

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

Run it in Release. `Program.cs` uses `BenchmarkSwitcher`, which *requires* a
selection — without `--filter` it stops and prompts, so the bare command hangs
a non-interactive shell:

```
dotnet run -c Release --project BgMoveGen.Benchmarks -- --filter '*'
```

Run it through the **project**, not the solution. `dotnet build BgMoveGen.slnx
-c Release` does not propagate the configuration across the out-of-solution
`ProjectReference`: it builds BgDataTypes_Lib in *Debug* and copies that
unoptimized assembly into the Release output, so a run staged that way
measures the dependency's Debug codegen. The `--project` form above
propagates Release correctly. When a measurement turns on BgDataTypes_Lib
codegen — `Play` construction does — check the copied
`BgDataTypes_Lib.dll` in the benchmark's output against the one under
`BgDataTypes_Lib/BgDataTypes_Lib/bin/Release/net10.0/` before believing
the numbers; if it matches `bin/Debug/` instead, the run is void.

Excluded from `dotnet test` via `IsTestProject=false` in its csproj — a run
takes minutes and asserts nothing, so it is measured on demand, never as part
of the suite.

## Public API

### Managed — `MoveGenerator`

```csharp
// Full play enumeration — for clients that need to animate or record moves.
List<Play> plays = MoveGenerator.GeneratePlays(state, die1, die2);

// Validating turn-boundary primitives.
bool legal = MoveGenerator.IsLegalPlay(state, play, die1, die2);
MoveGenerator.ApplyPlay(state, play, die1, die2);   // throws on illegal play
```

`GeneratePlays` enforces must-use-both-dice and must-use-larger-die. A
pass is represented as a single successor identical to the input board
(flipped by the interop layer). The successor-state wrappers
`GenerateStates` (materialized list, for RL evaluation) and
`EnumerateStates` (lazy iterator, for early termination) delegate to it
and inherit that behavior; both are `internal` (own-tests-only, no
external consumer) and would be widened to `public` if one appears.

`IsLegalPlay` matches by canonical `Play` equality — order- and
decomposition-insensitive, hit-sensitive. `ApplyPlay` is the validating
wrapper around `BoardState.ApplyPlay`; on a match it applies the generator's
encoding of the matched play (see Architecture); on rejection, the input
state is unchanged. The unvalidated form (`state.ApplyPlay(play)`) remains
available.

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

Renders from the play's canonical chain form (`Play.ToCanonical()`):
BgDataTypes_Lib's `CanonicalPlay` owns the chain-collapse semantics — hop
fusing, hit-visibility splitting, order/decomposition insensitivity,
deterministic chain ordering. The formatter owns display only: bar entry
(`FrPt == 25` → "bar"), bear off (`ToPt == 0` → "off"), hits (`ToPt < 0` →
"*" suffix), and duplicate-chain grouping — adjacent chains sharing
`(from, |to|)` collapse to "(n)", with the "*" following the count
("(n)*") and applied if **any** constituent chain hit.

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

- **No-legal-move returns a pass, not an empty list.** For a dance /
  closed-out position, `GeneratePlays` returns a one-element list holding the
  empty pass `Play` (`Count == 0`) — never `Count == 0` on the list itself.
  `GenerateStates` / `EnumerateStates` inherit this (one successor: the
  unchanged input). Consumers must handle a single "pass" candidate, not an
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
- **`EnumerateStates` yields fresh copies, not a shared buffer.** Every
  yielded `BoardState` is an independent `Copy()` of the input; consumers
  are free to retain or discard without further cloning.
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
  average. Sequential "measure, edit, measure" with nothing watching the
  machine remains not a valid comparison here.
- **`IsLegalPlay` and `ApplyPlay` are not hot-path.** Both re-enumerate
  via `GeneratePlays`. Acceptable for turn-boundary validation; for
  inner-loop repeated checks, drive the generator directly.
- **`MoveEntryState` legality is state-based, not move-list-based.** Do not
  "fix" entry by making `GeneratePlays` emit both die orderings of a combined
  move — the distinct-outcome dedup is correct and RL state enumeration,
  equity, and quiz `Play`-equality scoring all depend on it. Entry instead
  accepts any click that is a legal single move from the current state *and*
  keeps a generated final state reachable, then canonicalizes the completed
  `Play` by resulting board state. A click can be legal even though its move
  appears in no emitted play (e.g. `8/5` then `5/4`, since `8/4` is emitted as
  `8/7/4`). See the MoveEntryState architecture section.
- **`Play` equivalence is notation-level: decomposition-insensitive but
  hit-sensitive.** `IsLegalPlay` matches by canonical `Play` equality
  (BgDataTypes_Lib's `CanonicalPlay`): `{13/10, 10/8}` equals `{13/8}` —
  intermediate touch-down points are not part of a play's identity — but
  a hit-less `Move(24, 18)` is *not* the hitting `Move(24, -18)`, so a
  mis-encoded hit is rejected rather than validated (the old hit-blind
  key let it apply without barring the blot — silent board corruption).
  The flip side of intermediate-insensitivity: a caller's decomposition
  may name a blocked or blot-occupied point yet still be the legal play,
  which is why `ApplyPlay` applies the matched candidate's encoding, never
  the caller's hops.

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
