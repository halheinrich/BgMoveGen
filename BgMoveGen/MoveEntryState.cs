using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// Result of a one-click action against <see cref="MoveEntryState.TryAdvanceFrom"/>,
/// <see cref="MoveEntryState.TryBearOffMax"/>, or <see cref="MoveEntryState.TryMakePoint"/>.
/// </summary>
public enum ClickOutcome
{
    /// <summary>Click was rejected; state unchanged.</summary>
    Illegal,

    /// <summary>A complete legal move was applied. More moves remain in the play.</summary>
    MoveCommitted,

    /// <summary>The last move of the play was applied; <see cref="MoveEntryState.IsComplete"/> is now true.</summary>
    PlayCompleted,
}

/// <summary>
/// Stateful click-by-click assembly of a <see cref="Play"/> from a starting position
/// and dice. Anchored on <see cref="MoveGenerator.GeneratePlays"/> as the canonical
/// reference for legality.
///
/// Click semantics: one-click. <see cref="TryAdvanceFrom"/> advances the checker on a
/// clicked <i>source</i> point by one legal move (the caller's <c>diePreference</c>
/// resolves which die when both could play); <see cref="TryMakePoint"/> handles a click
/// on a <i>destination</i> point (an empty point or an opponent blot — never an
/// own-occupied point, which is always a source) by making it: landing two own checkers
/// there with the fewest sub-moves, hitting any blot automatically; <see cref="TryBearOffMax"/>
/// handles a tray click by bearing off the maximum number of checkers when a unique
/// completion achieves it. Own-occupied points are <see cref="TryAdvanceFrom"/> sources
/// and never <see cref="TryMakePoint"/> destinations, so a consumer can dispatch
/// advance-else-make by the click target alone without inspecting the board.
///
/// Click point conventions match BgDiag_Razor's event surface:
///   • 1..24  — regular board points (a source for <see cref="TryAdvanceFrom"/>,
///              a destination for <see cref="TryMakePoint"/>)
///   • 25     — player bar (advancing it = entering)
///   • 0      — bear-off tray (handled by <see cref="TryBearOffMax"/>)
///
/// <see cref="Move.ToPt"/>'s sign encoding (negative = hit, zero = bear off,
/// positive = regular) is hidden from consumers — clicks use positive point indices.
///
/// Ordering ambiguity is resolved by board <i>state</i>, not by literal move-lists.
/// <see cref="MoveGenerator.GeneratePlays"/> board-state-dedups equivalent die orderings
/// of a combined single-checker move (e.g. with a non-double 5-1, <c>11/5</c> emitted
/// only as <c>11→10→5</c>, never the equally-legal <c>11→6→5</c>) and likewise collapses
/// doubles permutations. So per-click legality is <b>not</b> anchored on the emitted
/// move-lists. A click is accepted iff (a) it is a legal single move from the current
/// intermediate state, <b>and</b> (b) after it, the position can still complete — using
/// the dice still to be played — to one of the final board states
/// <see cref="MoveGenerator.GeneratePlays"/> produced. When the play completes, the
/// resulting board state identifies a unique generated play (the generator dedups by
/// final state), and <see cref="CompletedPlay"/> is set to <i>that</i> canonical play.
/// Two different intermediate paths to the same final state therefore yield a
/// <see cref="CompletedPlay"/> that compares equal under <see cref="Play.Equals(Play)"/> /
/// <see cref="Play.DeduplicationKey"/>; paths that reach genuinely different states
/// (e.g. one hits an intermediate blot, the other does not) stay distinct.
///
/// Pass positions (no legal play): <see cref="IsComplete"/> is true at construction
/// and <see cref="CompletedPlay"/> is the empty <see cref="Play"/>.
/// </summary>
public sealed class MoveEntryState
{
    private readonly int[] _initialPoints = new int[26];
    private readonly int _initialHighPoint;
    private readonly int _die1, _die2;
    private readonly List<Play> _allPlays;
    private readonly int _maxMoveCount;

    /// <summary>The dice to be played this turn, length <see cref="_maxMoveCount"/>.</summary>
    private readonly List<int> _turnDice;

    /// <summary>Final-board-state signature → the canonical generated play reaching it.</summary>
    private readonly Dictionary<long, Play> _targetBySignature;

    private readonly BoardState _currentState;
    private readonly List<Move> _appliedMoves = new(4);
    /// <summary>Die consumed by each applied move (parallel to <see cref="_appliedMoves"/>).</summary>
    private readonly List<int> _appliedDice = new(4);
    /// <summary>Dice not yet consumed by an applied move.</summary>
    private readonly List<int> _remainingDice = new(4);
    private HashSet<int> _legalNextClicks = [];
    private Play? _completedPlay;

    /// <summary>
    /// Construct from an initial board state and the two dice rolled.
    /// The initial state is captured by deep copy — subsequent mutations of the
    /// argument do not affect this instance.
    /// </summary>
    public MoveEntryState(BoardState initialState, int die1, int die2)
    {
        ArgumentNullException.ThrowIfNull(initialState);

        _currentState = initialState.Copy();
        Array.Copy(_currentState.Points, _initialPoints, 26);
        _initialHighPoint = _currentState.HighPointOccupied;

        _die1 = die1;
        _die2 = die2;
        _allPlays = MoveGenerator.GeneratePlays(_currentState, die1, die2);
        _maxMoveCount = _allPlays[0].Count;

        _targetBySignature = BuildTargetIndex();
        _turnDice = BuildTurnDice();
        _remainingDice.AddRange(_turnDice);

        if (IsComplete)
            _completedPlay = CanonicalCompletedPlay();

        RecomputeLegalNextClicks();
    }

    public int Die1 => _die1;
    public int Die2 => _die2;

    /// <summary>
    /// The intermediate board after all clicks committed so far.
    /// Internal mutable state — consumers must not modify it.
    /// </summary>
    public BoardState Current => _currentState;

    /// <summary>
    /// Points the user can usefully click next: the set of points that have a legal
    /// advancing move now (the valid arguments to <see cref="TryAdvanceFrom"/>).
    /// </summary>
    public IReadOnlyCollection<int> LegalNextClicks => _legalNextClicks;

    /// <summary>True iff a full play has been assembled.</summary>
    public bool IsComplete => _appliedMoves.Count == _maxMoveCount;

    /// <summary>The completed <see cref="Play"/>, or null while still in progress.</summary>
    public Play? CompletedPlay => _completedPlay;

    /// <summary>Moves applied so far, in the order the user clicked them.</summary>
    public IReadOnlyList<Move> AppliedMoves => _appliedMoves;

    // ── Click handling ────────────────────────────────────────────

    /// <summary>
    /// One-click source-advance: commit the single legal move that advances the
    /// checker on <paramref name="point"/>, choosing which die to consume by
    /// <paramref name="diePreference"/>.
    ///
    /// Among the legal next single moves whose <see cref="Move.FrPt"/> is
    /// <paramref name="point"/> (as offered by <see cref="ComputeLegalNextSingleMoves"/>
    /// — already filtered to legal, target-reachable, remaining-die-only), the one
    /// whose consumed die ranks earliest in <paramref name="diePreference"/> is
    /// committed. A die not present in <paramref name="diePreference"/> ranks last.
    /// Returns <see cref="ClickOutcome.PlayCompleted"/> if the commit finishes the
    /// play, otherwise <see cref="ClickOutcome.MoveCommitted"/>;
    /// <see cref="ClickOutcome.Illegal"/> (no state change) if
    /// <paramref name="point"/> has no legal advancing move now or the play is
    /// already complete.
    ///
    /// Legality is <i>not</i> re-derived here: candidates come solely from
    /// <see cref="ComputeLegalNextSingleMoves"/>, the single source of truth for
    /// "what can move next", and the commit goes through <see cref="CommitMove"/>
    /// so all bookkeeping stays single-sourced. The model stays die-order-agnostic
    /// — the caller decides which dice to prefer, in order. From a given point a
    /// die yields at most one legal single move, so the only disambiguation is
    /// which die, exactly what <paramref name="diePreference"/> resolves. Bar
    /// advance (<paramref name="point"/> == 25, i.e. entering) and bear-off (a home
    /// point whose advancing move has <see cref="Move.ToPt"/> == 0) need no
    /// special-casing — they are ordinary candidates.
    /// </summary>
    public ClickOutcome TryAdvanceFrom(int point, IReadOnlyList<int> diePreference)
    {
        ArgumentNullException.ThrowIfNull(diePreference);
        if (IsComplete) return ClickOutcome.Illegal;

        Move best = default;
        int bestDie = 0;
        int bestRank = int.MaxValue;
        bool found = false;

        foreach (var (m, die) in ComputeLegalNextSingleMoves())
        {
            if (m.FrPt != point) continue;
            int rank = DieRank(diePreference, die);
            if (!found || rank < bestRank)
            {
                best = m;
                bestDie = die;
                bestRank = rank;
                found = true;
            }
        }

        if (!found) return ClickOutcome.Illegal;

        CommitMove(best, bestDie);
        return IsComplete ? ClickOutcome.PlayCompleted : ClickOutcome.MoveCommitted;
    }

    /// <summary>Rank of <paramref name="die"/> in <paramref name="preference"/>
    /// (earliest = lowest); a die absent from the preference ranks last.</summary>
    private static int DieRank(IReadOnlyList<int> preference, int die)
    {
        for (int i = 0; i < preference.Count; i++)
            if (preference[i] == die) return i;
        return int.MaxValue;
    }

    /// <summary>
    /// Tray-click bear-off-max: complete the turn by bearing off the <i>maximum</i>
    /// number of checkers, but only when that maximum is achieved by a <b>unique</b>
    /// reachable completion.
    ///
    /// From the current entry state (honouring moves already applied and the dice
    /// still to play), the reachable complete plays are enumerated. Because every
    /// legal turn must use the maximum dice, "bear off the most" is measured over
    /// <i>complete</i> plays — there is no legal "bear off some and stop". A
    /// completion's bear-off count equals own checkers removed reaching its final
    /// state; equivalently, the reachable final state with the <i>fewest</i> own
    /// checkers left on board bears off the most. If exactly one reachable
    /// completion attains that minimum-on-board (and it bears off at least one
    /// checker), its moves are committed and the turn completes →
    /// <see cref="ClickOutcome.PlayCompleted"/>. Otherwise — a tie for the maximum
    /// (ambiguous; the user bears off via individual checker clicks), no checker
    /// can bear off, or the play is already complete — returns
    /// <see cref="ClickOutcome.Illegal"/> with no state change.
    ///
    /// Reuses the existing target machinery — <see cref="_targetBySignature"/> (the
    /// generated final states) and the same depth-first reachability search as
    /// <see cref="CanReachTarget"/> — to enumerate completions and recover a
    /// committable path. Play generation and legality are not re-implemented; the
    /// generator remains the single source of truth. Each step is applied through
    /// the existing <see cref="CommitMove"/>, so completion fires normally, the
    /// moves remain undo-able via <see cref="UndoLast"/> / <see cref="UndoAll"/>,
    /// and nothing auto-submits — the user still clicks Submit.
    /// </summary>
    public ClickOutcome TryBearOffMax()
    {
        if (IsComplete) return ClickOutcome.Illegal;

        // Enumerate reachable completions on scratch copies — _currentState and
        // _remainingDice are left untouched until we decide to commit.
        var work = _currentState.Copy();
        var remaining = new List<int>(_remainingDice);
        var completions = new Dictionary<long, (int onBoard, List<(Move move, int die)> path)>();
        CollectReachableCompletions(work, remaining, new List<(Move, int)>(), completions);

        if (completions.Count == 0) return ClickOutcome.Illegal; // no reachable completion

        int minOnBoard = int.MaxValue;
        foreach (var c in completions.Values)
            if (c.onBoard < minOnBoard) minOnBoard = c.onBoard;

        // Max bear-off = own checkers removed reaching the fewest-on-board final.
        if (OwnCheckersOnBoard(_currentState) - minOnBoard < 1) return ClickOutcome.Illegal;

        long winner = 0;
        bool haveWinner = false;
        foreach (var kv in completions)
        {
            if (kv.Value.onBoard != minOnBoard) continue;
            if (haveWinner) return ClickOutcome.Illegal; // tie at the maximum → ambiguous
            winner = kv.Key;
            haveWinner = true;
        }

        foreach (var (m, d) in completions[winner].path)
            CommitMove(m, d);

        return ClickOutcome.PlayCompleted; // path lands on a generated final state ⇒ IsComplete
    }

    /// <summary>
    /// One-click make-the-point: click a <i>destination</i> point (1..24) to land two
    /// own checkers on it, hitting any opponent blot there automatically.
    ///
    /// Rejected (<see cref="ClickOutcome.Illegal"/>, no state change) when
    /// <paramref name="point"/> is off the board (not 1..24), the play is already
    /// complete, or the point already holds an own checker (an own-occupied point is a
    /// <see cref="TryAdvanceFrom"/> source, never a make destination — letting a consumer
    /// dispatch advance-else-make by the click target alone).
    ///
    /// <b>Make</b> (attempted first): a minimal-depth search for the fewest single-die
    /// sub-moves that bring the point to two own checkers (it starts empty per the guard)
    /// while keeping the turn completable. A checker may travel several die-steps to reach
    /// the point (a combined path whose intermediate sub-moves need not land on it); the
    /// first arrival onto an opponent blot is generated as a hit. Among the minimal-depth
    /// solutions the distinct resulting board states are counted (deduped by
    /// <see cref="Signature"/>): exactly one distinct state ⇒ its path is committed
    /// (<see cref="ClickOutcome.PlayCompleted"/> if it finishes the play — e.g. a
    /// non-doubles make consuming both dice — otherwise <see cref="ClickOutcome.MoveCommitted"/>,
    /// with the unused dice left for further <see cref="TryAdvanceFrom"/> clicks); two or
    /// more distinct states are a genuine tie ⇒ <see cref="ClickOutcome.Illegal"/>, no
    /// state change. (Non-doubles force a unique two-source make; doubles can tie only at
    /// the four-sub-move depth, and only when both source configurations are legal.)
    /// Minimal depth also resolves any cross-depth choice — a two-sub-move make is taken
    /// over a three- or four-sub-move one, leaving the most dice for the user.
    ///
    /// <b>Land-one</b> (fallback, only if the point is unmakeable at every depth): the same
    /// minimal-depth search, now for a <i>single</i> own checker on <paramref name="point"/>.
    /// It prefers a direct single-die landing (depth 1) and, only when none exists, a
    /// combined multi-die path — one checker walking several die-steps to the point (e.g.
    /// <c>18/16 16/10*</c> when the direct routes are blocked). Empty point and opponent
    /// blot are symmetric; the arrival onto a blot is generated as a hit. A unique
    /// minimal-depth landing is committed; a tie (two distinct landing states) or none ⇒
    /// <see cref="ClickOutcome.Illegal"/>. Because make is tried before land-one, a 3-1
    /// click on an empty 5-point makes it (8/5 6/5) rather than landing a lone 8/5.
    ///
    /// There is no die-preference parameter: both make and land-one commit only a unique
    /// minimal-depth solution and otherwise no-op, so there is never a die to choose
    /// (unlike <see cref="TryAdvanceFrom"/>, where the caller picks which die a source
    /// advance consumes).
    ///
    /// Legality and reachability are not re-implemented: candidate single moves come from
    /// <see cref="MoveGenerator.SingleMoves"/>, "the turn can still complete" is the shared
    /// <see cref="CanReachTarget"/>, and every commit goes through <see cref="CommitMove"/>
    /// so canonicalization, undo, and the legal-click recompute stay single-sourced. Make
    /// and land-one are one routine — <see cref="CollectMinimalLandingPaths"/> at target
    /// count two then one. The <see cref="Move.ToPt"/> hit encoding stays hidden — the
    /// click is a positive point.
    /// </summary>
    public ClickOutcome TryMakePoint(int point)
    {
        if (point < 1 || point > 24) return ClickOutcome.Illegal;
        if (IsComplete) return ClickOutcome.Illegal;
        if (_currentState.Points[point] > 0) return ClickOutcome.Illegal; // own-occupied ⇒ a source

        // ── Make (two own checkers on the point), preferred ────────
        var makes = CollectMinimalLandings(point, 2);
        if (makes.Count >= 2) return ClickOutcome.Illegal; // genuine tie at the minimal depth
        var makePath = UniquePathOrNull(makes);
        if (makePath is not null) return CommitPath(makePath);

        // ── Land-one fallback (one own checker), only if unmakeable ─
        var lands = CollectMinimalLandings(point, 1);
        var landPath = UniquePathOrNull(lands);
        if (landPath is not null) return CommitPath(landPath);

        return ClickOutcome.Illegal; // a tie or no landing at all
    }

    /// <summary>
    /// Run <see cref="CollectMinimalLandingPaths"/> for <paramref name="targetCount"/> own
    /// checkers on <paramref name="point"/> against scratch copies of the current state and
    /// remaining dice (the live state is untouched until a caller commits). Returns the
    /// distinct minimal-depth landing paths, keyed by resulting-state signature.
    /// </summary>
    private Dictionary<long, List<(Move move, int die)>> CollectMinimalLandings(int point, int targetCount)
    {
        var work = _currentState.Copy();
        var remaining = new List<int>(_remainingDice);
        var into = new Dictionary<long, List<(Move move, int die)>>();
        int minDepth = int.MaxValue;
        CollectMinimalLandingPaths(work, remaining, point, targetCount, new List<(Move, int)>(), into, ref minDepth);
        return into;
    }

    /// <summary>The single path when exactly one distinct landing was found, else null.</summary>
    private static List<(Move move, int die)>? UniquePathOrNull(
        Dictionary<long, List<(Move move, int die)>> paths)
    {
        if (paths.Count != 1) return null;
        foreach (var p in paths.Values) return p;
        return null; // unreachable: Count == 1
    }

    /// <summary>Commit each step of <paramref name="path"/> through <see cref="CommitMove"/>,
    /// returning the outcome by whether the play is now complete.</summary>
    private ClickOutcome CommitPath(List<(Move move, int die)> path)
    {
        foreach (var (m, d) in path) CommitMove(m, d);
        return IsComplete ? ClickOutcome.PlayCompleted : ClickOutcome.MoveCommitted;
    }

    /// <summary>
    /// Depth-first search for the paths of <i>minimal</i> length that land
    /// <paramref name="targetCount"/> own checkers on <paramref name="point"/> (which holds
    /// no own checker at entry per the caller's guard) and keep a generated final state
    /// reachable with the dice that remain. Each die-step is a legal single move over
    /// <paramref name="remaining"/>; a branch stops as soon as the point reaches
    /// <paramref name="targetCount"/> (a deeper landing is not minimal). The make path
    /// (<paramref name="targetCount"/> == 2) and the land-one fallback
    /// (<paramref name="targetCount"/> == 1) are the same search at two targets — a
    /// checker may walk several die-steps to the point (a combined path whose intermediate
    /// sub-moves need not land on it), and the arrival onto an opponent blot is generated
    /// as a hit. On finding a landing at depth <c>path.Count</c>: a shorter depth supersedes
    /// (clears prior, lowering <paramref name="minDepth"/>); an equal depth is kept, deduped
    /// by resulting-state <see cref="Signature"/> into <paramref name="into"/> — so its
    /// final count is the number of genuinely distinct minimal landings. Mutates
    /// <paramref name="state"/> / <paramref name="remaining"/> / <paramref name="path"/>
    /// transiently but restores all three before returning.
    /// </summary>
    private void CollectMinimalLandingPaths(
        BoardState state, List<int> remaining, int point, int targetCount,
        List<(Move move, int die)> path,
        Dictionary<long, List<(Move move, int die)>> into,
        ref int minDepth)
    {
        // No deeper search can beat or tie a landing already found at this depth.
        if (path.Count >= minDepth) return;

        var tried = new HashSet<int>();
        Span<Move> buffer = stackalloc Move[30];

        for (int idx = 0; idx < remaining.Count; idx++)
        {
            int d = remaining[idx];
            if (!tried.Add(d)) continue;

            remaining.RemoveAt(idx);
            int count = MoveGenerator.SingleMoves(state, d, buffer);
            for (int i = 0; i < count; i++)
            {
                var m = buffer[i];
                state.ApplyMove(m);
                path.Add((m, d));

                if (state.Points[point] == targetCount)
                {
                    // Target reached. A valid landing iff the turn can still complete with
                    // the dice now left; record it, then stop — descending is not minimal.
                    if (CanReachTarget(state, remaining))
                    {
                        int depth = path.Count;
                        long sig = Signature(state);
                        if (depth < minDepth)
                        {
                            minDepth = depth;
                            into.Clear();
                            into[sig] = new List<(Move, int)>(path);
                        }
                        else if (depth == minDepth && !into.ContainsKey(sig))
                        {
                            into[sig] = new List<(Move, int)>(path);
                        }
                    }
                }
                else
                {
                    CollectMinimalLandingPaths(state, remaining, point, targetCount, path, into, ref minDepth);
                }

                path.RemoveAt(path.Count - 1);
                state.UndoMove(m);
            }
            remaining.Insert(idx, d);
        }
    }

    /// <summary>
    /// Depth-first walk of every ordering of <paramref name="remaining"/> dice played
    /// as legal single moves from <paramref name="state"/>. Each ordering that consumes
    /// all dice and lands on a generated final state records — keyed by that state's
    /// signature, first path wins — the own checkers left on board and a committable
    /// (move, die) path. Mutates <paramref name="state"/> / <paramref name="remaining"/>
    /// / <paramref name="path"/> transiently but restores all three before returning.
    /// </summary>
    private void CollectReachableCompletions(
        BoardState state, List<int> remaining,
        List<(Move move, int die)> path,
        Dictionary<long, (int onBoard, List<(Move move, int die)> path)> into)
    {
        if (remaining.Count == 0)
        {
            long sig = Signature(state);
            if (_targetBySignature.ContainsKey(sig) && !into.ContainsKey(sig))
                into[sig] = (OwnCheckersOnBoard(state), new List<(Move, int)>(path));
            return;
        }

        var tried = new HashSet<int>();
        Span<Move> buffer = stackalloc Move[30];

        for (int idx = 0; idx < remaining.Count; idx++)
        {
            int d = remaining[idx];
            if (!tried.Add(d)) continue;

            remaining.RemoveAt(idx);
            int count = MoveGenerator.SingleMoves(state, d, buffer);
            for (int i = 0; i < count; i++)
            {
                var m = buffer[i];
                state.ApplyMove(m);
                path.Add((m, d));
                CollectReachableCompletions(state, remaining, path, into);
                path.RemoveAt(path.Count - 1);
                state.UndoMove(m);
            }
            remaining.Insert(idx, d);
        }
    }

    /// <summary>On-roll checkers still in play: own (positive) checkers on the
    /// playing surface and the on-roll bar (points 1..25). Decreases by one per
    /// bear-off and by nothing else (own checkers are never hit), so the reachable
    /// final state minimizing this maximizes checkers borne off this turn.</summary>
    private static int OwnCheckersOnBoard(BoardState s)
    {
        int total = 0;
        for (int i = 1; i <= 25; i++)
            if (s.Points[i] > 0) total += s.Points[i];
        return total;
    }

    /// <summary>
    /// Undo the last committed move. No-op if none have been applied.
    /// </summary>
    public void UndoLast()
    {
        if (_appliedMoves.Count == 0) return;

        var last = _appliedMoves[^1];
        int lastDie = _appliedDice[^1];
        _appliedMoves.RemoveAt(_appliedMoves.Count - 1);
        _appliedDice.RemoveAt(_appliedDice.Count - 1);
        _currentState.UndoMove(last);
        _remainingDice.Add(lastDie);
        _completedPlay = null;
        RecomputeLegalNextClicks();
    }

    /// <summary>
    /// Restore the initial state regardless of how many moves have been applied.
    /// </summary>
    public void UndoAll()
    {
        Array.Copy(_initialPoints, _currentState.Points, 26);
        _currentState.HighPointOccupied = _initialHighPoint;
        _appliedMoves.Clear();
        _appliedDice.Clear();
        _remainingDice.Clear();
        _remainingDice.AddRange(_turnDice);
        _completedPlay = IsComplete ? CanonicalCompletedPlay() : null;
        RecomputeLegalNextClicks();
    }

    // ── Internals ─────────────────────────────────────────────────

    private void CommitMove(Move m, int die)
    {
        _currentState.ApplyMove(m);
        _appliedMoves.Add(m);
        _appliedDice.Add(die);
        _remainingDice.Remove(die);
        if (IsComplete) _completedPlay = CanonicalCompletedPlay();
        RecomputeLegalNextClicks();
    }

    private void RecomputeLegalNextClicks()
    {
        var clicks = new HashSet<int>();
        if (!IsComplete)
        {
            foreach (var (m, _) in ComputeLegalNextSingleMoves()) clicks.Add(m.FrPt);
        }
        _legalNextClicks = clicks;
    }

    /// <summary>
    /// Legal next single moves from the current intermediate state: each move
    /// that (a) is a legal single move using one of the dice still to be played,
    /// and (b) keeps at least one generated final state reachable with the dice
    /// that remain after it. Each move is paired with the die it consumes.
    /// </summary>
    private List<(Move move, int die)> ComputeLegalNextSingleMoves()
    {
        var result = new List<(Move, int)>();
        if (IsComplete) return result;

        var added = new HashSet<Move>();
        // Snapshot distinct die values up front: the loop body mutates _remainingDice.
        var distinctDice = new HashSet<int>(_remainingDice);
        Span<Move> buffer = stackalloc Move[30];

        foreach (int d in distinctDice)
        {
            int count = MoveGenerator.SingleMoves(_currentState, d, buffer);
            for (int i = 0; i < count; i++)
            {
                var m = buffer[i];
                if (added.Contains(m)) continue;

                if (MoveKeepsTargetReachable(_currentState, _remainingDice, m, d))
                {
                    result.Add((m, d));
                    added.Add(m);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// True iff playing <paramref name="m"/> (consuming <paramref name="die"/>) from
    /// <paramref name="state"/> leaves a generated final state reachable with the dice
    /// still in <paramref name="remaining"/>. Applies the move, defers to the shared
    /// <see cref="CanReachTarget"/> over the dice that remain, then undoes — leaving both
    /// <paramref name="state"/> and <paramref name="remaining"/> unchanged. The single
    /// source for the "legal single move that keeps the turn completable" predicate.
    /// </summary>
    private bool MoveKeepsTargetReachable(BoardState state, List<int> remaining, Move m, int die)
    {
        state.ApplyMove(m);
        remaining.Remove(die);
        bool canComplete = CanReachTarget(state, remaining);
        remaining.Add(die);
        state.UndoMove(m);
        return canComplete;
    }

    /// <summary>
    /// True iff some ordering of <paramref name="remaining"/> dice, played as legal
    /// single moves from <paramref name="state"/>, reaches one of the generated final
    /// board states. Mutates <paramref name="state"/> and <paramref name="remaining"/>
    /// transiently but restores both before returning.
    /// </summary>
    private bool CanReachTarget(BoardState state, List<int> remaining)
    {
        if (remaining.Count == 0)
            return _targetBySignature.ContainsKey(Signature(state));

        var tried = new HashSet<int>();
        Span<Move> buffer = stackalloc Move[30];

        for (int idx = 0; idx < remaining.Count; idx++)
        {
            int d = remaining[idx];
            if (!tried.Add(d)) continue;

            remaining.RemoveAt(idx);
            int count = MoveGenerator.SingleMoves(state, d, buffer);
            bool found = false;
            for (int i = 0; i < count && !found; i++)
            {
                state.ApplyMove(buffer[i]);
                if (CanReachTarget(state, remaining)) found = true;
                state.UndoMove(buffer[i]);
            }
            remaining.Insert(idx, d);

            if (found) return true;
        }
        return false;
    }

    /// <summary>
    /// Index each generated play by the signature of the board state it produces.
    /// The generator dedups by final state, so signatures are distinct.
    /// </summary>
    private Dictionary<long, Play> BuildTargetIndex()
    {
        var map = new Dictionary<long, Play>(_allPlays.Count);
        foreach (var p in _allPlays)
        {
            for (int i = 0; i < p.Count; i++) _currentState.ApplyMove(p[i]);
            map[Signature(_currentState)] = p;
            for (int i = p.Count - 1; i >= 0; i--) _currentState.UndoMove(p[i]);
        }
        return map;
    }

    /// <summary>
    /// The multiset of dice played this turn, of length <see cref="_maxMoveCount"/>.
    /// Doubles: the die value repeated. Non-doubles using both: both dice. Non-doubles
    /// using one (the must-use-larger / only-one-playable case): the single die that
    /// actually reaches a generated final state.
    /// </summary>
    private List<int> BuildTurnDice()
    {
        var dice = new List<int>(4);
        if (_maxMoveCount == 0) return dice; // pass

        if (_die1 == _die2)
        {
            for (int i = 0; i < _maxMoveCount; i++) dice.Add(_die1);
            return dice;
        }

        if (_maxMoveCount >= 2)
        {
            dice.Add(Math.Min(_die1, _die2));
            dice.Add(Math.Max(_die1, _die2));
            return dice;
        }

        // Non-doubles, exactly one die playable.
        int big = Math.Max(_die1, _die2);
        int small = Math.Min(_die1, _die2);
        dice.Add(SingleDieReachesTarget(big) ? big : small);
        return dice;
    }

    /// <summary>True iff a single legal move with <paramref name="die"/> from the
    /// initial state lands on a generated final state. Called only at construction,
    /// before any move is applied.</summary>
    private bool SingleDieReachesTarget(int die)
    {
        Span<Move> buffer = stackalloc Move[30];
        int count = MoveGenerator.SingleMoves(_currentState, die, buffer);
        for (int i = 0; i < count; i++)
        {
            _currentState.ApplyMove(buffer[i]);
            bool hit = _targetBySignature.ContainsKey(Signature(_currentState));
            _currentState.UndoMove(buffer[i]);
            if (hit) return true;
        }
        return false;
    }

    /// <summary>
    /// The canonical generated play matching the current (completed) board state.
    /// Falls back to a literal snapshot of the applied moves if no match is found —
    /// that should not happen and indicates a generation/entry contract mismatch.
    /// </summary>
    private Play CanonicalCompletedPlay()
    {
        if (_targetBySignature.TryGetValue(Signature(_currentState), out var play))
            return play;
        return SnapshotAppliedAsPlay();
    }

    private Play SnapshotAppliedAsPlay()
    {
        var p = new Play();
        foreach (var m in _appliedMoves) p.Add(m);
        return p;
    }

    /// <summary>FNV-1a signature over the 26 board points — matches the generator's
    /// board-state dedup hash, so equal positions get equal signatures.</summary>
    private static long Signature(BoardState s)
    {
        long hash = unchecked((long)0xcbf29ce484222325);
        for (int i = 0; i < 26; i++)
        {
            hash ^= s.Points[i];
            hash = unchecked(hash * 0x100000001b3);
        }
        return hash;
    }
}
