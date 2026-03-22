namespace BgMoveGen;

/// <summary>
/// A complete play: the sequence of moves for one turn.
/// Uses a fixed-size buffer (max 4 moves for doubles) to avoid heap allocation.
/// </summary>
public struct Play : IEquatable<Play>
{
    // Fixed buffer: max 4 moves (doubles)
    private Move _m0, _m1, _m2, _m3;
    public int Count { get; private set; }

    public Move this[int index] => index switch
    {
        0 => _m0,
        1 => _m1,
        2 => _m2,
        3 => _m3,
        _ => throw new IndexOutOfRangeException()
    };

    public void Add(Move move)
    {
        switch (Count)
        {
            case 0: _m0 = move; break;
            case 1: _m1 = move; break;
            case 2: _m2 = move; break;
            case 3: _m3 = move; break;
            default: throw new InvalidOperationException("Play already has 4 moves");
        }
        Count++;
    }

    public void RemoveLast()
    {
        if (Count == 0) throw new InvalidOperationException("Play is empty");
        Count--;
    }

    public Play Snapshot()
    {
        var copy = new Play();
        copy._m0 = _m0;
        copy._m1 = _m1;
        copy._m2 = _m2;
        copy._m3 = _m3;
        copy.Count = Count;
        return copy;
    }

    /// <summary>
    /// Normalized key for deduplication: sorted (FrPt, |ToPt|) pairs.
    /// Used by legacy path and equivalence tests.
    /// </summary>
    public (int, int, int, int, int, int, int, int) DeduplicationKey()
    {
        Span<(int fr, int to)> pairs = stackalloc (int, int)[Count];
        for (int i = 0; i < Count; i++)
            pairs[i] = (this[i].FrPt, Math.Abs(this[i].ToPt));

        // Simple sort for up to 4 elements
        for (int i = 0; i < Count - 1; i++)
            for (int j = i + 1; j < Count; j++)
                if (pairs[j].fr > pairs[i].fr ||
                    (pairs[j].fr == pairs[i].fr && pairs[j].to > pairs[i].to))
                    (pairs[i], pairs[j]) = (pairs[j], pairs[i]);

        return (
            Count > 0 ? pairs[0].fr : -99, Count > 0 ? pairs[0].to : -99,
            Count > 1 ? pairs[1].fr : -99, Count > 1 ? pairs[1].to : -99,
            Count > 2 ? pairs[2].fr : -99, Count > 2 ? pairs[2].to : -99,
            Count > 3 ? pairs[3].fr : -99, Count > 3 ? pairs[3].to : -99
        );
    }

    public bool Equals(Play other) => DeduplicationKey() == other.DeduplicationKey();
    public override bool Equals(object? obj) => obj is Play p && Equals(p);
    public override int GetHashCode() => DeduplicationKey().GetHashCode();
}
