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
///   - A refused call returns a negative <see cref="Status"/>.
///   - NOT thread-safe within a single process. Each OS process gets its own
///     instance; multiple Python processes are fully safe. If multi-thread use
///     ever needed, change _state to [ThreadStatic].
///
/// <para>
/// <b>No exception crosses the native boundary.</b> An exception leaving an
/// <c>[UnmanagedCallersOnly]</c> method terminates the host process, so each
/// export's core refuses what it can see itself (null pointers, a negative
/// capacity, a malformed board) with a status, and catches the rest:
/// <see cref="ArgumentException"/> — the managed surface's refusal of an
/// argument, a die outside 1–6 from <see cref="MoveGenerator.GeneratePlays"/>
/// — becomes <see cref="Status.InvalidArgument"/>, and any other exception
/// <see cref="Status.Failed"/>.
/// </para>
/// </summary>
internal static unsafe class Interop
{
    /// <summary>
    /// The output buffer BgRLEngine allocates: output_buffer =
    /// (BgBoardState * MaxSuccessors)() on the Python side. <b>Not an upper
    /// bound on successors</b>: a legal position can have far more — fifteen
    /// checkers on fifteen points of a race have 1,547 distinct plays of 1-1.
    /// A call writes at most <c>bufferCapacity</c> successors and returns the
    /// number written, so a buffer smaller than the successor count truncates
    /// the list without saying so.
    /// </summary>
    public const int MaxSuccessors = 100;

    /// <summary>
    /// Version reported to consumers via get_version() export.
    /// BgRLEngine defines the required version; BgMoveGen must match it.
    /// </summary>
    public const int Version = 100;

    /// <summary>
    /// The exports' status codes: a successful call returns a count or 0, a
    /// refused one a negative code from here. A refused argument or board is
    /// refused before anything is written; <see cref="Failed"/> promises
    /// nothing about the output.
    /// </summary>
    internal enum Status
    {
        /// <summary>
        /// An argument is outside its domain: a null pointer, a negative
        /// buffer capacity, a die outside 1–6, an unknown starting-position
        /// variant.
        /// </summary>
        InvalidArgument = -1,

        /// <summary>
        /// The input board is not a well-formed position — the invariant stated
        /// on <see cref="BoardPosition"/>, which decides it.
        /// </summary>
        InvalidPosition = -2,

        /// <summary>
        /// The call failed inside the library for a reason no argument
        /// explains; the exception was caught at the boundary, so none
        /// crosses it.
        /// </summary>
        Failed = -3,
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
    /// Testable core — same logic, callable from managed code, and like the
    /// export it never throws: a refusal is a negative <see cref="Status"/>
    /// (see the type summary). Nothing is written before the pointers, the
    /// capacity, the board and the dice are accepted.
    /// </summary>
    internal static int GenerateSuccessorStatesCore(
        BgBoardState* input,
        int die1, int die2,
        BgBoardState* outputBuffer,
        int bufferCapacity)
    {
        if (input == null || outputBuffer == null || bufferCapacity < 0)
            return (int)Status.InvalidArgument;

        try
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

                // The managed successor rule, so the export and
                // GenerateSuccessors cannot disagree: the play's position,
                // flipped by the value. The off counts are this layout's own:
                // the mover's grow by the play's bear-offs, and the two swap
                // sides with the flip.
                var successor = MoveGenerator.SuccessorPositionOf(_state, in play);
                ToExternal(successor, offOpponent, offPlayer + BearOffs(in play), &outputBuffer[count]);
                count++;
            }

            return count;
        }
        catch (ArgumentException)
        {
            return (int)Status.InvalidArgument;
        }
        catch (Exception)
        {
            return (int)Status.Failed;
        }
    }

    /// <summary>The checkers <paramref name="play"/> bears off: its moves to 0.</summary>
    private static int BearOffs(in Play play)
    {
        int bearOffs = 0;
        foreach (var move in play)
            if (move.ToPt == 0) bearOffs++;
        return bearOffs;
    }

    // ── Starting position export ──────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "get_starting_position")]
    internal static int GetStartingPosition(
        int variant,    // 0=standard, 1=nackgammon, 2=bg960
        int seed,       // -1 = no seed; ignored for standard and nackgammon
        BgBoardState* output)
        => GetStartingPositionCore(variant, seed, output);

    /// <summary>
    /// Testable core of <c>get_starting_position</c>: 0 on success, or a
    /// negative <see cref="Status"/> having written nothing — never an
    /// exception (see the type summary).
    /// </summary>
    internal static int GetStartingPositionCore(
        int variant,
        int seed,
        BgBoardState* output)
    {
        if (output == null)
            return (int)Status.InvalidArgument;

        try
        {
            BoardPosition? start = variant switch
            {
                0 => BoardPosition.Standard,
                1 => BoardPosition.Nackgammon,
                2 => BoardPosition.Bg960(seed == -1 ? null : seed),
                _ => null
            };

            if (start is not { } position) return (int)Status.InvalidArgument;   // unknown variant

            // From the on-roll player's perspective: not flipped, nothing borne off.
            ToExternal(position, offPlayer: 0, offOpponent: 0, output);
            return 0;
        }
        catch (Exception)
        {
            // Bg960 can fail to find a position; nothing else here throws.
            return (int)Status.Failed;
        }
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
    /// Write <paramref name="position"/> into <paramref name="dest"/> as it
    /// stands, with the given off counts — the one writer of BgRLEngine's
    /// layout, the mirror of <see cref="TryReadPosition"/>. It never flips: a
    /// successor arrives already flipped by the value
    /// (<see cref="MoveGenerator.SuccessorPositionOf"/>), a starting position
    /// is in the on-roll frame. Allocation-free.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToExternal(
        BoardPosition position, int offPlayer, int offOpponent, BgBoardState* dest)
    {
        Span<int> counts = stackalloc int[26];
        position.CopyTo(counts);

        for (int k = 0; k < 24; k++)
            dest->Points[k] = (short)counts[k + 1];

        dest->BarPlayer = counts[25];
        dest->BarOpponent = -counts[0];
        dest->OffPlayer = offPlayer;
        dest->OffOpponent = offOpponent;
    }
}