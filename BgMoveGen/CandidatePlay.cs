using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// One legal candidate for a roll: the generator's <see cref="BgDataTypes_Lib.Play"/>
/// together with the board that play produces. Returned by
/// <see cref="MoveGenerator.GenerateCandidatePlays"/>; see that method for the
/// full contract.
///
/// <para>
/// <b>Only BgMoveGen associates a play with a board.</b> The constructor is
/// internal, so every instance a consumer holds pairs a play with the board
/// that play actually reaches — a caller cannot assemble an inconsistent pair,
/// and a class has no <c>default</c> instance with a null board.
/// </para>
///
/// <para>
/// <b>Equality is reference equality.</b> No value equality is defined: the
/// play already identifies its candidate within one call, and
/// <see cref="BoardState"/> has no value equality to build one from. Compare
/// plays with <see cref="BgDataTypes_Lib.Play"/> equality where that is needed.
/// </para>
/// </summary>
public sealed class CandidatePlay
{
    internal CandidatePlay(Play play, BoardState resultingState)
    {
        Play = play;
        ResultingState = resultingState;
    }

    /// <summary>
    /// The candidate play, in the generator's own encoding — the element
    /// <see cref="MoveGenerator.GeneratePlays"/> returns at the same index.
    /// <see cref="BgDataTypes_Lib.Play"/> is a value type holding its moves
    /// inline, so this property returns a complete copy and the stored play
    /// cannot be modified through it. The empty play is a forced pass.
    /// </summary>
    public Play Play { get; }

    /// <summary>
    /// The board after <see cref="Play"/> is applied to the input, in the
    /// <b>mover's frame</b> — no flip; the same board
    /// <see cref="MoveGenerator.GenerateResultingStates"/> returns at the same
    /// index.
    ///
    /// <para>
    /// An independent copy owned by the caller. <see cref="BoardState"/> is
    /// mutable and this property returns the same instance on every read, so
    /// a caller that mutates it breaks the association with
    /// <see cref="Play"/> — keeping them consistent is then the caller's
    /// responsibility.
    /// </para>
    /// </summary>
    public BoardState ResultingState { get; }
}
