namespace BgMoveGen;

/// <summary>
/// Result of a single click against <see cref="MoveEntryState.TryAddClick"/>.
/// </summary>
public enum ClickOutcome
{
    /// <summary>Click was rejected; state unchanged.</summary>
    Illegal,

    /// <summary>A source point is now picked (or an existing pick was replaced).</summary>
    SourceSelected,

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
/// Click semantics: alternating source / destination ("two-click"). Each call to
/// <see cref="TryAddClick"/> either picks a source point or attempts to commit a move
/// from the previously picked source. Clicking a different legal source while one is
/// already selected replaces the selection.
///
/// Click point conventions match BgDiag_Razor's existing event surface:
///   • 1..24  — regular board points
///   • 25     — player bar (legal source if player has a bar checker)
///   • 0      — bear-off tray (legal destination only)
///
/// <see cref="Move.ToPt"/>'s sign encoding (negative = hit, zero = bear off,
/// positive = regular) is hidden from consumers — clicks use positive point indices.
///
/// Doubles ordering ambiguity (e.g., 8/4(2) reachable via 8→4 then 4→0, or any chain
/// permutation) is resolved canonically: the multiset of moves committed over the
/// course of the play must be a multiset-subset of some play returned by
/// <see cref="MoveGenerator.GeneratePlays"/>, and the user's click sequence must be
/// a topological ordering of that multiset (chain dependencies are enforced by the
/// per-click single-move legality check against the current intermediate state).
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

    private readonly BoardState _currentState;
    private readonly List<Move> _appliedMoves = new(4);
    private List<Play> _candidatePlays;
    private int? _selectedSource;
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
        _candidatePlays = [.. _allPlays];

        if (IsComplete)
            _completedPlay = SnapshotAppliedAsPlay();

        RecomputeLegalNextClicks();
    }

    public int Die1 => _die1;
    public int Die2 => _die2;

    /// <summary>
    /// The intermediate board after all clicks committed so far.
    /// Internal mutable state — consumers must not modify it.
    /// </summary>
    public BoardState Current => _currentState;

    /// <summary>The currently selected source point, or null if awaiting a source click.</summary>
    public int? SelectedSource => _selectedSource;

    /// <summary>
    /// Points the user can usefully click next. When <see cref="SelectedSource"/> is
    /// null, this is the set of legal source points; when a source is selected, it
    /// is the set of legal destination points (with 0 representing bear-off).
    /// </summary>
    public IReadOnlyCollection<int> LegalNextClicks => _legalNextClicks;

    /// <summary>True iff a full play has been assembled.</summary>
    public bool IsComplete => _appliedMoves.Count == _maxMoveCount;

    /// <summary>The completed <see cref="Play"/>, or null while still in progress.</summary>
    public Play? CompletedPlay => _completedPlay;

    /// <summary>Moves applied so far, in the order the user clicked them.</summary>
    public IReadOnlyList<Move> AppliedMoves => _appliedMoves;

    // ── Click handling ────────────────────────────────────────────

    public ClickOutcome TryAddClick(int point)
    {
        if (IsComplete) return ClickOutcome.Illegal;

        var legalMoves = ComputeLegalNextSingleMoves();

        if (_selectedSource is int s)
        {
            // Awaiting destination: try to interpret `point` as a destination from s.
            foreach (var m in legalMoves)
            {
                if (m.FrPt != s) continue;
                int destClick = DestClickPoint(m);
                if (destClick == point)
                {
                    CommitMove(m);
                    return IsComplete ? ClickOutcome.PlayCompleted : ClickOutcome.MoveCommitted;
                }
            }
            // Not a legal destination — fall through to source-replacement check.
        }

        // Awaiting source (or replacing selection): is `point` a legal source for any next move?
        foreach (var m in legalMoves)
        {
            if (m.FrPt == point)
            {
                _selectedSource = point;
                RecomputeLegalNextClicks();
                return ClickOutcome.SourceSelected;
            }
        }

        return ClickOutcome.Illegal;
    }

    /// <summary>
    /// Roll back the most recent change. If a source is selected but no move is
    /// pending commit, clears the selection. Otherwise undoes the last committed
    /// move. No-op if neither holds.
    /// </summary>
    public void UndoLast()
    {
        if (_selectedSource is not null)
        {
            _selectedSource = null;
            RecomputeLegalNextClicks();
            return;
        }

        if (_appliedMoves.Count == 0) return;

        var last = _appliedMoves[^1];
        _appliedMoves.RemoveAt(_appliedMoves.Count - 1);
        MoveGenerator.UndoMove(_currentState, last);
        _completedPlay = null;
        RebuildCandidatePlays();
        RecomputeLegalNextClicks();
    }

    /// <summary>
    /// Restore the initial state regardless of how many moves have been applied.
    /// Clears any source selection.
    /// </summary>
    public void UndoAll()
    {
        Array.Copy(_initialPoints, _currentState.Points, 26);
        _currentState.HighPointOccupied = _initialHighPoint;
        _appliedMoves.Clear();
        _selectedSource = null;
        _completedPlay = null;
        _candidatePlays = [.. _allPlays];
        RecomputeLegalNextClicks();
    }

    // ── Internals ─────────────────────────────────────────────────

    private void CommitMove(Move m)
    {
        MoveGenerator.ApplyMove(_currentState, m);
        _appliedMoves.Add(m);
        _selectedSource = null;
        RebuildCandidatePlays();
        if (IsComplete) _completedPlay = SnapshotAppliedAsPlay();
        RecomputeLegalNextClicks();
    }

    private void RebuildCandidatePlays()
    {
        var next = new List<Play>(_allPlays.Count);
        foreach (var p in _allPlays)
            if (IsMultisetSubset(_appliedMoves, p))
                next.Add(p);
        _candidatePlays = next;
    }

    private void RecomputeLegalNextClicks()
    {
        var clicks = new HashSet<int>();
        if (!IsComplete)
        {
            var legalMoves = ComputeLegalNextSingleMoves();
            if (_selectedSource is int s)
            {
                foreach (var m in legalMoves)
                    if (m.FrPt == s) clicks.Add(DestClickPoint(m));
            }
            else
            {
                foreach (var m in legalMoves) clicks.Add(m.FrPt);
            }
        }
        _legalNextClicks = clicks;
    }

    /// <summary>
    /// Legal next single moves: across all surviving candidate plays, the moves
    /// in their as-yet-unconsumed remainder that are legal as a single move from
    /// the current intermediate state.
    /// </summary>
    private List<Move> ComputeLegalNextSingleMoves()
    {
        var result = new List<Move>();
        if (IsComplete) return result;
        var seen = new HashSet<Move>();

        foreach (var p in _candidatePlays)
        {
            foreach (var m in RemainingMoves(_appliedMoves, p))
            {
                if (!seen.Add(m)) continue;
                if (IsLegalSingleMoveFromState(_currentState, m)) result.Add(m);
            }
        }
        return result;
    }

    private static int DestClickPoint(Move m) =>
        m.ToPt > 0 ? m.ToPt : (m.ToPt < 0 ? -m.ToPt : 0);

    /// <summary>
    /// True if the multiset of `sub` is contained in the multiset of `super`'s moves.
    /// </summary>
    private static bool IsMultisetSubset(IReadOnlyList<Move> sub, Play super)
    {
        if (sub.Count > super.Count) return false;
        Span<bool> used = stackalloc bool[super.Count];
        foreach (var m in sub)
        {
            bool matched = false;
            for (int j = 0; j < super.Count; j++)
            {
                if (!used[j] && super[j].Equals(m))
                {
                    used[j] = true;
                    matched = true;
                    break;
                }
            }
            if (!matched) return false;
        }
        return true;
    }

    /// <summary>
    /// Multiset difference: moves of `play` minus moves of `applied`, in `play`'s order.
    /// </summary>
    private static List<Move> RemainingMoves(IReadOnlyList<Move> applied, Play play)
    {
        var result = new List<Move>(play.Count);
        Span<bool> usedInApplied = stackalloc bool[applied.Count == 0 ? 1 : applied.Count];
        for (int j = 0; j < play.Count; j++)
        {
            var m = play[j];
            bool foundInApplied = false;
            for (int i = 0; i < applied.Count; i++)
            {
                if (!usedInApplied[i] && applied[i].Equals(m))
                {
                    usedInApplied[i] = true;
                    foundInApplied = true;
                    break;
                }
            }
            if (!foundInApplied) result.Add(m);
        }
        return result;
    }

    /// <summary>
    /// Validate that <paramref name="m"/> is a legal single move from
    /// <paramref name="s"/>. Doesn't need the die because <paramref name="m"/> is
    /// drawn from a candidate play whose dice usage is already settled — this is a
    /// state-side sanity check (chain dependency, hit-versus-regular encoding,
    /// bar-must-enter-first, bear-off eligibility).
    /// </summary>
    private static bool IsLegalSingleMoveFromState(BoardState s, Move m)
    {
        if (m.FrPt < 1 || m.FrPt > 25) return false;
        if (s.Points[25] > 0 && m.FrPt != 25) return false;
        if (s.Points[m.FrPt] <= 0) return false;

        if (m.ToPt > 0)
            return s.Points[m.ToPt] >= 0;
        if (m.ToPt < 0)
            return s.Points[-m.ToPt] == -1;

        // Bear off
        return s.HighPointOccupied <= 6 && m.FrPt >= 1 && m.FrPt <= 6;
    }

    private Play SnapshotAppliedAsPlay()
    {
        var p = new Play();
        foreach (var m in _appliedMoves) p.Add(m);
        return p;
    }
}
