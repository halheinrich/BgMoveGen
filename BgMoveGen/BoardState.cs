namespace BgMoveGen;

/// <summary>
/// Mutable backgammon board position.
/// 
/// Points[0..25]: 26-element array.
///   Points[25] = on-roll player's bar
///   Points[1..24] = playing surface
///   Points[0] = opponent's bar
/// 
/// Positive values = on-roll player's checkers.
/// Negative values = opponent's checkers.
/// Player moves from high indices toward low (25 → 1, bearing off past 1).
/// 
/// Designed for apply/undo mutation — no heap allocations during move generation.
/// </summary>
public class BoardState
{
    public readonly int[] Points = new int[26];

    /// <summary>
    /// Highest point (1-25) with a player checker, 0 if none.
    /// Updated incrementally by ApplyMove/UndoMove.
    /// </summary>
    public int HighPointOccupied;

    public BoardState() { }

    /// <summary>
    /// Recompute HighPointOccupied from scratch. Call after setting up a position.
    /// </summary>
    public void RecalcHighPoint()
    {
        HighPointOccupied = 0;
        for (int i = 25; i >= 1; i--)
        {
            if (Points[i] > 0) { HighPointOccupied = i; return; }
        }
    }

    /// <summary>
    /// Deep copy.
    /// </summary>
    public BoardState Copy()
    {
        var copy = new BoardState();
        Array.Copy(Points, copy.Points, 26);
        copy.HighPointOccupied = HighPointOccupied;
        return copy;
    }

    // ── Standard starting positions ───────────────────────────────

    /// <summary>
    /// Standard backgammon starting position.
    /// Player's checkers: 6-pt(5), 8-pt(3), 13-pt(5), 24-pt(2)
    /// Opponent's checkers: 19-pt(-5), 17-pt(-3), 12-pt(-5), 1-pt(-2)
    /// </summary>
    public static BoardState Standard()
    {
        var s = new BoardState();
        s.Points[6] = 5;
        s.Points[8] = 3;
        s.Points[13] = 5;
        s.Points[24] = 2;
        s.Points[19] = -5;
        s.Points[17] = -3;
        s.Points[12] = -5;
        s.Points[1] = -2;
        s.RecalcHighPoint();
        return s;
    }

    /// <summary>
    /// Nackgammon starting position.
    /// </summary>
    public static BoardState Nackgammon()
    {
        var s = new BoardState();
        s.Points[6] = 4;
        s.Points[8] = 3;
        s.Points[13] = 4;
        s.Points[23] = 2;
        s.Points[24] = 2;
        s.Points[19] = -4;
        s.Points[17] = -3;
        s.Points[12] = -4;
        s.Points[2] = -2;
        s.Points[1] = -2;
        s.RecalcHighPoint();
        return s;
    }
    // ── Bg960 setup ───────────────────────────────────────────────

    // Quadrant boundaries (1-indexed point indices)
    private static readonly (int from, int to)[] Quadrants =
    [
    (1,  6),   // home board
    (7,  12),  // outer board
    (13, 18),  // opponent outer board
    (19, 24),  // opponent home board
];

    // Made-point weights: num_points → weight
    private static readonly (int points, int weight)[] MadePointWeights =
    [
        (2, 1), (3, 3), (4, 10), (5, 10), (6, 5), (7, 2),
];

    /// <summary>
    /// Generate a random Bg960 starting position.
    /// Constraints: symmetrical, no blots (≥2 per point), one point per quadrant,
    /// no mirror conflicts, pip count ≥ 100, weighted toward 4–5 made points.
    /// </summary>
    /// <param name="seed">Optional RNG seed for reproducibility. Null = random.</param>
    /// <exception cref="RuntimeException">Thrown if no valid position found in 1000 attempts.</exception>
    public static BoardState Bg960(int? seed = null)
    {
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();

        // Precompute sampling distribution
        int maxPoints = 15 / 2;  // min 2 checkers per point → max 7 points
        int totalWeight = 0;
        for (int i = 0; i < MadePointWeights.Length; i++)
            if (MadePointWeights[i].points >= 4 && MadePointWeights[i].points <= maxPoints)
                totalWeight += MadePointWeights[i].weight;

        for (int attempt = 0; attempt < 1000; attempt++)
        {
            int numPoints = SampleNumPoints(rng, totalWeight);
            int[]? points = SelectPoints(rng, numPoints);
            if (points == null) continue;

            int[] checkers = DistributeCheckers(rng, points, 15, 2);

            // Check pip count (1-indexed: point i contributes checkers[i-1] * i)
            int pips = 0;
            for (int i = 0; i < points.Length; i++)
                pips += checkers[i] * points[i];
            if (pips < 100) continue;

            // Build board
            var s = new BoardState();
            for (int i = 0; i < points.Length; i++)
            {
                int pt = points[i];
                int mirror = 25 - pt;   // 1-indexed mirror
                s.Points[pt] = checkers[i];
                s.Points[mirror] = -checkers[i];
            }
            s.RecalcHighPoint();
            return s;
        }

        throw new InvalidOperationException("Bg960: failed to generate valid position in 1000 attempts");
    }

    private static int SampleNumPoints(Random rng, int totalWeight)
    {
        int r = rng.Next(totalWeight);
        int cumulative = 0;
        for (int i = 0; i < MadePointWeights.Length; i++)
        {
            var (points, weight) = MadePointWeights[i];
            if (points < 4 || points > 15 / 2) continue;
            cumulative += weight;
            if (r < cumulative) return points;
        }
        return MadePointWeights[^1].points;
    }

    /// <summary>
    /// Select numPoints distinct points satisfying quadrant coverage and no mirror conflicts.
    /// Returns null if 1000 inner attempts fail.
    /// </summary>
    private static int[]? SelectPoints(Random rng, int numPoints)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            var blocked = new HashSet<int>();
            var mandatory = new List<int>();
            bool failed = false;

            // One mandatory point per quadrant
            foreach (var (from, to) in Quadrants)
            {
                var candidates = new List<int>();
                for (int p = from; p <= to; p++)
                    if (!blocked.Contains(p)) candidates.Add(p);

                if (candidates.Count == 0) { failed = true; break; }

                int pt = candidates[rng.Next(candidates.Count)];
                mandatory.Add(pt);
                blocked.Add(pt);
                blocked.Add(25 - pt);   // block mirror
            }

            if (failed) continue;

            if (numPoints < mandatory.Count) continue;

            // Fill remaining slots
            int remaining = numPoints - mandatory.Count;
            var available = new List<int>();
            for (int p = 1; p <= 24; p++)
                if (!blocked.Contains(p)) available.Add(p);

            if (remaining > available.Count) continue;

            var extra = new List<int>();
            for (int i = 0; i < remaining; i++)
            {
                if (available.Count == 0) break;
                int idx = rng.Next(available.Count);
                int pt = available[idx];
                extra.Add(pt);
                available.RemoveAt(idx);
                available.Remove(25 - pt);  // remove mirror
            }

            if (extra.Count < remaining) continue;

            mandatory.AddRange(extra);
            mandatory.Sort();
            return mandatory.ToArray();
        }

        return null;
    }

    /// <summary>
    /// Distribute totalCheckers across points with at least minPerPoint each.
    /// Remainder distributed via stars-and-bars (sorted random dividers).
    /// </summary>
    private static int[] DistributeCheckers(Random rng, int[] points, int totalCheckers, int minPerPoint)
    {
        int k = points.Length;
        int remainder = totalCheckers - minPerPoint * k;
        int[] extra = new int[k];

        if (remainder > 0)
        {
            // Stars and bars: k-1 random dividers in [0, remainder]
            int[] dividers = new int[k - 1];
            for (int i = 0; i < dividers.Length; i++)
                dividers[i] = rng.Next(remainder + 1);
            Array.Sort(dividers);

            int prev = 0;
            for (int i = 0; i < k - 1; i++)
            {
                extra[i] = dividers[i] - prev;
                prev = dividers[i];
            }
            extra[k - 1] = remainder - prev;
        }

        int[] result = new int[k];
        for (int i = 0; i < k; i++)
            result[i] = minPerPoint + extra[i];
        return result;
    }
}
