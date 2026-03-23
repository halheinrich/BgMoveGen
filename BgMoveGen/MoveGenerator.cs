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

    // ── Non-doubles: ordered generation with avoidance-based dedup ──

    /// <summary>
    /// Generate all legal plays for a non-doubles roll.
    /// Two passes: smallDie first (all plays kept), then bigDie first (skip
    /// same-checker duplicates where the smallDie-first path is also legal
    /// and produces the same board state).
    /// Enforces must-use-both-dice and must-use-larger-die rules.
    /// </summary>
    public static List<Play> GenerateNonDoubles(BoardState state, int die1, int die2)
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
                ApplyMove(state, m1);
                int fr2 = 26;
                while (NextMove(state, bigDie, fr2, out Move m2))
                {
                    anyTwoMoves = true;
                    var play = new Play();
                    play.Add(m1); play.Add(m2);
                    results.Add(play);
                    fr2 = m2.FrPt;
                }
                UndoMove(state, m1);
            }

            // Bar entry with bigDie
            int toPtB = 25 - bigDie;
            if (toPtB >= 1 && state.Points[toPtB] >= -1)
            {
                Move m1 = state.Points[toPtB] == -1 ? new Move(25, -toPtB) : new Move(25, toPtB);
                ApplyMove(state, m1);
                int fr2 = 26;
                while (NextMove(state, smallDie, fr2, out Move m2))
                {
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
                    var play = new Play();
                    play.Add(m1); play.Add(m2);
                    results.Add(play);
                    fr2 = m2.FrPt;
                }
                UndoMove(state, m1);
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
                    ApplyMove(state, m1.Value);
                    int fr2 = frPt1 + 1; // allow same point
                    while (NextMove(state, bigDie, fr2, out Move m2))
                    {
                        anyTwoMoves = true;
                        var play = new Play();
                        play.Add(m1.Value); play.Add(m2);
                        results.Add(play);
                        fr2 = m2.FrPt;
                    }
                    UndoMove(state, m1.Value);
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
                    ApplyMove(state, m1.Value);
                    int fr2 = frPt1 + 1; // allow same point
                    while (NextMove(state, smallDie, fr2, out Move m2))
                    {
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
                        anyTwoMoves = true;
                        var play = new Play();
                        play.Add(m1.Value); play.Add(m2);
                        results.Add(play);
                        fr2 = m2.FrPt;
                    }
                    UndoMove(state, m1.Value);
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
                var play = new Play();
                play.Add(m1);
                results.Add(play);
                fr1 = m1.FrPt;
            }

            if (results.Count == 0)
            {
                // bigDie can't be played. Try smallDie.
                fr1 = 26;
                while (NextMove(state, smallDie, fr1, out Move m1))
                {
                    var play = new Play();
                    play.Add(m1);
                    results.Add(play);
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
    /// Fast board hash for dedup. Uses FNV-1a over the 26 points.
    /// </summary>
    private static long BoardHash(BoardState state)
    {
        long hash = unchecked((long)0xcbf29ce484222325);
        for (int i = 0; i < 26; i++)
        {
            hash ^= state.Points[i];
            hash = unchecked(hash * 0x100000001b3);
        }
        return hash;
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

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Generate all legal complete plays for a dice roll.
    /// </summary>
    public static List<Play> GeneratePlays(BoardState state, int die1, int die2)
    {
        if (die1 == die2)
            return GenerateDoubles(state, die1);
        else
            return GenerateNonDoubles(state, die1, die2);
    }

    /// <summary>
    /// Generate all unique legal successor board states for a dice roll.
    /// Convenience wrapper for consumers that only need resulting positions
    /// (e.g., RL evaluation), not the move sequences.
    /// </summary>
    public static List<BoardState> GenerateStates(BoardState state, int die1, int die2)
    {
        var plays = GeneratePlays(state, die1, die2);
        var states = new List<BoardState>(plays.Count);
        foreach (var play in plays)
        {
            var copy = state.Copy();
            for (int i = 0; i < play.Count; i++)
                ApplyMove(copy, play[i]);
            states.Add(copy);
        }
        return states;
    }

    /// <summary>
    /// Lazily enumerate all unique legal successor board states for a dice roll.
    /// Each yielded BoardState is an independent copy — safe to store or discard.
    /// Allows early termination (e.g., alpha-beta pruning, first-legal-move).
    /// </summary>
    public static IEnumerable<BoardState> EnumerateStates(BoardState state, int die1, int die2)
    {
        var plays = GeneratePlays(state, die1, die2);
        foreach (var play in plays)
        {
            var copy = state.Copy();
            for (int i = 0; i < play.Count; i++)
                ApplyMove(copy, play[i]);
            yield return copy;
        }
    }

    // ── Reference implementation (brute-force, obviously correct) ──

    /// <summary>
    /// Brute-force move generation. Generates all possible plays by trying
    /// every legal move sequence, then deduplicates by final board state.
    /// Slow but guaranteed correct. Used as the ground truth for testing.
    /// </summary>
    public static List<Play> Reference_GeneratePlays(BoardState state, int die1, int die2)
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

        // Board-state dedup
        var seen = new HashSet<long>();
        var unique = new List<Play>();
        foreach (var play in best)
        {
            ApplyMove(state, play[0]);
            for (int i = 1; i < play.Count; i++)
                ApplyMove(state, play[i]);

            long hash = BoardHash(state);
            if (seen.Add(hash))
                unique.Add(play);

            for (int i = play.Count - 1; i >= 0; i--)
                UndoMove(state, play[i]);
        }

        return unique.Count > 0 ? unique : [new Play()];
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
            ApplyMove(state, move);
            current.Add(move);

            Reference_Recurse(state, dice, diceIndex + 1, ref current, allPlays, buffers);

            current.RemoveLast();
            UndoMove(state, move);
        }
    }
}