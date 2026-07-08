using System.Text;
using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// Formats a <see cref="Play"/> into standard backgammon notation
/// ("8/5(2)", "bar/22", "24/18*", "6/off").
/// </summary>
/// <remarks>
/// Rendering starts from the play's canonical chain form
/// (<see cref="Play.ToCanonical"/>). The chain-collapse semantics — which
/// hops fuse into one trajectory, how an intermediate hit splits a chain so
/// its "*" stays visible, insensitivity to move order and decomposition, and
/// the deterministic chain ordering (FrPt descending, |ToPt| descending,
/// hit first) — are owned and tested by BgDataTypes_Lib's
/// <see cref="CanonicalPlay"/>. This formatter owns display only:
///
///   FrPt == 25   → bar entry, rendered "bar".
///   ToPt == 0    → bear off, rendered "off".
///   ToPt &lt; 0  → hit — land on |ToPt|, append "*".
///
/// Adjacent chains sharing (FrPt, |ToPt|) collapse to a count suffix —
/// "8/5(2)". Chains landing on one point with mixed hit status still group
/// (canonical order keeps them adjacent): the hit flag is OR-aggregated
/// across the group and the "*" follows the count — "6/2(2)*" if any
/// constituent chain hit.
/// </remarks>
public static class MoveNotationFormatter
{
    public static string Format(Play play)
    {
        var canonical = play.ToCanonical();
        if (canonical.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        int idx = 0;
        while (idx < canonical.Count)
        {
            var chain = canonical[idx];
            int to = Math.Abs(chain.ToPt);
            bool anyHit = chain.ToPt < 0;

            int run = 1;
            while (idx + run < canonical.Count
                   && canonical[idx + run].FrPt == chain.FrPt
                   && Math.Abs(canonical[idx + run].ToPt) == to)
            {
                if (canonical[idx + run].ToPt < 0) anyHit = true;
                run++;
            }

            if (sb.Length > 0) sb.Append(' ');
            sb.Append(FromLabel(chain.FrPt)).Append('/').Append(ToLabel(to));
            if (run > 1) sb.Append('(').Append(run).Append(')');
            if (anyHit) sb.Append('*');

            idx += run;
        }

        return sb.ToString();
    }

    private static string FromLabel(int pt) => pt == 25 ? "bar" : pt.ToString();
    private static string ToLabel(int pt) => pt == 0 ? "off" : pt.ToString();
}
