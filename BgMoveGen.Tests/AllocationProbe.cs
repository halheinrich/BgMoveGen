namespace BgMoveGen.Tests;

/// <summary>
/// How an allocation pin measures a path: what it allocates in its steady
/// state, on the test thread, immune to a one-off allocation the runtime
/// makes there. The method is BgDataTypes_Lib's (its <c>AllocationProbe</c>),
/// which found that a single measured loop fails on an allocation that is not
/// the path's: the path is first run for a whole window's worth of calls, so
/// every first-call cost falls outside the measurement; then up to
/// <see cref="Windows"/> windows of <see cref="CallsPerWindow"/> calls each are
/// measured, and the fewest bytes any window allocated is the result. A
/// one-off allocation lands in at most one window, so it cannot make every
/// window allocate; a path that allocates on its calls allocates in every
/// window, and is measured. <c>AllocationProbeTests</c> pins both halves here.
/// </summary>
internal static class AllocationProbe
{
    /// <summary>The calls in the warm-up and in each measured window.</summary>
    internal const int CallsPerWindow = 1000;

    /// <summary>The most windows measured.</summary>
    internal const int Windows = 8;

    /// <summary>
    /// The fewest bytes <paramref name="call"/> allocated on this thread in any
    /// measured window of <see cref="CallsPerWindow"/> calls, after a warm-up
    /// of as many calls — see the class summary.
    /// </summary>
    /// <param name="call">One call of the path; it keeps its result observable (a sink it writes).</param>
    internal static long SteadyStateBytes(Action call)
    {
        ArgumentNullException.ThrowIfNull(call);

        for (int i = 0; i < CallsPerWindow; i++)
            call();

        long fewest = long.MaxValue;
        for (int window = 0; window < Windows; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < CallsPerWindow; i++)
                call();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return fewest;
    }
}

/// <summary>
/// The allocation pins' measurement, pinned on its own: it reads a path that
/// allocates nothing as nothing, a path that allocates on each call as what it
/// allocates, and a one-off allocation as nothing.
/// </summary>
public class AllocationProbeTests
{
    // A sink the paths below write, so no allocation can be elided.
    private static object? _kept;

    [Fact]
    public void APathThatAllocatesNothing_MeasuresNothing()
    {
        int sink = 0;

        Assert.Equal(0, AllocationProbe.SteadyStateBytes(() => sink++));
        Assert.True(sink > 0);
    }

    [Fact]
    public void APathThatAllocatesOnEveryCall_IsMeasuredExactly()
    {
        long array = AllocationProbe.SteadyStateBytes(() => _kept = new byte[16]);
        long twice = AllocationProbe.SteadyStateBytes(() => { _kept = new byte[16]; _kept = new byte[16]; });

        Assert.True(array >= AllocationProbe.CallsPerWindow * 16L, $"measured {array}");
        Assert.Equal(2 * array, twice);
    }

    [Fact]
    public void AOneOffAllocation_IsNotCounted()
    {
        int calls = 0;

        long allocated = AllocationProbe.SteadyStateBytes(() =>
        {
            if (++calls == AllocationProbe.CallsPerWindow + 1)
                _kept = new byte[4096];
        });

        Assert.Equal(0, allocated);
    }
}
