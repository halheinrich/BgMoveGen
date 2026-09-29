using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// One checker's single-die hop as a caller states it: the point the checker
/// leaves and the point it lands on, and nothing more — no hit mark, no die,
/// no place in an order. The input of <see cref="MoveGenerator.ResolvePlay"/>,
/// which resolves a play's hops to the legal play they describe; see that
/// method for the contract.
///
/// <para>
/// <b>No hit mark, by representation.</b> A <see cref="Move"/> marks a hit in
/// the sign of its <see cref="Move.ToPt"/>. A hop has no mark to carry: its
/// landing point is 0 to 24 by construction, so a marked destination cannot
/// be written into it. Whether a hop hits is the board's fact, derived when
/// the hops are resolved, never the caller's to state.
/// </para>
///
/// <para>
/// <b>Well-formed by construction.</b> <see cref="From"/> is a point 1–24 or
/// the bar, 25, and <see cref="To"/> is a point 1–24 or off, 0 — the numbering
/// of <see cref="Move"/>, in the mover's frame. The constructor refuses any
/// other number, and <see cref="TryCreate"/> is its non-throwing form for a
/// caller holding outside data. Whether a hop is a legal move — toward home,
/// by a die of the roll, from a point the mover holds, onto a point the
/// opponent does not hold — is not the hop's to say: that is the move
/// generator's, and hops that make no legal play resolve to none.
/// </para>
///
/// <para>
/// <b>Equality</b> is the value's: two hops are equal when their source and
/// landing points are. That is hop equality only. Whether two plays are the
/// same play is <see cref="BoardState.IsSamePlay"/>'s, from a position, and a
/// list of hops is not a play.
/// </para>
///
/// <para>
/// <c>default(Hop)</c> is <b>not meaningful</b>: the default of a struct
/// bypasses construction, so it has a source of 0, which no hop can have. It
/// is no legal move, so hops holding it resolve to none. Construct hops
/// explicitly.
/// </para>
/// </summary>
public readonly record struct Hop
{
    /// <summary>
    /// Creates the hop from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="from">The point the checker leaves: 1–24, or 25 for the bar.</param>
    /// <param name="to">The point the checker lands on: 1–24, or 0 for off.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="from"/> is outside 1–25, or <paramref name="to"/> is
    /// outside 0–24; the exception names the parameter.
    /// </exception>
    public Hop(int from, int to)
    {
        if (!IsSource(from))
            throw new ArgumentOutOfRangeException(nameof(from), from,
                "A hop's source must be a point 1-24, or 25 for the bar.");
        if (!IsLanding(to))
            throw new ArgumentOutOfRangeException(nameof(to), to,
                "A hop's landing point must be a point 1-24, or 0 for off.");
        From = from;
        To = to;
    }

    /// <summary>The point the checker leaves: 1–24, or 25 for the bar.</summary>
    public int From { get; }

    /// <summary>The point the checker lands on: 1–24, or 0 when it is borne off.</summary>
    public int To { get; }

    /// <summary>
    /// Creates the hop from <paramref name="from"/> to <paramref name="to"/>
    /// without throwing: the non-throwing form of
    /// <see cref="Hop(int, int)"/>, for a caller holding outside data.
    /// </summary>
    /// <param name="from">The point the checker leaves.</param>
    /// <param name="to">The point the checker lands on.</param>
    /// <param name="hop">
    /// The hop when both points are in range; otherwise
    /// <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="from"/> is 1–25 and
    /// <paramref name="to"/> is 0–24; otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryCreate(int from, int to, out Hop hop)
    {
        if (IsSource(from) && IsLanding(to))
        {
            hop = new Hop(from, to);
            return true;
        }
        hop = default;
        return false;
    }

    /// <summary>Deconstructs into (<see cref="From"/>, <see cref="To"/>).</summary>
    /// <param name="from">The point the checker leaves.</param>
    /// <param name="to">The point the checker lands on.</param>
    public void Deconstruct(out int from, out int to)
    {
        from = From;
        to = To;
    }

    /// <summary>
    /// Whether <paramref name="move"/> is this hop: it leaves this hop's
    /// source and lands on this hop's landing point, whatever its hit mark
    /// says — the one place a mark-free hop is read against
    /// <see cref="Move"/>'s encoding, where a hit is the landing point
    /// negated.
    /// </summary>
    internal bool Describes(Move move) =>
        move.FrPt == From && (move.ToPt == To || move.ToPt == -To);

    private static bool IsSource(int point) => point is >= 1 and <= 25;

    private static bool IsLanding(int point) => point is >= 0 and <= 24;
}
