namespace BgMoveGen;

/// <summary>
/// A single checker move. Immutable value type — no heap allocation.
/// 
/// Stores enough information to both apply and undo the move:
/// - Source/dest identify the checker movement
/// - Hits flag records whether an opponent blot was sent to the bar
/// </summary>
public readonly record struct Move(
    /// <summary>Source point (0-23), or 24 for bar entry.</summary>
    int Source,
    /// <summary>Destination point (0-23), or -1 for bear off.</summary>
    int Dest,
    /// <summary>Die value used (1-6).</summary>
    int Die,
    /// <summary>True if this move hits an opponent blot.</summary>
    bool Hits = false
);
