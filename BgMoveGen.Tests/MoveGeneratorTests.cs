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

    [Fact]
    public void Optimized_MatchesReference_AcrossSyntheticPositions()
    {
        // The InlineData rows above are hand-picked boards. This is the
        // breadth counterpart, and specifically the guard on the avoidance
        // dedup: every skip pass 2 makes is a claim that pass 1 already
        // emitted the play, and the failure mode of a wrong skip is a *missing*
        // legal play — invisible to a distinctness pin, which only ever
        // complains about too many. Ground truth is the brute-force reference.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(1_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);

                var refStates = GetBoardStates(state, MoveGenerator.Reference_GeneratePlays(state, die1, die2));
                var optStates = GetBoardStates(state, MoveGenerator.GeneratePlays(state, die1, die2));

                Assert.True(refStates.SetEquals(optStates),
                    $"Position {index} {die1}-{die2}: ref={refStates.Count} states, " +
                    $"opt={optStates.Count} states, missing={refStates.Except(optStates).Count()}, " +
                    $"extra={optStates.Except(refStates).Count()}.");
            }
        }
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
        Play play = [new(24, 18), new(13, 9)];

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
        Play bogus = [
            new(24, 17),   // distance 7 — neither die
            new(13, 9),
        ];

        Assert.False(MoveGenerator.IsLegalPlay(state, bogus, 6, 4));
    }

    [Fact]
    public void Standard_TooFewMoves_ReturnsFalse()
    {
        // Single-move "play" when both dice are usable — fails the must-use-both
        // requirement, so cannot be in the legal set.
        var state = BoardState.Standard();
        Play stub = [new(24, 18)];   // uses the 6 only

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
    public void GeneratePlays_NoLegalMove_ReturnsSingleEmptyPlay()
    {
        // Pins the no-legal-move convention: a dance / closed-out position
        // yields a one-element list holding the empty pass play, never an empty
        // list. Same closed-out setup as EmptyPlay_OnClosedOutPosition_ReturnsTrue:
        // on-roll has a bar checker, opponent holds every entry point (19..24).
        var state = new BoardState();
        state.Points[25] = 1;
        for (int i = 19; i <= 24; i++) state.Points[i] = -2;
        state.RecalcHighPoint();

        var plays = MoveGenerator.GeneratePlays(state, 6, 4);

        Assert.Single(plays);
        Assert.Equal(0, plays[0].Count);
    }

    [Fact]
    public void HitSensitive_HitlessEncodingOfHittingPlay_IsRejected()
    {
        // Deliberate inversion of the old HitInvariant_DeduplicationKey pin
        // (mirroring the producer-side reversal in BgDataTypes_Lib's PlayTests).
        // The old DeduplicationKey stripped the hit sign, so a play differing
        // from a legal one only in the hit flag still matched — which underlay
        // the ApplyPlay/IsLegalPlay silent board-corruption hazard: a hit-less
        // encoding could validate and then apply without barring the blot.
        // Canonical Play equality is fully hit-sensitive: 24/18 is not the
        // same play as 24/18*, so the hit-less encoding is rejected.
        var state = new BoardState();
        state.Points[24] = 2;
        state.Points[13] = 5;
        state.Points[6] = 5;
        state.Points[8] = 3;
        state.Points[18] = -1;   // opponent blot — a 6 from 24 must hit it
        state.Points[1] = -2;
        state.Points[12] = -5;
        state.Points[17] = -2;
        state.Points[19] = -5;
        state.RecalcHighPoint();

        Play noHitVariant = [
            new(24, 18),   // ToPt positive — hit-less form
            new(13, 9),    // 13/9 with the 4
        ];

        Assert.False(MoveGenerator.IsLegalPlay(state, noHitVariant, 6, 4));

        // The correctly-encoded hit form is, of course, legal.
        Play hitVariant = [new(24, -18), new(13, 9)];

        Assert.True(MoveGenerator.IsLegalPlay(state, hitVariant, 6, 4));
    }

    [Fact]
    public void DecomposedEntry_MatchesCombinedCandidate()
    {
        // The quiz-entry repro that motivated canonical equality: a play entered
        // as decomposed hops must match the candidate list regardless of which
        // encoding the generator kept and which intermediate the entry routed
        // through. From Standard with 3-2, 13/8 is legal with both intermediates
        // (10 and 11) open; the generator emits exactly one encoding. Under the
        // old raw-hop DeduplicationKey, whichever decomposition the generator
        // did not keep failed to match — now all encodings of 13/8 are one play.
        var state = BoardState.Standard();

        Play viaTen = [new(13, 10), new(10, 8)];      // big die first
        Play viaEleven = [new(13, 11), new(11, 8)];   // small die first
        Play combined = [new(13, 8)];                 // the analyzer-style collapsed form

        Assert.True(MoveGenerator.IsLegalPlay(state, viaTen, 3, 2));
        Assert.True(MoveGenerator.IsLegalPlay(state, viaEleven, 3, 2));
        Assert.True(MoveGenerator.IsLegalPlay(state, combined, 3, 2));
    }

    [Theory]
    // Standard opening — all 21 rolls
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(1, 5)]
    [InlineData(1, 6)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(2, 4)]
    [InlineData(2, 5)]
    [InlineData(2, 6)]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    [InlineData(3, 5)]
    [InlineData(3, 6)]
    [InlineData(4, 4)]
    [InlineData(4, 5)]
    [InlineData(4, 6)]
    [InlineData(5, 5)]
    [InlineData(5, 6)]
    [InlineData(6, 6)]
    public void GeneratePlays_CandidatesAreCanonicallyDistinct(int die1, int die2)
    {
        // The generator dedups candidates by final board state, and equal
        // canonical forms reach equal final states — so the candidate list must
        // already be duplicate-free under canonical Play equality. This pins
        // that alignment: if generation ever emitted two encodings of one play,
        // consumers keying on Play equality (quiz scoring, IsLegalPlay) would
        // see a double candidate.
        var state = BoardState.Standard();
        var plays = MoveGenerator.GeneratePlays(state, die1, die2);

        for (int i = 0; i < plays.Count; i++)
            for (int j = i + 1; j < plays.Count; j++)
                Assert.False(plays[i] == plays[j],
                    $"Candidates {i} {Raw(plays[i])} and {j} {Raw(plays[j])} " +
                    $"are canonically equal for {die1}-{die2}.");
    }

    [Fact]
    public void GeneratePlays_TwoDieBearOff_EmitsTheOnePlayOnce()
    {
        // The minimal counterexample to the opening board's distinctness: two
        // on-roll checkers, on the 5- and the 4-point, nothing else, roll 6-5.
        // Each die bears one checker off, and a bear-off encodes as
        // (point, 0) whichever die paid for it — so both die assignments
        // build the identical move pair, and pass 2's order-duplicate
        // avoidance, which compares moves rather than dice, used to miss it.
        // One play, one candidate.
        var mop = new int[26];
        mop[5] = 1;
        mop[4] = 1;

        var plays = MoveGenerator.GeneratePlays(BoardState.FromMop(mop), 6, 5);

        var only = Assert.Single(plays);
        Assert.Equal(2, only.Count);
        Assert.Equal(MoveGenerator.GeneratePlays(BoardState.FromMop(mop), 5, 6).Count, plays.Count);
        Assert.True(MoveGenerator.IsLegalPlay(BoardState.FromMop(mop), only, 6, 5));
    }

    [Fact]
    public void GeneratePlays_CandidatesAreCanonicallyDistinct_AcrossSyntheticPositions()
    {
        // The opening-board theory above pins one board. This pins breadth:
        // the whole deterministic corpus crossed with all 21 rolls. The
        // opening board never reaches a bear-off, so it could not have caught
        // the two-die bear-off duplicate; half of this corpus is drawn inside
        // the home board precisely so it can.
        var distinct = new HashSet<Play>();
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(4_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var plays = MoveGenerator.GeneratePlays(BoardState.FromMop(mop), die1, die2);

                // Play.GetHashCode is canonical (it hashes ToCanonical), so a
                // set does the whole check in one pass; the quadratic scan runs
                // only to name the offending pair once something is wrong.
                distinct.Clear();
                foreach (var play in plays)
                    distinct.Add(play);
                if (distinct.Count == plays.Count)
                    continue;

                for (int i = 0; i < plays.Count; i++)
                    for (int j = i + 1; j < plays.Count; j++)
                        Assert.False(plays[i] == plays[j],
                            $"Position {index} ({Mop(mop)}) {die1}-{die2}: candidates " +
                            $"{i} {Raw(plays[i])} and {j} {Raw(plays[j])} are canonically equal.");

                Assert.Fail($"Position {index} ({Mop(mop)}) {die1}-{die2}: " +
                            $"{plays.Count} candidates collapse to {distinct.Count} " +
                            "under canonical equality, but no pair compares equal — " +
                            "Play equality and Play.GetHashCode have drifted apart.");
            }
        }
    }

    private static string Mop(int[] mop)
    {
        var parts = new List<string>();
        for (int i = 0; i < mop.Length; i++)
            if (mop[i] != 0) parts.Add($"{i}:{mop[i]}");
        return string.Join(" ", parts);
    }

    private static string Raw(Play p)
    {
        var parts = new List<string>(p.Count);
        for (int i = 0; i < p.Count; i++)
            parts.Add($"({p[i].FrPt},{p[i].ToPt})");
        return "{" + string.Join(" ", parts) + "}";
    }

    [Fact]
    public void GeneratePlays_TwoBarEntries_SingleCandidate()
    {
        // Two checkers on the bar, both entry points open, non-doubles: the
        // only legal play enters both — one play, whichever die goes first.
        // The bigDie-first bar branch re-derived it move-for-move in the other
        // order, so without the same-source skip this was a double candidate.
        var state = new BoardState();
        state.Points[25] = 2;
        state.Points[13] = 5;
        state.Points[19] = -2;
        state.Points[24] = -2;
        state.RecalcHighPoint();

        var plays = MoveGenerator.GeneratePlays(state, 2, 3);

        Play expected = [new(25, 23), new(25, 22)];

        var only = Assert.Single(plays);
        Assert.Equal(expected, only);
    }

    [Fact]
    public void GeneratePlays_SamePointBearOffPair_SingleCandidate()
    {
        // Two checkers on the 2-point (nothing else), dice 2-5: the exact
        // bear-off (die 2) and the overshoot bear-off (die 5) both encode as
        // (2, 0), so the play is the same in either die order. The bigDie-first
        // pass re-derived it — without the same-source skip, a double candidate.
        var state = new BoardState();
        state.Points[2] = 2;
        state.RecalcHighPoint();

        var plays = MoveGenerator.GeneratePlays(state, 2, 5);

        Play expected = [new(2, 0), new(2, 0)];

        var only = Assert.Single(plays);
        Assert.Equal(expected, only);
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

        Play play = [new(24, 18), new(13, 9)];

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
        Play bogus = [
            new(24, 17),   // distance 7 — illegal under 6-4
            new(13, 9),
        ];

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

        Play bogus = [new(24, 17), new(13, 9)];

        Assert.Throws<ArgumentException>(
            () => MoveGenerator.ApplyPlay(state, bogus, 6, 4));

        Assert.Equal(snapshotPoints, state.Points);
        Assert.Equal(snapshotHigh, state.HighPointOccupied);
    }

    [Fact]
    public void HitlessEncodingOfHittingPlay_Throws_AndCannotCorruptBoard()
    {
        // Closes the booked silent-corruption hazard. Under the old hit-blind
        // DeduplicationKey, this hit-less encoding of the forced hit 24/18*
        // validated as legal and then applied verbatim — moving onto the blot
        // without sending it to the bar (the blot was annihilated in place).
        // Hit-sensitive canonical equality rejects the encoding outright, and
        // throw-before-mutate leaves the board (blot included) untouched.
        var state = new BoardState();
        state.Points[24] = 2;
        state.Points[13] = 5;
        state.Points[6] = 5;
        state.Points[8] = 3;
        state.Points[18] = -1;   // opponent blot — a 6 from 24 must hit it
        state.Points[1] = -2;
        state.Points[12] = -5;
        state.Points[17] = -2;
        state.Points[19] = -5;
        state.RecalcHighPoint();

        int[] snapshotPoints = (int[])state.Points.Clone();

        Play noHitVariant = [
            new(24, 18),   // hit-less form of the hitting play
            new(13, 9),
        ];

        Assert.Throws<ArgumentException>(
            () => MoveGenerator.ApplyPlay(state, noHitVariant, 6, 4));

        Assert.Equal(snapshotPoints, state.Points);
        Assert.Equal(-1, state.Points[18]);   // blot still on the board
    }

    [Fact]
    public void DecomposedEncoding_AppliesTheMatchedCandidate_SameFinalState()
    {
        // ApplyPlay applies the generator's encoding of the matched play, not
        // the caller's hops. For an ordinary open-board decomposition the two
        // are interchangeable — the final state must equal that of the
        // generator's own candidate applied directly.
        var state = BoardState.Standard();

        Play decomposed = [new(13, 10), new(10, 8)];

        var legal = MoveGenerator.GeneratePlays(BoardState.Standard(), 3, 2);
        var candidate = legal.Single(p => p == decomposed);

        var direct = BoardState.Standard();
        direct.ApplyPlay(candidate);

        MoveGenerator.ApplyPlay(state, decomposed, 3, 2);

        for (int i = 0; i <= 25; i++)
            Assert.Equal(direct.Points[i], state.Points[i]);
        Assert.Equal(direct.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void DecomposedEncoding_ThroughBlockedIntermediate_AppliesSafely()
    {
        // The sharp edge of canonical equality: it is deliberately insensitive
        // to which intermediate a trajectory touches, so a caller's decomposition
        // can name a blocked point even though the play itself (13/8 with 3-2,
        // legal via the open 11) is fine. Applying the caller's hops verbatim
        // would land a checker on the opponent's 10-point anchor and corrupt the
        // board; applying the matched candidate's encoding realizes the play
        // safely. Pins the canonicalize-then-apply contract.
        var state = new BoardState();
        state.Points[13] = 2;
        state.Points[6] = 2;
        state.Points[10] = -2;   // opponent anchor — blocks the 13/10 route
        state.Points[20] = -2;
        state.RecalcHighPoint();

        Play decomposed = [
            new(13, 10),   // routes through the blocked point
            new(10, 8),
        ];

        Assert.True(MoveGenerator.IsLegalPlay(state, decomposed, 3, 2));

        var legal = MoveGenerator.GeneratePlays(state, 3, 2);
        var candidate = legal.Single(p => p == decomposed);

        var direct = state.Copy();
        direct.ApplyPlay(candidate);

        MoveGenerator.ApplyPlay(state, decomposed, 3, 2);

        for (int i = 0; i <= 25; i++)
            Assert.Equal(direct.Points[i], state.Points[i]);
        Assert.Equal(direct.HighPointOccupied, state.HighPointOccupied);
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