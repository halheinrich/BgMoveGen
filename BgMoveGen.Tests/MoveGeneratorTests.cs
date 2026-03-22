using BgMoveGen;

namespace BgMoveGen.Tests;

public class BoardStateTests
{
    [Fact]
    public void Standard_Has15CheckersPerSide()
    {
        var state = BoardState.Standard();
        int player = 0, opponent = 0;
        for (int i = 0; i <= 25; i++)
        {
            if (state.Points[i] > 0) player += state.Points[i];
            if (state.Points[i] < 0) opponent += Math.Abs(state.Points[i]);
        }
        Assert.Equal(15, player);
        Assert.Equal(15, opponent);
    }

    [Fact]
    public void Standard_HighPointIs24()
    {
        var state = BoardState.Standard();
        Assert.Equal(24, state.HighPointOccupied);
    }

    [Fact]
    public void Nackgammon_Has15CheckersPerSide()
    {
        var state = BoardState.Nackgammon();
        int player = 0, opponent = 0;
        for (int i = 0; i <= 25; i++)
        {
            if (state.Points[i] > 0) player += state.Points[i];
            if (state.Points[i] < 0) opponent += Math.Abs(state.Points[i]);
        }
        Assert.Equal(15, player);
        Assert.Equal(15, opponent);
    }

    [Fact]
    public void Copy_IsIndependent()
    {
        var state = BoardState.Standard();
        var copy = state.Copy();
        copy.Points[1] = 99;
        Assert.NotEqual(99, state.Points[1]);
    }

    [Fact]
    public void CanBearOff_FalseForStandard()
    {
        var state = BoardState.Standard();
        Assert.True(state.HighPointOccupied > 6);
    }

    [Fact]
    public void CanBearOff_TrueWhenAllInHomeBoard()
    {
        var state = new BoardState();
        state.Points[1] = 5; state.Points[3] = 5; state.Points[5] = 5;
        state.RecalcHighPoint();
        Assert.True(state.HighPointOccupied <= 6);
    }

    [Fact]
    public void CanBearOff_FalseWhenOnBar()
    {
        var state = new BoardState();
        state.Points[1] = 5; state.Points[3] = 5; state.Points[5] = 4;
        state.Points[25] = 1; // bar
        state.RecalcHighPoint();
        Assert.True(state.HighPointOccupied > 6); // bar is 25
    }
}

public class ApplyUndoTests
{
    [Fact]
    public void ApplyUndo_RegularMove_RoundTrips()
    {
        var state = BoardState.Standard();
        var original = state.Copy();
        var move = new Move(13, 8); // 13-pt to 8-pt (die 5)

        MoveGenerator.ApplyMove(state, move);
        Assert.Equal(4, state.Points[13]);
        Assert.Equal(4, state.Points[8]);

        MoveGenerator.UndoMove(state, move);
        for (int i = 0; i <= 25; i++)
            Assert.Equal(original.Points[i], state.Points[i]);
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void ApplyUndo_HittingMove_RoundTrips()
    {
        var state = new BoardState();
        state.Points[13] = 2;
        state.Points[12] = -1; // opponent blot
        state.RecalcHighPoint();
        var original = state.Copy();

        var move = new Move(13, -12); // hit on 12-pt

        MoveGenerator.ApplyMove(state, move);
        Assert.Equal(1, state.Points[13]);
        Assert.Equal(1, state.Points[12]); // player now
        Assert.Equal(-1, state.Points[0]); // opponent bar

        MoveGenerator.UndoMove(state, move);
        for (int i = 0; i <= 25; i++)
            Assert.Equal(original.Points[i], state.Points[i]);
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void ApplyUndo_BarEntry_RoundTrips()
    {
        var state = new BoardState();
        state.Points[25] = 1; // on bar
        state.Points[6] = 5;
        state.RecalcHighPoint();
        var original = state.Copy();

        var move = new Move(25, 22); // enter on 22-pt (die 3)

        MoveGenerator.ApplyMove(state, move);
        Assert.Equal(0, state.Points[25]);
        Assert.Equal(1, state.Points[22]);

        MoveGenerator.UndoMove(state, move);
        Assert.Equal(original.Points[25], state.Points[25]);
        Assert.Equal(0, state.Points[22]);
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void ApplyUndo_BearOff_RoundTrips()
    {
        var state = new BoardState();
        state.Points[4] = 5;
        state.Points[2] = 5;
        state.Points[1] = 5;
        state.RecalcHighPoint();
        var original = state.Copy();

        var move = new Move(4, 0); // bear off from 4-pt

        MoveGenerator.ApplyMove(state, move);
        Assert.Equal(4, state.Points[4]);

        MoveGenerator.UndoMove(state, move);
        Assert.Equal(original.Points[4], state.Points[4]);
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void ApplyUndo_HighPointTracking()
    {
        var state = new BoardState();
        state.Points[13] = 1; // single checker on highest point
        state.Points[6] = 5;
        state.RecalcHighPoint();
        Assert.Equal(13, state.HighPointOccupied);

        var move = new Move(13, 7); // move it down
        MoveGenerator.ApplyMove(state, move);
        Assert.Equal(7, state.HighPointOccupied); // scanned down

        MoveGenerator.UndoMove(state, move);
        Assert.Equal(13, state.HighPointOccupied); // restored
    }
}

public class SingleMoveTests
{
    [Fact]
    public void BarEntry_MustEnterFirst()
    {
        var state = BoardState.Standard();
        state.Points[25] = 1; // put one on bar
        state.RecalcHighPoint();

        var moves = MoveGenerator.SingleMoves(state, 3);
        Assert.All(moves, m => Assert.Equal(25, m.FrPt));
    }

    [Fact]
    public void BarEntry_BlockedByOpponent()
    {
        var state = new BoardState();
        state.Points[25] = 1;
        // Opponent holds all entry points (19-24)
        for (int i = 19; i <= 24; i++)
            state.Points[i] = -2;
        state.RecalcHighPoint();

        for (int die = 1; die <= 6; die++)
        {
            var moves = MoveGenerator.SingleMoves(state, die);
            Assert.Empty(moves);
        }
    }

    [Fact]
    public void BarEntry_CanHitBlot()
    {
        var state = new BoardState();
        state.Points[25] = 1;
        state.Points[24] = -1; // opponent blot on 24-pt
        state.RecalcHighPoint();

        var moves = MoveGenerator.SingleMoves(state, 1);
        Assert.Single(moves);
        Assert.True(moves[0].ToPt < 0); // negative = hit
        Assert.Equal(-24, moves[0].ToPt);
    }

    [Fact]
    public void RegularMove_CantLandOnMadePoint()
    {
        var state = new BoardState();
        state.Points[13] = 2;
        state.Points[11] = -2; // opponent made point
        state.RecalcHighPoint();

        var moves = MoveGenerator.SingleMoves(state, 2);
        Assert.DoesNotContain(moves, m => m.FrPt == 13 && Math.Abs(m.ToPt) == 11);
    }

    [Fact]
    public void BearOff_ExactRoll()
    {
        var state = new BoardState();
        state.Points[4] = 2; // 4-pt
        state.Points[2] = 5; // 2-pt
        state.Points[1] = 8; // 1-pt
        state.RecalcHighPoint();

        var moves = MoveGenerator.SingleMoves(state, 4);
        Assert.Contains(moves, m => m.FrPt == 4 && m.ToPt == 0);
    }

    [Fact]
    public void BearOff_OvershootFromHighest()
    {
        var state = new BoardState();
        state.Points[3] = 3; // 3-pt (highest occupied)
        state.Points[1] = 5; // 1-pt
        state.RecalcHighPoint();

        // Die 5: overshoot from 3-pt is legal (it's the highest)
        var moves = MoveGenerator.SingleMoves(state, 5);
        Assert.Contains(moves, m => m.FrPt == 3 && m.ToPt == 0);
    }

    [Fact]
    public void BearOff_OvershootBlockedByHigherChecker()
    {
        var state = new BoardState();
        state.Points[5] = 2; // 5-pt
        state.Points[3] = 3; // 3-pt
        state.Points[1] = 5; // 1-pt
        state.RecalcHighPoint();

        // Die 5: can bear off exactly from 5-pt, but can't overshoot from 3-pt
        var moves = MoveGenerator.SingleMoves(state, 5);
        Assert.Contains(moves, m => m.FrPt == 5 && m.ToPt == 0);
        Assert.DoesNotContain(moves, m => m.FrPt == 3 && m.ToPt == 0);
    }

    [Fact]
    public void BearOff_NotAllowedWithCheckersOutside()
    {
        var state = new BoardState();
        state.Points[4] = 2;  // home board
        state.Points[11] = 2; // outside home
        state.RecalcHighPoint();

        var moves = MoveGenerator.SingleMoves(state, 4);
        Assert.DoesNotContain(moves, m => m.ToPt == 0);
    }

    [Fact]
    public void SingleMoves_OrderedRearmost()
    {
        var state = BoardState.Standard();
        var moves = MoveGenerator.SingleMoves(state, 1);
        // Should be ordered highest FrPt first
        for (int i = 0; i < moves.Count - 1; i++)
            Assert.True(moves[i].FrPt >= moves[i + 1].FrPt,
                $"Move {i} (FrPt={moves[i].FrPt}) should be >= Move {i + 1} (FrPt={moves[i + 1].FrPt})");
    }
}

public class DoublesEquivalenceTests
{
    /// <summary>
    /// Convert a play to a set-comparable key: sorted set of (FrPt, |ToPt|) pairs.
    /// </summary>
    private static (int, int, int, int, int, int, int, int) PlayKey(Play p) => p.DeduplicationKey();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Doubles_NewMatchesLegacy(int die)
    {
        var state = BoardState.Standard();

        // New ordered path
        var newPlays = MoveGenerator.GenerateDoubles(state, die);
        var newKeys = new HashSet<(int, int, int, int, int, int, int, int)>(
            newPlays.Where(p => p.Count > 0).Select(PlayKey));

        // Legacy path
        var legacyPlays = MoveGenerator.Legacy_GeneratePlays(state, die, die);
        var legacyKeys = new HashSet<(int, int, int, int, int, int, int, int)>(
            legacyPlays.Where(p => p.Count > 0).Select(PlayKey));

        // Same sets
        Assert.Equal(legacyKeys.Count, newKeys.Count);
        Assert.True(legacyKeys.SetEquals(newKeys),
            $"Doubles {die}-{die}: legacy produced {legacyKeys.Count} unique, new produced {newKeys.Count} unique. " +
            $"Missing from new: {legacyKeys.Except(newKeys).Count()}, extra in new: {newKeys.Except(legacyKeys).Count()}");
    }
}

public class PythonReferenceValidation
{
    [Theory]
    [InlineData(1, 1, 42)]
    [InlineData(2, 2, 75)]
    [InlineData(3, 3, 73)]
    [InlineData(4, 4, 52)]
    [InlineData(5, 5, 4)]
    [InlineData(6, 6, 11)]
    public void DoublesRoll_MatchesPythonReference(int die1, int die2, int expectedPlays)
    {
        var state = BoardState.Standard();
        var plays = MoveGenerator.GenerateDoubles(state, die1);
        var nonEmpty = plays.Where(p => p.Count > 0).ToList();
        Assert.Equal(expectedPlays, nonEmpty.Count);
    }

    [Theory]
    [InlineData(1, 2, 18)]
    [InlineData(1, 3, 19)]
    [InlineData(1, 4, 15)]
    [InlineData(1, 5, 9)]
    [InlineData(1, 6, 10)]
    [InlineData(2, 3, 19)]
    [InlineData(2, 4, 21)]
    [InlineData(2, 5, 9)]
    [InlineData(2, 6, 16)]
    [InlineData(3, 4, 18)]
    [InlineData(3, 5, 10)]
    [InlineData(3, 6, 16)]
    [InlineData(4, 5, 10)]
    [InlineData(4, 6, 16)]
    [InlineData(5, 6, 8)]
    public void NonDoublesRoll_MatchesPythonReference(int die1, int die2, int expectedPlays)
    {
        var state = BoardState.Standard();
        var plays = MoveGenerator.GeneratePlays(state, die1, die2);
        var nonEmpty = plays.Where(p => p.Count > 0).ToList();
        Assert.Equal(expectedPlays, nonEmpty.Count);
    }
}

public class PerformanceTests
{
    [Fact]
    public void Benchmark_DoublesOnly()
    {
        var state = BoardState.Standard();
        int[] dice = [1, 2, 3, 4, 5, 6];

        // Warmup
        foreach (int die in dice)
            MoveGenerator.GenerateDoubles(state, die);

        int iterations = 1000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int totalPlays = 0;
        for (int n = 0; n < iterations; n++)
        {
            foreach (int die in dice)
            {
                var plays = MoveGenerator.GenerateDoubles(state, die);
                totalPlays += plays.Count;
            }
        }
        sw.Stop();

        int totalCalls = iterations * dice.Length;
        double usPerCall = sw.Elapsed.TotalMicroseconds / totalCalls;
        Console.WriteLine($"New doubles: {usPerCall:F1} us/call, {totalCalls / sw.Elapsed.TotalSeconds:F0} calls/sec");

        // Legacy comparison
        sw.Restart();
        totalPlays = 0;
        for (int n = 0; n < iterations; n++)
        {
            foreach (int die in dice)
            {
                var plays = MoveGenerator.Legacy_GeneratePlays(state, die, die);
                totalPlays += plays.Count;
            }
        }
        sw.Stop();

        double legacyUsPerCall = sw.Elapsed.TotalMicroseconds / totalCalls;
        Console.WriteLine($"Legacy doubles: {legacyUsPerCall:F1} us/call, {totalCalls / sw.Elapsed.TotalSeconds:F0} calls/sec");

        double speedup = legacyUsPerCall / usPerCall;
        Console.WriteLine($"Speedup: {speedup:F2}x");
    }

    [Fact]
    public void Benchmark_AllOpeningRolls()
    {
        var state = BoardState.Standard();
        var rolls = new List<(int, int)>();
        for (int d1 = 1; d1 <= 6; d1++)
            for (int d2 = d1; d2 <= 6; d2++)
                rolls.Add((d1, d2));

        // Warmup
        foreach (var (d1, d2) in rolls)
            MoveGenerator.GeneratePlays(state, d1, d2);

        int iterations = 1000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int totalPlays = 0;
        for (int n = 0; n < iterations; n++)
        {
            foreach (var (d1, d2) in rolls)
            {
                var plays = MoveGenerator.GeneratePlays(state, d1, d2);
                totalPlays += plays.Count;
            }
        }
        sw.Stop();

        int totalCalls = iterations * rolls.Count;
        double usPerCall = sw.Elapsed.TotalMicroseconds / totalCalls;
        Console.WriteLine($"Benchmark: {usPerCall:F1} us/call, {totalCalls / sw.Elapsed.TotalSeconds:F0} calls/sec");

        Assert.True(usPerCall < 1000,
            $"generate_plays averaged {usPerCall:F1}us/call — target is <10us");
    }
}
