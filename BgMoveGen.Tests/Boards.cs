using BgDataTypes_Lib;

namespace BgMoveGen.Tests;

/// <summary>
/// Hand-built boards more than one test class shares. A board cannot be
/// written count by count, so each is built from its counts through
/// <see cref="BoardState.FromMop"/>, the door from raw counts, which refuses
/// counts that do not form a position.
/// </summary>
internal static class Boards
{
    /// <summary>
    /// Closed out: the on-roll player has one checker on the bar and the
    /// opponent holds every entry point, 19 to 24, so no roll has a legal
    /// move and the only play is the empty pass.
    /// </summary>
    internal static BoardState ClosedOut()
    {
        var mop = new int[26];
        mop[25] = 1;
        for (int i = 19; i <= 24; i++) mop[i] = -2;
        return BoardState.FromMop(mop);
    }

    /// <summary>
    /// The opening board with the opponent's 17-point stack split into a blot
    /// on 18 and two on 17: a 6 from the 24-point lands on the blot, so 6-4
    /// must hit.
    /// </summary>
    internal static BoardState BlotOnEighteen()
    {
        var mop = new int[26];
        mop[24] = 2;
        mop[13] = 5;
        mop[6] = 5;
        mop[8] = 3;
        mop[18] = -1;   // opponent blot — a 6 from 24 must hit it
        mop[1] = -2;
        mop[12] = -5;
        mop[17] = -2;
        mop[19] = -5;
        return BoardState.FromMop(mop);
    }

    /// <summary>The opening board after 8/5 6/5, written out count by count.</summary>
    internal static BoardState OpeningAfterMakingTheFivePoint()
    {
        var mop = new int[26];
        BoardPosition.Standard.CopyTo(mop);
        mop[8] = 2;
        mop[6] = 4;
        mop[5] = 2;
        return BoardState.FromMop(mop);
    }
}
