using BgDataTypes_Lib;

namespace BgMoveGen.Tests;

/// <summary>
/// How a test failure names a play: its raw encoding, move by move in the
/// order written, hit marks included — what two encodings of one play differ
/// in, and what <see cref="Play.ToNotation"/> does not show.
/// </summary>
internal static class PlayText
{
    /// <summary><paramref name="play"/> as <c>{(13,10) (10,-8)}</c>.</summary>
    internal static string Raw(Play play)
    {
        var parts = new List<string>(play.Count);
        foreach (var move in play)
            parts.Add($"({move.FrPt},{move.ToPt})");
        return "{" + string.Join(" ", parts) + "}";
    }
}
