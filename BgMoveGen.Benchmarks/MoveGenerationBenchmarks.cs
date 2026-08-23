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
/// The four cases cover the distinct shapes of the generator's play-assembly
/// paths: full-depth doubles (four moves in hand at the innermost loop),
/// partial-depth doubles (the reduced-depth fallbacks that fire when fewer
/// than four dice can be played), a non-doubles roll (the two-pass
/// avoidance-dedup path), and an all-21-rolls sweep as the aggregate
/// regression signal.
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
    private (int Die1, int Die2)[] _allRolls = null!;

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

        var rolls = new List<(int, int)>();
        for (int d1 = 1; d1 <= 6; d1++)
            for (int d2 = d1; d2 <= 6; d2++)
                rolls.Add((d1, d2));
        _allRolls = [.. rolls];
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
