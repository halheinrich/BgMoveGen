using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BgMoveGen;

/// <summary>
/// Python interop surface for BgRLEngine.
///
/// Contract: generate_successor_states(input, die1, die2, outputBuffer, bufferCapacity)
///   - input and every output are always from the on-roll player's perspective.
///   - Each successor is flipped before writing so the next call is already oriented.
///   - Returns successor count. Pass = 1 (flipped state with no moves applied).
///   - NOT thread-safe within a single process. Each OS process gets its own
///     instance; multiple Python processes are fully safe. If multi-thread use
///     ever needed, change _state to [ThreadStatic].
/// </summary>
public static unsafe class Interop
{
    /// <summary>
    /// Safe upper bound on successors for any position.
    /// Allocate output_buffer = (BgBoardState * MaxSuccessors)() on the Python side.
    /// </summary>
    public const int MaxSuccessors = 100;

    // Reused across calls — avoids allocation in the hot path.
    private static readonly BoardState _state = new BoardState();

    // ── Blittable struct matching BgRLEngine's layout exactly ─────

    [StructLayout(LayoutKind.Sequential)]
    public struct BgBoardState
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
    public static int GenerateSuccessorStates(
        BgBoardState* input,
        int die1, int die2,
        BgBoardState* outputBuffer,
        int bufferCapacity)
        => GenerateSuccessorStatesCore(input, die1, die2, outputBuffer, bufferCapacity);

    /// <summary>
    /// Testable core — same logic, callable from managed code.
    /// </summary>
    internal static int GenerateSuccessorStatesCore(
        BgBoardState* input,
        int die1, int die2,
        BgBoardState* outputBuffer,
        int bufferCapacity)
    {
        FromExternal(input, _state, out int offPlayer, out int offOpponent);

        var plays = MoveGenerator.GeneratePlays(_state, die1, die2);

        int count = 0;
        foreach (var play in plays)
        {
            if (count >= bufferCapacity) break;

            for (int i = 0; i < play.Count; i++)
                MoveGenerator.ApplyMove(_state, play[i]);

            ToExternalFlipped(_state, offPlayer, offOpponent, play, &outputBuffer[count]);
            count++;

            for (int i = play.Count - 1; i >= 0; i--)
                MoveGenerator.UndoMove(_state, play[i]);
        }

        return count;
    }

    // ── Starting position export ──────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "get_starting_position")]
    public static int GetStartingPosition(
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
            // 2 => SetupGenerator.Generate(seed == -1 ? null : seed),  // deferred
            _ => null
        };

        if (s == null) return -1;   // unknown variant

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
    /// Translate BgRLEngine's BgBoardState into the internal int[26] representation.
    /// BgRLEngine: points[0]=1-pt … points[23]=24-pt, separate bar/off fields.
    /// Internal:   Points[1]=1-pt … Points[24]=24-pt,
    ///             Points[25]=player bar, Points[0]=opponent bar (negative).
    /// Also passes through offPlayer/offOpponent for use in ToExternalFlipped.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromExternal(
        BgBoardState* ext,
        BoardState state,
        out int offPlayer,
        out int offOpponent)
    {
        var pts = state.Points;

        pts[25] = ext->BarPlayer;
        pts[0] = -ext->BarOpponent;

        for (int i = 0; i < 24; i++)
            pts[i + 1] = ext->Points[i];

        offPlayer = ext->OffPlayer;
        offOpponent = ext->OffOpponent;

        state.RecalcHighPoint();
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