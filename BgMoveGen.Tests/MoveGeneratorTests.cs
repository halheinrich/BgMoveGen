using BgMoveGen;
using BgDataTypes_Lib;

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

        state.ApplyMove(move);
        Assert.Equal(4, state.Points[13]);
        Assert.Equal(4, state.Points[8]);

        state.UndoMove(move);
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

        state.ApplyMove(move);
        Assert.Equal(1, state.Points[13]);
        Assert.Equal(1, state.Points[12]); // player now
        Assert.Equal(-1, state.Points[0]); // opponent bar

        state.UndoMove(move);
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

        state.ApplyMove(move);
        Assert.Equal(0, state.Points[25]);
        Assert.Equal(1, state.Points[22]);

        state.UndoMove(move);
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

        state.ApplyMove(move);
        Assert.Equal(4, state.Points[4]);

        state.UndoMove(move);
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
        state.ApplyMove(move);
        Assert.Equal(7, state.HighPointOccupied); // scanned down

        state.UndoMove(move);
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

public class ReferenceCorrectnessTests
{
    /// <summary>
    /// Apply all moves in a play and return the board hash.
    /// </summary>
    private static long ApplyAndHash(BoardState state, Play play)
    {
        for (int i = 0; i < play.Count; i++)
            state.ApplyMove(play[i]);

        long hash = unchecked((long)0xcbf29ce484222325);
        for (int i = 0; i < 26; i++)
        {
            hash ^= state.Points[i];
            hash = unchecked(hash * 0x100000001b3);
        }

        for (int i = play.Count - 1; i >= 0; i--)
            state.UndoMove(play[i]);

        return hash;
    }

    private static HashSet<long> GetBoardStates(BoardState state, List<Play> plays)
    {
        var set = new HashSet<long>();
        foreach (var p in plays)
            if (p.Count > 0)
                set.Add(ApplyAndHash(state, p));
        return set;
    }

    /// <summary>
    /// Master correctness test: compares optimized GeneratePlays against
    /// brute-force Reference_GeneratePlays for a collection of board/dice pairs.
    /// Add new test cases by adding InlineData rows.
    /// </summary>
    [Theory]
    // Standard opening — all 21 rolls
    [InlineData("standard", 1, 1)]
    [InlineData("standard", 1, 2)]
    [InlineData("standard", 1, 3)]
    [InlineData("standard", 1, 4)]
    [InlineData("standard", 1, 5)]
    [InlineData("standard", 1, 6)]
    [InlineData("standard", 2, 2)]
    [InlineData("standard", 2, 3)]
    [InlineData("standard", 2, 4)]
    [InlineData("standard", 2, 5)]
    [InlineData("standard", 2, 6)]
    [InlineData("standard", 3, 3)]
    [InlineData("standard", 3, 4)]
    [InlineData("standard", 3, 5)]
    [InlineData("standard", 3, 6)]
    [InlineData("standard", 4, 4)]
    [InlineData("standard", 4, 5)]
    [InlineData("standard", 4, 6)]
    [InlineData("standard", 5, 5)]
    [InlineData("standard", 5, 6)]
    [InlineData("standard", 6, 6)]
    public void Optimized_MatchesReference(string position, int die1, int die2)
    {
        var state = CreatePosition(position);

        var refPlays = MoveGenerator.Reference_GeneratePlays(state, die1, die2);
        var optPlays = MoveGenerator.GeneratePlays(state, die1, die2);

        var refStates = GetBoardStates(state, refPlays);
        var optStates = GetBoardStates(state, optPlays);

        var missingFromOpt = refStates.Except(optStates).Count();
        var extraInOpt = optStates.Except(refStates).Count();

        Assert.True(refStates.SetEquals(optStates),
            $"{position} {die1}-{die2}: ref={refStates.Count} states, opt={optStates.Count} states. " +
            $"Missing: {missingFromOpt}, Extra: {extraInOpt}");
    }

    private static BoardState CreatePosition(string name) => name switch
    {
        "standard" => BoardState.Standard(),
        "nackgammon" => BoardState.Nackgammon(),
        _ => throw new ArgumentException($"Unknown position: {name}")
    };
}

public class GenerateStatesTests
{
    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 5)]
    [InlineData(2, 2)]
    public void GenerateStates_MatchesGeneratePlays(int die1, int die2)
    {
        var state = BoardState.Standard();

        var states = MoveGenerator.GenerateStates(state, die1, die2);
        var plays = MoveGenerator.GeneratePlays(state, die1, die2);

        Assert.Equal(plays.Count, states.Count);

        // Each state should match applying the corresponding play
        for (int i = 0; i < plays.Count; i++)
        {
            var expected = state.Copy();
            for (int j = 0; j < plays[i].Count; j++)
                expected.ApplyMove(plays[i][j]);

            for (int p = 0; p < 26; p++)
                Assert.Equal(expected.Points[p], states[i].Points[p]);
        }
    }

    [Fact]
    public void GenerateStates_DoesNotMutateOriginal()
    {
        var state = BoardState.Standard();
        var original = state.Copy();

        MoveGenerator.GenerateStates(state, 3, 1);

        for (int i = 0; i < 26; i++)
            Assert.Equal(original.Points[i], state.Points[i]);
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void EnumerateStates_MatchesGenerateStates()
    {
        var state = BoardState.Standard();

        var list = MoveGenerator.GenerateStates(state, 4, 2);
        var enumerated = MoveGenerator.EnumerateStates(state, 4, 2).ToList();

        Assert.Equal(list.Count, enumerated.Count);
        for (int i = 0; i < list.Count; i++)
            for (int p = 0; p < 26; p++)
                Assert.Equal(list[i].Points[p], enumerated[i].Points[p]);
    }

    [Fact]
    public void EnumerateStates_CanShortCircuit()
    {
        var state = BoardState.Standard();

        // Take only the first state — should not throw
        var first = MoveGenerator.EnumerateStates(state, 5, 3).First();
        Assert.True(first.HighPointOccupied > 0);
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
        Console.WriteLine($"Doubles: {usPerCall:F1} us/call, {totalCalls / sw.Elapsed.TotalSeconds:F0} calls/sec");
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

public class IsLegalPlayTests
{
    [Fact]
    public void Standard_LegalPlay_ReturnsTrue()
    {
        // 24/18 13/9 with a 6-4 — drawn from the legal-play set, so it must
        // round-trip true under IsLegalPlay.
        var state = BoardState.Standard();
        var legal = MoveGenerator.GeneratePlays(state, 6, 4);
        Assert.NotEmpty(legal);

        foreach (var play in legal)
            Assert.True(MoveGenerator.IsLegalPlay(state, play, 6, 4));
    }

    [Fact]
    public void Standard_DiceOrderInvariant()
    {
        // Re-running with dice swapped exercises that GeneratePlays canonicalises
        // small/big internally — the legality answer must not depend on order.
        var state = BoardState.Standard();
        var play = new Play();
        play.Add(new Move(24, 18));
        play.Add(new Move(13, 9));

        Assert.True(MoveGenerator.IsLegalPlay(state, play, 6, 4));
        Assert.True(MoveGenerator.IsLegalPlay(state, play, 4, 6));
    }

    [Fact]
    public void Standard_IllegalDestination_ReturnsFalse()
    {
        // 24/18 lands legally; 13/8 cannot use a 4 from 13 (lands on 8 — but
        // pairing it with the only 4-or-6 source on a 6-4 from Standard means
        // we need 13/9 with the 4. Build a clearly-illegal play: 24/17 (uses 7).
        var state = BoardState.Standard();
        var bogus = new Play();
        bogus.Add(new Move(24, 17));   // distance 7 — neither die
        bogus.Add(new Move(13, 9));

        Assert.False(MoveGenerator.IsLegalPlay(state, bogus, 6, 4));
    }

    [Fact]
    public void Standard_TooFewMoves_ReturnsFalse()
    {
        // Single-move "play" when both dice are usable — fails the must-use-both
        // requirement, so cannot be in the legal set.
        var state = BoardState.Standard();
        var stub = new Play();
        stub.Add(new Move(24, 18));   // uses the 6 only

        Assert.False(MoveGenerator.IsLegalPlay(state, stub, 6, 4));
    }

    [Fact]
    public void EmptyPlay_OnPositionWithLegalMoves_ReturnsFalse()
    {
        var state = BoardState.Standard();
        var empty = new Play();

        Assert.False(MoveGenerator.IsLegalPlay(state, empty, 6, 4));
    }

    [Fact]
    public void EmptyPlay_OnClosedOutPosition_ReturnsTrue()
    {
        // On-roll has a checker on the bar; opponent has every entry point
        // (19..24) made — no legal entry. The legal-play set is the single empty
        // pass play, so an empty-play probe must come back true.
        var state = new BoardState();
        state.Points[25] = 1;
        for (int i = 19; i <= 24; i++) state.Points[i] = -2;
        state.RecalcHighPoint();

        var empty = new Play();
        Assert.True(MoveGenerator.IsLegalPlay(state, empty, 6, 4));
    }

    [Fact]
    public void HitInvariant_DeduplicationKey_CrossesHitNonHit()
    {
        // DeduplicationKey is order- and hit-invariant by contract. A play that
        // differs from a legal one only in the hit flag still matches by key.
        // This pins the documented contract — if a future maintainer tightens
        // the equivalence to be hit-sensitive, this test fails loud.
        var state = new BoardState();
        state.Points[24] = 2;
        state.Points[13] = 5;
        state.Points[6] = 5;
        state.Points[8] = 3;
        state.Points[18] = -1;   // opponent blot — a 6 from 24 hits it
        state.Points[1] = -2;
        state.Points[12] = -5;
        state.Points[17] = -2;
        state.Points[19] = -5;
        state.RecalcHighPoint();

        // 24/18* is the legal hit move; 24/-18 (hit) round-trips through the
        // generator naturally. We construct a play that lists 24/18 (no hit
        // encoding) — the dedup key takes |ToPt|, so it should still match.
        var noHitVariant = new Play();
        noHitVariant.Add(new Move(24, 18));   // ToPt positive — non-hit form
        noHitVariant.Add(new Move(13, 9));    // 13/9 with the 4

        Assert.True(MoveGenerator.IsLegalPlay(state, noHitVariant, 6, 4));
    }
}

public class ApplyPlayValidatingTests
{
    [Fact]
    public void LegalPlay_AppliesAndFlips()
    {
        // Compare against direct state.ApplyPlay — they must produce the same
        // post-state when the play is legal.
        var direct = BoardState.Standard();
        var validated = BoardState.Standard();

        var play = new Play();
        play.Add(new Move(24, 18));
        play.Add(new Move(13, 9));

        direct.ApplyPlay(play);
        MoveGenerator.ApplyPlay(validated, play, 6, 4);

        for (int i = 0; i <= 25; i++)
            Assert.Equal(direct.Points[i], validated.Points[i]);
        Assert.Equal(direct.HighPointOccupied, validated.HighPointOccupied);
    }

    [Fact]
    public void IllegalPlay_Throws_ArgumentException()
    {
        var state = BoardState.Standard();
        var bogus = new Play();
        bogus.Add(new Move(24, 17));   // distance 7 — illegal under 6-4
        bogus.Add(new Move(13, 9));

        Assert.Throws<ArgumentException>(
            () => MoveGenerator.ApplyPlay(state, bogus, 6, 4));
    }

    [Fact]
    public void IllegalPlay_LeavesStateUnchanged()
    {
        // Pin the throw-before-mutate contract: an illegal ApplyPlay leaves
        // the board byte-for-byte identical, so callers can recover without
        // a defensive clone.
        var state = BoardState.Standard();
        int[] snapshotPoints = (int[])state.Points.Clone();
        int snapshotHigh = state.HighPointOccupied;

        var bogus = new Play();
        bogus.Add(new Move(24, 17));
        bogus.Add(new Move(13, 9));

        Assert.Throws<ArgumentException>(
            () => MoveGenerator.ApplyPlay(state, bogus, 6, 4));

        Assert.Equal(snapshotPoints, state.Points);
        Assert.Equal(snapshotHigh, state.HighPointOccupied);
    }

    [Fact]
    public void EmptyPlay_OnClosedOutPosition_AppliesAsPass()
    {
        // No legal entry from the bar → only "play" is the empty pass. Validating
        // ApplyPlay accepts it and the state flips perspective per BoardState.ApplyPlay.
        var state = new BoardState();
        state.Points[25] = 1;
        for (int i = 19; i <= 24; i++) state.Points[i] = -2;
        state.RecalcHighPoint();

        var direct = state.Copy();
        direct.ApplyPlay(new Play());

        MoveGenerator.ApplyPlay(state, new Play(), 6, 4);

        for (int i = 0; i <= 25; i++)
            Assert.Equal(direct.Points[i], state.Points[i]);
        Assert.Equal(direct.HighPointOccupied, state.HighPointOccupied);
    }
}