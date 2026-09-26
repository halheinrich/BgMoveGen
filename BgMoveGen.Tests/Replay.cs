using BgDataTypes_Lib;

namespace BgMoveGen.Tests;

/// <summary>
/// The tests' own statement of where a move sequence leads, independent of
/// the library's: the moves applied one by one with the raw pair to a copy of
/// the start. Positions are compared as <see cref="BoardPosition"/> values,
/// the one definition of "the same position" — never by a hash.
/// </summary>
internal static class Replay
{
    /// <summary>
    /// The position <paramref name="play"/>'s moves reach from
    /// <paramref name="start"/>, in the mover's frame. The moves must be a
    /// legal sequence from <paramref name="start"/> (the raw pair trusts
    /// them); <paramref name="start"/> is not touched.
    /// </summary>
    internal static BoardPosition PositionAfter(BoardState start, Play play)
    {
        var board = start.Copy();
        foreach (var move in play)
            board.ApplyMove(move);
        return board.ToPosition();
    }
}
