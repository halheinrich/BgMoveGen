namespace BgMoveGen;

/// <summary>
/// High-performance legal move generation for backgammon.
/// 
/// Two generation paths:
///   - GenerateDoubles: ordered generation (rearmost-first), no dedup needed.
///   - Legacy_GeneratePlays: original recursive approach with post-hoc dedup.
///     Kept temporarily for equivalence testing; will be removed.
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
    // ── Core: Apply / Undo ────────────────────────────────────────

    public static void ApplyMove(BoardState state, Move move)
    {
        state.Points[move.FrPt]--;
        if (move.ToPt > 0)
        {
            state.Points[move.ToPt]++;
        }
        else if (move.ToPt < 0)
        {
            int dest = -move.ToPt;
            state.Points[dest] = 1;
            state.Points[0]--;
        }
        // ToPt == 0: bear off, checker disappears

        if (move.FrPt == state.HighPointOccupied && state.Points[move.FrPt] == 0)
        {
            state.HighPointOccupied = 0;
            for (int i = move.FrPt - 1; i >= 1; i--)
            {
                if (state.Points[i] > 0) { state.HighPointOccupied = i; break; }
            }
        }
    }

    public static void UndoMove(BoardState state, Move move)
    {
        if (move.ToPt > 0)
        {
            state.Points[move.ToPt]--;
        }
        else if (move.ToPt < 0)
        {
            int dest = -move.ToPt;
            state.Points[dest] = -1;
            state.Points[0]++;
        }

        state.Points[move.FrPt]++;
        if (move.FrPt > state.HighPointOccupied)
            state.HighPointOccupied = move.FrPt;
    }

    // ── Core: Single move enumeration ─────────────────────────────

    /// <summary>
    /// Find the next legal move scanning down from prevFrPt - 1.
    /// Returns true if a move was found. Zero heap allocations.
    /// </summary>
    public static bool NextMove(BoardState state, int die, int prevFrPt, out Move move)
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
    public static int SingleMoves(BoardState state, int die, Span<Move> buffer)
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
    /// Convenience overload returning a List (allocates). For public API / tests.
    /// </summary>
    public static List<Move> SingleMoves(BoardState state, int die)
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
    public static List<Play> GenerateDoubles(BoardState state, int die)
    {
        var results = new List<Play>();

        int fr1 = 26;
        while (NextMove(state, die, fr1, out Move m1))
        {
            ApplyMove(state, m1);
            int fr2 = m1.FrPt + 1;
            bool anyAt2 = false;
            while (NextMove(state, die, fr2, out Move m2))
            {
                anyAt2 = true;
                ApplyMove(state, m2);
                int fr3 = m2.FrPt + 1;
                bool anyAt3 = false;
                while (NextMove(state, die, fr3, out Move m3))
                {
                    anyAt3 = true;
                    ApplyMove(state, m3);
                    int fr4 = m3.FrPt + 1;
                    bool anyAt4 = false;
                    while (NextMove(state, die, fr4, out Move m4))
                    {
                        anyAt4 = true;
                        var play = new Play();
                        play.Add(m1); play.Add(m2); play.Add(m3); play.Add(m4);
                        results.Add(play);
                        fr4 = m4.FrPt;
                    }
                    if (!anyAt4 && results.Count == 0)
                    {
                        var play = new Play();
                        play.Add(m1); play.Add(m2); play.Add(m3);
                        results.Add(play);
                    }
                    UndoMove(state, m3);
                    fr3 = m3.FrPt;
                }
                if (!anyAt3 && results.Count == 0)
                {
                    var play = new Play();
                    play.Add(m1); play.Add(m2);
                    results.Add(play);
                }
                UndoMove(state, m2);
                fr2 = m2.FrPt;
            }
            if (!anyAt2 && results.Count == 0)
            {
                var play = new Play();
                play.Add(m1);
                results.Add(play);
            }
            UndoMove(state, m1);
            fr1 = m1.FrPt;
        }

        if (results.Count == 0)
            results.Add(new Play());

        return results;
    }

    // ── Public API (doubles only for now) ─────────────────────────

    /// <summary>
    /// Generate all legal complete plays for a dice roll.
    /// Currently handles doubles via ordered generation.
    /// Non-doubles still uses legacy path.
    /// </summary>
    public static List<Play> GeneratePlays(BoardState state, int die1, int die2)
    {
        if (die1 == die2)
            return GenerateDoubles(state, die1);
        else
            return Legacy_GeneratePlays(state, die1, die2);
    }

    // ── Legacy path (kept for equivalence testing) ────────────────

    public static List<Play> Legacy_GeneratePlays(BoardState state, int die1, int die2)
    {
        List<Play> allPlays;

        if (die1 == die2)
        {
            int[] dice = [die1, die1, die1, die1];
            allPlays = Legacy_GenerateDoubles(state, dice);
        }
        else
        {
            allPlays = Legacy_GenerateRegular(state, die1, die2);
        }

        // Deduplicate
        var seen = new HashSet<(int, int, int, int, int, int, int, int)>();
        var unique = new List<Play>();
        foreach (var play in allPlays)
        {
            var key = play.DeduplicationKey();
            if (seen.Add(key))
                unique.Add(play);
        }

        if (unique.Count == 0)
            unique.Add(new Play());

        return unique;
    }

    private static List<Play> Legacy_GenerateRegular(BoardState state, int die1, int die2)
    {
        var plays1 = new List<Play>();
        var plays2 = new List<Play>();
        var current = new Play();
        var buffers = new Move[2][];
        buffers[0] = new Move[30];
        buffers[1] = new Move[30];

        Legacy_Recurse(state, [die1, die2], 0, ref current, plays1, buffers);
        Legacy_Recurse(state, [die2, die1], 0, ref current, plays2, buffers);

        var allPlays = new List<Play>(plays1.Count + plays2.Count);
        allPlays.AddRange(plays1);
        allPlays.AddRange(plays2);

        if (allPlays.Count == 0)
            return allPlays;

        int maxUsed = 0;
        foreach (var p in allPlays)
            if (p.Count > maxUsed) maxUsed = p.Count;

        if (maxUsed == 0)
            return [];

        var best = new List<Play>();
        foreach (var p in allPlays)
            if (p.Count == maxUsed) best.Add(p);

        // If only one die usable, must use the larger
        if (maxUsed == 1)
        {
            int maxDie = Math.Max(die1, die2);
            // plays1 used die1 first, plays2 used die2 first
            // 1-move plays from each list used that list's first die
            var withMax = new List<Play>();
            if (die1 == maxDie)
                foreach (var p in plays1)
                    if (p.Count == 1) withMax.Add(p);
            if (die2 == maxDie)
                foreach (var p in plays2)
                    if (p.Count == 1) withMax.Add(p);

            if (withMax.Count > 0)
            {
                // Dedup
                var seen = new HashSet<(int, int, int, int, int, int, int, int)>();
                var unique = new List<Play>();
                foreach (var p in withMax)
                    if (seen.Add(p.DeduplicationKey())) unique.Add(p);
                return unique;
            }
        }

        return best;
    }

    private static List<Play> Legacy_GenerateDoubles(BoardState state, int[] dice)
    {
        var allPlays = new List<Play>();
        var current = new Play();
        var buffers = new Move[dice.Length][];
        for (int i = 0; i < dice.Length; i++)
            buffers[i] = new Move[30];

        Legacy_Recurse(state, dice, 0, ref current, allPlays, buffers);

        if (allPlays.Count == 0)
            return allPlays;

        int maxUsed = 0;
        foreach (var p in allPlays)
            if (p.Count > maxUsed) maxUsed = p.Count;

        var best = new List<Play>();
        foreach (var p in allPlays)
            if (p.Count == maxUsed) best.Add(p);

        return best;
    }

    private static void Legacy_Recurse(
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
            ApplyMove(state, move);
            current.Add(move);

            Legacy_Recurse(state, dice, diceIndex + 1, ref current, allPlays, buffers);

            current.RemoveLast();
            UndoMove(state, move);
        }
    }
}