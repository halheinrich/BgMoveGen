using Xunit;
using BgMoveGen;
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

    // ── Tests ─────────────────────────────────────────────────────

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
    public void SuccessorCount_MatchesGenerateStates()
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
    public void PassPosition_Returns0Successors()
    {
        var s = new BoardState();
        s.Points[25] = 2;
        s.Points[19] = -2; s.Points[20] = -2; s.Points[21] = -2;
        s.Points[22] = -2; s.Points[23] = -2; s.Points[24] = -2;
        s.RecalcHighPoint();

        var results = RunInterop(MakeExternal(s), 3, 1);
        Assert.Empty(results);
    }

    [Fact]
    public void BearOff_OffCountsCorrectAfterFlip()
    {
        var s = new BoardState();
        s.Points[3] = 2;
        s.RecalcHighPoint();

        int offPlayerIn = 13;
        int offOpponentIn = 7;
        var input = MakeExternal(s, offPlayer: offPlayerIn, offOpponent: offOpponentIn);

        var plays = MoveGenerator.GeneratePlays(s, 3, 3);
        var results = RunInterop(input, 3, 3);

        Assert.Equal(plays.Count, results.Length);

        for (int i = 0; i < plays.Count; i++)
        {
            int bearOffs = 0;
            for (int j = 0; j < plays[i].Count; j++)
                if (plays[i][j].ToPt == 0) bearOffs++;

            int expectedOffOpponent = offPlayerIn + bearOffs;
            int expectedOffPlayer = offOpponentIn;

            Assert.Equal(expectedOffPlayer, results[i].OffPlayer);
            Assert.Equal(expectedOffOpponent, results[i].OffOpponent);
        }
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
}