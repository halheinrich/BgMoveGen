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
///
/// <para>
/// The output buffer holds the most successors any of the 21 rolls has from
/// the opening, so every measured call writes its whole list — the export
/// writes nothing when a list outgrows the buffer, and a row measuring that
/// would measure the refusal instead.
/// </para>
/// </summary>
[MemoryDiagnoser]
public unsafe class NativeExportBenchmarks
{
    private Interop.BgBoardState* _input;
    private Interop.BgBoardState* _output;
    private int _capacity;
    private (int Die1, int Die2)[] _allRolls = null!;

    /// <summary>
    /// Writes the opening position into the input buffer once, in
    /// BgRLEngine's layout, and allocates the output buffer at the largest
    /// successor count among the 21 rolls from it.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var rolls = new List<(int, int)>();
        for (int d1 = 1; d1 <= 6; d1++)
            for (int d2 = d1; d2 <= 6; d2++)
                rolls.Add((d1, d2));
        _allRolls = [.. rolls];

        _capacity = 0;
        foreach (var (die1, die2) in _allRolls)
            _capacity = Math.Max(_capacity, MoveGenerator.GeneratePlays(BoardState.Standard(), die1, die2).Count);

        _input = (Interop.BgBoardState*)NativeMemory.AllocZeroed((nuint)sizeof(Interop.BgBoardState));
        _output = (Interop.BgBoardState*)NativeMemory.AllocZeroed(
            (nuint)(sizeof(Interop.BgBoardState) * _capacity));

        Span<int> counts = stackalloc int[26];
        BoardPosition.Standard.CopyTo(counts);
        for (int k = 0; k < 24; k++)
            _input->Points[k] = (short)counts[k + 1];
        _input->BarPlayer = counts[25];
        _input->BarOpponent = -counts[0];
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
            total += Interop.GenerateSuccessorStatesCore(_input, die1, die2, _output, _capacity);
        return total;
    }
}
