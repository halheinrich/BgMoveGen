using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BgDataTypes_Lib;

namespace BgMoveGen;

/// <summary>
/// Python interop surface for BgRLEngine.
///
/// Contract: generate_successor_states(input, die1, die2, outputBuffer, bufferCapacity)
///   - input and every output are always from the on-roll player's perspective.
///   - Each successor is flipped before writing so the next call is already oriented.
///   - Returns successor count. Pass = 1 (flipped state with no moves applied).
///     An input board that is not a well-formed position returns
///     <see cref="Status.InvalidPosition"/> and writes nothing.
///   - NOT thread-safe within a single process. Each OS process gets its own
///     instance; multiple Python processes are fully safe. If multi-thread use
///     ever needed, change _state to [ThreadStatic].
/// </summary>
internal static unsafe class Interop
{
    /// <summary>
    /// Safe upper bound on successors for any position.
    /// Allocate output_buffer = (BgBoardState * MaxSuccessors)() on the Python side.
    /// </summary>
    public const int MaxSuccessors = 100;

    /// <summary>
    /// Version reported to consumers via get_version() export.
    /// BgRLEngine defines the required version; BgMoveGen must match it.
    /// </summary>
    public const int Version = 100;

    /// <summary>
    /// The exports' status codes: a successful call returns a count or 0, a
    /// refused one a negative code from here.
    /// </summary>
    internal enum Status
    {
        /// <summary>An argument is outside its domain: an unknown starting-position variant.</summary>
        InvalidArgument = -1,

        /// <summary>
        /// The input board is not a well-formed position — the invariant stated
        /// on <see cref="BoardPosition"/>, which decides it.
        /// </summary>
        InvalidPosition = -2,
    }

    // Reused across calls — avoids allocation in the hot path. Reset for each
    // call through SetPosition, the board's one whole-position write.
    private static readonly BoardState _state = new(BoardPosition.Empty);

    // ── Blittable struct matching BgRLEngine's layout exactly ─────

    [StructLayout(LayoutKind.Sequential)]
    internal struct BgBoardState
    {
        // points[0]=1-pt … points[23]=24-pt
        // positive = on-roll player, negative = opponent, player moves high→low
        public fixed short Points[24];
        public int BarPlayer;
        public int BarOpponent;
        public int OffPlayer;
        public int OffOpponent;
    }

    // ── NativeAOT export ──────────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "generate_successor_states")]
    internal static int GenerateSuccessorStates(
        BgBoardState* input,
        int die1, int die2,
        BgBoardState* outputBuffer,
        int bufferCapacity)
        => GenerateSuccessorStatesCore(input, die1, die2, outputBuffer, bufferCapacity);

    [UnmanagedCallersOnly(EntryPoint = "get_version")]
    internal static int GetVersion() => Version;

    /// <summary>
    /// Testable core — same logic, callable from managed code.
    /// </summary>
    internal static int GenerateSuccessorStatesCore(
        BgBoardState* input,
        int die1, int die2,
        BgBoardState* outputBuffer,
        int bufferCapacity)
    {
        if (!TryReadPosition(input, out var position))
            return (int)Status.InvalidPosition;
        _state.SetPosition(position);
        int offPlayer = input->OffPlayer;
        int offOpponent = input->OffOpponent;

        var plays = MoveGenerator.GeneratePlays(_state, die1, die2);

        int count = 0;
        foreach (var play in plays)
        {
            if (count >= bufferCapacity) break;

            for (int i = 0; i < play.Count; i++)
                _state.ApplyMove(play[i]);

            ToExternalFlipped(_state, offPlayer, offOpponent, play, &outputBuffer[count]);
            count++;

            for (int i = play.Count - 1; i >= 0; i--)
                _state.UndoMove(play[i]);
        }

        return count;
    }

    // ── Starting position export ──────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "get_starting_position")]
    internal static int GetStartingPosition(
        int variant,    // 0=standard, 1=nackgammon, 2=bg960
        int seed,       // -1 = no seed; ignored for standard and nackgammon
        BgBoardState* output)
        => GetStartingPositionCore(variant, seed, output);

    internal static int GetStartingPositionCore(
        int variant,
        int seed,
        BgBoardState* output)
    {
        BoardState? s = variant switch
        {
            0 => BoardState.Standard(),
            1 => BoardState.Nackgammon(),
            2 => BoardState.Bg960(seed == -1 ? null : seed),
            _ => null
        };

        if (s == null) return (int)Status.InvalidArgument;   // unknown variant

        ToExternal(s, output);
        return 0;
    }

    /// <summary>
    /// Write a BoardState into a BgBoardState without flipping.
    /// Used for starting positions — always from the on-roll player's perspective.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToExternal(BoardState state, BgBoardState* dest)
    {
        var pts = state.Points;

        for (int k = 0; k < 24; k++)
            dest->Points[k] = (short)pts[k + 1];

        dest->BarPlayer = pts[25];
        dest->BarOpponent = -pts[0];
        dest->OffPlayer = 0;
        dest->OffOpponent = 0;
    }

    // ── Translation helpers ───────────────────────────────────────

    /// <summary>
    /// Read BgRLEngine's BgBoardState as a <see cref="BoardPosition"/>, or
    /// false when its board is not a well-formed position — the value decides,
    /// through its non-throwing form, so malformed input is refused rather than
    /// computed on. Allocation-free.
    /// BgRLEngine: points[0]=1-pt … points[23]=24-pt, separate bar/off fields.
    /// Position:   [1]=1-pt … [24]=24-pt, [25]=player bar,
    ///             [0]=opponent bar (negative).
    /// The off counts are not part of a position; the caller reads them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadPosition(BgBoardState* ext, out BoardPosition position)
    {
        Span<int> counts = stackalloc int[26];

        counts[25] = ext->BarPlayer;
        counts[0] = -ext->BarOpponent;

        for (int i = 0; i < 24; i++)
            counts[i + 1] = ext->Points[i];

        return BoardPosition.TryCreate(counts, out position);
    }

    /// <summary>
    /// Write the current board state into dest as a flipped BgBoardState.
    /// Flip = negate + reverse points, swap bars, swap off counts.
    /// Bear-off delta applied to off counts based on moves in play.
    /// Single pass, no allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToExternalFlipped(
        BoardState state,
        int offPlayer,
        int offOpponent,
        Play play,
        BgBoardState* dest)
    {
        var pts = state.Points;

        // Reverse and negate: internal Points[24]=24-pt → external points[23], negated
        //                     internal Points[1] =1-pt  → external points[0],  negated
        for (int k = 0; k < 24; k++)
            dest->Points[k] = (short)(-pts[24 - k]);

        // Bar: swap and negate
        // pts[0] stores opponent bar as a negative value → new player bar = positive
        dest->BarPlayer = -pts[0];
        dest->BarOpponent = pts[25];

        // Off: count bear-off moves in this play
        int newOffPlayer = offPlayer;
        for (int i = 0; i < play.Count; i++)
        {
            if (play[i].ToPt == 0) newOffPlayer++;
        }

        // Swap: after flip, player becomes opponent and vice versa
        dest->OffPlayer = offOpponent;
        dest->OffOpponent = newOffPlayer;
    }
}