namespace BgMoveGen;

/// <summary>
/// Mutable backgammon board position.
/// 
/// Points[0..23]: positive = player's checkers, negative = opponent's.
/// Index 0 = player's 1-point (bearing off destination).
/// Index 23 = player's 24-point.
/// Player moves from high indices toward 0.
/// 
/// Designed for apply/undo mutation — no heap allocations during move generation.
/// </summary>
public class BoardState
{
    public const int NumPoints = 24;
    public const int CheckersPerPlayer = 15;
    public const int BarIndex = 24; // virtual index for bar entry

    public readonly int[] Points = new int[NumPoints];
    public int BarPlayer;
    public int BarOpponent;
    public int OffPlayer;
    public int OffOpponent;

    /// <summary>
    /// Number of player checkers outside the home board (points 7-24 + bar).
    /// When this is 0 and BarPlayer is 0, bearing off is legal.
    /// Updated incrementally by ApplyMove/UndoMove.
    /// </summary>
    public int PlayerOutsideHome;

    public bool CanBearOff => BarPlayer == 0 && PlayerOutsideHome == 0;

    public BoardState() { }

    /// <summary>
    /// Deep copy.
    /// </summary>
    public BoardState Copy()
    {
        var copy = new BoardState();
        Array.Copy(Points, copy.Points, NumPoints);
        copy.BarPlayer = BarPlayer;
        copy.BarOpponent = BarOpponent;
        copy.OffPlayer = OffPlayer;
        copy.OffOpponent = OffOpponent;
        copy.PlayerOutsideHome = PlayerOutsideHome;
        return copy;
    }

    /// <summary>
    /// Recompute PlayerOutsideHome from scratch. Call after setting up a position.
    /// </summary>
    public void RecalcOutsideHome()
    {
        PlayerOutsideHome = BarPlayer;
        for (int i = 6; i < NumPoints; i++)
        {
            if (Points[i] > 0)
                PlayerOutsideHome += Points[i];
        }
    }

    /// <summary>
    /// Player's total pip count (distance to bear off all checkers).
    /// </summary>
    public int PlayerPipCount()
    {
        int pips = BarPlayer * 25;
        for (int i = 0; i < NumPoints; i++)
        {
            if (Points[i] > 0)
                pips += Points[i] * (i + 1);
        }
        return pips;
    }

    /// <summary>
    /// Opponent's total pip count.
    /// </summary>
    public int OpponentPipCount()
    {
        int pips = BarOpponent * 25;
        for (int i = 0; i < NumPoints; i++)
        {
            if (Points[i] < 0)
                pips += Math.Abs(Points[i]) * (NumPoints - i);
        }
        return pips;
    }

    /// <summary>
    /// True if no future contact is possible (pure race).
    /// </summary>
    public bool IsRace()
    {
        if (BarPlayer > 0 || BarOpponent > 0)
            return false;

        int lowestOpp = -1;
        for (int i = 0; i < NumPoints; i++)
        {
            if (Points[i] < 0) { lowestOpp = i; break; }
        }
        if (lowestOpp == -1) return true;

        int highestPlayer = -1;
        for (int i = NumPoints - 1; i >= 0; i--)
        {
            if (Points[i] > 0) { highestPlayer = i; break; }
        }
        if (highestPlayer == -1) return true;

        return highestPlayer < lowestOpp;
    }

    /// <summary>
    /// Return a new BoardState with player/opponent swapped.
    /// </summary>
    public BoardState FlipPerspective()
    {
        var flipped = new BoardState();
        for (int i = 0; i < NumPoints; i++)
            flipped.Points[i] = -Points[NumPoints - 1 - i];
        flipped.BarPlayer = BarOpponent;
        flipped.BarOpponent = BarPlayer;
        flipped.OffPlayer = OffOpponent;
        flipped.OffOpponent = OffPlayer;
        flipped.RecalcOutsideHome();
        return flipped;
    }

    // ── Standard starting positions ───────────────────────────────

    public static BoardState Standard()
    {
        var s = new BoardState();
        s.Points[5] = 5;
        s.Points[7] = 3;
        s.Points[12] = 5;
        s.Points[23] = 2;
        s.Points[18] = -5;
        s.Points[16] = -3;
        s.Points[11] = -5;
        s.Points[0] = -2;
        s.RecalcOutsideHome();
        return s;
    }

    public static BoardState Nackgammon()
    {
        var s = new BoardState();
        s.Points[5] = 4;
        s.Points[7] = 3;
        s.Points[12] = 4;
        s.Points[22] = 2;
        s.Points[23] = 2;
        s.Points[18] = -4;
        s.Points[16] = -3;
        s.Points[11] = -4;
        s.Points[1] = -2;
        s.Points[0] = -2;
        s.RecalcOutsideHome();
        return s;
    }
}
