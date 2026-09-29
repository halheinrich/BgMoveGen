using BgMoveGen;
using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;

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
        copy.ApplyMove(new Move(13, 8));
        Assert.Equal(BoardPosition.Standard, state.ToPosition());
        Assert.NotEqual(BoardPosition.Standard, copy.ToPosition());
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
        var mop = new int[26];
        mop[1] = 5; mop[3] = 5; mop[5] = 5;
        var state = BoardState.FromMop(mop);
        Assert.True(state.HighPointOccupied <= 6);
    }

    [Fact]
    public void CanBearOff_FalseWhenOnBar()
    {
        var mop = new int[26];
        mop[1] = 5; mop[3] = 5; mop[5] = 4;
        mop[25] = 1; // bar
        var state = BoardState.FromMop(mop);
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
        Assert.Equal(original.ToPosition(), state.ToPosition());
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void ApplyUndo_HittingMove_RoundTrips()
    {
        var mop = new int[26];
        mop[13] = 2;
        mop[12] = -1; // opponent blot
        var state = BoardState.FromMop(mop);
        var original = state.Copy();

        var move = new Move(13, -12); // hit on 12-pt

        state.ApplyMove(move);
        Assert.Equal(1, state.Points[13]);
        Assert.Equal(1, state.Points[12]); // player now
        Assert.Equal(-1, state.Points[0]); // opponent bar

        state.UndoMove(move);
        Assert.Equal(original.ToPosition(), state.ToPosition());
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void ApplyUndo_BarEntry_RoundTrips()
    {
        var mop = new int[26];
        mop[25] = 1; // on bar
        mop[6] = 5;
        var state = BoardState.FromMop(mop);
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
        var mop = new int[26];
        mop[4] = 5;
        mop[2] = 5;
        mop[1] = 5;
        var state = BoardState.FromMop(mop);
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
        var mop = new int[26];
        mop[13] = 1; // single checker on highest point
        mop[6] = 5;
        var state = BoardState.FromMop(mop);
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
        var mop = new int[26];
        BoardPosition.Standard.CopyTo(mop);
        mop[24]--; mop[25]++; // put one back checker on the bar
        var state = BoardState.FromMop(mop);

        var moves = MoveGenerator.SingleMoves(state, 3);
        Assert.All(moves, m => Assert.Equal(25, m.FrPt));
    }

    [Fact]
    public void BarEntry_BlockedByOpponent()
    {
        var mop = new int[26];
        mop[25] = 1;
        // Opponent holds all entry points (19-24)
        for (int i = 19; i <= 24; i++)
            mop[i] = -2;
        var state = BoardState.FromMop(mop);

        for (int die = 1; die <= 6; die++)
        {
            var moves = MoveGenerator.SingleMoves(state, die);
            Assert.Empty(moves);
        }
    }

    [Fact]
    public void BarEntry_CanHitBlot()
    {
        var mop = new int[26];
        mop[25] = 1;
        mop[24] = -1; // opponent blot on 24-pt
        var state = BoardState.FromMop(mop);

        var moves = MoveGenerator.SingleMoves(state, 1);
        Assert.Single(moves);
        Assert.True(moves[0].ToPt < 0); // negative = hit
        Assert.Equal(-24, moves[0].ToPt);
    }

    [Fact]
    public void RegularMove_CantLandOnMadePoint()
    {
        var mop = new int[26];
        mop[13] = 2;
        mop[11] = -2; // opponent made point
        var state = BoardState.FromMop(mop);

        var moves = MoveGenerator.SingleMoves(state, 2);
        Assert.DoesNotContain(moves, m => m.FrPt == 13 && Math.Abs(m.ToPt) == 11);
    }

    [Fact]
    public void BearOff_ExactRoll()
    {
        var mop = new int[26];
        mop[4] = 2; // 4-pt
        mop[2] = 5; // 2-pt
        mop[1] = 8; // 1-pt
        var state = BoardState.FromMop(mop);

        var moves = MoveGenerator.SingleMoves(state, 4);
        Assert.Contains(moves, m => m.FrPt == 4 && m.ToPt == 0);
    }

    [Fact]
    public void BearOff_OvershootFromHighest()
    {
        var mop = new int[26];
        mop[3] = 3; // 3-pt (highest occupied)
        mop[1] = 5; // 1-pt
        var state = BoardState.FromMop(mop);

        // Die 5: overshoot from 3-pt is legal (it's the highest)
        var moves = MoveGenerator.SingleMoves(state, 5);
        Assert.Contains(moves, m => m.FrPt == 3 && m.ToPt == 0);
    }

    [Fact]
    public void BearOff_OvershootBlockedByHigherChecker()
    {
        var mop = new int[26];
        mop[5] = 2; // 5-pt
        mop[3] = 3; // 3-pt
        mop[1] = 5; // 1-pt
        var state = BoardState.FromMop(mop);

        // Die 5: can bear off exactly from 5-pt, but can't overshoot from 3-pt
        var moves = MoveGenerator.SingleMoves(state, 5);
        Assert.Contains(moves, m => m.FrPt == 5 && m.ToPt == 0);
        Assert.DoesNotContain(moves, m => m.FrPt == 3 && m.ToPt == 0);
    }

    [Fact]
    public void BearOff_NotAllowedWithCheckersOutside()
    {
        var mop = new int[26];
        mop[4] = 2;  // home board
        mop[11] = 2; // outside home
        var state = BoardState.FromMop(mop);

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
    /// The positions <paramref name="plays"/> reach, as a set of
    /// <see cref="BoardPosition"/> values — the same value the reference
    /// deduplicates by, so the two sides compare in one definition of "the
    /// same position".
    /// </summary>
    private static HashSet<BoardPosition> GetBoardStates(BoardState state, List<Play> plays)
    {
        var set = new HashSet<BoardPosition>();
        foreach (var p in plays)
            if (p.Count > 0)
                set.Add(Replay.PositionAfter(state, p));
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

    [Fact]
    public void GeneratePlays_HoldsExactlyOnePlayPerLegalPosition_AcrossSyntheticPositions()
    {
        // halheinrich/backgammon#279, stated for position identity: the legal
        // list holds exactly one play per distinct position a legal play
        // reaches. Checked against the whole space of legal single-die
        // sequences (the reference's, before it keeps one per position), all
        // 21 rolls on a bounded, seeded sample, positions compared as
        // BoardPosition values:
        //   - no two listed plays reach one position;
        //   - every legal sequence reaches a position a listed play reaches,
        //     and is the same play as that listed play under
        //     BoardState.IsSamePlay — so the rule behind identity agrees with
        //     where the moves physically lead, for every way of writing a
        //     legal play. With the listed plays distinct as plays
        //     (GeneratePlays_CandidatesAreDistinctPlays_AcrossSyntheticPositions),
        //     that is the list match (IndexOfSamePlay) finding exactly that
        //     play, asked here per sequence at one rule evaluation instead of
        //     one per candidate;
        //   - every listed play reaches a position some legal sequence
        //     reaches: the list holds nothing a legal play cannot reach.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(PropertySample).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(state, die1, die2);
                string where = $"Position {index} ({state.ToPosition()}) {die1}-{die2}";

                var listedAt = new Dictionary<BoardPosition, int>();
                for (int i = 0; i < plays.Count; i++)
                {
                    var reached = Replay.PositionAfter(state, plays[i]);
                    if (!listedAt.TryAdd(reached, i))
                        Assert.Fail($"{where}: plays {listedAt[reached]} and {i} reach one position, {reached}.");
                }

                var legalPositions = new HashSet<BoardPosition>();
                foreach (var sequence in MoveGenerator.Reference_LegalSequences(state, die1, die2))
                {
                    var reached = Replay.PositionAfter(state, sequence);
                    legalPositions.Add(reached);

                    if (!listedAt.TryGetValue(reached, out int listed))
                        Assert.Fail($"{where}: the legal sequence {PlayText.Raw(sequence)} reaches {reached}, which no listed play reaches.");

                    if (!state.IsSamePlay(sequence, plays[listed]))
                        Assert.Fail($"{where}: the legal sequence {PlayText.Raw(sequence)} reaches play {listed}'s position, " +
                                    $"but is not the same play as {PlayText.Raw(plays[listed])}.");
                }

                foreach (var (position, i) in listedAt)
                {
                    if (!legalPositions.Contains(position))
                        Assert.Fail($"{where}: play {i} reaches {position}, which no legal sequence reaches.");
                }
            }
        }
    }

    [Fact]
    public void Reference_ALoneDieIsTheDieThatPlayedIt_EvenABearOffBelowTheLargerDie()
    {
        // A lone checker on the 2-point, 6-3: either die bears it off, and
        // then nothing is left for the other, so only one die is played — the
        // larger, by the rule. Its move (2, 0) travels 2 pips, fewer than the
        // 6, so inferring the die from the distance filed it as the smaller
        // die's, kept the smaller die's (2, 0) beside it, and was right only
        // because both reach one position. The reference records the die: the
        // one legal sequence is the larger die's.
        var mop = new int[26];
        mop[2] = 1;
        mop[24] = -2;
        var state = BoardState.FromMop(mop);

        var sequences = MoveGenerator.Reference_LegalSequences(state, 6, 3);

        var only = Assert.Single(sequences);
        Assert.True(only.IsSameEncoding([new(2, 0)]));
        Assert.True(state.IsSamePlay(only, Assert.Single(MoveGenerator.GeneratePlays(state, 6, 3))));
    }

    /// <summary>
    /// The property sweep's sample: the first positions of the shared seeded
    /// corpus. Bounded because it walks every legal sequence, not one per
    /// position, and a spread position's doubles run to thousands.
    /// </summary>
    private const int PropertySample = 1_000;

    private static BoardState CreatePosition(string name) => name switch
    {
        "standard" => BoardState.Standard(),
        "nackgammon" => BoardState.Nackgammon(),
        _ => throw new ArgumentException($"Unknown position: {name}")
    };
}

public class GenerateResultingPositionsTests
{
    // A sink the allocation pin's paths write, so no allocation can be elided.
    private static object? _kept;

    [Fact]
    public void GenerateResultingPositions_AreTheFinalPositionsOfGeneratePlays_AcrossSyntheticPositions()
    {
        // The defining relation: position i is exactly the input with
        // candidate i's moves applied — same count, same order — across a
        // corpus that reaches bar entry, hits and bear-offs, not only the
        // opening board.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(1_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(state, die1, die2);
                var positions = MoveGenerator.GenerateResultingPositions(state, die1, die2);

                Assert.Equal(plays.Count, positions.Count);
                for (int i = 0; i < plays.Count; i++)
                    Assert.True(Replay.PositionAfter(state, plays[i]) == positions[i],
                        $"Position {index} {die1}-{die2}: position {i} is not candidate {i} applied.");
            }
        }
    }

    [Fact]
    public void GenerateResultingPositions_AreInMoversFrame_NotFlipped()
    {
        // Opening 3-1 cannot hit, so in the mover's frame every resulting
        // position keeps the opponent's four stacks exactly where they
        // started; a flip would move them to 25 - i. The point-making 8/5 6/5
        // must be present exactly as the mover sees it.
        var positions = MoveGenerator.GenerateResultingPositions(BoardState.Standard(), 3, 1);

        foreach (var p in positions)
        {
            Assert.Equal(-2, p[1]);
            Assert.Equal(-5, p[12]);
            Assert.Equal(-3, p[17]);
            Assert.Equal(-5, p[19]);
        }

        Assert.Contains(Boards.OpeningAfterMakingTheFivePoint().ToPosition(), positions);
    }

    [Fact]
    public void GenerateResultingPositions_NoLegalMove_ReturnsTheInputPosition()
    {
        var state = Boards.ClosedOut();

        var only = Assert.Single(MoveGenerator.GenerateResultingPositions(state, 6, 4));

        Assert.Equal(state.ToPosition(), only);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 5)]
    [InlineData(2, 2)]
    public void GenerateResultingPositions_DoesNotMutateInput(int die1, int die2)
    {
        var state = BoardState.Standard();

        MoveGenerator.GenerateResultingPositions(state, die1, die2);

        Assert.Equal(BoardPosition.Standard, state.ToPosition());
        Assert.Equal(BoardState.Standard().HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void GenerateResultingPositions_AreDistinct_AcrossSyntheticPositions()
    {
        // The reference sweep compares position *sets*, which a repeated
        // position survives, so this compares the positions themselves, as
        // BoardPosition values whose equality decides — a hash only buckets
        // them. It pins the property a consumer choosing among positions
        // relies on: no position is returned — and so weighted — twice.
        var distinct = new HashSet<BoardPosition>();
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(4_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var positions = MoveGenerator.GenerateResultingPositions(BoardState.FromMop(mop), die1, die2);

                distinct.Clear();
                foreach (var p in positions)
                    distinct.Add(p);

                Assert.True(distinct.Count == positions.Count,
                    $"Position {index} {die1}-{die2}: {positions.Count} resulting positions " +
                    $"collapse to {distinct.Count} distinct ones.");
            }
        }
    }

    [Theory]
    [InlineData(6, 4)]
    [InlineData(3, 3)]
    public void GenerateResultingPositions_AllocatesOnlyItsList_NothingPerCandidate(int die1, int die2)
    {
        // The positions are values: the call allocates what GeneratePlays
        // does plus the one BoardPosition array it returns, to the byte. A
        // board built per candidate, or a position boxed, would show here.
        var state = BoardState.Standard();
        int count = MoveGenerator.GeneratePlays(state, die1, die2).Count;
        Assert.True(count > 1);

        long positions = AllocationProbe.SteadyStateBytes(() => _kept = MoveGenerator.GenerateResultingPositions(state, die1, die2));
        long plays = AllocationProbe.SteadyStateBytes(() => _kept = MoveGenerator.GeneratePlays(state, die1, die2));
        long array = AllocationProbe.SteadyStateBytes(() => _kept = new BoardPosition[count]);

        Assert.Equal(plays + array, positions);
    }
}

public class GenerateCandidatePlaysTests
{
    // A sink the allocation pin's paths write, so no allocation can be elided.
    private static object? _kept;

    [Fact]
    public void GenerateCandidatePlays_PairsEachGeneratedPlayWithItsResultingPosition_AcrossSyntheticPositions()
    {
        // The association is the whole contract: candidate i carries
        // GeneratePlays' candidate i in the generator's own encoding (the
        // identical moves, not merely the same play) and exactly the position
        // GenerateResultingPositions returns at i — which is that play applied.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(1_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(state, die1, die2);
                var positions = MoveGenerator.GenerateResultingPositions(state, die1, die2);
                var candidates = MoveGenerator.GenerateCandidatePlays(state, die1, die2);

                Assert.Equal(plays.Count, candidates.Count);
                for (int i = 0; i < plays.Count; i++)
                {
                    string where = $"Position {index} {die1}-{die2} candidate {i}";
                    Assert.True(plays[i].IsSameEncoding(candidates[i].Play), $"{where}: play is not the generator's encoding.");
                    Assert.True(Replay.PositionAfter(state, plays[i]) == candidates[i].ResultingPosition,
                        $"{where}: the resulting position is not the play applied.");
                    Assert.True(positions[i] == candidates[i].ResultingPosition,
                        $"{where}: the resulting position differs from GenerateResultingPositions.");
                }
            }
        }
    }

    [Fact]
    public void GenerateCandidatePlays_ResultingPositionsAreInMoversFrame_NotFlipped()
    {
        // Opening 3-1 cannot hit: every position keeps the opponent's stacks
        // in place, and the 8/5 6/5 candidate carries the point-made position
        // as the mover sees it.
        var state = BoardState.Standard();

        var candidates = MoveGenerator.GenerateCandidatePlays(state, 3, 1);

        foreach (var c in candidates)
        {
            Assert.Equal(-2, c.ResultingPosition[1]);
            Assert.Equal(-5, c.ResultingPosition[12]);
            Assert.Equal(-3, c.ResultingPosition[17]);
            Assert.Equal(-5, c.ResultingPosition[19]);
        }

        Play eightFiveSixFive = [new(8, 5), new(6, 5)];
        var makePoint = Assert.Single(candidates, c => state.IsSamePlay(c.Play, eightFiveSixFive));
        Assert.Equal(Boards.OpeningAfterMakingTheFivePoint().ToPosition(), makePoint.ResultingPosition);
    }

    [Fact]
    public void GenerateCandidatePlays_NoLegalMove_ReturnsSinglePassWithTheInputPosition()
    {
        var state = Boards.ClosedOut();

        var only = Assert.Single(MoveGenerator.GenerateCandidatePlays(state, 6, 4));

        Assert.Equal(0, only.Play.Count);
        Assert.Equal(state.ToPosition(), only.ResultingPosition);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 5)]
    [InlineData(2, 2)]
    public void GenerateCandidatePlays_DoesNotMutateInput(int die1, int die2)
    {
        var state = BoardState.Standard();

        MoveGenerator.GenerateCandidatePlays(state, die1, die2);

        Assert.Equal(BoardPosition.Standard, state.ToPosition());
        Assert.Equal(BoardState.Standard().HighPointOccupied, state.HighPointOccupied);
    }

    [Theory]
    [InlineData(6, 4)]
    [InlineData(3, 3)]
    public void GenerateCandidatePlays_AllocatesOnlyItsList_NothingPerCandidate(int die1, int die2)
    {
        // Candidates are values: the call allocates what GeneratePlays does
        // plus the one CandidatePlay array it returns, to the byte.
        var state = BoardState.Standard();
        int count = MoveGenerator.GeneratePlays(state, die1, die2).Count;
        Assert.True(count > 1);

        long candidates = AllocationProbe.SteadyStateBytes(() => _kept = MoveGenerator.GenerateCandidatePlays(state, die1, die2));
        long plays = AllocationProbe.SteadyStateBytes(() => _kept = MoveGenerator.GeneratePlays(state, die1, die2));
        long array = AllocationProbe.SteadyStateBytes(() => _kept = new CandidatePlay[count]);

        Assert.Equal(plays + array, candidates);
    }

    [Fact]
    public void CandidatePlay_IsAValueOnlyBgMoveGenBuilds_WithNoEquality()
    {
        // Construction: a readonly value type whose only constructor is
        // internal, with get-only members — so a candidate a consumer holds is
        // one the generator paired, or default, and nothing it does to what
        // it reads reaches the pairing. Equality: none — it holds a Play,
        // which has none (halheinrich/backgammon#273, ruling 1): no == to
        // compile, and the runtime routes throw rather than compare the play
        // field-wise.
        var type = typeof(CandidatePlay);
        Assert.True(type.IsValueType);
        Assert.Contains(type.CustomAttributes,
            a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        Assert.Empty(type.GetConstructors());
        Assert.All(type.GetProperties(), p => Assert.Null(p.SetMethod));

        CandidatePlay none = default;
        Assert.Equal(0, none.Play.Count);
        Assert.Equal(BoardPosition.Empty, none.ResultingPosition);

        var candidate = MoveGenerator.GenerateCandidatePlays(BoardState.Standard(), 6, 4)[0];
        Assert.Null(type.GetMethod("op_Equality"));
        Assert.Null(type.GetMethod("op_Inequality"));
        Assert.Throws<NotSupportedException>(() => candidate.Equals((object)candidate));
        Assert.Throws<NotSupportedException>(() => candidate.GetHashCode());
        Assert.Throws<NotSupportedException>(() => new HashSet<CandidatePlay> { candidate });
    }

    [Fact]
    public void GenerateCandidatePlays_PlayPropertyReturnsACopy()
    {
        // Play is a value type holding its moves inline: modifying the copy a
        // caller receives cannot reach the candidate's stored play.
        var candidate = MoveGenerator.GenerateCandidatePlays(BoardState.Standard(), 6, 5)[0];
        var before = candidate.Play;
        Assert.True(before.Count < 4);

        var received = candidate.Play;
        received.Add(new Move(24, 23));

        Assert.True(before.IsSameEncoding(candidate.Play));
    }

    [Fact]
    public void GenerateCandidatePlays_CandidateOrderIsRepeatableWithinAProcess_AcrossSyntheticPositions()
    {
        // Candidate order is GeneratePlays' order. This pins only that two
        // calls in one process agree: hash-ordered iteration is also stable
        // within a process, so this test cannot see run-to-run variation.
        // Cross-run stability rests on construction (ordered generation, no
        // hashing or randomness in the optimized paths), not on this test.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(200).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var first = MoveGenerator.GenerateCandidatePlays(BoardState.FromMop(mop), die1, die2);
                var second = MoveGenerator.GenerateCandidatePlays(BoardState.FromMop(mop), die1, die2);

                Assert.Equal(first.Count, second.Count);
                for (int i = 0; i < first.Count; i++)
                    Assert.True(first[i].Play.IsSameEncoding(second[i].Play),
                        $"Position {index} {die1}-{die2}: candidate {i} differs between calls.");
            }
        }
    }
}

public class GenerateSuccessorsTests
{
    // A sink the allocation pin's paths write, so no allocation can be elided.
    private static object? _kept;

    [Fact]
    public void GenerateSuccessors_PairsEachGeneratedPlayWithTheBoardApplyPlayLeaves_AcrossSyntheticPositions()
    {
        // The whole contract: successor i carries GeneratePlays' play i in
        // the generator's own encoding, and the position that play leads to
        // seen from the next mover's side — the board BoardState.ApplyPlay
        // leaves (the producer's rule, applied and flipped) and, equally, the
        // tests' own replay flipped by the value. The positions are distinct.
        var distinct = new HashSet<BoardPosition>();
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(1_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(state, die1, die2);
                var successors = MoveGenerator.GenerateSuccessors(state, die1, die2);

                Assert.Equal(plays.Count, successors.Count);
                distinct.Clear();
                for (int i = 0; i < plays.Count; i++)
                {
                    string where = $"Position {index} {die1}-{die2} successor {i}";
                    Assert.True(plays[i].IsSameEncoding(successors[i].Play), $"{where}: play is not the generator's encoding.");

                    var applied = state.Copy();
                    applied.ApplyPlay(plays[i]);
                    Assert.True(applied.ToPosition() == successors[i].Position,
                        $"{where}: {successors[i].Position} is not the board ApplyPlay leaves, {applied.ToPosition()}.");
                    Assert.True(Replay.PositionAfter(state, plays[i]).Flipped() == successors[i].Position,
                        $"{where}: the position is not the play's, flipped.");

                    distinct.Add(successors[i].Position);
                }
                Assert.True(distinct.Count == successors.Count,
                    $"Position {index} {die1}-{die2}: {successors.Count} successors reach {distinct.Count} positions.");
            }
        }
    }

    [Fact]
    public void GenerateSuccessors_NoLegalMove_ReturnsSinglePassWithTheInputFlipped()
    {
        var state = Boards.ClosedOut();

        var only = Assert.Single(MoveGenerator.GenerateSuccessors(state, 6, 4));

        Assert.Equal(0, only.Play.Count);
        Assert.Equal(state.ToPosition().Flipped(), only.Position);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 5)]
    [InlineData(2, 2)]
    public void GenerateSuccessors_DoesNotMutateInput(int die1, int die2)
    {
        var state = BoardState.Standard();

        MoveGenerator.GenerateSuccessors(state, die1, die2);

        Assert.Equal(BoardPosition.Standard, state.ToPosition());
        Assert.Equal(BoardState.Standard().HighPointOccupied, state.HighPointOccupied);
    }

    [Theory]
    [InlineData(6, 4)]   // the two-pass non-doubles path
    [InlineData(3, 3)]   // full-depth doubles
    [InlineData(6, 6)]   // doubles whose continuations the opponent blocks
    public void GenerateSuccessors_AllocatesOnlyItsList_NothingPerSuccessor(int die1, int die2)
    {
        // Successors are values: the call allocates what GeneratePlays does
        // plus the one Successor array it returns, to the byte. A board copied
        // or built per successor, or a successor boxed, would show here.
        var state = BoardState.Standard();
        int count = MoveGenerator.GeneratePlays(state, die1, die2).Count;
        Assert.True(count > 1);

        long successors = AllocationProbe.SteadyStateBytes(() => _kept = MoveGenerator.GenerateSuccessors(state, die1, die2));
        long plays = AllocationProbe.SteadyStateBytes(() => _kept = MoveGenerator.GeneratePlays(state, die1, die2));
        long array = AllocationProbe.SteadyStateBytes(() => _kept = new Successor[count]);

        Assert.Equal(plays + array, successors);
    }

    [Fact]
    public void Successor_IsAValueOnlyBgMoveGenBuilds_WithNoEquality()
    {
        // Construction: a readonly value type whose only constructor is
        // internal, with get-only members — so a successor a consumer holds is
        // one the generator paired, or default, and nothing it does to what
        // it reads reaches the pairing. Equality: none — it holds a Play,
        // which has none (halheinrich/backgammon#273, ruling 1): no == to
        // compile, and the runtime routes throw rather than compare the play
        // field-wise.
        var type = typeof(Successor);
        Assert.True(type.IsValueType);
        Assert.Contains(type.CustomAttributes,
            a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        Assert.Empty(type.GetConstructors());
        Assert.All(type.GetProperties(), p => Assert.Null(p.SetMethod));

        Successor none = default;
        Assert.Equal(0, none.Play.Count);
        Assert.Equal(BoardPosition.Empty, none.Position);

        var successor = MoveGenerator.GenerateSuccessors(BoardState.Standard(), 6, 4)[0];
        Assert.Null(type.GetMethod("op_Equality"));
        Assert.Null(type.GetMethod("op_Inequality"));
        Assert.Throws<NotSupportedException>(() => successor.Equals((object)successor));
        Assert.Throws<NotSupportedException>(() => successor.GetHashCode());
        Assert.Throws<NotSupportedException>(() => new HashSet<Successor> { successor });
    }
}

public class ArgumentValidationTests
{
    // halheinrich/backgammon#244: a null state and a die outside 1-6 are
    // refused once, in GeneratePlays, and every public entry inherits the
    // refusal — each is called here, not assumed. MoveEntryState takes its
    // start as a BoardPosition value, which cannot be null, so it is in the
    // die table only.
    public static TheoryData<string> StateEntries =>
    [
        nameof(MoveGenerator.GeneratePlays),
        nameof(MoveGenerator.GenerateResultingPositions),
        nameof(MoveGenerator.GenerateCandidatePlays),
        nameof(MoveGenerator.GenerateSuccessors),
        nameof(MoveGenerator.IsLegalPlay),
        nameof(MoveGenerator.ApplyPlay),
        nameof(MoveGenerator.ResolvePlay),
    ];

    public static TheoryData<string> Entries => [.. StateEntries, nameof(MoveEntryState)];

    public static TheoryData<string, int, int, string> EntriesWithABadDie()
    {
        var data = new TheoryData<string, int, int, string>();
        foreach (string entry in Entries)
        {
            data.Add(entry, 0, 3, "die1");
            data.Add(entry, 7, 3, "die1");
            data.Add(entry, 3, 0, "die2");
            data.Add(entry, 3, 7, "die2");
            data.Add(entry, -1, -1, "die1");
            data.Add(entry, 4, int.MinValue, "die2");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(StateEntries))]
    public void NullState_IsRefused(string entry)
    {
        var refusal = Assert.Throws<ArgumentNullException>(() => Call(entry, null, 3, 1));

        Assert.Equal("state", refusal.ParamName);
    }

    [Theory]
    [MemberData(nameof(EntriesWithABadDie))]
    public void DieOutsideOneToSix_IsRefused_NamingTheDie_BeforeTheStateIsTouched(
        string entry, int die1, int die2, string refusedDie)
    {
        var state = BoardState.Standard();

        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => Call(entry, state, die1, die2));

        Assert.Equal(refusedDie, refusal.ParamName);
        Assert.Equal(BoardPosition.Standard, state.ToPosition());
        Assert.Equal(BoardState.Standard().HighPointOccupied, state.HighPointOccupied);
    }

    /// <summary>A play legal on the opening board with 6-4, for the entries that take one.</summary>
    private static readonly Play OpeningSixFour = [new(24, 18), new(13, 9)];

    /// <summary>That play's hops, for the entry that takes hops.</summary>
    private static readonly Hop[] OpeningSixFourHops = [new(24, 18), new(13, 9)];

    private static object? Call(string entry, BoardState? state, int die1, int die2)
    {
        switch (entry)
        {
            case nameof(MoveGenerator.GeneratePlays): return MoveGenerator.GeneratePlays(state!, die1, die2);
            case nameof(MoveGenerator.GenerateResultingPositions): return MoveGenerator.GenerateResultingPositions(state!, die1, die2);
            case nameof(MoveGenerator.GenerateCandidatePlays): return MoveGenerator.GenerateCandidatePlays(state!, die1, die2);
            case nameof(MoveGenerator.GenerateSuccessors): return MoveGenerator.GenerateSuccessors(state!, die1, die2);
            case nameof(MoveGenerator.IsLegalPlay): return MoveGenerator.IsLegalPlay(state!, OpeningSixFour, die1, die2);
            case nameof(MoveGenerator.ApplyPlay): MoveGenerator.ApplyPlay(state!, OpeningSixFour, die1, die2); return null;
            case nameof(MoveGenerator.ResolvePlay): return MoveGenerator.ResolvePlay(state!, OpeningSixFourHops, die1, die2);
            case nameof(MoveEntryState): return new MoveEntryState(state!.ToPosition(), die1, die2);
            default: throw new ArgumentOutOfRangeException(nameof(entry), entry, "Not a public entry.");
        }
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
        var state = Boards.ClosedOut();

        var empty = new Play();
        Assert.True(MoveGenerator.IsLegalPlay(state, empty, 6, 4));
    }

    [Fact]
    public void GeneratePlays_NoLegalMove_ReturnsSingleEmptyPlay()
    {
        // Pins the no-legal-move convention: a dance / closed-out position
        // yields a one-element list holding the empty pass play, never an empty
        // list. Same closed-out setup as EmptyPlay_OnClosedOutPosition_ReturnsTrue.
        var state = Boards.ClosedOut();

        var plays = MoveGenerator.GeneratePlays(state, 6, 4);

        Assert.Single(plays);
        Assert.Equal(0, plays[0].Count);
    }

    [Fact]
    public void HitMarkDisagreeingWithTheBoard_IsRejected()
    {
        // A hit mark that disagrees with the board makes a play invalid from
        // it, and an invalid play is the same play as nothing (the rule is
        // BoardState.IsSamePlay's). Both directions: landing on the blot on 18
        // without the mark, and marking a landing on 9, where no blot sits.
        // The old hit-blind key once let the first apply without barring the
        // blot — silent board corruption.
        var state = Boards.BlotOnEighteen();

        Play unmarkedLandingOnBlot = [new(24, 18), new(13, 9)];
        Play markWithoutBlot = [new(24, -18), new(13, -9)];
        Play hit = [new(24, -18), new(13, 9)];

        Assert.False(MoveGenerator.IsLegalPlay(state, unmarkedLandingOnBlot, 6, 4));
        Assert.False(MoveGenerator.IsLegalPlay(state, markWithoutBlot, 6, 4));
        Assert.True(MoveGenerator.IsLegalPlay(state, hit, 6, 4));
    }

    [Fact]
    public void DecomposedEntry_MatchesCombinedCandidate()
    {
        // The quiz-entry repro: a play entered as decomposed hops must match
        // the candidate list whichever encoding the generator kept and
        // whichever open intermediate the entry routed through. From Standard
        // with 3-2, 13/8 is legal with both intermediates (10 and 11) open;
        // the generator emits exactly one encoding, and every encoding reaches
        // its position, so every encoding is the legal play.
        var state = BoardState.Standard();

        Play viaTen = [new(13, 10), new(10, 8)];      // big die first
        Play viaEleven = [new(13, 11), new(11, 8)];   // small die first
        Play combined = [new(13, 8)];                 // the analyzer-style collapsed form

        Assert.True(MoveGenerator.IsLegalPlay(state, viaTen, 3, 2));
        Assert.True(MoveGenerator.IsLegalPlay(state, viaEleven, 3, 2));
        Assert.True(MoveGenerator.IsLegalPlay(state, combined, 3, 2));
    }

    [Fact]
    public void HitMarkedOnEitherCheckerMakingThePoint_IsTheLegalPlay()
    {
        // halheinrich/backgammon#273's report: making the 3-point on a blot
        // with 5-4, the hit written on the 8-point checker or on the 7-point
        // checker. Both reach one position, so both are the legal play,
        // whichever the generator's encoding marks.
        var mop = new int[26];
        mop[8] = 2;
        mop[7] = 1;
        mop[6] = 2;
        mop[3] = -1;   // the blot the point is made on
        mop[19] = -2;
        var state = BoardState.FromMop(mop);

        Play markOnEight = [new(8, -3), new(7, 3)];
        Play markOnSeven = [new(8, 3), new(7, -3)];

        Assert.True(MoveGenerator.IsLegalPlay(state, markOnEight, 5, 4));
        Assert.True(MoveGenerator.IsLegalPlay(state, markOnSeven, 5, 4));
    }

    [Fact]
    public void CombinedMovesPairedDifferently_AreTheLegalPlay()
    {
        // halheinrich/backgammon#277: written as combined moves, one play can
        // pair its sources with its destinations two ways. With 2-2 from
        // checkers on 13 and 11, 13/9 11/7 and 13/7 11/9 move the same
        // checkers to the same points, so both are the legal play the
        // generator emits once, in hops of 2.
        var mop = new int[26];
        mop[13] = 1;
        mop[11] = 1;
        mop[19] = -2;
        var state = BoardState.FromMop(mop);

        Play pairedOneWay = [new(13, 9), new(11, 7)];
        Play pairedTheOther = [new(13, 7), new(11, 9)];

        Assert.True(MoveGenerator.IsLegalPlay(state, pairedOneWay, 2, 2));
        Assert.True(MoveGenerator.IsLegalPlay(state, pairedTheOther, 2, 2));
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
    public void GeneratePlays_CandidatesAreDistinctPlays(int die1, int die2)
    {
        // No two candidates are the same play from the board they are
        // generated on (BoardState.IsSamePlay, which compares the positions
        // they reach). If generation ever emitted two encodings of one play,
        // consumers matching a play among the candidates (quiz scoring,
        // IsLegalPlay) would see a double candidate.
        var state = BoardState.Standard();
        var plays = MoveGenerator.GeneratePlays(state, die1, die2);

        for (int i = 0; i < plays.Count; i++)
            for (int j = i + 1; j < plays.Count; j++)
                Assert.False(state.IsSamePlay(plays[i], plays[j]),
                    $"Candidates {i} {PlayText.Raw(plays[i])} and {j} {PlayText.Raw(plays[j])} " +
                    $"are the same play for {die1}-{die2}.");
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
    public void GeneratePlays_CandidatesAreDistinctPlays_AcrossSyntheticPositions()
    {
        // The opening-board theory above pins one board. This pins breadth:
        // the whole deterministic corpus crossed with all 21 rolls. The
        // opening board never reaches a bear-off, so it could not have caught
        // the two-die bear-off duplicate; half of this corpus is drawn inside
        // the home board precisely so it can.
        //
        // Asked the way consumers ask it: the list match finds every
        // candidate at its own index, so no earlier candidate is the same
        // play.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(4_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(state, die1, die2);

                for (int i = 0; i < plays.Count; i++)
                {
                    int first = state.IndexOfSamePlay(plays[i], plays);
                    if (first != i)
                        Assert.Fail(
                            $"Position {index} ({state.ToPosition()}) {die1}-{die2}: candidates " +
                            $"{first} {PlayText.Raw(plays[first])} and {i} {PlayText.Raw(plays[i])} are the same play.");
                }
            }
        }
    }

    [Fact]
    public void GeneratePlays_TwoBarEntries_SingleCandidate()
    {
        // Two checkers on the bar, both entry points open, non-doubles: the
        // only legal play enters both — one play, whichever die goes first.
        // The bigDie-first bar branch re-derived it move-for-move in the other
        // order, so without the same-source skip this was a double candidate.
        var mop = new int[26];
        mop[25] = 2;
        mop[13] = 5;
        mop[19] = -2;
        mop[24] = -2;
        var state = BoardState.FromMop(mop);

        var plays = MoveGenerator.GeneratePlays(state, 2, 3);

        Play expected = [new(25, 23), new(25, 22)];

        var only = Assert.Single(plays);
        Assert.True(state.IsSamePlay(expected, only));
    }

    [Fact]
    public void GeneratePlays_SamePointBearOffPair_SingleCandidate()
    {
        // Two checkers on the 2-point (nothing else), dice 2-5: the exact
        // bear-off (die 2) and the overshoot bear-off (die 5) both encode as
        // (2, 0), so the play is the same in either die order. The bigDie-first
        // pass re-derived it — without the same-source skip, a double candidate.
        var mop = new int[26];
        mop[2] = 2;
        var state = BoardState.FromMop(mop);

        var plays = MoveGenerator.GeneratePlays(state, 2, 5);

        Play expected = [new(2, 0), new(2, 0)];

        var only = Assert.Single(plays);
        Assert.True(state.IsSamePlay(expected, only));
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

        Assert.Equal(direct.ToPosition(), validated.ToPosition());
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
        var snapshot = state.ToPosition();
        int snapshotHigh = state.HighPointOccupied;

        Play bogus = [new(24, 17), new(13, 9)];

        Assert.Throws<ArgumentException>(
            () => MoveGenerator.ApplyPlay(state, bogus, 6, 4));

        Assert.Equal(snapshot, state.ToPosition());
        Assert.Equal(snapshotHigh, state.HighPointOccupied);
    }

    [Fact]
    public void ValidPlayWrongForTheDice_IsRejected_BoardUnchanged()
    {
        // 24/20 13/9 is a valid play from the opening board — every hop lands
        // on an open point, so BoardState.ApplyPlay would apply it — but it
        // plays two 4s, not 6-4. Legality is the roll's: only the match
        // against GeneratePlays refuses it. (The other illegal fixtures here
        // are also invalid by the board rule, which would refuse them even if
        // the match were skipped.)
        var state = BoardState.Standard();
        var snapshot = state.ToPosition();
        Play twoFours = [new(24, 20), new(13, 9)];

        Assert.True(state.Copy().TryApplyPlay(twoFours));
        Assert.False(MoveGenerator.IsLegalPlay(state, twoFours, 6, 4));
        Assert.Throws<ArgumentException>(() => MoveGenerator.ApplyPlay(state, twoFours, 6, 4));
        Assert.Equal(snapshot, state.ToPosition());
    }

    [Fact]
    public void HitlessEncodingOfHittingPlay_Throws_AndCannotCorruptBoard()
    {
        // Closes the booked silent-corruption hazard. Under the old hit-blind
        // DeduplicationKey, this hit-less encoding of the forced hit 24/18*
        // validated as legal and then applied verbatim — moving onto the blot
        // without sending it to the bar (the blot was annihilated in place).
        // Landing on a blot without a hit mark makes the play invalid from
        // this board, so it matches no candidate and is refused, and
        // throw-before-mutate leaves the board (blot included) untouched.
        var state = Boards.BlotOnEighteen();

        var snapshot = state.ToPosition();

        Play noHitVariant = [
            new(24, 18),   // hit-less form of the hitting play
            new(13, 9),
        ];

        Assert.Throws<ArgumentException>(
            () => MoveGenerator.ApplyPlay(state, noHitVariant, 6, 4));

        Assert.Equal(snapshot, state.ToPosition());
        Assert.Equal(-1, state.Points[18]);   // blot still on the board
    }

    [Fact]
    public void DecomposedEncoding_ReachesTheMatchedCandidatesBoard()
    {
        // ApplyPlay applies the caller's play once it is legal, and a legal
        // play reaches its candidate's position whatever the encoding: the
        // board must equal the generator's own candidate applied directly.
        var state = BoardState.Standard();

        Play decomposed = [new(13, 10), new(10, 8)];

        var legal = MoveGenerator.GeneratePlays(BoardState.Standard(), 3, 2);
        var candidate = legal[state.IndexOfSamePlay(decomposed, legal)];
        Assert.False(candidate.IsSameEncoding(decomposed));

        var direct = BoardState.Standard();
        direct.ApplyPlay(candidate);

        MoveGenerator.ApplyPlay(state, decomposed, 3, 2);

        Assert.Equal(direct.ToPosition(), state.ToPosition());
        Assert.Equal(direct.HighPointOccupied, state.HighPointOccupied);
    }

    [Theory]
    [InlineData(true)]    // the hit marked on the 8-point checker
    [InlineData(false)]   // the hit marked on the 7-point checker
    public void EitherHitMarkOfThePointMakingPlay_ReachesTheCandidatesBoard(bool markOnEight)
    {
        // halheinrich/backgammon#273's report, applied: 8/3 7/3 on a blot with
        // 5-4, the mark on either checker. Both are the legal play, and both
        // leave the board the generator's candidate leaves: the blot on the
        // bar and the point made.
        var mop = new int[26];
        mop[8] = 2;
        mop[7] = 1;
        mop[6] = 2;
        mop[3] = -1;
        mop[19] = -2;
        var state = BoardState.FromMop(mop);

        Play play = markOnEight ? [new(8, -3), new(7, 3)] : [new(8, 3), new(7, -3)];

        var legal = MoveGenerator.GeneratePlays(state, 5, 4);
        var direct = state.Copy();
        direct.ApplyPlay(legal[state.IndexOfSamePlay(play, legal)]);

        MoveGenerator.ApplyPlay(state, play, 5, 4);

        Assert.Equal(direct.ToPosition(), state.ToPosition());
    }

    [Fact]
    public void DecomposedEncoding_ThroughBlockedPoint_IsRejected_BoardUnchanged()
    {
        // 13/8 with 3-2 is legal through the open 11, but a caller's hops
        // through the opponent's 10-point anchor land on a point the opponent
        // holds, which makes that encoding invalid from this board — it
        // reaches no position, so it is the same play as nothing and is
        // refused, the board untouched. (Before play identity was decided by
        // position, notation-level equality matched it and the generator's
        // encoding was applied in its place.) The open route is the play.
        var mop = new int[26];
        mop[13] = 2;
        mop[6] = 2;
        mop[10] = -2;   // opponent anchor — blocks the 13/10 route
        mop[20] = -2;
        var state = BoardState.FromMop(mop);
        var snapshot = state.ToPosition();

        Play throughTheAnchor = [
            new(13, 10),   // lands on the blocked point
            new(10, 8),
        ];
        Play throughTheOpenPoint = [new(13, 11), new(11, 8)];

        Assert.False(MoveGenerator.IsLegalPlay(state, throughTheAnchor, 3, 2));
        Assert.Throws<ArgumentException>(
            () => MoveGenerator.ApplyPlay(state, throughTheAnchor, 3, 2));
        Assert.Equal(snapshot, state.ToPosition());

        Assert.True(MoveGenerator.IsLegalPlay(state, throughTheOpenPoint, 3, 2));
    }

    [Fact]
    public void EmptyPlay_OnClosedOutPosition_AppliesAsPass()
    {
        // No legal entry from the bar → only "play" is the empty pass. Validating
        // ApplyPlay accepts it and the state flips perspective per BoardState.ApplyPlay.
        var state = Boards.ClosedOut();

        var direct = state.Copy();
        direct.ApplyPlay(new Play());

        MoveGenerator.ApplyPlay(state, new Play(), 6, 4);

        Assert.Equal(direct.ToPosition(), state.ToPosition());
        Assert.Equal(direct.HighPointOccupied, state.HighPointOccupied);
    }
}

public class HopTests
{
    [Theory]
    [InlineData(0, 10, "from")]    // the opponent's bar is no source
    [InlineData(26, 10, "from")]
    [InlineData(-1, 10, "from")]
    [InlineData(int.MinValue, 10, "from")]
    [InlineData(13, 25, "to")]     // nor is the mover's bar a landing point
    [InlineData(13, int.MaxValue, "to")]
    [InlineData(26, -1, "from")]   // both out: the source is named first
    public void Construction_RefusesANumberThatIsNoSourceOrLandingPoint_NamingIt(int from, int to, string refused)
    {
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => new Hop(from, to));

        Assert.Equal(refused, refusal.ParamName);
        Assert.False(Hop.TryCreate(from, to, out var hop));
        Assert.Equal(default, hop);
    }

    [Fact]
    public void EverySourceAndLandingPoint_MakesAHop_AndTryCreateAgrees()
    {
        for (int from = 1; from <= 25; from++)
        {
            for (int to = 0; to <= 24; to++)
            {
                var hop = new Hop(from, to);

                Assert.Equal((from, to), (hop.From, hop.To));
                Assert.True(Hop.TryCreate(from, to, out var tried));
                Assert.Equal(hop, tried);
            }
        }
    }

    [Fact]
    public void AHitMarkCannotBeWritten()
    {
        // halheinrich/backgammon#273, ruling C: the input representation
        // cannot express a hit mark. A Move marks one by negating its landing
        // point; a hop's landing point is 0-24, so every negated landing point
        // is refused, and a hop's state is its two points and nothing else —
        // no member a mark could be added through.
        for (int to = -24; to <= -1; to++)
        {
            int landing = to;
            Assert.Throws<ArgumentOutOfRangeException>(() => new Hop(13, landing));
            Assert.False(Hop.TryCreate(13, landing, out _));
        }

        (string, Type)[] twoPoints = [(nameof(Hop.From), typeof(int)), (nameof(Hop.To), typeof(int))];
        var state = typeof(Hop).GetProperties().OrderBy(p => p.Name).Select(p => (p.Name, p.PropertyType));
        Assert.Equal(twoPoints, state);
    }

    [Fact]
    public void EqualityIsTheTwoPoints_AndAHopDeconstructsIntoThem()
    {
        Assert.Equal(new Hop(13, 10), new Hop(13, 10));
        Assert.NotEqual(new Hop(13, 10), new Hop(13, 11));
        Assert.NotEqual(new Hop(13, 10), new Hop(12, 10));

        var (from, to) = new Hop(25, 22);
        Assert.Equal((25, 22), (from, to));
    }

    [Fact]
    public void AHopDescribesTheMoveLandingOnItsPoint_MarkedOrNot()
    {
        var hop = new Hop(13, 8);

        Assert.True(hop.Describes(new Move(13, 8)));
        Assert.True(hop.Describes(new Move(13, -8)));
        Assert.False(hop.Describes(new Move(13, 9)));
        Assert.False(hop.Describes(new Move(13, -9)));
        Assert.False(hop.Describes(new Move(12, 8)));
        Assert.True(new Hop(6, 0).Describes(new Move(6, 0)));
        Assert.False(new Hop(6, 0).Describes(new Move(6, 1)));
        Assert.True(new Hop(25, 20).Describes(new Move(25, -20)));
    }
}

public class ResolvePlayTests
{
    /// <summary>
    /// halheinrich/backgammon#304's board: a lone checker of the mover's on
    /// 13, points 10 and 11 empty, an opposing blot on 8, and opposing pairs
    /// on 19, 20 and 24.
    /// </summary>
    private static BoardState ProbeBoard()
    {
        var mop = new int[26];
        mop[13] = 1;
        mop[8] = -1;
        mop[19] = -2;
        mop[20] = -2;
        mop[24] = -2;
        return BoardState.FromMop(mop);
    }

    [Theory]
    [InlineData(10)]   // 13/10 10/8: the route the generator does not keep — the probe
    [InlineData(11)]   // 13/11 11/8: the generator's own route — the control
    public void EitherRouteOfAHittingPlay_InEitherOrder_ResolvesToTheOneCandidate_WhichHits(int via)
    {
        // halheinrich/backgammon#304's first probe. With 3-2 both routes are
        // legal and reach one position, so the generator keeps one, 13/11 11/8*.
        // A reply by the other route must resolve to it, not to nothing — and
        // to the candidate itself, its route and its hit mark, not to a play
        // spelled from the hops.
        var state = ProbeBoard();
        var candidate = Assert.Single(MoveGenerator.GeneratePlays(state, 3, 2));
        Assert.True(candidate.IsSameEncoding([new(13, 11), new(11, -8)]), PlayText.Raw(candidate));

        foreach (var order in Orders([new(13, via), new(via, 8)]))
        {
            var resolved = Assert.NotNull(MoveGenerator.ResolvePlay(state, order, 3, 2));

            Assert.True(resolved.IsSameEncoding(candidate), PlayText.Raw(resolved));
        }

        var after = Replay.PositionAfter(state, candidate);
        Assert.Equal(-1, after[0]);   // the blot is on the opponent's bar
        Assert.Equal(1, after[8]);
    }

    [Theory]
    [InlineData(12)]   // 13/12 12/6: the generator's own route — the control
    [InlineData(7)]    // 13/7 7/6: the other route of the same play
    public void TheRollsOwnHops_ResolveToTheCandidate(int via)
    {
        var state = ProbeBoard();
        var candidate = Assert.Single(MoveGenerator.GeneratePlays(state, 6, 1));

        var resolved = Assert.NotNull(MoveGenerator.ResolvePlay(state, [new(13, via), new(via, 6)], 6, 1));

        Assert.True(resolved.IsSameEncoding(candidate), PlayText.Raw(resolved));
    }

    [Fact]
    public void HopsNoDieOfTheRollProduces_ResolveToNone_ThoughTheyAreTheSamePlay()
    {
        // halheinrich/backgammon#304's second probe: with 6-1, 13/9 9/6
        // reaches the one candidate's position, so as a play it is the same
        // play and IsLegalPlay accepts it — play identity is the position.
        // But a 4 and a 3 are no dice of the roll, so the hops describe no
        // play of it. TheRollsOwnHops_ResolveToTheCandidate is the control.
        var state = ProbeBoard();
        Play asAPlay = [new(13, 9), new(9, 6)];

        Assert.True(MoveGenerator.IsLegalPlay(state, asAPlay, 6, 1));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(13, 9), new(9, 6)], 6, 1));
    }

    [Fact]
    public void AMergedHop_ResolvesToNone_ThoughItIsTheSamePlay()
    {
        // 13/8 with 3-2 on the opening board: one hop covering both dice. As
        // a play it is the legal 13/11 11/8 (IsLegalPlay agrees); as hops it
        // is no single die's move. Split, it resolves.
        var state = BoardState.Standard();

        Assert.True(MoveGenerator.IsLegalPlay(state, [new(13, 8)], 3, 2));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(13, 8)], 3, 2));
        Assert.NotNull(MoveGenerator.ResolvePlay(state, [new(13, 11), new(11, 8)], 3, 2));
    }

    [Fact]
    public void AMergedHopAmongADoublesHops_ResolvesToNone_EvenAtTheRightCount()
    {
        // 3-3 on the opening board: four hops, as many as the roll plays, but
        // 24/18 covers two 3s — five dice in all. Split into 24/21 21/18 with
        // the 13/10s, it is a play of the roll.
        var state = BoardState.Standard();

        Assert.Null(MoveGenerator.ResolvePlay(state, [new(24, 18), new(13, 10), new(13, 10), new(8, 5)], 3, 3));
        Assert.NotNull(MoveGenerator.ResolvePlay(state, [new(24, 21), new(21, 18), new(13, 10), new(13, 10)], 3, 3));
    }

    [Fact]
    public void HopsShortOfTheDiceTheRollPlays_ResolveToNone_ThoughTheyReachTheCandidatesPosition()
    {
        // A lone checker on the 3-point, 5-1. The 5 bears it off at once, but
        // the 1 can be played first, so a legal play plays both: 3/2 2/off,
        // the one candidate. 3/off reaches its position — IsLegalPlay agrees —
        // yet is one die's move where the roll's legal plays make two.
        var mop = new int[26];
        mop[3] = 1;
        mop[24] = -2;
        var state = BoardState.FromMop(mop);
        var candidate = Assert.Single(MoveGenerator.GeneratePlays(state, 5, 1));
        Assert.Equal(2, candidate.Count);

        Assert.True(MoveGenerator.IsLegalPlay(state, [new(3, 0)], 5, 1));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(3, 0)], 5, 1));
        foreach (var order in Orders([new(3, 2), new(2, 0)]))
            Assert.True(Assert.NotNull(MoveGenerator.ResolvePlay(state, order, 5, 1)).IsSameEncoding(candidate));
    }

    [Fact]
    public void ALoneSmallerDie_ResolvesToNone_WhereTheLargerCanBePlayed()
    {
        // A lone checker on 8 and the opponent's point on 1, 6-1: 8/2 then
        // 2/1, or 8/7 then 7/1, lands on the point, so only one die can be
        // played, and the rule makes it the larger. 8/7 is a legal single
        // move of the 1, but no play of the roll.
        var mop = new int[26];
        mop[8] = 1;
        mop[1] = -2;
        var state = BoardState.FromMop(mop);
        var candidate = Assert.Single(MoveGenerator.GeneratePlays(state, 6, 1));
        Assert.True(candidate.IsSameEncoding([new(8, 2)]), PlayText.Raw(candidate));

        Assert.Null(MoveGenerator.ResolvePlay(state, [new(8, 7)], 6, 1));
        Assert.True(Assert.NotNull(MoveGenerator.ResolvePlay(state, [new(8, 2)], 6, 1)).IsSameEncoding(candidate));
    }

    [Theory]
    [MemberData(nameof(AllRolls))]
    public void EveryOrderOfEachCandidatesHops_ResolvesToThatCandidate_OnTheOpeningBoard(int die1, int die2)
    {
        // Order independence: whatever order a legal play's hops arrive in,
        // including orders no legal sequence plays them in (21/18 before
        // 24/21), they resolve to the one candidate — and each candidate is
        // resolved to by its own hops, so no candidate is out of reach.
        var state = BoardState.Standard();

        foreach (var candidate in MoveGenerator.GeneratePlays(state, die1, die2))
        {
            foreach (var order in Orders(HopsOf(candidate)))
            {
                var resolved = MoveGenerator.ResolvePlay(state, order, die1, die2);

                Assert.True(resolved is Play play && play.IsSameEncoding(candidate),
                    $"{die1}-{die2}: the hops {string.Join(", ", order)} of {PlayText.Raw(candidate)} " +
                    $"resolved to {(resolved is Play p ? PlayText.Raw(p) : "none")}.");
            }
        }
    }

    [Fact]
    public void ADoubleThatCannotBePlayedInFull_ResolvesOnlyAsManyHopsAsItPlays()
    {
        // A lone checker on 19 and the opponent's point on 1, 6-6: 19/13 13/7,
        // and 7/1 is blocked, so the roll plays two 6s. Those two hops resolve
        // in either order; one, the merged 19/7, and a third hop do not.
        var mop = new int[26];
        mop[19] = 1;
        mop[1] = -2;
        var state = BoardState.FromMop(mop);
        var candidate = Assert.Single(MoveGenerator.GeneratePlays(state, 6, 6));
        Assert.Equal(2, candidate.Count);

        foreach (var order in Orders([new(19, 13), new(13, 7)]))
            Assert.True(Assert.NotNull(MoveGenerator.ResolvePlay(state, order, 6, 6)).IsSameEncoding(candidate));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(19, 13)], 6, 6));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(19, 7)], 6, 6));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(19, 13), new(13, 7), new(7, 1)], 6, 6));
    }

    [Fact]
    public void EnteringFromTheBar_ResolvesAsTheGeneratorPlaysIt()
    {
        // One checker on the bar, two on the 6-point, an opposing blot on 16,
        // 5-4. bar/21 21/16* and bar/20 20/16* are one play, which the
        // generator keeps by the first route; either resolves to it. The entry
        // need not be listed first, but it must be played: two hops from the
        // 6-point leave the checker on the bar and describe no play.
        var mop = new int[26];
        mop[25] = 1;
        mop[6] = 2;
        mop[16] = -1;
        mop[12] = -2;
        var state = BoardState.FromMop(mop);
        var plays = MoveGenerator.GeneratePlays(state, 5, 4);
        var hit = plays.Single(p => p.IsSameEncoding([new(25, 21), new(21, -16)]));
        var enterAndMoveOn = plays.Single(p => p.IsSameEncoding([new(25, 20), new(6, 2)]));

        foreach (int via in (int[])[21, 20])
            foreach (var order in Orders([new(25, via), new(via, 16)]))
                Assert.True(Assert.NotNull(MoveGenerator.ResolvePlay(state, order, 5, 4)).IsSameEncoding(hit));
        Assert.True(Assert.NotNull(MoveGenerator.ResolvePlay(state, [new(6, 2), new(25, 20)], 5, 4)).IsSameEncoding(enterAndMoveOn));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(6, 1), new(6, 2)], 5, 4));
    }

    [Fact]
    public void BearingOffWithALargerDie_ResolvesFromTheHighestPoint_AndNotPastAHigherChecker()
    {
        // Checkers on 5 and 4 only, 6-5: each bears off, the 4 by the larger
        // die once the 5 has gone — one play, in either order.
        var fiveAndFour = new int[26];
        fiveAndFour[5] = 1;
        fiveAndFour[4] = 1;
        var state = BoardState.FromMop(fiveAndFour);
        var both = Assert.Single(MoveGenerator.GeneratePlays(state, 6, 5));

        foreach (var order in Orders([new(5, 0), new(4, 0)]))
            Assert.True(Assert.NotNull(MoveGenerator.ResolvePlay(state, order, 6, 5)).IsSameEncoding(both));

        // Two on the 6-point and one on the 2, 5-4: the 2-point checker may not
        // bear off by a larger die while the 6-point still holds one, in either
        // order — so 6/1 2/off describes no play, and 6/2 6/1 is the one.
        var twoSixesAndATwo = new int[26];
        twoSixesAndATwo[6] = 2;
        twoSixesAndATwo[2] = 1;
        state = BoardState.FromMop(twoSixesAndATwo);
        var fromTheSix = Assert.Single(MoveGenerator.GeneratePlays(state, 5, 4));

        Assert.Null(MoveGenerator.ResolvePlay(state, [new(6, 1), new(2, 0)], 5, 4));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(6, 2), new(2, 0)], 5, 4));
        Assert.True(Assert.NotNull(MoveGenerator.ResolvePlay(state, [new(6, 1), new(6, 2)], 5, 4)).IsSameEncoding(fromTheSix));
    }

    [Fact]
    public void NoHops_ResolveToThePass_OnlyWhenPassingIsTheOnlyPlay()
    {
        var closedOut = Boards.ClosedOut();
        var pass = Assert.Single(MoveGenerator.GeneratePlays(closedOut, 6, 4));

        var resolved = Assert.NotNull(MoveGenerator.ResolvePlay(closedOut, [], 6, 4));
        Assert.True(resolved.IsSameEncoding(pass));
        Assert.Equal(0, resolved.Count);

        Assert.Null(MoveGenerator.ResolvePlay(BoardState.Standard(), [], 6, 4));
        Assert.Null(MoveGenerator.ResolvePlay(closedOut, [new(25, 21)], 6, 4));
    }

    [Fact]
    public void HopsThatAreNoPlayOfAnyRoll_ResolveToNone_NeverAnException()
    {
        // Well-formed hops that describe no legal play are an answer: more
        // hops than any play has, a hop no checker makes, and the meaningless
        // default hop.
        var state = BoardState.Standard();

        Assert.Null(MoveGenerator.ResolvePlay(state,
            [new(24, 20), new(24, 20), new(13, 9), new(13, 9), new(8, 4)], 4, 4));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(24, 18), new(20, 16)], 6, 4));
        Assert.Null(MoveGenerator.ResolvePlay(state, [default, new(13, 9)], 6, 4));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(9, 13), new(24, 18)], 6, 4));
    }

    [Fact]
    public void TheStateIsOnlyRead_WhetherTheHopsResolveOrNot()
    {
        var state = ProbeBoard();
        var before = state.ToPosition();
        int high = state.HighPointOccupied;

        Assert.NotNull(MoveGenerator.ResolvePlay(state, [new(13, 10), new(10, 8)], 3, 2));
        Assert.Null(MoveGenerator.ResolvePlay(state, [new(13, 9), new(9, 6)], 6, 1));

        Assert.Equal(before, state.ToPosition());
        Assert.Equal(high, state.HighPointOccupied);
    }

    [Fact]
    public void NullHops_AreRefused()
    {
        var refusal = Assert.Throws<ArgumentNullException>(
            () => MoveGenerator.ResolvePlay(BoardState.Standard(), null!, 6, 4));

        Assert.Equal("hops", refusal.ParamName);
    }

    [Fact]
    public void ResolvePlay_AgreesWithTheLegalSequences_AcrossSyntheticPositions()
    {
        // The Strict model against the brute-force reference, on the seeded
        // corpus's first positions and all 21 rolls. The reference lists
        // every legal play as single-die moves (Reference_LegalSequences);
        // with their marks set aside, their hop sets are exactly the inputs
        // that should resolve. So:
        //   - no hop set is the hops of legal sequences reaching two
        //     different candidates — the premise of at most one answer,
        //     checked over every legal sequence;
        //   - a legal hop set resolves, in its sequence's order and reversed,
        //     to the candidate its sequences reach;
        //   - a near miss — one hop's landing point moved one point — resolves
        //     exactly when it is itself a legal hop set, and then to that
        //     set's candidate, and otherwise to none.
        // Each resolution re-runs the generator, so the last two checks take
        // an evenly spaced sample of a position-roll's hop sets
        // (ResolvedPerRoll), and every one where there are few: a doubles
        // roll on a spread board has a thousand, and resolving every set cost
        // about 20 s a Release run when this sweep was written, against 3 s
        // sampled.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(SweepSample).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(state, die1, die2);
                string where = $"Position {index} ({state.ToPosition()}) {die1}-{die2}";

                // A sequence's candidate is the one reaching its position, by
                // the tests' own replay — the association
                // GeneratePlays_HoldsExactlyOnePlayPerLegalPosition_AcrossSyntheticPositions
                // pins against IsSamePlay, found here without a list match per
                // sequence.
                var listedAt = new Dictionary<BoardPosition, int>();
                for (int i = 0; i < plays.Count; i++)
                    listedAt.Add(Replay.PositionAfter(state, plays[i]), i);

                var legal = new Dictionary<long, (Hop[] Hops, int Candidate)>();
                var inOrder = new List<(Hop[] Hops, int Candidate)>();
                foreach (var sequence in MoveGenerator.Reference_LegalSequences(state, die1, die2))
                {
                    var hops = HopsOf(sequence);
                    int candidate = listedAt[Replay.PositionAfter(state, sequence)];
                    if (legal.TryGetValue(Key(hops), out var seen))
                    {
                        if (seen.Candidate != candidate)
                            Assert.Fail($"{where}: the hops {string.Join(", ", hops)} are legal sequences of two " +
                                        $"candidates, {PlayText.Raw(plays[seen.Candidate])} and {PlayText.Raw(plays[candidate])}.");
                        continue;
                    }
                    legal.Add(Key(hops), (hops, candidate));
                    inOrder.Add((hops, candidate));
                }

                int stride = Math.Max(1, inOrder.Count / ResolvedPerRoll);
                for (int s = 0; s < inOrder.Count; s += stride)
                {
                    var (hops, candidate) = inOrder[s];
                    AssertResolvesTo(state, hops, die1, die2, plays[candidate], where);
                    AssertResolvesTo(state, [.. Enumerable.Reverse(hops)], die1, die2, plays[candidate], where);

                    foreach (var miss in NearMisses(hops))
                    {
                        if (legal.TryGetValue(Key(miss), out var other))
                            AssertResolvesTo(state, miss, die1, die2, plays[other.Candidate], where);
                        else if (MoveGenerator.ResolvePlay(state, miss, die1, die2) is Play wrong)
                            Assert.Fail($"{where}: the hops {string.Join(", ", miss)} are no legal sequence's, " +
                                        $"but resolved to {PlayText.Raw(wrong)}.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The sweep's sample: the first positions of the shared seeded corpus,
    /// bounded as the reference property sweep is, because it walks every
    /// legal sequence.
    /// </summary>
    private const int SweepSample = 1_000;

    /// <summary>
    /// How many of a position-roll's legal hop sets the sweep resolves, at
    /// least: every one when there are fewer than twice this many, an evenly
    /// spaced sample otherwise.
    /// </summary>
    private const int ResolvedPerRoll = 16;

    public static TheoryData<int, int> AllRolls()
    {
        var data = new TheoryData<int, int>();
        foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            data.Add(die1, die2);
        return data;
    }

    private static void AssertResolvesTo(BoardState state, Hop[] hops, int die1, int die2, Play expected, string where)
    {
        var resolved = MoveGenerator.ResolvePlay(state, hops, die1, die2);
        if (resolved is not Play play || !play.IsSameEncoding(expected))
            Assert.Fail($"{where}: the hops {string.Join(", ", hops)} resolved to " +
                        $"{(resolved is Play p ? PlayText.Raw(p) : "none")}, not {PlayText.Raw(expected)}.");
    }

    /// <summary>A play's moves with their hit marks set aside, in the play's order.</summary>
    private static Hop[] HopsOf(Play play)
    {
        var hops = new Hop[play.Count];
        for (int i = 0; i < play.Count; i++)
            hops[i] = new Hop(play[i].FrPt, Math.Abs(play[i].ToPt));
        return hops;
    }

    /// <summary>
    /// A hop set's key for the sweep's oracle, the same for every order of
    /// the same hops and different for different hops: the count, then each
    /// hop as <c>From * 32 + To</c> in ascending order, ten bits apiece —
    /// allocation-free, since the sweep keys every legal sequence.
    /// </summary>
    private static long Key(ReadOnlySpan<Hop> hops)
    {
        Span<int> packed = stackalloc int[hops.Length];
        for (int i = 0; i < hops.Length; i++)
            packed[i] = hops[i].From * 32 + hops[i].To;
        packed.Sort();

        long key = hops.Length;
        foreach (int hop in packed)
            key = key * 1024 + hop;
        return key;
    }

    /// <summary>Every order of <paramref name="hops"/>, repeats included.</summary>
    private static IEnumerable<Hop[]> Orders(Hop[] hops)
    {
        if (hops.Length <= 1)
        {
            yield return hops;
            yield break;
        }
        for (int i = 0; i < hops.Length; i++)
        {
            Hop[] rest = [.. hops[..i], .. hops[(i + 1)..]];
            foreach (var tail in Orders(rest))
                yield return [hops[i], .. tail];
        }
    }

    /// <summary>
    /// <paramref name="hops"/> with one hop's landing point moved one point
    /// either way, for each hop, where the moved point is still a landing
    /// point.
    /// </summary>
    private static IEnumerable<Hop[]> NearMisses(Hop[] hops)
    {
        for (int i = 0; i < hops.Length; i++)
        {
            foreach (int step in (int[])[-1, 1])
            {
                if (!Hop.TryCreate(hops[i].From, hops[i].To + step, out var moved))
                    continue;
                var miss = (Hop[])hops.Clone();
                miss[i] = moved;
                yield return miss;
            }
        }
    }
}