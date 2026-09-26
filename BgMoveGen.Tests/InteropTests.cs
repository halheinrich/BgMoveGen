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

    /// <summary>
    /// The successors, fetched the way the export's contract tells a caller
    /// to: call with a buffer, and when the count that comes back exceeds its
    /// capacity — nothing was written — call again with a buffer of exactly
    /// that count. The first buffer is deliberately small, so the tests that
    /// go through here exercise the retry as well as the plain call.
    /// </summary>
    private static BgBoardState[] RunInterop(BgBoardState input, int die1, int die2)
    {
        var (count, buffer) = RunInteropRaw(input, die1, die2, FirstCapacity);
        Assert.True(count >= 1, $"The export refused the call with status {count}.");
        if (count > FirstCapacity)
        {
            (int again, buffer) = RunInteropRaw(input, die1, die2, count);
            Assert.Equal(count, again);
        }
        return buffer[..count];
    }

    /// <summary>The first buffer <see cref="RunInterop"/> offers: smaller than many rolls' successor lists.</summary>
    private const int FirstCapacity = 8;

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
    /// Call the export core once, stating <paramref name="capacity"/>, with a
    /// buffer pre-filled with a sentinel, and return what came back alongside
    /// the buffer, so a test can see what was and was not written. The buffer
    /// has <paramref name="capacity"/> entries, and at least one: an empty
    /// array pins to a null pointer, which the export refuses before it
    /// reads the capacity.
    /// </summary>
    private static (int Returned, BgBoardState[] Buffer) RunInteropRaw(
        BgBoardState input, int die1, int die2, int capacity = RawCapacity)
    {
        var buffer = new BgBoardState[Math.Max(capacity, 1)];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i].OffPlayer = Sentinel;
        var inputArr = new[] { input };
        fixed (BgBoardState* pIn = inputArr)
        fixed (BgBoardState* pOut = buffer)
        {
            int returned = Interop.GenerateSuccessorStatesCore(pIn, die1, die2, pOut, capacity);
            return (returned, buffer);
        }
    }

    /// <summary>A buffer comfortably larger than any opening roll's successor list.</summary>
    private const int RawCapacity = 64;

    private const int Sentinel = 0x5EED;

    /// <summary>
    /// A race with one on-roll checker on each point from 2 to 16 and the
    /// opponent's fifteen in its home board: 1-1 has 1,547 distinct plays.
    /// </summary>
    private static BoardState SpreadRace()
    {
        var mop = new int[26];
        for (int p = 2; p <= 16; p++) mop[p] = 1;
        mop[19] = -3; mop[20] = -3; mop[21] = -3;
        mop[22] = -2; mop[23] = -2; mop[24] = -2;
        return BoardState.FromMop(mop);
    }

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

    [Theory]
    [InlineData(0, 3)]
    [InlineData(7, 3)]
    [InlineData(3, 0)]
    [InlineData(3, 7)]
    [InlineData(-1, int.MaxValue)]
    public void DieOutsideOneToSix_ReturnsInvalidArgument_AndNothingIsWritten(int die1, int die2)
    {
        // GeneratePlays refuses the die with an ArgumentOutOfRangeException;
        // the export turns the refusal into a status rather than let it cross
        // the native boundary, which would terminate BgRLEngine's process.
        var (status, buffer) = RunInteropRaw(MakeExternal(BoardState.Standard()), die1, die2);

        Assert.Equal((int)Interop.Status.InvalidArgument, status);
        Assert.All(buffer, b => Assert.Equal(Sentinel, b.OffPlayer));
    }

    [Fact]
    public void NullPointersAndANegativeCapacity_ReturnInvalidArgument()
    {
        var input = new[] { MakeExternal(BoardState.Standard()) };
        var buffer = new BgBoardState[RawCapacity];
        fixed (BgBoardState* pIn = input)
        fixed (BgBoardState* pOut = buffer)
        {
            Assert.Equal((int)Interop.Status.InvalidArgument,
                Interop.GenerateSuccessorStatesCore(null, 3, 1, pOut, RawCapacity));
            Assert.Equal((int)Interop.Status.InvalidArgument,
                Interop.GenerateSuccessorStatesCore(pIn, 3, 1, null, RawCapacity));
            Assert.Equal((int)Interop.Status.InvalidArgument,
                Interop.GenerateSuccessorStatesCore(pIn, 3, 1, pOut, -1));
            Assert.Equal((int)Interop.Status.InvalidArgument,
                Interop.GetStartingPositionCore(0, -1, null));
        }
    }

    [Fact]
    public void MoreSuccessorsThanTheCapacity_ReturnsTheFullCount_AndWritesNothing()
    {
        // BgRLEngine's buffer holds 100; this position's 1-1 has 1,547
        // successors. The export no longer truncates: it writes none of them
        // and returns the full count, which the caller compares with its
        // capacity and retries with.
        var race = SpreadRace();
        int count = MoveGenerator.GeneratePlays(race, 1, 1).Count;
        Assert.Equal(1547, count);

        var (returned, buffer) = RunInteropRaw(MakeExternal(race), 1, 1, capacity: 100);

        Assert.Equal(count, returned);
        Assert.All(buffer, b => Assert.Equal(Sentinel, b.OffPlayer));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]   // one short of the opening 3-1's successors
    public void ACapacityShortOfTheCount_ReturnsTheCount_AndWritesNothing(int capacity)
    {
        var state = BoardState.Standard();
        int count = MoveGenerator.GeneratePlays(state, 3, 1).Count;
        Assert.True(capacity < count, $"The opening 3-1 has {count} successors.");

        var (returned, buffer) = RunInteropRaw(MakeExternal(state), 3, 1, capacity);

        Assert.Equal(count, returned);
        Assert.All(buffer, b => Assert.Equal(Sentinel, b.OffPlayer));
    }

    [Fact]
    public void ABufferOfExactlyTheCount_ReceivesEverySuccessor_EqualToGenerateSuccessors()
    {
        // The retry the overflow return asks for: a buffer of exactly the
        // count receives every successor, each the managed view's position.
        var race = SpreadRace();
        var managed = MoveGenerator.GenerateSuccessors(race, 1, 1);

        var (returned, buffer) = RunInteropRaw(MakeExternal(race), 1, 1, capacity: managed.Count);

        Assert.Equal(managed.Count, returned);
        for (int i = 0; i < managed.Count; i++)
            Assert.True(managed[i].Position == PositionOf(buffer[i]), $"Successor {i} differs from GenerateSuccessors.");
    }

    [Fact]
    public void TheExportedEntryPoints_ReturnStatuses_NeverAnException()
    {
        // Through the [UnmanagedCallersOnly] methods themselves, called as
        // native code calls them — by unmanaged function pointer. An
        // exception escaping one terminates the process, so this test run
        // finishing at all is half the pin; the statuses are the other half.
        delegate* unmanaged<BgBoardState*, int, int, BgBoardState*, int, int> generate = &Interop.GenerateSuccessorStates;
        delegate* unmanaged<int, int, BgBoardState*, int> start = &Interop.GetStartingPosition;

        var input = new[] { MakeExternal(BoardState.Standard()) };
        var malformed = new[] { MakeExternal(BoardState.Standard()) };
        malformed[0].BarPlayer = 1;   // a sixteenth on-roll checker
        var buffer = new BgBoardState[RawCapacity];
        var tooSmall = new BgBoardState[1];
        tooSmall[0].OffPlayer = Sentinel;
        int count = MoveGenerator.GeneratePlays(BoardState.Standard(), 3, 1).Count;
        fixed (BgBoardState* pIn = input)
        fixed (BgBoardState* pBad = malformed)
        fixed (BgBoardState* pOut = buffer)
        fixed (BgBoardState* pSmall = tooSmall)
        {
            Assert.Equal(count, generate(pIn, 3, 1, pOut, RawCapacity));
            Assert.Equal(count, generate(pIn, 3, 1, pSmall, 1));   // overflow: the count, nothing written
            Assert.Equal(Sentinel, tooSmall[0].OffPlayer);
            Assert.Equal((int)Interop.Status.InvalidArgument, generate(pIn, 0, 1, pOut, RawCapacity));
            Assert.Equal((int)Interop.Status.InvalidArgument, generate(pIn, 3, 7, pOut, RawCapacity));
            Assert.Equal((int)Interop.Status.InvalidArgument, generate(null, 3, 1, pOut, RawCapacity));
            Assert.Equal((int)Interop.Status.InvalidPosition, generate(pBad, 3, 1, pOut, RawCapacity));

            Assert.Equal(0, start(0, -1, pOut));
            Assert.Equal((int)Interop.Status.InvalidArgument, start(99, -1, pOut));
            Assert.Equal((int)Interop.Status.InvalidArgument, start(0, -1, null));
        }
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

                var results = RunInterop(MakeExternal(fresh, offPlayer: 2, offOpponent: 5), die1, die2);
                var managed = MoveGenerator.GenerateSuccessors(fresh, die1, die2);

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
                    Assert.True(managed[i].Position == PositionOf(results[i]), $"{where}: differs from GenerateSuccessors.");
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