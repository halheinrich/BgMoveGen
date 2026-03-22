namespace BgMoveGen;

/// <summary>
/// Mutable backgammon board position.
/// 
/// Points[0..25]: 26-element array.
///   Points[25] = on-roll player's bar
///   Points[1..24] = playing surface
///   Points[0] = opponent's bar
/// 
/// Positive values = on-roll player's checkers.
/// Negative values = opponent's checkers.
/// Player moves from high indices toward low (25 → 1, bearing off past 1).
/// 
/// Designed for apply/undo mutation — no heap allocations during move generation.
/// </summary>
public class BoardState
{
    public readonly int[] Points = new int[26];

    /// <summary>
    /// Highest point (1-25) with a player checker, 0 if none.
    /// Updated incrementally by ApplyMove/UndoMove.
    /// </summary>
    public int HighPointOccupied;

    public BoardState() { }

    /// <summary>
    /// Recompute HighPointOccupied from scratch. Call after setting up a position.
    /// </summary>
    public void RecalcHighPoint()
    {
        HighPointOccupied = 0;
        for (int i = 25; i >= 1; i--)
        {
            if (Points[i] > 0) { HighPointOccupied = i; return; }
        }
    }

    /// <summary>
    /// Deep copy.
    /// </summary>
    public BoardState Copy()
    {
        var copy = new BoardState();
        Array.Copy(Points, copy.Points, 26);
        copy.HighPointOccupied = HighPointOccupied;
        return copy;
    }

    // ── Standard starting positions ───────────────────────────────

    /// <summary>
    /// Standard backgammon starting position.
    /// Player's checkers: 6-pt(5), 8-pt(3), 13-pt(5), 24-pt(2)
    /// Opponent's checkers: 19-pt(-5), 17-pt(-3), 12-pt(-5), 1-pt(-2)
    /// </summary>
    public static BoardState Standard()
    {
        var s = new BoardState();
        s.Points[6] = 5;
        s.Points[8] = 3;
        s.Points[13] = 5;
        s.Points[24] = 2;
        s.Points[19] = -5;
        s.Points[17] = -3;
        s.Points[12] = -5;
        s.Points[1] = -2;
        s.RecalcHighPoint();
        return s;
    }

    /// <summary>
    /// Nackgammon starting position.
    /// </summary>
    public static BoardState Nackgammon()
    {
        var s = new BoardState();
        s.Points[6] = 4;
        s.Points[8] = 3;
        s.Points[13] = 4;
        s.Points[23] = 2;
        s.Points[24] = 2;
        s.Points[19] = -4;
        s.Points[17] = -3;
        s.Points[12] = -4;
        s.Points[2] = -2;
        s.Points[1] = -2;
        s.RecalcHighPoint();
        return s;
    }
}
