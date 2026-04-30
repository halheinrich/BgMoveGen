using System.Text;
using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// Formats a <see cref="Play"/> into standard backgammon notation
/// ("8/5(2)", "bar/22", "24/18*", "6/off").
/// </summary>
/// <remarks>
/// Move encoding:
///   FrPt == 25   → bar entry, rendered "bar".
///   FrPt 1..24   → regular source point.
///   ToPt &gt; 0  → regular destination.
///   ToPt == 0    → bear off, rendered "off".
///   ToPt &lt; 0  → hit — land on |ToPt|, append "*".
///
/// Consecutive legs of a single checker collapse — (21, 20)(20, 14)
/// renders as "21/14". Matching is bidirectional so legs emitted out of
/// time order still collapse: given (20, 14) then (21, 20), the second
/// leg extends the chain's start backward to produce "21/14".
///
/// Chain-extension rules:
///   Forward  (chain endpoint == new from): disallowed when the chain
///            has a hit at its endpoint — the "*" at the intermediate
///            point must stay visible.
///   Backward (chain start == new to): disallowed when the new leg has
///            a hit at its destination — same intermediate-visibility
///            rule, in the other direction. Physically the "new leg
///            hit" case cannot arise (opponent blot on a point the
///            player just vacated is impossible within one turn), but
///            the guard keeps the invariant local to the formatter.
///
/// Output ordering: chains are sorted by from-pt descending, with
/// |to-pt| descending as tiebreaker. Adjacent chains sharing
/// `(from, to)` collapse to a count suffix — "8/5(2)". The hit
/// flag is OR-aggregated across the group, and the "*" follows the
/// count: "6/2(2)*" if any constituent chain hit.
/// </remarks>
public static class MoveNotationFormatter
{
    private record struct Chain(int FromPt, int ToPt, bool Hit);

    public static string Format(Play play)
    {
        if (play.Count == 0) return string.Empty;

        Span<Chain> chains = stackalloc Chain[4];
        int chainCount = 0;

        for (int i = 0; i < play.Count; i++)
        {
            var move = play[i];
            int from = move.FrPt;
            bool hit = move.ToPt < 0;
            int to = hit ? -move.ToPt : move.ToPt;

            int matchIdx = -1;
            bool isForward = false;

            for (int j = 0; j < chainCount; j++)
            {
                var c = chains[j];
                if (!c.Hit && c.ToPt == from && from >= 1 && from <= 24)
                {
                    matchIdx = j;
                    isForward = true;
                    break;
                }
            }

            if (matchIdx < 0 && !hit && to >= 1 && to <= 24)
            {
                for (int j = 0; j < chainCount; j++)
                {
                    if (chains[j].FromPt == to)
                    {
                        matchIdx = j;
                        isForward = false;
                        break;
                    }
                }
            }

            if (matchIdx >= 0)
            {
                var c = chains[matchIdx];
                chains[matchIdx] = isForward
                    ? new Chain(c.FromPt, to, hit)
                    : new Chain(from, c.ToPt, c.Hit);
            }
            else
            {
                chains[chainCount++] = new Chain(from, to, hit);
            }
        }

        // Standard notation orders chains by from-pt desc, with |to-pt| desc
        // as tiebreaker. Fully-equal chains share the sort key and end up
        // adjacent, so the run-grouping below still collapses them to "(n)".
        chains[..chainCount].Sort(static (a, b) =>
            a.FromPt != b.FromPt
                ? b.FromPt.CompareTo(a.FromPt)
                : b.ToPt.CompareTo(a.ToPt));

        var sb = new StringBuilder();
        int idx = 0;
        while (idx < chainCount)
        {
            // Group by (FromPt, ToPt) only — chains landing on the same
            // point with mixed hit status still collapse to "(n)". The
            // "*" is OR-aggregated across the group: present if any
            // constituent chain hit.
            int run = 1;
            bool anyHit = chains[idx].Hit;
            while (idx + run < chainCount
                   && chains[idx + run].FromPt == chains[idx].FromPt
                   && chains[idx + run].ToPt == chains[idx].ToPt)
            {
                if (chains[idx + run].Hit) anyHit = true;
                run++;
            }

            if (sb.Length > 0) sb.Append(' ');
            var ch = chains[idx];
            sb.Append(FromLabel(ch.FromPt)).Append('/').Append(ToLabel(ch.ToPt));
            if (run > 1) sb.Append('(').Append(run).Append(')');
            if (anyHit) sb.Append('*');

            idx += run;
        }

        return sb.ToString();
    }

    private static string FromLabel(int pt) => pt == 25 ? "bar" : pt.ToString();
    private static string ToLabel(int pt) => pt == 0 ? "off" : pt.ToString();
}
