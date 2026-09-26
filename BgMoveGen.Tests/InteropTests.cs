using Xunit;
using BgMoveGen;
using BgDataTypes_Lib;
using static BgMoveGen.Interop;

namespace BgMoveGen.Tests;

[CollectionDefinition("Interop", DisableParallelization = true)]
public class InteropCollection { }

[Collection("Interop")]
public unsafe class InteropTests
{
    // ── Helpers ───────────────────────────────────────────────────

    private static BgBoardState MakeExternal(BoardState s,
        int offPlayer = 0, int offOpponent = 0)
    {
        var ext = new BgBoardState();
        for (int i = 0; i < 24; i++)
            ext.Points[i] = (short)s.Points[i + 1];
        ext.BarPlayer = s.Points[25];
        ext.BarOpponent = -s.Points[0];
        ext.OffPlayer = offPlayer;
        ext.OffOpponent = offOpponent;
        return ext;
    }

    private static BgBoardState[] RunInterop(BgBoardState input, int die1, int die2)
    {
        var buffer = new BgBoardState[MaxSuccessors];
        // Heap-allocate so we can take a pointer without fixed on a local
        var inputArr = new BgBoardState[1];
        inputArr[0] = input;
        fixed (BgBoardState* pIn = inputArr)
        fixed (BgBoardState* pOut = buffer)
        {
            int count = Interop.GenerateSuccessorStatesCore(
                pIn, die1, die2, pOut, MaxSuccessors);
            return buffer[..count];
        }
    }

    /// <summary>
    /// Read an external board back as a position — the test's own statement
    /// of BgRLEngine's layout, independent of the one under test.
    /// </summary>
    private static BoardPosition PositionOf(BgBoardState ext)
    {
        var counts = new int[26];
        counts[0] = -ext.BarOpponent;
        for (int i = 0; i < 24; i++)
            counts[i + 1] = ext.Points[i];
        counts[25] = ext.BarPlayer;
        return new BoardPosition(counts);
    }

    /// <summary>
    /// Call the export core with a buffer pre-filled with a sentinel, and
    /// return the status alongside the buffer, so a test can see what was and
    /// was not written.
    /// </summary>
    private static (int Status, BgBoardState[] Buffer) RunInteropRaw(BgBoardState input, int die1, int die2)
    {
        var buffer = new BgBoardState[MaxSuccessors];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i].OffPlayer = Sentinel;
        var inputArr = new[] { input };
        fixed (BgBoardState* pIn = inputArr)
        fixed (BgBoardState* pOut = buffer)
        {
            int status = Interop.GenerateSuccessorStatesCore(pIn, die1, die2, pOut, MaxSuccessors);
            return (status, buffer);
        }
    }

    private const int Sentinel = 0x5EED;

    // ── Tests ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("16 on-roll checkers")]
    [InlineData("an opposing checker on the on-roll bar")]
    [InlineData("an on-roll checker on the opponent's bar")]
    [InlineData("a count beyond a side's checkers")]
    public void MalformedInputBoard_IsRefusedAsInvalidPosition_AndNothingIsWritten(string fault)
    {
        // The reused board is reset through BoardState.SetPosition, whose
        // argument is a well-formed position by its own invariant, so an input
        // that does not form one is refused before any generation runs.
        var input = MakeExternal(BoardState.Standard());
        switch (fault)
        {
            case "16 on-roll checkers": input.BarPlayer = 1; break;
            case "an opposing checker on the on-roll bar": input.BarPlayer = -1; break;
            case "an on-roll checker on the opponent's bar": input.BarOpponent = -1; break;
            case "a count beyond a side's checkers": input.Points[12] = 16; break;
        }

        var (status, buffer) = RunInteropRaw(input, 3, 1);

        Assert.Equal((int)Interop.Status.InvalidPosition, status);
        Assert.All(buffer, b => Assert.Equal(Sentinel, b.OffPlayer));
    }

    [Fact]
    public void SuccessiveCalls_ResetTheReusedBoard_EachMatchingAFreshBoard()
    {
        // The export reuses one board across calls. Back to back, calls on
        // different positions must each produce exactly the successors a
        // fresh board of that position gives: each generated play's position,
        // flipped to the next mover, with the off counts carried and swapped.
        // A reset that left anything behind — a stale high point included,
        // which the generator scans from — shows up as a different successor.
        foreach ((int index, int[] mop) in SyntheticPositions.Corpus(300).Index())
        {
            foreach ((int die1, int die2) in SyntheticPositions.AllRolls())
            {
                var fresh = BoardState.FromMop(mop);
                var plays = MoveGenerator.GeneratePlays(fresh, die1, die2);
                if (plays.Count > MaxSuccessors)
                    continue;   // the buffer truncates; not this pin's subject

                var results = RunInterop(MakeExternal(fresh, offPlayer: 2, offOpponent: 5), die1, die2);

                Assert.Equal(plays.Count, results.Length);
                for (int i = 0; i < plays.Count; i++)
                {
                    var after = fresh.Copy();
                    int bearOffs = 0;
                    foreach (var move in plays[i])
                    {
                        after.ApplyMove(move);
                        if (move.ToPt == 0) bearOffs++;
                    }

                    string where = $"Position {index} {die1}-{die2} successor {i}";
                    Assert.True(after.ToPosition().Flipped() == PositionOf(results[i]), where);
                    Assert.True(results[i].OffPlayer == 5, where);
                    Assert.True(results[i].OffOpponent == 2 + bearOffs, where);
                }
            }
        }
    }

    [Fact]
    public void StandardPosition_OpeningRoll_3_1_MatchesGeneratePlays()
    {
        var state = BoardState.Standard();
        var expected = MoveGenerator.GeneratePlays(state, 3, 1).Count;
        var input = MakeExternal(state);
        var results = RunInterop(input, 3, 1);
        Assert.Equal(expected, results.Length);
    }

    [Fact]
    public void EachSuccessor_IsFromOpponentPerspective_CheckerSignsFlipped()
    {
        var input = MakeExternal(BoardState.Standard());
        var results = RunInterop(input, 3, 1);

        foreach (var r in results)
        {
            bool hasPositive = false;
            bool hasNegative = false;
            for (int i = 0; i < 24; i++)
            {
                if (r.Points[i] > 0) hasPositive = true;
                if (r.Points[i] < 0) hasNegative = true;
            }
            Assert.True(hasPositive && hasNegative,
                "Successor should have both player and opponent checkers");
        }
    }
    [Fact]
    public void SuccessorCount_MatchesGeneratePlays()
    {
        var state = BoardState.Standard();
        for (int d1 = 1; d1 <= 6; d1++)
            for (int d2 = d1; d2 <= 6; d2++)
            {
                var plays = MoveGenerator.GeneratePlays(state, d1, d2);
                bool isPass = plays.Count == 1 && plays[0].Count == 0;
                int expected = isPass ? 0 : plays.Count;

                Assert.True(expected < MaxSuccessors,
                    $"Roll ({d1},{d2}) has {expected} successors — raise MaxSuccessors");

                var input = MakeExternal(state);
                var results = RunInterop(input, d1, d2);

                Assert.Equal(expected, results.Length);
            }
    }

    [Fact]
    public void PassPosition_ReturnsFlippedState()
    {
        var mop = new int[26];
        mop[25] = 2;
        mop[19] = -2; mop[20] = -2; mop[21] = -2;
        mop[22] = -2; mop[23] = -2; mop[24] = -2;
        var s = BoardState.FromMop(mop);

        var results = RunInterop(MakeExternal(s), 3, 1);

        // Pass = 1 flipped state with no moves applied
        Assert.Single(results);

        // Flipped: opponent's blocking points become player's points
        // Original opponent had -2 on pts[19..24] → after flip: +2 on pts[0..5] (external)
        // external points[0]=1-pt … after flip of internal pts[24..1]:
        // internal pts[19]=-2 → external points[18] after flip = +2
        int positiveCount = 0;
        for (int i = 0; i < 24; i++)
            if (results[0].Points[i] > 0) positiveCount++;
        Assert.True(positiveCount > 0);
    }
    [Fact]
    public void CheckerCounts_ConservedAcrossFlip()
    {
        var input = MakeExternal(BoardState.Standard());
        var results = RunInterop(input, 6, 5);

        foreach (var r in results)
        {
            int playerTotal = r.BarPlayer + r.OffPlayer;
            int oppTotal = r.BarOpponent + r.OffOpponent;
            for (int i = 0; i < 24; i++)
            {
                if (r.Points[i] > 0) playerTotal += r.Points[i];
                if (r.Points[i] < 0) oppTotal += -r.Points[i];
            }
            Assert.Equal(15, playerTotal);
            Assert.Equal(15, oppTotal);
        }
    }
    [Fact]
    public void GetStartingPosition_Standard_Returns15CheckersEachSide()
    {
        var output = new BgBoardState[1];
        fixed (BgBoardState* pOut = output)
        {
            int result = Interop.GetStartingPositionCore(0, -1, pOut);
            Assert.Equal(0, result);
        }

        int playerTotal = output[0].BarPlayer + output[0].OffPlayer;
        int oppTotal = output[0].BarOpponent + output[0].OffOpponent;
        for (int i = 0; i < 24; i++)
        {
            if (output[0].Points[i] > 0) playerTotal += output[0].Points[i];
            if (output[0].Points[i] < 0) oppTotal += -output[0].Points[i];
        }
        Assert.Equal(15, playerTotal);
        Assert.Equal(15, oppTotal);
    }

    [Fact]
    public void GetStartingPosition_Nackgammon_Returns15CheckersEachSide()
    {
        var output = new BgBoardState[1];
        fixed (BgBoardState* pOut = output)
        {
            int result = Interop.GetStartingPositionCore(1, -1, pOut);
            Assert.Equal(0, result);
        }

        int playerTotal = output[0].BarPlayer + output[0].OffPlayer;
        int oppTotal = output[0].BarOpponent + output[0].OffOpponent;
        for (int i = 0; i < 24; i++)
        {
            if (output[0].Points[i] > 0) playerTotal += output[0].Points[i];
            if (output[0].Points[i] < 0) oppTotal += -output[0].Points[i];
        }
        Assert.Equal(15, playerTotal);
        Assert.Equal(15, oppTotal);
    }

    [Fact]
    public void GetStartingPosition_UnknownVariant_ReturnsError()
    {
        var output = new BgBoardState[1];
        fixed (BgBoardState* pOut = output)
        {
            int result = Interop.GetStartingPositionCore(99, -1, pOut);
            Assert.Equal(-1, result);
        }
    }

    [Fact]
    public void GetStartingPosition_Bg960_Returns15CheckersEachSide()
    {
        var output = new BgBoardState[1];
        fixed (BgBoardState* pOut = output)
        {
            int result = Interop.GetStartingPositionCore(2, -1, pOut);
            Assert.Equal(0, result);
        }

        int playerTotal = output[0].BarPlayer + output[0].OffPlayer;
        int oppTotal = output[0].BarOpponent + output[0].OffOpponent;
        for (int i = 0; i < 24; i++)
        {
            if (output[0].Points[i] > 0) playerTotal += output[0].Points[i];
            if (output[0].Points[i] < 0) oppTotal += -output[0].Points[i];
        }
        Assert.Equal(15, playerTotal);
        Assert.Equal(15, oppTotal);
    }

    [Fact]
    public void GetStartingPosition_Bg960_SeedIsReproducible()
    {
        var out1 = new BgBoardState[1];
        var out2 = new BgBoardState[1];
        fixed (BgBoardState* p1 = out1)
        fixed (BgBoardState* p2 = out2)
        {
            Interop.GetStartingPositionCore(2, 42, p1);
            Interop.GetStartingPositionCore(2, 42, p2);
        }

        for (int i = 0; i < 24; i++)
            Assert.Equal(out1[0].Points[i], out2[0].Points[i]);
        Assert.Equal(out1[0].BarPlayer, out2[0].BarPlayer);
        Assert.Equal(out1[0].BarOpponent, out2[0].BarOpponent);
    }

    [Fact]
    public void GetStartingPosition_Bg960_PipCountAtLeast100()
    {
        // Run 20 times with different seeds to verify pip count constraint
        for (int seed = 0; seed < 20; seed++)
        {
            var output = new BgBoardState[1];
            fixed (BgBoardState* pOut = output)
                Interop.GetStartingPositionCore(2, seed, pOut);

            int pips = 0;
            for (int i = 0; i < 24; i++)
                if (output[0].Points[i] > 0)
                    pips += output[0].Points[i] * (i + 1);  // points[0]=1-pt
            Assert.True(pips >= 100, $"Seed {seed} produced pip count {pips} < 100");
        }
    }

}