namespace BgMoveGen.Tests;

/// <summary>
/// A deterministic corpus of synthetic board positions, for pins that need
/// breadth rather than a hand-picked shape.
///
/// <para>
/// Synthetic on purpose. Nothing gating may depend on <c>TestData/</c>: a
/// real-game corpus is verification colour, not a fixture, and a pin that
/// reads one is a pin that stops running the day the file moves. These
/// positions are generated from a fixed seed, so the corpus is byte-identical
/// on every machine and every run, and a failure names a position the reader
/// can reconstruct from the seed and the index alone.
/// </para>
///
/// <para>
/// Positions are <c>Mop</c> arrays — the layout
/// <see cref="BgDataTypes_Lib.BoardState.FromMop"/> accepts: <c>[0]</c> the
/// opponent's bar, <c>[1..24]</c> the playing surface, <c>[25]</c> the on-roll
/// player's bar; positive counts are the on-roll player's. They are not
/// necessarily reachable game positions — checker counts vary and the two
/// sides are not balanced — which is deliberate: the generator asks nothing of
/// a board but its points, so an unreachable board exercises it exactly as a
/// reachable one does, and dropping the reachability constraint buys far
/// denser coverage of the shapes that matter.
/// </para>
///
/// <para>
/// Half the corpus is drawn with every on-roll checker inside the home board.
/// That is where bear-off encoding lives, and bear-off encoding is where the
/// generator's die-ordering assumptions are thinnest — a bear-off is
/// <c>(point, 0)</c> whichever die paid for it.
/// </para>
/// </summary>
internal static class SyntheticPositions
{
    /// <summary>The seed every caller draws from, so failures are shared and reproducible.</summary>
    internal const int DefaultSeed = 20260827;

    /// <summary>
    /// Generate <paramref name="count"/> positions from <paramref name="seed"/>.
    /// The sequence depends only on those two arguments.
    /// </summary>
    internal static List<int[]> Corpus(int count, int seed = DefaultSeed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var rng = new Random(seed);
        var positions = new List<int[]>(count);
        for (int i = 0; i < count; i++)
            positions.Add(Position(rng));
        return positions;
    }

    /// <summary>
    /// One position. Every draw consumes a fixed number of values from
    /// <paramref name="rng"/>'s stream regardless of outcome, so position
    /// <c>i</c> is a function of the seed and <c>i</c> alone.
    /// </summary>
    private static int[] Position(Random rng)
    {
        var mop = new int[26];

        // Highest point the on-roll player occupies. Half the time confine it
        // to the home board so the draw lands on a bear-off position.
        bool bearOff = rng.Next(2) == 0;
        int high = bearOff ? rng.Next(1, 7) : rng.Next(1, 25);

        int checkers = rng.Next(1, 16);
        mop[high] = 1;
        for (int i = 1; i < checkers; i++)
            mop[rng.Next(1, high + 1)]++;

        // Opponent stacks, on points the on-roll player has left empty. A
        // blocked point and a blot are both interesting — the first prunes
        // branches, the second makes two die orderings distinct plays.
        int stacks = rng.Next(0, 6);
        for (int i = 0; i < stacks; i++)
        {
            int pt = rng.Next(1, 25);
            int size = rng.Next(1, 4);
            if (mop[pt] == 0)
                mop[pt] = -size;
        }

        // Occasionally send one of the on-roll player's checkers to the bar,
        // which forces the enter-first branch and suppresses bear-off. Taking
        // the last checker off the board is fine — a lone checker on the bar
        // is a board like any other to the generator.
        if (rng.Next(8) == 0)
        {
            mop[high]--;
            mop[25] = 1;
        }

        return mop;
    }

    /// <summary>The 21 distinct dice rolls, ordered as <c>(low, high)</c>.</summary>
    internal static IEnumerable<(int Die1, int Die2)> AllRolls()
    {
        for (int d1 = 1; d1 <= 6; d1++)
            for (int d2 = d1; d2 <= 6; d2++)
                yield return (d1, d2);
    }
}
