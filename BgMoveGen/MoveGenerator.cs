using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// High-performance legal move generation for backgammon.
///
/// Two optimized generation paths:
///   - GenerateDoubles: ordered generation (rearmost-first), no dedup needed.
///   - GenerateNonDoubles: ordered generation with avoidance-based dedup.
///
/// Reference_GeneratePlays: brute-force ground truth for testing.
///
/// Rules enforced:
/// - Must enter from bar before moving other checkers
/// - Must use both dice if possible; if only one, must use the larger
/// - Bearing off: exact roll, or overshoot from highest occupied point only
/// - Hitting: landing on single opponent checker sends it to bar
/// - Doubles: use the die value four times
/// </summary>
public static class MoveGenerator
{
    // ── Core: Single move enumeration ─────────────────────────────

    /// <summary>
    /// Find the next legal move scanning down from prevFrPt - 1.
    /// Returns true if a move was found. Zero heap allocations.
    /// </summary>
    internal static bool NextMove(BoardState state, int die, int prevFrPt, out Move move)
    {
        move = default;
        int start = prevFrPt - 1;

        // Must enter from bar first
        if (state.Points[25] > 0)
        {
            if (25 > start) return false;
            int toPt = 25 - die;
            if (state.Points[toPt] == -1)
            {
                move = new Move(25, -toPt);
                return true;
            }
            else if (state.Points[toPt] >= 0)
            {
                move = new Move(25, toPt);
                return true;
            }
            return false; // on bar but blocked
        }

        int scanStart = Math.Min(start, state.HighPointOccupied);
        for (int frPt = scanStart; frPt >= 1; frPt--)
        {
            if (state.Points[frPt] <= 0)
                continue;

            int toPt = frPt <= die ? 0 : frPt - die;

            if (toPt == 0)
            {
                if (state.HighPointOccupied <= 6)
                {
                    if (frPt == die || frPt == state.HighPointOccupied)
                    {
                        move = new Move(frPt, 0);
                        return true;
                    }
                }
            }
            else
            {
                if (state.Points[toPt] == -1)
                {
                    move = new Move(frPt, -toPt);
                    return true;
                }
                else if (state.Points[toPt] >= 0)
                {
                    move = new Move(frPt, toPt);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Generate all legal single-checker moves into a caller-supplied buffer.
    /// Ordered: highest FrPt first (canonical order).
    /// Returns the number of moves written. Zero heap allocations.
    /// </summary>
    internal static int SingleMoves(BoardState state, int die, Span<Move> buffer)
    {
        int count = 0;
        int prevFrPt = 26;
        while (NextMove(state, die, prevFrPt, out Move m))
        {
            buffer[count++] = m;
            prevFrPt = m.FrPt;
        }
        return count;
    }

    /// <summary>
    /// Convenience overload returning a List (allocates). Used by tests.
    /// </summary>
    internal static List<Move> SingleMoves(BoardState state, int die)
    {
        Span<Move> buffer = stackalloc Move[30];
        int count = SingleMoves(state, die, buffer);
        var moves = new List<Move>(count);
        for (int i = 0; i < count; i++)
            moves.Add(buffer[i]);
        return moves;
    }

    // ── Doubles: ordered generation (no dedup needed) ─────────────

    /// <summary>
    /// Generate all legal plays for a doubles roll.
    /// Uses canonical ordering (non-increasing FrPt via NextMove) to avoid duplicates.
    /// If fewer than 4 dice can be used, there is exactly one result.
    /// </summary>
    internal static List<Play> GenerateDoubles(BoardState state, int die)
    {
        var results = new List<Play>();

        int fr1 = 26;
        while (NextMove(state, die, fr1, out Move m1))
        {
            state.ApplyMove(m1);
            int fr2 = m1.FrPt + 1;
            bool anyAt2 = false;
            while (NextMove(state, die, fr2, out Move m2))
            {
                anyAt2 = true;
                state.ApplyMove(m2);
                int fr3 = m2.FrPt + 1;
                bool anyAt3 = false;
                while (NextMove(state, die, fr3, out Move m3))
                {
                    anyAt3 = true;
                    state.ApplyMove(m3);
                    int fr4 = m3.FrPt + 1;
                    bool anyAt4 = false;
                    while (NextMove(state, die, fr4, out Move m4))
                    {
                        anyAt4 = true;
                        results.Add(Play.Create(m1, m2, m3, m4));
                        fr4 = m4.FrPt;
                    }
                    if (!anyAt4 && results.Count == 0)
                        results.Add(Play.Create(m1, m2, m3));
                    state.UndoMove(m3);
                    fr3 = m3.FrPt;
                }
                if (!anyAt3 && results.Count == 0)
                    results.Add(Play.Create(m1, m2));
                state.UndoMove(m2);
                fr2 = m2.FrPt;
            }
            if (!anyAt2 && results.Count == 0)
                results.Add(Play.Create(m1));
            state.UndoMove(m1);
            fr1 = m1.FrPt;
        }

        if (results.Count == 0)
            results.Add(new Play());

        return results;
    }

    // ── Non-doubles: ordered generation with avoidance-based dedup ──

    /// <summary>
    /// Generate all legal plays for a non-doubles roll.
    /// Two passes: smallDie first (all plays kept), then bigDie first (skip
    /// duplicates of pass-1 plays). Three duplicate shapes are avoided:
    /// same-checker plays where the smallDie-first path is also legal and
    /// produces the same board state; same-source-point plays (two checkers
    /// leaving one point — including two bar entries), which pass 2 would
    /// re-emit move-for-move in the other order; and two bear-offs from
    /// different points, where the encoding (point, 0) is the same whichever
    /// die paid, so pass 1's pair and pass 2's pair are the same two moves.
    /// Enforces must-use-both-dice and must-use-larger-die rules.
    /// </summary>
    internal static List<Play> GenerateNonDoubles(BoardState state, int die1, int die2)
    {
        int smallDie = Math.Min(die1, die2);
        int bigDie = Math.Max(die1, die2);

        var results = new List<Play>();
        bool anyTwoMoves = false;

        // ── Pass 1: smallDie first (canonical — keep everything) ────

        if (state.Points[25] > 0)
        {
            // Bar entry with smallDie
            int toPt = 25 - smallDie;
            if (toPt >= 1 && state.Points[toPt] >= -1)
            {
                Move m1 = state.Points[toPt] == -1 ? new Move(25, -toPt) : new Move(25, toPt);
                state.ApplyMove(m1);
                int fr2 = 26;
                while (NextMove(state, bigDie, fr2, out Move m2))
                {
                    anyTwoMoves = true;
                    results.Add(Play.Create(m1, m2));
                    fr2 = m2.FrPt;
                }
                state.UndoMove(m1);
            }

            // Bar entry with bigDie
            int toPtB = 25 - bigDie;
            if (toPtB >= 1 && state.Points[toPtB] >= -1)
            {
                Move m1 = state.Points[toPtB] == -1 ? new Move(25, -toPtB) : new Move(25, toPtB);
                state.ApplyMove(m1);
                int fr2 = 26;
                while (NextMove(state, smallDie, fr2, out Move m2))
                {
                    if (m2.FrPt == 25)
                    {
                        // Both dice enter from the bar: (enter big, enter small)
                        // is the small-first branch's pair in the other order —
                        // the entry points differ, so neither entry affects the
                        // other and both orders reach the same state. Skip.
                        fr2 = m2.FrPt;
                        continue;
                    }
                    int m1Land = m1.ToPt > 0 ? m1.ToPt : -m1.ToPt;
                    if (m2.FrPt == m1Land)
                    {
                        // Same checker: bar → FrPt-b → FrPt-b-s
                        // Duplicate of bar → FrPt-s → FrPt-s-b if:
                        //   smallInt (25-s) not blocked AND no blot on either intermediate
                        int smallInt = 25 - smallDie;
                        if (state.Points[smallInt] >= -1) // not blocked (m1 didn't touch smallInt)
                        {
                            bool blotOnSmall = state.Points[smallInt] == -1;
                            bool blotOnBig = m1.ToPt < 0;
                            if (!blotOnSmall && !blotOnBig)
                            {
                                fr2 = m2.FrPt;
                                continue; // skip duplicate
                            }
                        }
                    }
                    anyTwoMoves = true;
                    results.Add(Play.Create(m1, m2));
                    fr2 = m2.FrPt;
                }
                state.UndoMove(m1);
            }
        }
        else
        {
            // ── No bar: iterate board points from rearmost down ──

            // Pass 1: smallDie first (keep all)
            for (int frPt1 = state.HighPointOccupied; frPt1 >= 1; frPt1--)
            {
                if (state.Points[frPt1] <= 0)
                    continue;

                int toPt = frPt1 <= smallDie ? 0 : frPt1 - smallDie;
                Move? m1 = TryMakeMove(state, frPt1, toPt, smallDie);
                if (m1.HasValue)
                {
                    state.ApplyMove(m1.Value);
                    int fr2 = frPt1 + 1; // allow same point
                    while (NextMove(state, bigDie, fr2, out Move m2))
                    {
                        anyTwoMoves = true;
                        results.Add(Play.Create(m1.Value, m2));
                        fr2 = m2.FrPt;
                    }
                    state.UndoMove(m1.Value);
                }
            }

            // Pass 2: bigDie first (skip same-checker duplicates)
            for (int frPt1 = state.HighPointOccupied; frPt1 >= 1; frPt1--)
            {
                if (state.Points[frPt1] <= 0)
                    continue;

                int toPt = frPt1 <= bigDie ? 0 : frPt1 - bigDie;
                Move? m1 = TryMakeMove(state, frPt1, toPt, bigDie);
                if (m1.HasValue)
                {
                    // Does pass 1 build on the *identical* first move? Only a
                    // bear-off can make that true: every other move encodes its
                    // destination as FrPt - die, so the two dice give two
                    // encodings, while a bear-off is (FrPt, 0) whichever die
                    // paid for it. Where it is true the FrPt-ordering argument
                    // that keeps different-point pairs apart collapses, because
                    // both passes then build the same pair from the same first
                    // move — see the second-leg check inside the loop.
                    bool pass1SharesFirstMove =
                        m1.Value.ToPt == 0 && ReproducedBy(state, m1.Value, smallDie);

                    state.ApplyMove(m1.Value);
                    int fr2 = frPt1 + 1; // allow same point
                    while (NextMove(state, smallDie, fr2, out Move m2))
                    {
                        if (m2.FrPt == frPt1)
                        {
                            // Two checkers leave the same point: (big from P,
                            // small from P) is pass 1's (small from P, big from P)
                            // in the other order. The destinations differ, so
                            // neither move affects the other's legality (the
                            // point stays occupied, so bear-off eligibility and
                            // HighPointOccupied are unchanged) — both orders are
                            // legal together and reach the same state. Skip.
                            fr2 = m2.FrPt;
                            continue;
                        }
                        int m1Land = m1.Value.ToPt > 0 ? m1.Value.ToPt : -m1.Value.ToPt;
                        if (m2.FrPt == m1Land)
                        {
                            // Same checker: frPt1 → frPt1-b → frPt1-b-s
                            // Duplicate of frPt1 → frPt1-s → frPt1-s-b if:
                            //   (a) both intermediates on-board (not bear-off)
                            //   (b) smallInt not blocked in original state
                            //   (c) no blot on either intermediate in original state
                            int smallInt = frPt1 - smallDie;
                            if (smallInt >= 1)
                            {
                                // smallInt untouched by m1, so current == original
                                bool smallBlocked = state.Points[smallInt] < -1;
                                if (!smallBlocked)
                                {
                                    bool blotOnSmall = state.Points[smallInt] == -1;
                                    bool blotOnBig = m1.Value.ToPt < 0;
                                    if (!blotOnSmall && !blotOnBig)
                                    {
                                        fr2 = m2.FrPt;
                                        continue; // skip duplicate
                                    }
                                }
                            }
                            // If smallInt <= 0, bear-off intermediate — not a dup
                        }
                        if (pass1SharesFirstMove && ReproducedBy(state, m2, bigDie))
                        {
                            // Two bear-offs from different points — the third
                            // duplicate shape, and the only one where pass 1
                            // emits this pair *move for move* rather than in the
                            // other order. m1 is a bear-off that pass 1 also
                            // plays, with smallDie; the state it leaves behind
                            // is identical because the move is identical, so
                            // pass 1 stands exactly here with bigDie still in
                            // hand. This check asks whether bigDie bears m2's
                            // checker off from here too. If it does, pass 1
                            // already emitted {m1, m2}. Skip.
                            fr2 = m2.FrPt;
                            continue;
                        }
                        anyTwoMoves = true;
                        results.Add(Play.Create(m1.Value, m2));
                        fr2 = m2.FrPt;
                    }
                    state.UndoMove(m1.Value);
                }
            }
        }

        if (!anyTwoMoves)
        {
            // Only one die can be played — must use the larger.
            results.Clear();
            int fr1 = 26;
            while (NextMove(state, bigDie, fr1, out Move m1))
            {
                results.Add(Play.Create(m1));
                fr1 = m1.FrPt;
            }

            if (results.Count == 0)
            {
                // bigDie can't be played. Try smallDie.
                fr1 = 26;
                while (NextMove(state, smallDie, fr1, out Move m1))
                {
                    results.Add(Play.Create(m1));
                    fr1 = m1.FrPt;
                }
            }
        }
        else
        {
            // Filter to 2-move plays only (must use both dice)
            var twoMovePlays = new List<Play>();
            foreach (var p in results)
                if (p.Count == 2) twoMovePlays.Add(p);
            results = twoMovePlays;
        }

        if (results.Count == 0)
            results.Add(new Play());

        return results;
    }

    /// <summary>
    /// Try to make a move from frPt with the given toPt. Returns null if illegal.
    /// Handles bear-off eligibility check.
    /// </summary>
    private static Move? TryMakeMove(BoardState state, int frPt, int toPt, int die)
    {
        if (toPt == 0)
        {
            if (state.HighPointOccupied <= 6)
            {
                if (frPt == die || frPt == state.HighPointOccupied)
                    return new Move(frPt, 0);
            }
            return null;
        }
        else
        {
            if (state.Points[toPt] == -1)
                return new Move(frPt, -toPt);
            else if (state.Points[toPt] >= 0)
                return new Move(frPt, toPt);
            return null;
        }
    }

    /// <summary>
    /// Would <paramref name="die"/>, played from <paramref name="state"/> off
    /// <paramref name="move"/>'s source point, produce exactly
    /// <paramref name="move"/>?
    ///
    /// <para>
    /// The question the non-doubles duplicate avoidance asks when it needs to
    /// know whether the *other* pass's die assignment lands on the same
    /// encoding rather than merely the same board state. Legality is
    /// <see cref="TryMakeMove"/>'s — this only re-asks it with the other die,
    /// so the two passes cannot drift apart on what a legal move is. Not valid
    /// for bar entries (<c>FrPt == 25</c>); the callers are inside the no-bar
    /// branch.
    /// </para>
    /// </summary>
    private static bool ReproducedBy(BoardState state, Move move, int die)
    {
        int frPt = move.FrPt;
        int toPt = frPt <= die ? 0 : frPt - die;
        return TryMakeMove(state, frPt, toPt, die) is Move alt && alt == move;
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Generate all legal complete plays for a dice roll.
    ///
    /// <para>
    /// <b>No-legal-move convention.</b> When the position admits no legal move
    /// (a dance / closed-out on the bar), the result is a one-element list
    /// holding the empty pass <see cref="Play"/> (<c>Count == 0</c>) — never an
    /// empty list. Callers must handle a single "pass" candidate; the returned
    /// list is never <c>Count == 0</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Distinct by resulting position.</b> The list holds exactly one play
    /// per distinct position a legal play reaches: no two candidates reach the
    /// same position, so no two are the same play from
    /// <paramref name="state"/> — play identity is
    /// <see cref="BoardState.IsSamePlay"/>'s, whose remarks state it — and
    /// every legal way of playing the roll reaches one candidate's position. A
    /// consumer may therefore treat <c>Count == 1</c> as "no choice": the one
    /// candidate, pass or not, is the only play. Pinned against every legal
    /// single-die sequence by
    /// <c>GeneratePlays_HoldsExactlyOnePlayPerLegalPosition_AcrossSyntheticPositions</c>,
    /// with <c>GeneratePlays_CandidatesAreDistinctPlays</c> (all 21 rolls on
    /// the opening board) and its <c>_AcrossSyntheticPositions</c> sweep
    /// (84,000 position–roll pairs) beside it, in <c>MoveGeneratorTests</c>.
    /// </para>
    /// </summary>
    public static List<Play> GeneratePlays(BoardState state, int die1, int die2)
    {
        if (die1 == die2)
            return GenerateDoubles(state, die1);
        else
            return GenerateNonDoubles(state, die1, die2);
    }

    /// <summary>
    /// The distinct board positions reachable by a complete legal play of
    /// <paramref name="die1"/>, <paramref name="die2"/> from
    /// <paramref name="state"/> — for consumers that choose among resulting
    /// positions and need nothing of the plays that reach them.
    ///
    /// <para>
    /// <b>Mover's frame.</b> Each board is the position immediately after the
    /// mover's play, still from the mover's perspective: <i>no</i> flip is
    /// applied. This differs from <see cref="BoardState.ApplyPlay"/> and from
    /// the native <c>generate_successor_states</c> export, which both
    /// re-express the board from the next mover's perspective.
    /// </para>
    ///
    /// <para>
    /// <b>Distinct by position.</b> One board per <see cref="GeneratePlays"/>
    /// candidate, in candidate order, and no two boards are equal position for
    /// position — a position reachable by several move sequences appears once,
    /// so a consumer choosing among the results never weights one position
    /// twice. <c>Count == 1</c> means "no choice". Pinned by
    /// <c>GenerateResultingStates_BoardsAreDistinct_AcrossSyntheticPositions</c>
    /// (4,000 positions × 21 rolls) in <c>MoveGeneratorTests</c>.
    /// </para>
    ///
    /// <para>
    /// <b>No legal move.</b> A dance / closed-out position yields exactly one
    /// board equal to <paramref name="state"/> — never an empty list, and never
    /// <paramref name="state"/> itself.
    /// </para>
    ///
    /// <para>
    /// <b>Ownership.</b> The list is fully built before this method returns.
    /// <paramref name="state"/> is left unchanged, and every returned
    /// <see cref="BoardState"/> is an independent copy owned by the caller —
    /// sharing no storage with <paramref name="state"/>, with another result,
    /// or with anything BgMoveGen retains. The list interface is read-only;
    /// the boards themselves are ordinary mutable <see cref="BoardState"/>s.
    /// </para>
    /// </summary>
    public static IReadOnlyList<BoardState> GenerateResultingStates(BoardState state, int die1, int die2)
    {
        var plays = GeneratePlays(state, die1, die2);
        var states = new BoardState[plays.Count];
        for (int p = 0; p < plays.Count; p++)
            states[p] = ResultingStateOf(state, plays[p]);
        return states;
    }

    /// <summary>
    /// Every legal candidate for a roll, each pairing the generator's
    /// <see cref="Play"/> with the board that play produces — for consumers
    /// that choose among resulting positions and must also report the play
    /// that reaches the chosen one.
    ///
    /// <para>
    /// <b>One call, one association.</b> Candidate <c>i</c> holds
    /// <see cref="GeneratePlays"/>' candidate <c>i</c>, in the generator's own
    /// encoding and in candidate order, and the board that play reaches — the
    /// board <see cref="GenerateResultingStates"/> returns at the same index,
    /// built by the same routine. Consumers never zip the two lists
    /// themselves, and move generation runs once.
    /// </para>
    ///
    /// <para>
    /// Every other guarantee is <see cref="GenerateResultingStates"/>'s, with
    /// the same meaning: <b>mover's frame</b> (no flip); boards distinct by
    /// position, so the plays are distinct candidates and <c>Count == 1</c>
    /// means "no choice"; a no-legal-move position yields one candidate holding
    /// the empty play and a board equal to — never the same object as —
    /// <paramref name="state"/>; the list is fully built before return;
    /// <paramref name="state"/> is left unchanged; and every
    /// <see cref="CandidatePlay.ResultingState"/> is an independent copy owned
    /// by the caller, sharing no storage with <paramref name="state"/> or with
    /// another candidate.
    /// </para>
    ///
    /// <para>
    /// Choosing among the candidates — and breaking ties — is the consumer's
    /// decision; this method only enumerates them.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CandidatePlay> GenerateCandidatePlays(BoardState state, int die1, int die2)
    {
        var plays = GeneratePlays(state, die1, die2);
        var candidates = new CandidatePlay[plays.Count];
        for (int p = 0; p < plays.Count; p++)
            candidates[p] = new CandidatePlay(plays[p], ResultingStateOf(state, plays[p]));
        return candidates;
    }

    /// <summary>
    /// The board behind <see cref="GenerateResultingStates"/> and
    /// <see cref="GenerateCandidatePlays"/>: a new board holding the position
    /// <paramref name="play"/> reaches (<see cref="ResultingPositionOf"/>), in
    /// the mover's frame. <paramref name="state"/> is left unchanged.
    /// </summary>
    private static BoardState ResultingStateOf(BoardState state, in Play play)
        => new(ResultingPositionOf(state, in play));

    /// <summary>
    /// The position a generated <paramref name="play"/> reaches from
    /// <paramref name="state"/>, in the mover's frame — the single source of
    /// the generator's play → position rule, behind every view of the
    /// candidates, the reference's position dedup, and
    /// <see cref="MoveEntryState"/>'s target positions. The moves are applied
    /// one by one with the raw pair, the position is taken as a value, and the
    /// moves are undone in reverse, so <paramref name="state"/> is left as it
    /// was and nothing is allocated.
    /// </summary>
    /// <remarks>
    /// For the generator's own encodings only: the raw pair trusts its moves
    /// (<see cref="BoardState.ApplyMove"/> states the precondition), and a
    /// generated play is a legal move sequence from <paramref name="state"/>,
    /// so each move agrees with the board as it stands when it is applied. A
    /// caller's play goes through <see cref="BoardState.ApplyPlay"/> instead.
    /// </remarks>
    internal static BoardPosition ResultingPositionOf(BoardState state, in Play play)
    {
        for (int i = 0; i < play.Count; i++)
            state.ApplyMove(play[i]);
        var position = state.ToPosition();
        for (int i = play.Count - 1; i >= 0; i--)
            state.UndoMove(play[i]);
        return position;
    }

    /// <summary>
    /// True iff <paramref name="play"/> is a legal play for
    /// <paramref name="state"/> with dice <paramref name="die1"/>,
    /// <paramref name="die2"/>: the same play, from <paramref name="state"/>,
    /// as one of <see cref="GeneratePlays"/>' candidates. "The same play" is
    /// <see cref="BoardState.IsSamePlay"/>'s, whose remarks state it, and the
    /// candidate is found with <see cref="BoardState.IndexOfSamePlay"/> — so
    /// every encoding of a legal play is legal, and a play invalid from
    /// <paramref name="state"/> matches nothing. The single legality-match
    /// rule: <see cref="ApplyPlay"/> asks it too.
    ///
    /// <para>
    /// Implementation re-runs <see cref="GeneratePlays"/>; this is the simple
    /// correct approach. Not a hot-path primitive — callers running tight loops
    /// should drive the generator directly.
    /// </para>
    /// </summary>
    public static bool IsLegalPlay(BoardState state, Play play, int die1, int die2)
    {
        var legal = GeneratePlays(state, die1, die2);
        return state.IndexOfSamePlay(play, legal) >= 0;
    }

    /// <summary>
    /// Validating turn-boundary apply: throws <see cref="ArgumentException"/> if
    /// <paramref name="play"/> is not legal for <paramref name="state"/> with
    /// <paramref name="die1"/>, <paramref name="die2"/> (<see cref="IsLegalPlay"/>);
    /// otherwise applies it with <see cref="BoardState.ApplyPlay"/> (apply-all +
    /// flip).
    ///
    /// <para>
    /// <b>The caller's play is what is applied.</b> A legal play is the same
    /// play as one of the generator's candidates, so it reaches that
    /// candidate's position, and <see cref="BoardState.ApplyPlay"/> applies a
    /// play through that same rule rather than hop by hop: the board reached is
    /// the candidate's whichever encoding the caller wrote. So the turn has one
    /// encoding, the one the caller holds, and a caller that records the play
    /// it passed records the play that was applied.
    /// </para>
    ///
    /// <para>
    /// On rejection, <paramref name="state"/> is unchanged — validation runs
    /// to completion before any mutation. Callers that already know the play is
    /// legal can skip the check by invoking <see cref="BoardState.ApplyPlay"/>
    /// directly.
    /// </para>
    /// </summary>
    public static void ApplyPlay(BoardState state, Play play, int die1, int die2)
    {
        if (!IsLegalPlay(state, play, die1, die2))
            throw new ArgumentException(
                $"Play is not legal for the given state and dice ({die1}, {die2}).",
                nameof(play));
        state.ApplyPlay(play);
    }

    // ── Reference implementation (brute-force, obviously correct) ──

    /// <summary>
    /// Brute-force move generation. Takes every legal single-die sequence
    /// (<see cref="Reference_LegalSequences"/>) and keeps one per resulting
    /// position — positions compared as <see cref="BoardPosition"/> values,
    /// whose equality decides. Slow but guaranteed correct. Used as the
    /// ground truth for testing.
    /// </summary>
    internal static List<Play> Reference_GeneratePlays(BoardState state, int die1, int die2)
    {
        // Never empty: a position with no legal move yields the one pass, so
        // the first sequence is always kept.
        var seen = new HashSet<BoardPosition>();
        var unique = new List<Play>();
        foreach (var play in Reference_LegalSequences(state, die1, die2))
        {
            if (seen.Add(ResultingPositionOf(state, in play)))
                unique.Add(play);
        }

        return unique;
    }

    /// <summary>
    /// Every legal play of the roll as a sequence of single-die moves, one
    /// entry per sequence — brute force, with no deduplication: both die
    /// orders of a non-double, every order of a double's moves the recursion
    /// reaches, each move a legal single move (<see cref="SingleMoves(BoardState, int, Span{Move})"/>)
    /// from the board the moves before it leave. The rules on the whole play
    /// are applied after: as many dice as can be played, and a lone
    /// non-double die the larger when either could be. The one pass when
    /// there is no legal move. The space
    /// <see cref="Reference_GeneratePlays"/> reduces to one play per position.
    /// </summary>
    internal static List<Play> Reference_LegalSequences(BoardState state, int die1, int die2)
    {
        var allPlays = new List<Play>();
        var current = new Play();

        if (die1 == die2)
        {
            int[] dice = [die1, die1, die1, die1];
            var buffers = new Move[4][];
            for (int i = 0; i < 4; i++) buffers[i] = new Move[30];
            Reference_Recurse(state, dice, 0, ref current, allPlays, buffers);
        }
        else
        {
            // Try both orderings
            var buffers = new Move[2][];
            buffers[0] = new Move[30];
            buffers[1] = new Move[30];
            Reference_Recurse(state, [die1, die2], 0, ref current, allPlays, buffers);
            Reference_Recurse(state, [die2, die1], 0, ref current, allPlays, buffers);
        }

        if (allPlays.Count == 0)
            return [new Play()];

        // Must use maximum number of dice
        int maxUsed = 0;
        foreach (var p in allPlays)
            if (p.Count > maxUsed) maxUsed = p.Count;

        if (maxUsed == 0)
            return [new Play()];

        var best = new List<Play>();
        foreach (var p in allPlays)
            if (p.Count == maxUsed) best.Add(p);

        // If only one die usable for non-doubles, must use the larger
        if (die1 != die2 && maxUsed == 1)
        {
            int maxDie = Math.Max(die1, die2);
            // Try each play: apply it and check if the distance moved equals maxDie
            // Since we don't store the die, infer from the move
            var withBig = new List<Play>();
            var withSmall = new List<Play>();
            foreach (var p in best)
            {
                int frPt = p[0].FrPt;
                int toPt = p[0].ToPt;
                int dist = toPt == 0 ? frPt : frPt - Math.Abs(toPt);
                if (dist >= maxDie)
                    withBig.Add(p);
                else
                    withSmall.Add(p);
            }
            if (withBig.Count > 0)
                best = withBig;
        }

        return best;
    }

    private static void Reference_Recurse(
        BoardState state,
        int[] dice,
        int diceIndex,
        ref Play current,
        List<Play> allPlays,
        Move[][] buffers)
    {
        if (diceIndex >= dice.Length)
        {
            allPlays.Add(current.Snapshot());
            return;
        }

        int die = dice[diceIndex];
        int legalCount = SingleMoves(state, die, buffers[diceIndex]);

        if (legalCount == 0)
        {
            allPlays.Add(current.Snapshot());
            return;
        }

        for (int i = 0; i < legalCount; i++)
        {
            var move = buffers[diceIndex][i];
            state.ApplyMove(move);
            current.Add(move);

            Reference_Recurse(state, dice, diceIndex + 1, ref current, allPlays, buffers);

            current.RemoveLast();
            state.UndoMove(move);
        }
    }
}
