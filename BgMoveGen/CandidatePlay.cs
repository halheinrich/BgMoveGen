using System.Diagnostics.CodeAnalysis;
using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// One legal candidate for a roll: the generator's <see cref="BgDataTypes_Lib.Play"/>
/// together with the position that play reaches, in the mover's frame.
/// Returned by <see cref="MoveGenerator.GenerateCandidatePlays"/>; see that
/// method for the full contract. The counterpart of <see cref="Successor"/>,
/// which holds the position a play leaves the next mover.
///
/// <para>
/// <b>A value.</b> The play and the position are held inline, so a list of
/// candidates is one array and a candidate costs no allocation of its own.
/// Both parts are values: <see cref="Play"/> returns a copy, and
/// <see cref="ResultingPosition"/> is immutable, so nothing a caller does to
/// what it reads can break the pairing.
/// </para>
///
/// <para>
/// <b>Only BgMoveGen pairs a play with its position.</b> The constructor is
/// internal, so a candidate a consumer holds pairs a play with the position
/// that play actually reaches. The one other instance a consumer can make is
/// <see langword="default"/>: the empty play with the empty position.
/// </para>
///
/// <para>
/// <b>No equality.</b> A candidate holds a <see cref="BgDataTypes_Lib.Play"/>,
/// which has none — whether two plays are the same play depends on the
/// position they are played from (<see cref="BoardState.IsSamePlay"/>). So
/// <c>==</c> is not defined, and <see cref="Equals(object)"/> and
/// <see cref="GetHashCode"/> throw rather than compare the play field-wise.
/// Positions compare as <see cref="BoardPosition"/> values:
/// <c>a.ResultingPosition == b.ResultingPosition</c>.
/// </para>
/// </summary>
public readonly struct CandidatePlay
{
    internal CandidatePlay(Play play, BoardPosition resultingPosition)
    {
        Play = play;
        ResultingPosition = resultingPosition;
    }

    /// <summary>
    /// The candidate play, in the generator's own encoding — the element
    /// <see cref="MoveGenerator.GeneratePlays"/> returns at the same index.
    /// The empty play is a forced pass.
    /// </summary>
    public Play Play { get; }

    /// <summary>
    /// The position after <see cref="Play"/> is applied to the input, in the
    /// <b>mover's frame</b> — no flip; the position
    /// <see cref="MoveGenerator.GenerateResultingPositions"/> returns at the
    /// same index.
    /// </summary>
    public BoardPosition ResultingPosition { get; }

    /// <summary>
    /// Not supported: a candidate holds a play, and
    /// <see cref="BgDataTypes_Lib.Play"/> has no equality (see the type
    /// summary), so this throws.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override bool Equals([NotNullWhen(true)] object? obj) => throw NoEquality();

    /// <summary>
    /// Not supported, as for <see cref="Equals(object)"/>: a candidate has no
    /// equality, so it has no hash either, and this throws.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int GetHashCode() => throw NoEquality();

    private static NotSupportedException NoEquality() => new(
        "CandidatePlay has no equality: it holds a Play, and whether two plays are the same play "
        + "depends on the position they are played from (BoardState.IsSamePlay). Compare positions "
        + "with a.ResultingPosition == b.ResultingPosition.");
}
