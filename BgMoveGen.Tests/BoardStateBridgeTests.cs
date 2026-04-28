using BgMoveGen;

namespace BgMoveGen.Tests;

public class BoardStateBridgeTests
{
    private static void AssertSamePoints(BoardState a, BoardState b)
    {
        for (int i = 0; i <= 25; i++)
            Assert.Equal(a.Points[i], b.Points[i]);
        Assert.Equal(a.HighPointOccupied, b.HighPointOccupied);
    }

    [Fact]
    public void FromMop_ToMop_RoundTrips_Standard()
    {
        var original = BoardState.Standard();
        var roundTripped = BoardState.FromMop(original.ToMop());
        AssertSamePoints(original, roundTripped);
    }

    [Fact]
    public void FromMop_ToMop_RoundTrips_Nackgammon()
    {
        var original = BoardState.Nackgammon();
        var roundTripped = BoardState.FromMop(original.ToMop());
        AssertSamePoints(original, roundTripped);
    }

    [Fact]
    public void FromMop_ToMop_RoundTrips_Bg960Seeded()
    {
        var original = BoardState.Bg960(seed: 42);
        var roundTripped = BoardState.FromMop(original.ToMop());
        AssertSamePoints(original, roundTripped);
    }

    [Fact]
    public void FromMop_ToMop_RoundTrips_MidGamePosition()
    {
        // Hand-built mid-game: both bars occupied, hit-eligible blot, partial bear-off,
        // both signs present at non-trivial counts.
        var original = new BoardState();
        original.Points[0] = -1;   // opponent on bar
        original.Points[1] = 3;    // player home-board point
        original.Points[3] = -1;   // opponent blot in player's home
        original.Points[6] = 4;
        original.Points[8] = 2;
        original.Points[12] = -3;
        original.Points[13] = 2;
        original.Points[17] = -4;
        original.Points[19] = -5;
        original.Points[24] = 1;   // player blot on 24
        original.Points[25] = 1;   // player on bar
        original.RecalcHighPoint();

        var roundTripped = BoardState.FromMop(original.ToMop());
        AssertSamePoints(original, roundTripped);
        Assert.Equal(25, roundTripped.HighPointOccupied);
    }

    [Fact]
    public void ToMop_ReturnsLength26()
    {
        var mop = BoardState.Standard().ToMop();
        Assert.Equal(26, mop.Count);
    }

    [Fact]
    public void ToMop_IsIndependentOfSource()
    {
        var state = BoardState.Standard();
        var mop = state.ToMop();
        int original5 = state.Points[5];

        state.Points[5] = 99;

        Assert.Equal(original5, mop[5]);
    }

    [Fact]
    public void FromMop_RecalcsHighPoint()
    {
        var mop = new int[26];
        mop[5] = 3;
        mop[20] = 2;   // highest player checker

        var state = BoardState.FromMop(mop);

        Assert.Equal(20, state.HighPointOccupied);
    }

    [Fact]
    public void FromMop_RecalcsHighPoint_BarOccupied()
    {
        var mop = new int[26];
        mop[6] = 5;
        mop[25] = 1;   // player on bar — highest

        var state = BoardState.FromMop(mop);

        Assert.Equal(25, state.HighPointOccupied);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(27)]
    public void FromMop_WrongLength_Throws(int length)
    {
        var mop = new int[length];
        Assert.Throws<ArgumentException>(() => BoardState.FromMop(mop));
    }

    [Fact]
    public void FromMop_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BoardState.FromMop(null!));
    }

    [Fact]
    public void FromMop_AcceptsPseudoboard_AllZero()
    {
        // Borne-off / cube-decision shells with no checkers are legitimate inputs.
        var state = BoardState.FromMop(new int[26]);
        Assert.Equal(0, state.HighPointOccupied);
        for (int i = 0; i <= 25; i++)
            Assert.Equal(0, state.Points[i]);
    }
}
