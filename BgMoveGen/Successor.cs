using System.Diagnostics.CodeAnalysis;
using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// One legal play of a roll with its successor: the position the play leads
/// to, seen from the next mover's side. Returned by
/// <see cref="MoveGenerator.GenerateSuccessors"/>; see that method for the
/// full contract.
///
/// <para>
/// <b>A value.</b> The play and the position are held inline, so a list of
/// successors is one array and a successor costs no allocation of its own.
/// Both parts are values: <see cref="Play"/> returns a copy, and
/// <see cref="Position"/> is immutable, so nothing a caller does to what it
/// reads can break the pairing.
/// </para>
///
/// <para>
/// <b>Only BgMoveGen pairs a play with its successor.</b> The constructor is
/// internal, so a successor a consumer holds pairs a play with the position
/// that play actually leads to. The one other instance a consumer can make is
/// <see langword="default"/>: the empty play with the empty position.
/// </para>
///
/// <para>
/// <b>No equality.</b> A successor holds a <see cref="BgDataTypes_Lib.Play"/>,
/// which has none — whether two plays are the same play depends on the
/// position they are played from (<see cref="BoardState.IsSamePlay"/>). So,
/// as for <see cref="BgDataTypes_Lib.Play"/>, <c>==</c> is not defined and
/// <see cref="Equals(object)"/> and <see cref="GetHashCode"/> throw, rather
/// than fall back to a field-wise comparison of the play. Positions compare
/// as <see cref="BoardPosition"/> values: <c>a.Position == b.Position</c>.
/// </para>
/// </summary>
public readonly struct Successor
{
    internal Successor(Play play, BoardPosition position)
    {
        Play = play;
        Position = position;
    }

    /// <summary>
    /// The play, in the generator's own encoding — the play
    /// <see cref="MoveGenerator.GeneratePlays"/> returns at the same index. The
    /// empty play is a forced pass.
    /// </summary>
    public Play Play { get; }

    /// <summary>
    /// The position after <see cref="Play"/>, seen from the next mover's side:
    /// the input with the play applied, then flipped
    /// (<see cref="BoardPosition.Flipped"/>) — the board
    /// <see cref="BoardState.ApplyPlay"/> leaves, as a value.
    /// </summary>
    public BoardPosition Position { get; }

    /// <summary>
    /// Not supported: a successor holds a play, and <see cref="BgDataTypes_Lib.Play"/>
    /// has no equality (see the type summary), so this throws.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override bool Equals([NotNullWhen(true)] object? obj) => throw NoEquality();

    /// <summary>
    /// Not supported, as for <see cref="Equals(object)"/>: a successor has no
    /// equality, so it has no hash either, and this throws.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int GetHashCode() => throw NoEquality();

    private static NotSupportedException NoEquality() => new(
        "Successor has no equality: it holds a Play, and whether two plays are the same play depends "
        + "on the position they are played from (BoardState.IsSamePlay). Compare positions with "
        + "a.Position == b.Position.");
}
