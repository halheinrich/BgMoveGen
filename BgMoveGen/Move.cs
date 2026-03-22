namespace BgMoveGen;

/// <summary>
/// A single checker move. Immutable value type — no heap allocation.
/// 
/// FrPt: source point (1-24 on board, 25 for bar entry).
/// ToPt: destination point, sign-encoded:
///   Positive (1-24): regular move to that point.
///   Zero: bear off (checker removed from board).
///   Negative (-1 to -24): hit — land on |ToPt| and send opponent blot to bar.
/// 
/// The encoding stores everything needed to undo the move.
/// </summary>
public readonly record struct Move(int FrPt, int ToPt);
