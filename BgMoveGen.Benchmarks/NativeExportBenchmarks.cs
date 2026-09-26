using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BgDataTypes_Lib;

namespace BgMoveGen.Benchmarks;

/// <summary>
/// The native export's own cost, through the core behind
/// <c>generate_successor_states</c> — the call BgRLEngine makes millions of
/// times a training run. <see cref="MoveGenerationBenchmarks"/> measures the
/// generator; this measures what the export adds around it: reading the input
/// as a position and resetting the reused board with it, computing each
/// successor by the managed successor rule, and writing BgRLEngine's layout.
///
/// <para>
/// Measured through the internal core rather than the exported entry point:
/// an <c>[UnmanagedCallersOnly]</c> method cannot be called from managed code
/// except through a function pointer, whose transition cost no change here
/// can move. The buffers are native memory, allocated once, so
/// <c>Allocated</c> is the call's own.
/// </para>
/// </summary>
[MemoryDiagnoser]
public unsafe class NativeExportBenchmarks
{
    private Interop.BgBoardState* _input;
    private Interop.BgBoardState* _output;
    private (int Die1, int Die2)[] _allRolls = null!;

    /// <summary>
    /// Writes the opening position into the input buffer once, in
    /// BgRLEngine's layout, and allocates the output buffer at the export's
    /// documented capacity.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _input = (Interop.BgBoardState*)NativeMemory.AllocZeroed((nuint)sizeof(Interop.BgBoardState));
        _output = (Interop.BgBoardState*)NativeMemory.AllocZeroed(
            (nuint)(sizeof(Interop.BgBoardState) * Interop.MaxSuccessors));

        Span<int> counts = stackalloc int[26];
        BoardPosition.Standard.CopyTo(counts);
        for (int k = 0; k < 24; k++)
            _input->Points[k] = (short)counts[k + 1];
        _input->BarPlayer = counts[25];
        _input->BarOpponent = -counts[0];

        var rolls = new List<(int, int)>();
        for (int d1 = 1; d1 <= 6; d1++)
            for (int d2 = d1; d2 <= 6; d2++)
                rolls.Add((d1, d2));
        _allRolls = [.. rolls];
    }

    /// <summary>Frees the native buffers.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_input);
        NativeMemory.Free(_output);
    }

    /// <summary>
    /// All 21 rolls from the opening position through the export core — the
    /// counterpart of <see cref="MoveGenerationBenchmarks.AllOpeningRolls"/>,
    /// so the difference between the two rows is the export's own work.
    /// </summary>
    [Benchmark]
    public int NativeAllOpeningRolls()
    {
        int total = 0;
        foreach (var (die1, die2) in _allRolls)
            total += Interop.GenerateSuccessorStatesCore(_input, die1, die2, _output, Interop.MaxSuccessors);
        return total;
    }
}
