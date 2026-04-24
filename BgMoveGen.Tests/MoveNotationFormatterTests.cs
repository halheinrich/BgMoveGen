using BgMoveGen;

namespace BgMoveGen.Tests;

public class MoveNotationFormatterTests
{
    private static Play MakePlay(params Move[] moves)
    {
        var play = new Play();
        foreach (var m in moves) play.Add(m);
        return play;
    }

    // Regular moves --------------------------------------------------------

    [Fact]
    public void Format_TwoDistinctMoves_SortedByFromPtDesc()
    {
        // Input order (13, 8) then (24, 22); output sorted from-pt desc.
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(13, 8), new Move(24, 22)));
        Assert.Equal("24/22 13/8", result);
    }

    [Fact]
    public void Format_SingleMove_Renders()
    {
        var result = MoveNotationFormatter.Format(MakePlay(new Move(13, 8)));
        Assert.Equal("13/8", result);
    }

    [Fact]
    public void Format_SameMoveTwice_GroupsWithCount()
    {
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(13, 11), new Move(13, 11)));
        Assert.Equal("13/11(2)", result);
    }

    // Bar entry ------------------------------------------------------------

    [Fact]
    public void Format_BarEntry_RendersBarPrefix()
    {
        var result = MoveNotationFormatter.Format(MakePlay(new Move(25, 21)));
        Assert.Equal("bar/21", result);
    }

    [Fact]
    public void Format_BarEntryHit_AppendsAsterisk()
    {
        var result = MoveNotationFormatter.Format(MakePlay(new Move(25, -22)));
        Assert.Equal("bar/22*", result);
    }

    [Fact]
    public void Format_TwoBarEntries_Grouped()
    {
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(25, 23), new Move(25, 23)));
        Assert.Equal("bar/23(2)", result);
    }

    [Fact]
    public void Format_TwoBarEntriesDifferentPoints_SortedByToPtDescTiebreak()
    {
        // Tied on from-pt (both bar=25); |to-pt| desc tiebreaker: 22 > 20.
        // Input in reverse order to exercise the sort.
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(25, 20), new Move(25, 22)));
        Assert.Equal("bar/22 bar/20", result);
    }

    // Bear off -------------------------------------------------------------

    [Fact]
    public void Format_BearOff_RendersOffSuffix()
    {
        var result = MoveNotationFormatter.Format(MakePlay(new Move(2, 0)));
        Assert.Equal("2/off", result);
    }

    [Fact]
    public void Format_TwoBearOffs_SeparateWhenDistinct()
    {
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(6, 0), new Move(5, 0)));
        Assert.Equal("6/off 5/off", result);
    }

    [Fact]
    public void Format_DoublesBearOffSamePoint_Grouped()
    {
        var result = MoveNotationFormatter.Format(MakePlay(
            new Move(5, 1), new Move(4, 0), new Move(4, 0), new Move(4, 0)));
        Assert.Equal("5/1 4/off(3)", result);
    }

    [Fact]
    public void Format_BearOffFromPoint1_RendersAs1Off()
    {
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(6, 3), new Move(1, 0)));
        Assert.Equal("6/3 1/off", result);
    }

    // Hits -----------------------------------------------------------------

    [Fact]
    public void Format_HitOnDestination_AppendsAsterisk()
    {
        var result = MoveNotationFormatter.Format(MakePlay(new Move(24, -18)));
        Assert.Equal("24/18*", result);
    }

    [Fact]
    public void Format_TwoSeparateHits_BothMarked()
    {
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(24, -18), new Move(13, -9)));
        Assert.Equal("24/18* 13/9*", result);
    }

    [Fact]
    public void Format_HitAtIntermediatePreventsForwardChainCollapse()
    {
        // 24/18*/17 — hit at 18 must stay visible, so chain doesn't collapse
        // to "24/17".
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(24, -18), new Move(18, 17)));
        Assert.Equal("24/18* 18/17", result);
    }

    [Fact]
    public void Format_HitAtFinalPointInChain_PreservesAsterisk()
    {
        // 24→21→15*: hit is at the chain's endpoint, so collapse is fine
        // and the "*" lands on the endpoint.
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(24, 21), new Move(21, -15)));
        Assert.Equal("24/15*", result);
    }

    [Fact]
    public void Format_OutOfOrderLegs_HitOnLaterLegPreventsBackwardExtension()
    {
        // Hypothetical out-of-order input: (20, 14) then (21, -20). The hit
        // on 20 is an intermediate-point hit of the notional chain 21→20*→14,
        // so the legs must not collapse (otherwise the "*" is lost). Output
        // then sorts by from-pt desc: 21 > 20.
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(20, 14), new Move(21, -20)));
        Assert.Equal("21/20* 20/14", result);
    }

    // Doubles --------------------------------------------------------------

    [Fact]
    public void Format_FullDoubles_RendersAllFour()
    {
        var result = MoveNotationFormatter.Format(MakePlay(
            new Move(24, 20), new Move(13, 9), new Move(13, 9), new Move(8, 4)));
        Assert.Equal("24/20 13/9(2) 8/4", result);
    }

    [Fact]
    public void Format_DoublesAllSame_GroupsAsFour()
    {
        var result = MoveNotationFormatter.Format(MakePlay(
            new Move(8, 4), new Move(8, 4), new Move(8, 4), new Move(8, 4)));
        Assert.Equal("8/4(4)", result);
    }

    [Fact]
    public void Format_PartialRepeat_GroupsOnlyMatching()
    {
        // 8/5 6/3 6/3 — first leg distinct, next two identical.
        var result = MoveNotationFormatter.Format(MakePlay(
            new Move(8, 5), new Move(6, 3), new Move(6, 3)));
        Assert.Equal("8/5 6/3(2)", result);
    }

    // Chain compression ----------------------------------------------------

    [Fact]
    public void Format_SameCheckerChainInOrder_CollapsesToSingleLeg()
    {
        // 24→21→15 emitted in time order.
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(24, 21), new Move(21, 15)));
        Assert.Equal("24/15", result);
    }

    [Fact]
    public void Format_SameCheckerChainOutOfOrder_CollapsesVia21Slash14BugFix()
    {
        // The 21/14 bug: XG emits (20, 14) before (21, 20). Forward-only
        // matching produced "20/14 21/20"; bidirectional matching extends
        // the chain's start backward to produce "21/14".
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(20, 14), new Move(21, 20)));
        Assert.Equal("21/14", result);
    }

    [Fact]
    public void Format_BarEntryChain_CompressesToBarSlashFinal()
    {
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(25, 21), new Move(21, 15)));
        Assert.Equal("bar/15", result);
    }

    [Fact]
    public void Format_ChainThenBearOff_Compresses()
    {
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(4, 1), new Move(1, 0)));
        Assert.Equal("4/off", result);
    }

    [Fact]
    public void Format_ChainThenBearOff_OutOfOrder_StillCompresses()
    {
        // (1, off) emitted first, then (4, 1) — backward extend the bear-off
        // chain. Start becomes 4; end stays at off.
        var result = MoveNotationFormatter.Format(
            MakePlay(new Move(1, 0), new Move(4, 1)));
        Assert.Equal("4/off", result);
    }

    [Fact]
    public void Format_TwoIdenticalChains_GroupAfterMerge()
    {
        // Two checkers each chain 24→20→16.
        var result = MoveNotationFormatter.Format(MakePlay(
            new Move(24, 20), new Move(20, 16),
            new Move(24, 20), new Move(20, 16)));
        Assert.Equal("24/16(2)", result);
    }

    [Fact]
    public void Format_InterleavedDoublesChains_GroupAcrossInterleave()
    {
        // Doubles where XG emits the starts together then the continuations
        // together: (20,14)(20,14)(14,8)(14,8). Each (14,8) extends one of
        // the earlier open chains forward, yielding two identical (20,8)
        // chains that group as "20/8(2)".
        var result = MoveNotationFormatter.Format(MakePlay(
            new Move(20, 14), new Move(20, 14),
            new Move(14, 8),  new Move(14, 8)));
        Assert.Equal("20/8(2)", result);
    }

    [Fact]
    public void Format_MixChainableAndNonChainable_OnlyMatchingLegsCollapse()
    {
        // 24→21→15 chains; 13/8 is independent.
        var result = MoveNotationFormatter.Format(MakePlay(
            new Move(24, 21), new Move(13, 8), new Move(21, 15)));
        Assert.Equal("24/15 13/8", result);
    }

    // Edge cases -----------------------------------------------------------

    [Fact]
    public void Format_EmptyPlay_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, MoveNotationFormatter.Format(new Play()));
    }
}
