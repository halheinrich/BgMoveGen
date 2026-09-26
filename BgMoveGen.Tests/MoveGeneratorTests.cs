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

public class GenerateResultingStatesTests
{
    [Fact]
    public void GenerateResultingStates_MatchesFinalBoardsOfGeneratePlays_AcrossSyntheticPositions()
    {
        // The defining relation: board i is exactly the input with candidate
        // i's moves applied — same count, same order, and HighPointOccupied
        // carried along — across a corpus that reaches bar entry, hits and
        // bear-offs, not only the opening board.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(1_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(state, die1, die2);
                var states = MoveGenerator.GenerateResultingStates(state, die1, die2);

                Assert.Equal(plays.Count, states.Count);
                for (int i = 0; i < plays.Count; i++)
                {
                    var expected = state.Copy();
                    for (int j = 0; j < plays[i].Count; j++)
                        expected.ApplyMove(plays[i][j]);

                    Assert.True(expected.ToPosition() == states[i].ToPosition(),
                        $"Position {index} {die1}-{die2}: board {i} is not candidate {i} applied.");
                    Assert.Equal(expected.HighPointOccupied, states[i].HighPointOccupied);
                }
            }
        }
    }

    [Fact]
    public void GenerateResultingStates_BoardsAreInMoversFrame_NotFlipped()
    {
        // Opening 3-1 cannot hit, so in the mover's frame every resulting board
        // keeps the opponent's four stacks exactly where they started; a flip
        // would move them to 25 - i. The point-making 8/5 6/5 must be present
        // exactly as the mover sees it.
        var state = BoardState.Standard();

        var states = MoveGenerator.GenerateResultingStates(state, 3, 1);

        foreach (var s in states)
        {
            Assert.Equal(-2, s.Points[1]);
            Assert.Equal(-5, s.Points[12]);
            Assert.Equal(-3, s.Points[17]);
            Assert.Equal(-5, s.Points[19]);
        }

        var pointMade = Boards.OpeningAfterMakingTheFivePoint();
        Assert.Contains(states, s => s.ToPosition() == pointMade.ToPosition());
    }

    [Fact]
    public void GenerateResultingStates_NoLegalMove_ReturnsSingleCopyEqualToInput()
    {
        var state = Boards.ClosedOut();

        var states = MoveGenerator.GenerateResultingStates(state, 6, 4);

        var only = Assert.Single(states);
        Assert.NotSame(state, only);
        Assert.False(state.Points.Overlaps(only.Points));
        Assert.Equal(state.ToPosition(), only.ToPosition());
        Assert.Equal(state.HighPointOccupied, only.HighPointOccupied);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 5)]
    [InlineData(2, 2)]
    public void GenerateResultingStates_DoesNotMutateInput(int die1, int die2)
    {
        var state = BoardState.Standard();
        var original = state.Copy();

        MoveGenerator.GenerateResultingStates(state, die1, die2);

        Assert.Equal(original.ToPosition(), state.ToPosition());
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void GenerateResultingStates_BoardsAreIndependentCallerOwnedCopies()
    {
        // Ownership is the contract, not immutability: a caller may scribble
        // on any returned board without reaching the input, another result,
        // or a later call's results.
        var state = BoardState.Standard();
        var original = state.Copy();

        var states = MoveGenerator.GenerateResultingStates(state, 6, 5);
        Assert.True(states.Count > 1);
        var snapshot = states.Select(s => s.Copy()).ToList();

        for (int i = 0; i < states.Count; i++)
        {
            Assert.False(state.Points.Overlaps(states[i].Points));
            for (int j = i + 1; j < states.Count; j++)
                Assert.False(states[i].Points.Overlaps(states[j].Points));
        }

        // The one whole-board write a caller has: every slot and the high
        // point change at once.
        states[0].SetPosition(BoardPosition.Empty);

        Assert.Equal(original.ToPosition(), state.ToPosition());
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
        for (int i = 1; i < states.Count; i++)
            Assert.Equal(snapshot[i].ToPosition(), states[i].ToPosition());

        var again = MoveGenerator.GenerateResultingStates(state, 6, 5);
        Assert.Equal(snapshot.Count, again.Count);
        for (int i = 0; i < again.Count; i++)
            Assert.Equal(snapshot[i].ToPosition(), again[i].ToPosition());
    }

    [Fact]
    public void GenerateResultingStates_BoardsAreDistinct_AcrossSyntheticPositions()
    {
        // The reference sweep compares board *sets*, which a repeated board
        // survives, so this compares the boards themselves, as BoardPosition
        // values whose equality decides — a hash only buckets them. It pins
        // the property a consumer choosing among positions relies on: no
        // position is returned — and so weighted — twice.
        var distinct = new HashSet<BoardPosition>();
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(4_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var states = MoveGenerator.GenerateResultingStates(BoardState.FromMop(mop), die1, die2);

                distinct.Clear();
                foreach (var s in states)
                    distinct.Add(s.ToPosition());

                Assert.True(distinct.Count == states.Count,
                    $"Position {index} {die1}-{die2}: {states.Count} resulting boards " +
                    $"collapse to {distinct.Count} distinct positions.");
            }
        }
    }
}

public class GenerateCandidatePlaysTests
{
    [Fact]
    public void GenerateCandidatePlays_PairsEachGeneratedPlayWithItsResultingState_AcrossSyntheticPositions()
    {
        // The association is the whole contract: candidate i carries
        // GeneratePlays' candidate i in the generator's own encoding (the
        // identical moves, not merely the same play) and exactly the board
        // GenerateResultingStates returns at i — which is that play applied.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(1_000).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var state = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(state, die1, die2);
                var boards = MoveGenerator.GenerateResultingStates(state, die1, die2);
                var candidates = MoveGenerator.GenerateCandidatePlays(state, die1, die2);

                Assert.Equal(plays.Count, candidates.Count);
                for (int i = 0; i < plays.Count; i++)
                {
                    string where = $"Position {index} {die1}-{die2} candidate {i}";
                    Assert.True(plays[i].IsSameEncoding(candidates[i].Play), $"{where}: play is not the generator's encoding.");

                    var expected = state.Copy();
                    for (int j = 0; j < plays[i].Count; j++)
                        expected.ApplyMove(plays[i][j]);

                    Assert.True(expected.ToPosition() == candidates[i].ResultingState.ToPosition(),
                        $"{where}: resulting state is not the play applied.");
                    Assert.Equal(expected.HighPointOccupied, candidates[i].ResultingState.HighPointOccupied);
                    Assert.True(boards[i].ToPosition() == candidates[i].ResultingState.ToPosition(),
                        $"{where}: resulting state differs from GenerateResultingStates.");
                }
            }
        }
    }

    [Fact]
    public void GenerateCandidatePlays_ResultingStatesAreInMoversFrame_NotFlipped()
    {
        // Opening 3-1 cannot hit: every board keeps the opponent's stacks in
        // place, and the 8/5 6/5 candidate carries the point-made board as
        // the mover sees it.
        var state = BoardState.Standard();

        var candidates = MoveGenerator.GenerateCandidatePlays(state, 3, 1);

        foreach (var c in candidates)
        {
            Assert.Equal(-2, c.ResultingState.Points[1]);
            Assert.Equal(-5, c.ResultingState.Points[12]);
            Assert.Equal(-3, c.ResultingState.Points[17]);
            Assert.Equal(-5, c.ResultingState.Points[19]);
        }

        var pointMade = Boards.OpeningAfterMakingTheFivePoint();

        Play eightFiveSixFive = [new(8, 5), new(6, 5)];
        var makePoint = Assert.Single(candidates, c => state.IsSamePlay(c.Play, eightFiveSixFive));
        Assert.Equal(pointMade.ToPosition(), makePoint.ResultingState.ToPosition());
    }

    [Fact]
    public void GenerateCandidatePlays_NoLegalMove_ReturnsSinglePassWithCopyOfInput()
    {
        var state = Boards.ClosedOut();

        var candidates = MoveGenerator.GenerateCandidatePlays(state, 6, 4);

        var only = Assert.Single(candidates);
        Assert.Equal(0, only.Play.Count);
        Assert.NotSame(state, only.ResultingState);
        Assert.False(state.Points.Overlaps(only.ResultingState.Points));
        Assert.Equal(state.ToPosition(), only.ResultingState.ToPosition());
        Assert.Equal(state.HighPointOccupied, only.ResultingState.HighPointOccupied);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 5)]
    [InlineData(2, 2)]
    public void GenerateCandidatePlays_DoesNotMutateInput(int die1, int die2)
    {
        var state = BoardState.Standard();
        var original = state.Copy();

        MoveGenerator.GenerateCandidatePlays(state, die1, die2);

        Assert.Equal(original.ToPosition(), state.ToPosition());
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
    }

    [Fact]
    public void GenerateCandidatePlays_ResultingStatesAreIndependentCallerOwnedCopies()
    {
        // Ownership, not immutability: scribbling on one candidate's board
        // reaches neither the input, another candidate, nor a later call.
        var state = BoardState.Standard();
        var original = state.Copy();

        var candidates = MoveGenerator.GenerateCandidatePlays(state, 6, 5);
        Assert.True(candidates.Count > 1);
        var snapshot = candidates.Select(c => c.ResultingState.Copy()).ToList();

        for (int i = 0; i < candidates.Count; i++)
        {
            Assert.False(state.Points.Overlaps(candidates[i].ResultingState.Points));
            for (int j = i + 1; j < candidates.Count; j++)
                Assert.False(candidates[i].ResultingState.Points.Overlaps(candidates[j].ResultingState.Points));
        }

        // The one whole-board write a caller has: every slot and the high
        // point change at once.
        candidates[0].ResultingState.SetPosition(BoardPosition.Empty);

        Assert.Equal(original.ToPosition(), state.ToPosition());
        Assert.Equal(original.HighPointOccupied, state.HighPointOccupied);
        for (int i = 1; i < candidates.Count; i++)
            Assert.Equal(snapshot[i].ToPosition(), candidates[i].ResultingState.ToPosition());

        var again = MoveGenerator.GenerateCandidatePlays(state, 6, 5);
        Assert.Equal(snapshot.Count, again.Count);
        for (int i = 0; i < again.Count; i++)
            Assert.Equal(snapshot[i].ToPosition(), again[i].ResultingState.ToPosition());
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