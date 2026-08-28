using BenchmarkDotNet.Attributes;
using BgDataTypes_Lib;

namespace BgMoveGen.Benchmarks;

/// <summary>
/// Full-roll move generation through the public
/// <see cref="MoveGenerator.GeneratePlays(BoardState, int, int)"/> entry
/// point — the surface BgRLEngine drives through interop, and the one whose
/// cost matters.
///
/// <para>
/// The five generator cases cover the distinct shapes of the play-assembly
/// paths: full-depth doubles (four moves in hand at the innermost loop),
/// partial-depth doubles (the reduced-depth fallbacks that fire when fewer
/// than four dice can be played), a non-doubles roll (the two-pass
/// avoidance-dedup path), a non-doubles bear-off (that same path where a move
/// encodes as (point, 0) and the dedup has to work harder), and an
/// all-21-rolls sweep as the aggregate regression signal.
/// </para>
///
/// <para>
/// <see cref="MemoryDiagnoserAttribute"/> is on because allocation, not
/// nanoseconds, is the property this generator is designed around — the
/// documented invariant is zero allocation in the recursion, with only the
/// result <c>List&lt;Play&gt;</c> and its backing array on the heap. A change
/// to the generator that moves the allocation number is a regression whether
/// or not the clock notices.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class MoveGenerationBenchmarks
{
    private BoardState _standard = null!;
    private BoardState _partialEntry = null!;
    private BoardState _bearOff = null!;
    private (int Die1, int Die2)[] _allRolls = null!;
    private Play[] _sentinelPlays = null!;

    /// <summary>
    /// Builds the positions once. <see cref="MoveGenerator.GeneratePlays"/>
    /// is apply/undo-balanced, so every benchmark leaves its board
    /// byte-for-byte as it found it and the instances are safely reused
    /// across iterations.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _standard = BoardState.Standard();
        _partialEntry = PartialEntryPosition();
        _bearOff = BearOffPosition();

        var rolls = new List<(int, int)>();
        for (int d1 = 1; d1 <= 6; d1++)
            for (int d2 = d1; d2 <= 6; d2++)
                rolls.Add((d1, d2));
        _allRolls = [.. rolls];

        _sentinelPlays = SentinelPlays();
    }

    /// <summary>
    /// Doubles from the opening position — every branch reaches depth four,
    /// so the innermost four-move assembly site carries the whole run.
    /// </summary>
    [Benchmark]
    public List<Play> DoublesFullDepth() => MoveGenerator.GeneratePlays(_standard, 3, 3);

    /// <summary>
    /// Doubles that can only be played twice — the reduced-depth fallback
    /// assembly site. See <see cref="PartialEntryPosition"/> for why the
    /// position stops at two.
    /// </summary>
    [Benchmark]
    public List<Play> DoublesPartialDepth() => MoveGenerator.GeneratePlays(_partialEntry, 4, 4);

    /// <summary>
    /// A regular (non-doubles) roll from the opening position — the two-pass
    /// smallDie-first / bigDie-first path with avoidance-based dedup.
    /// </summary>
    [Benchmark]
    public List<Play> NonDoubles() => MoveGenerator.GeneratePlays(_standard, 6, 4);

    /// <summary>
    /// A non-doubles roll on a home board — the bear-off shape of the two-pass
    /// path. The other four rows never reach a bear-off at all (the opening
    /// position has checkers on the 24-point; the partial-depth position has
    /// two on the bar), so without this row the two-bear-off duplicate
    /// avoidance is unmeasured. See <see cref="BearOffPosition"/> for why the
    /// position is shaped the way it is.
    /// </summary>
    [Benchmark]
    public List<Play> NonDoublesBearOff() => MoveGenerator.GeneratePlays(_bearOff, 6, 5);

    /// <summary>
    /// All 21 distinct rolls from the opening position — the aggregate
    /// figure, least sensitive to any one roll's shape.
    /// </summary>
    [Benchmark]
    public int AllOpeningRolls()
    {
        int total = 0;
        foreach (var (die1, die2) in _allRolls)
            total += MoveGenerator.GeneratePlays(_standard, die1, die2).Count;
        return total;
    }

    /// <summary>
    /// Load canary. Formats a fixed set of plays through
    /// <see cref="MoveNotationFormatter.Format(Play)"/> — a code path no
    /// change to the generator can reach.
    ///
    /// <para>
    /// It exists to make a *sequential* A/B valid on this machine. The
    /// preferred form — both variants as sibling <c>[Benchmark]</c> methods
    /// in one process — is unavailable when the two variants are two
    /// spellings of the same method, since only one can be compiled into a
    /// given binary. The fallback is to run two binaries alternately and
    /// carry this row in both: it is measured under the same load as the
    /// generator rows in its own run, so if it holds across runs the machine
    /// held still and the generator deltas are real, and if it drifts the
    /// runs are contaminated and no generator delta can be read from them.
    /// See the contention Pitfall in <c>INSTRUCTIONS.md</c>.
    /// </para>
    ///
    /// <para>
    /// Sized to land in the same low-microsecond range as
    /// <see cref="AllOpeningRolls"/>, so background load perturbs the two
    /// comparably rather than swamping one and sparing the other.
    /// </para>
    /// </summary>
    [Benchmark]
    public int SentinelNotationFormat()
    {
        int total = 0;
        foreach (var play in _sentinelPlays)
            total += MoveNotationFormatter.Format(play).Length;
        return total;
    }

    /// <summary>
    /// The canary's fixed play set — twelve plays spanning the shapes the
    /// formatter branches on (plain moves, repeats that collapse to a count
    /// suffix, bar entry, hits, chained hops, bear-offs) so the row's cost is
    /// spread across its paths rather than concentrated in one.
    ///
    /// <para>
    /// Built here rather than generated, so the canary shares no code with
    /// the generator it is watching. Construction runs in
    /// <see cref="Setup"/> and is not measured.
    /// </para>
    /// </summary>
    private static Play[] SentinelPlays() =>
    [
        Play.Create(new Move(13, 8), new Move(24, 22)),
        Play.Create(new Move(13, 11), new Move(13, 11)),
        Play.Create(new Move(25, 20), new Move(25, 22)),
        Play.Create(new Move(24, -18), new Move(13, -9)),
        Play.Create(new Move(13, 11), new Move(11, -9), new Move(9, 7)),
        Play.Create(new Move(24, 20), new Move(13, 9), new Move(13, 9), new Move(8, 4)),
        Play.Create(new Move(5, 1), new Move(4, 0), new Move(4, 0), new Move(4, 0)),
        Play.Create(new Move(6, 0), new Move(5, 0)),
        Play.Create(new Move(25, -22)),
        Play.Create(new Move(24, 21), new Move(21, -15)),
        Play.Create(new Move(8, 5), new Move(8, 5), new Move(6, 3), new Move(6, 3)),
        Play.Create(new Move(6, 3), new Move(1, 0)),
    ];

    /// <summary>
    /// All fifteen on-roll checkers inside the home board, the opponent's
    /// fifteen inside his own — a plain race with no contact — and a
    /// <b>lone</b> checker on the highest occupied point.
    ///
    /// <para>
    /// The lone checker is what makes this row reach the two-bear-off
    /// duplicate avoidance rather than merely walk past it. Under 6-5 the
    /// 5-point bears off either way — exactly with the 5, as the highest
    /// point with the 6 — and taking that checker drops the highest point to
    /// the 4, which then bears off with whichever die is left. Same two moves
    /// from both die assignments, which is precisely the pair Pass 2 has to
    /// recognise and drop. Stack the 5-point instead and the position never
    /// gets there: the highest point stays put, the lower points can only
    /// bear off on an exact die, and the skip's second leg is unreachable.
    /// </para>
    /// </summary>
    private static BoardState BearOffPosition()
    {
        var state = new BoardState();

        state.Points[5] = 1;     // alone on the highest point
        state.Points[4] = 3;
        state.Points[3] = 3;
        state.Points[2] = 4;
        state.Points[1] = 4;

        state.Points[19] = -3;   // opponent, home board, out of contact
        state.Points[20] = -3;
        state.Points[21] = -3;
        state.Points[22] = -2;
        state.Points[23] = -2;
        state.Points[24] = -2;

        state.RecalcHighPoint();
        return state;
    }

    /// <summary>
    /// Two checkers on the bar against a five-point board with only the
    /// 21-point open, the 17-point blocked behind it, and the rest of the
    /// player's checkers stacked on the 1-point.
    ///
    /// <para>
    /// Under 4-4 that admits exactly one play: both bar checkers enter on 21
    /// and stop. The 21 → 17 continuation is blocked, and the 1-point stack
    /// cannot bear off while checkers sit outside the home board — so the
    /// generator runs out of moves at depth two and takes the reduced-depth
    /// fallback rather than the four-move site.
    /// </para>
    /// </summary>
    private static BoardState PartialEntryPosition()
    {
        var state = new BoardState();

        state.Points[25] = 2;    // on the bar
        state.Points[1] = 13;    // the rest, stacked and immobile under a 4

        state.Points[24] = -2;   // opponent's five-point board: 21 is the
        state.Points[23] = -2;   // only entry, everything else shut
        state.Points[22] = -2;
        state.Points[20] = -2;
        state.Points[19] = -2;
        state.Points[17] = -2;   // blocks the 21 → 17 continuation
        state.Points[2] = -3;    // opponent's remaining checkers

        state.RecalcHighPoint();
        return state;
    }
}
