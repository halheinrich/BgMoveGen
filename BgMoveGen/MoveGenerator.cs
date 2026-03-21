namespace BgMoveGen;

/// <summary>
/// High-performance legal move generation for backgammon.
/// 
/// Key optimization: mutable apply/undo instead of copy-per-branch.
/// The recursive generator mutates the board state in place and reverses
/// each move when backtracking. Zero heap allocations in the hot path.
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
    /// <summary>
    /// Generate all legal complete plays for a dice roll.
    /// </summary>
    public static List<Play> GeneratePlays(BoardState state, int die1, int die2)
    {
        List<Play> allPlays;

        if (die1 == die2)
        {
            int[] dice = [die1, die1, die1, die1];
            allPlays = GenerateDoubles(state, dice);
        }
        else
        {
            allPlays = GenerateRegular(state, die1, die2);
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
            unique.Add(new Play()); // empty play = no legal moves

        return unique;
    }

    /// <summary>
    /// Generate all legal single-checker moves into a caller-supplied buffer.
    /// Returns the number of moves written. Zero heap allocations.
    /// </summary>
    public static int SingleMoves(BoardState state, int die, Span<Move> buffer)
    {
        int count = 0;

        // Must enter from bar first
        if (state.BarPlayer > 0)
        {
            int dest = BoardState.NumPoints - die;
            if (dest >= 0 && dest < BoardState.NumPoints)
            {
                int pointVal = state.Points[dest];
                if (pointVal >= -1)
                {
                    bool hits = pointVal == -1;
                    buffer[count++] = new Move(BoardState.BarIndex, dest, die, hits);
                }
            }
            return count;
        }

        // Regular moves and bearing off
        bool canBearOff = state.CanBearOff;

        for (int src = 0; src < BoardState.NumPoints; src++)
        {
            if (state.Points[src] <= 0)
                continue;

            int dest = src - die;

            if (dest >= 0)
            {
                int pointVal = state.Points[dest];
                if (pointVal >= -1)
                {
                    bool hits = pointVal == -1;
                    buffer[count++] = new Move(src, dest, die, hits);
                }
            }
            else if (canBearOff)
            {
                if (dest == -1)
                {
                    // Exact roll
                    buffer[count++] = new Move(src, -1, die);
                }
                else // dest < -1, overshoot
                {
                    // Only legal if no checker on a higher point in home board
                    bool higherExists = false;
                    for (int j = src + 1; j < 6; j++)
                    {
                        if (state.Points[j] > 0) { higherExists = true; break; }
                    }
                    if (!higherExists)
                        buffer[count++] = new Move(src, -1, die);
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Generate all legal single-checker moves for one die value.
    /// Convenience overload that returns a List (allocates).
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

    /// <summary>
    /// Apply a move to the board state in place. No allocation.
    /// </summary>
    public static void ApplyMove(BoardState state, Move move)
    {
        // Remove checker from source
        if (move.Source == BoardState.BarIndex)
        {
            state.BarPlayer--;
            if (move.Dest >= 0 && move.Dest < 6)
                state.PlayerOutsideHome--;
        }
        else
        {
            state.Points[move.Source]--;
            if (move.Source >= 6)
                state.PlayerOutsideHome--;
        }

        // Place checker at destination
        if (move.Dest == -1)
        {
            // Bearing off
            state.OffPlayer++;
        }
        else
        {
            if (move.Hits)
            {
                state.Points[move.Dest] = 0; // remove opponent blot
                state.BarOpponent++;
            }
            state.Points[move.Dest]++;
            if (move.Dest >= 6)
                state.PlayerOutsideHome++;
        }
    }

    /// <summary>
    /// Undo a previously applied move. Exact reverse of ApplyMove.
    /// </summary>
    public static void UndoMove(BoardState state, Move move)
    {
        // Reverse destination
        if (move.Dest == -1)
        {
            state.OffPlayer--;
        }
        else
        {
            state.Points[move.Dest]--;
            if (move.Dest >= 6)
                state.PlayerOutsideHome--;

            if (move.Hits)
            {
                state.Points[move.Dest] = -1; // restore opponent blot
                state.BarOpponent--;
            }
        }

        // Reverse source
        if (move.Source == BoardState.BarIndex)
        {
            state.BarPlayer++;
            if (move.Dest >= 0 && move.Dest < 6)
                state.PlayerOutsideHome++;
        }
        else
        {
            state.Points[move.Source]++;
            if (move.Source >= 6)
                state.PlayerOutsideHome++;
        }
    }

    // ── Private generation methods ────────────────────────────────

    private static List<Play> GenerateRegular(BoardState state, int die1, int die2)
    {
        var allPlays = new List<Play>();
        var current = new Play();

        // Try both orderings
        int[] order1 = [die1, die2];
        int[] order2 = [die2, die1];

        Recurse(state, order1, 0, ref current, allPlays);
        Recurse(state, order2, 0, ref current, allPlays);

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
            var withMax = new List<Play>();
            foreach (var p in best)
                if (p[0].Die == maxDie) withMax.Add(p);
            if (withMax.Count > 0)
                return withMax;
        }

        return best;
    }

    private static List<Play> GenerateDoubles(BoardState state, int[] dice)
    {
        var allPlays = new List<Play>();
        var current = new Play();

        Recurse(state, dice, 0, ref current, allPlays);

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

    private static void Recurse(
        BoardState state,
        int[] dice,
        int diceIndex,
        ref Play current,
        List<Play> allPlays)
    {
        if (diceIndex >= dice.Length)
        {
            allPlays.Add(current.Snapshot());
            return;
        }

        int die = dice[diceIndex];
        Span<Move> legal = stackalloc Move[30];
        int legalCount = SingleMoves(state, die, legal);

        if (legalCount == 0)
        {
            // Can't use this die
            allPlays.Add(current.Snapshot());
            return;
        }

        for (int i = 0; i < legalCount; i++)
        {
            var move = legal[i];
            ApplyMove(state, move);
            current.Add(move);

            Recurse(state, dice, diceIndex + 1, ref current, allPlays);

            current.RemoveLast();
            UndoMove(state, move);
        }
    }
}