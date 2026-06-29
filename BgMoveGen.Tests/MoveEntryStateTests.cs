using BgMoveGen;
using BgDataTypes_Lib;

namespace BgMoveGen.Tests;

public class MoveEntryStateTests
{
    // ── Helpers ───────────────────────────────────────────────────

    private static BoardState ClosedOutOnBar()
    {
        var s = new BoardState();
        s.Points[25] = 1;
        for (int i = 19; i <= 24; i++) s.Points[i] = -2;
        s.Points[6] = 5;
        s.Points[1] = -2;
        s.Points[12] = -1; // pad opponent counts (need not be a legal game state)
        s.RecalcHighPoint();
        return s;
    }

    private static BoardState BearOffPosition_HighFour()
    {
        // Highest = 4, in home board.
        var s = new BoardState();
        s.Points[4] = 5;
        s.Points[3] = 5;
        s.Points[2] = 5;
        s.Points[19] = -5;
        s.Points[17] = -5;
        s.Points[12] = -5;
        s.RecalcHighPoint();
        return s;
    }

    private static BoardState BearOffPosition_HighSixWithGap()
    {
        // Highest = 6; nothing on 5; testing overshoot-not-from-highest illegality.
        var s = new BoardState();
        s.Points[6] = 5;
        s.Points[3] = 5;
        s.Points[1] = 5;
        s.Points[19] = -5;
        s.Points[17] = -5;
        s.Points[12] = -5;
        s.RecalcHighPoint();
        return s;
    }

    private static BoardState SimpleHitPosition()
    {
        // Two-checker minimal: one player on 13, one opponent blot on 10.
        var s = new BoardState();
        s.Points[13] = 1;
        s.Points[10] = -1;
        s.RecalcHighPoint();
        return s;
    }

    private static BoardState SingleCheckerOn(int point)
    {
        var s = new BoardState();
        s.Points[point] = 1;
        s.RecalcHighPoint();
        return s;
    }

    private static BoardState BarEnterThenHit_5_4()
    {
        // Player on bar; opponent blot on 16. With dice (5,4):
        //   bar/20 (die 5) then 20/16* (die 4)   ← non-emitted ordering
        //   bar/21 (die 4) then 21/16* (die 5)   ← canonical (emitted) ordering
        // Both hit the 16 blot and reach the same final state, so GeneratePlays
        // dedups to the canonical bar/21 21/16*.
        var s = new BoardState();
        s.Points[25] = 1;
        s.Points[16] = -1;
        s.RecalcHighPoint();
        return s;
    }

    private static BoardState TwoCheckers(int a, int b)
    {
        var s = new BoardState();
        s.Points[a]++;
        s.Points[b]++;
        s.RecalcHighPoint();
        return s;
    }

    private static int OwnOnBoard(BoardState s)
    {
        int total = 0;
        for (int i = 1; i <= 25; i++)
            if (s.Points[i] > 0) total += s.Points[i];
        return total;
    }

    private static int BearOffCount(Play play)
    {
        int n = 0;
        for (int i = 0; i < play.Count; i++)
            if (play[i].ToPt == 0) n++;
        return n;
    }

    // ── Construction ──────────────────────────────────────────────

    [Fact]
    public void Construction_CapturesInitialByCopy()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);

        initial.Points[8] = 99;

        Assert.NotEqual(99, entry.Current.Points[8]);
    }

    [Fact]
    public void Construction_LegalNextClicks_StandardOpener_3_1()
    {
        // Sources clickable as the first move of a (3,1) standard opener:
        //   24, 13, 8, 6 (each has at least one die-1 or die-3 advance available).
        // Chain-only intermediate FrPts (e.g., 23 in 24→23→20) are excluded — they
        // are not legal sources from the initial state.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.Equal(
            new HashSet<int> { 24, 13, 8, 6 },
            new HashSet<int>(entry.LegalNextClicks));
    }

    [Fact]
    public void Construction_NotComplete_AndCompletedPlayIsNull()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.False(entry.IsComplete);
        Assert.Null(entry.CompletedPlay);
        Assert.Empty(entry.AppliedMoves);
    }

    [Fact]
    public void Construction_PassPosition_IsCompleteImmediately()
    {
        var entry = new MoveEntryState(ClosedOutOnBar(), 3, 1);
        Assert.True(entry.IsComplete);
        Assert.NotNull(entry.CompletedPlay);
        Assert.Equal(0, entry.CompletedPlay!.Value.Count);
        Assert.Empty(entry.LegalNextClicks);
    }

    [Fact]
    public void Construction_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MoveEntryState(null!, 3, 1));
    }

    // ── One-click source-advance (TryAdvanceFrom) ─────────────────
    //
    // diePreference is an ordered list of dice to prefer. The model stays
    // die-order-agnostic: "leftmost die else right" is just [left, right], and
    // "use the other die once one is played" falls out because the candidate set
    // already contains only remaining-die moves.

    [Fact]
    public void TryAdvanceFrom_UsesPreferredDie_WhenLegalFromPoint()
    {
        // Standard (3,1): from 8 both dice play — 8/5 (die 3) and 8/7 (die 1).
        // Prefer die 3 first ⇒ commits 8/5.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(8, new[] { 3, 1 }));
        Assert.Single(entry.AppliedMoves);
        Assert.Equal(8, entry.AppliedMoves[0].FrPt);
        Assert.Equal(5, entry.AppliedMoves[0].ToPt); // die 3
        Assert.Equal(2, entry.Current.Points[8]);
        Assert.Equal(1, entry.Current.Points[5]);
    }

    [Fact]
    public void TryAdvanceFrom_OtherPreferenceOrder_PicksOtherDie()
    {
        // Same point, reversed preference ⇒ commits 8/7 (die 1) instead.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(8, new[] { 1, 3 }));
        Assert.Equal(8, entry.AppliedMoves[0].FrPt);
        Assert.Equal(7, entry.AppliedMoves[0].ToPt); // die 1
    }

    [Fact]
    public void TryAdvanceFrom_FallsBackToOtherDie_WhenPreferredHasNoMoveFromPoint()
    {
        // Highest home point = 6; checkers on 6/3/1, dice (5,1). From point 3 only
        // die 1 advances (3/2). Die 5 cannot: it overshoots 3 but bear-off with the
        // big die is legal only from the highest point (6, occupied), and 3/-2 is
        // off the board. Prefer die 5 first ⇒ fall back to die 1.
        var s = BearOffPosition_HighSixWithGap();
        var entry = new MoveEntryState(s, 5, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(3, new[] { 5, 1 }));
        Assert.Equal(3, entry.AppliedMoves[0].FrPt);
        Assert.Equal(2, entry.AppliedMoves[0].ToPt); // die 1, despite preferring 5
        Assert.Equal(4, entry.Current.Points[3]);    // 5 - 1
    }

    [Fact]
    public void TryAdvanceFrom_AfterOneDiePlayed_UsesRemainingDie_RegardlessOfPreference()
    {
        // Commit 8/5 (die 3); die 1 is all that remains. Advancing from 6 must use
        // die 1 (6/5) even though we prefer die 3.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 }); // consumes die 3

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(6, new[] { 3, 1 }));
        Assert.Equal(6, entry.AppliedMoves[1].FrPt);
        Assert.Equal(5, entry.AppliedMoves[1].ToPt); // die 1, the only one left
        Assert.True(entry.IsComplete);
    }

    [Fact]
    public void TryAdvanceFrom_Doubles_AdvancesIrrespectiveOfPreference()
    {
        // (6,6) standard opener: from 24, die 6 plays 24/18. Preference order is
        // moot under doubles — an empty preference still advances.
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 6, 6);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(24, Array.Empty<int>()));
        Assert.Equal(24, entry.AppliedMoves[0].FrPt);
        Assert.Equal(18, entry.AppliedMoves[0].ToPt);
    }

    [Fact]
    public void TryAdvanceFrom_SequenceCompletesPlay_MatchesCanonicalGeneratedPlay()
    {
        // Drive a full (3,1) play with one-click advances: 8/5 then 6/5.
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(8, new[] { 3, 1 }));
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(6, new[] { 3, 1 }));

        Assert.True(entry.IsComplete);
        var allPlays = MoveGenerator.GeneratePlays(initial, 3, 1);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_LoversLeap_24to18_then_18to13_Completes()
    {
        // (6,5) lovers leap: a single back checker chains 24→18 (die 6) → 13 (die 5).
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 6, 5);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(24, new[] { 6, 5 }));
        Assert.Equal(18, entry.AppliedMoves[0].ToPt);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(18, new[] { 5, 6 }));

        Assert.True(entry.IsComplete);
        var allPlays = MoveGenerator.GeneratePlays(initial, 6, 5);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_Doubles_FullPlay_ReachesCompletion()
    {
        // (6,6) standard opener: 24/18, 24/18, 13/7, 13/7.
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 6, 6);

        entry.TryAdvanceFrom(24, new[] { 6 });
        entry.TryAdvanceFrom(24, new[] { 6 });
        entry.TryAdvanceFrom(13, new[] { 6 });
        Assert.False(entry.IsComplete);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(13, new[] { 6 }));

        Assert.True(entry.IsComplete);
        var allPlays = MoveGenerator.GeneratePlays(initial, 6, 6);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_BarEntry_AdvancesTheBarChecker()
    {
        var s = new BoardState();
        s.Points[25] = 1;
        s.Points[6] = 5; s.Points[8] = 3; s.Points[13] = 5; s.Points[24] = 1;
        s.Points[19] = -5; s.Points[17] = -3; s.Points[12] = -5; s.Points[1] = -2;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 3, 1);
        // Bar entries: 22 (die 3) and 24 (die 1). Prefer die 3 ⇒ enter on 22.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(25, new[] { 3, 1 }));
        Assert.Equal(0, entry.Current.Points[25]);
        Assert.Equal(1, entry.Current.Points[22]);
    }

    [Fact]
    public void TryAdvanceFrom_BearOff_AdvancingHomeCheckerBearsItOff()
    {
        var s = BearOffPosition_HighFour();
        var entry = new MoveEntryState(s, 4, 1);

        // From 4, die 4 bears off (ToPt 0); die 1 → 4/3. Prefer die 4 ⇒ bear off.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(4, new[] { 4, 1 }));
        Assert.Equal(0, entry.AppliedMoves[0].ToPt); // bore off
        Assert.Equal(4, entry.Current.Points[4]);    // 5 - 1
    }

    [Fact]
    public void TryAdvanceFrom_BearOff_OvershootFromHighest_BearsOff()
    {
        // Highest = 4; die 5 overshoots and bears off from the highest point.
        var s = BearOffPosition_HighFour();
        var entry = new MoveEntryState(s, 5, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(4, new[] { 5, 1 }));
        Assert.Equal(0, entry.AppliedMoves[0].ToPt); // overshoot bear-off, ToPt 0
    }

    [Fact]
    public void TryAdvanceFrom_OpponentPoint_ReturnsIllegal()
    {
        // Standard (3,1): 12 is an opponent point — no own checker to advance.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.Equal(ClickOutcome.Illegal, entry.TryAdvanceFrom(12, new[] { 3, 1 }));
        Assert.Empty(entry.AppliedMoves);
    }

    [Fact]
    public void TryAdvanceFrom_PointWithNoLegalMove_ReturnsIllegal_NoStateChange()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);

        // 10 is empty — no own checker, no advancing move.
        Assert.Equal(ClickOutcome.Illegal, entry.TryAdvanceFrom(10, new[] { 3, 1 }));
        Assert.Empty(entry.AppliedMoves);
        Assert.Equal(0, entry.Current.Points[10]);
    }

    [Fact]
    public void TryAdvanceFrom_AfterComplete_ReturnsIllegal()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 });
        entry.TryAdvanceFrom(6, new[] { 3, 1 });
        Assert.True(entry.IsComplete);

        Assert.Equal(ClickOutcome.Illegal, entry.TryAdvanceFrom(13, new[] { 3, 1 }));
    }

    [Fact]
    public void TryAdvanceFrom_NullPreference_Throws()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.Throws<ArgumentNullException>(() => entry.TryAdvanceFrom(8, null!));
    }

    [Fact]
    public void TryAdvanceFrom_Current_ReflectsAppliedMoves_MidPlay()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 }); // 8/5

        // Standard: Points[8]=3, Points[5]=0. After 8→5: Points[8]=2, Points[5]=1.
        Assert.Equal(2, entry.Current.Points[8]);
        Assert.Equal(1, entry.Current.Points[5]);
    }

    [Fact]
    public void TryAdvanceFrom_Hit_LandsAndSendsOpponentToBar_InternalEncodingIsNegative()
    {
        var s = SimpleHitPosition();
        var entry = new MoveEntryState(s, 3, 5);

        // From 13, die 3 → 13/10*, hitting the blot on 10.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(13, new[] { 3, 5 }));

        var hit = entry.AppliedMoves[0];
        Assert.Equal(13, hit.FrPt);
        Assert.Equal(-10, hit.ToPt); // hit encoded internally as negative
        Assert.Equal(1, entry.Current.Points[10]);   // player landed on 10
        Assert.Equal(-1, entry.Current.Points[0]);   // opponent on bar
    }

    // ── LegalNextClicks (advance-source surface) ──────────────────

    [Fact]
    public void LegalNextClicks_PostFirstCommit_MatchesStateReachableMoves()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 }); // commit 8/5 (die 3); die 1 remains

        // Legality is by reachable board STATE, not literal move-lists. After 8/5,
        // every legal die-1 single move from the current board forms a legal 2-move
        // play (it uses both dice) and so reaches a generated final state. Expected
        // sources = FrPts of those die-1 moves.
        var expected = new HashSet<int>();
        Span<Move> buf = stackalloc Move[30];
        int n = MoveGenerator.SingleMoves(entry.Current, 1, buf);
        for (int i = 0; i < n; i++) expected.Add(buf[i].FrPt);

        Assert.Equal(expected, new HashSet<int>(entry.LegalNextClicks));

        // Includes 5: advancing 8/5 then 5/4 yields 8/4, which GeneratePlays emits
        // canonically as 8/7/4 — a move-list that does NOT contain the move 8/5.
        // State-based legality still admits 5 as a source.
        Assert.Contains(5, entry.LegalNextClicks);
    }

    [Fact]
    public void LegalNextClicks_OnBar_BarIsTheOnlySource_NonBarAdvanceIllegal()
    {
        var s = new BoardState();
        s.Points[25] = 1;
        s.Points[6] = 5; s.Points[8] = 3; s.Points[13] = 5; s.Points[24] = 1;
        s.Points[19] = -5; s.Points[17] = -3; s.Points[12] = -5; s.Points[1] = -2;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 3, 1);
        Assert.Equal(new HashSet<int> { 25 }, new HashSet<int>(entry.LegalNextClicks));
        // With a checker on the bar, no other point can advance.
        Assert.Equal(ClickOutcome.Illegal, entry.TryAdvanceFrom(8, new[] { 3, 1 }));
    }

    [Fact]
    public void TryAdvanceFrom_ForcedSingleEntryDie_EntersOnTheOnlyOpenPoint()
    {
        // Bar checker; 5 of 6 entry points blocked, leaving 22 (die-3 entry) open.
        var s = new BoardState();
        s.Points[25] = 1;
        s.Points[24] = -2; s.Points[23] = -2;
        s.Points[21] = -2; s.Points[20] = -2; s.Points[19] = -2;
        s.Points[6] = 5;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 3, 1);
        Assert.Equal(new HashSet<int> { 25 }, new HashSet<int>(entry.LegalNextClicks));

        // Die 1 entry (24) is blocked; preferring die 1 still enters via die 3 on 22.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(25, new[] { 1, 3 }));
        Assert.Equal(0, entry.Current.Points[25]);
        Assert.Equal(1, entry.Current.Points[22]);
    }

    [Fact]
    public void TryAdvanceFrom_Doubles_ChainSequence_TracksIntermediateState()
    {
        // Single back checker chains forward 24→21→18→… with dice (3,3,3,3).
        var s = new BoardState();
        s.Points[24] = 1;
        s.Points[2] = -2;
        s.Points[1] = -2;
        s.Points[6] = 5;
        s.Points[5] = 5;
        s.Points[4] = 4;
        s.Points[19] = -5; s.Points[17] = -3; s.Points[12] = -3;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 3, 3);
        Assert.Contains(24, entry.LegalNextClicks);

        entry.TryAdvanceFrom(24, new[] { 3 }); // 24→21
        Assert.Contains(21, entry.LegalNextClicks);

        entry.TryAdvanceFrom(21, new[] { 3 }); // 21→18
        Assert.Contains(18, entry.LegalNextClicks);
    }

    // ── Combined single-checker moves: both die orderings ─────────
    //
    // Regression: GeneratePlays board-state-dedups equivalent die orderings
    // (both intermediates open → same final square) to one canonical play.
    // MoveEntryState must accept *either* physical path (the caller picks the die
    // via diePreference), and must canonicalize the completed Play so it equals the
    // generated one regardless of path taken.

    [Fact]
    public void TryAdvanceFrom_CombinedSingleChecker_EmittedOrdering_11to10to5_Completes()
    {
        // One checker on 11, dice (5,1). Path 11→10 (die 1) → 5 (die 5).
        var initial = SingleCheckerOn(11);
        var entry = new MoveEntryState(initial, 5, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(11, new[] { 1, 5 }));
        Assert.Equal(10, entry.AppliedMoves[0].ToPt);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(10, new[] { 5, 1 }));

        Assert.True(entry.IsComplete);
        Assert.Equal(1, entry.Current.Points[5]);
        var allPlays = MoveGenerator.GeneratePlays(initial, 5, 1);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_CombinedSingleChecker_NonEmittedOrdering_11to6to5_Completes()
    {
        // Same position, the OTHER ordering: 11→6 (die 5) → 5 (die 1).
        // GeneratePlays emits only 11/10/5 for this position; this path must
        // still be enterable and canonicalize to the same Play.
        var initial = SingleCheckerOn(11);
        var entry = new MoveEntryState(initial, 5, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(11, new[] { 5, 1 }));
        Assert.Equal(6, entry.AppliedMoves[0].ToPt);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(6, new[] { 1, 5 }));

        Assert.True(entry.IsComplete);
        Assert.Equal(1, entry.Current.Points[5]);
        var allPlays = MoveGenerator.GeneratePlays(initial, 5, 1);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_CombinedSingleChecker_BothOrderings_YieldEqualCanonicalPlay()
    {
        var initial = SingleCheckerOn(11);

        var viaTen = new MoveEntryState(initial, 5, 1);
        viaTen.TryAdvanceFrom(11, new[] { 1, 5 }); // 11→10
        viaTen.TryAdvanceFrom(10, new[] { 5, 1 }); // 10→5

        var viaSix = new MoveEntryState(initial, 5, 1);
        viaSix.TryAdvanceFrom(11, new[] { 5, 1 }); // 11→6
        viaSix.TryAdvanceFrom(6, new[] { 1, 5 });  // 6→5

        Assert.True(viaTen.IsComplete);
        Assert.True(viaSix.IsComplete);
        // Same canonical Play regardless of the intermediate path the user took.
        Assert.Equal(viaTen.CompletedPlay!.Value, viaSix.CompletedPlay!.Value);
    }

    [Fact]
    public void TryAdvanceFrom_CombinedSingleChecker_FullBoard_5_1_BothOrderings()
    {
        // Full-board reconstruction of the reported 5-1 bug. A back checker on 11
        // can play 11/5 as a combined move; both intermediates (10 and 6) are open,
        // so GeneratePlays emits a single canonical ordering.
        var s = new BoardState();
        s.Points[24] = 2;
        s.Points[13] = 4;
        s.Points[11] = 1;
        s.Points[8] = 3;
        s.Points[6] = 5;
        s.Points[19] = -5;
        s.Points[17] = -3;
        s.Points[12] = -5;
        s.Points[1] = -2;
        s.RecalcHighPoint();

        var allPlays = MoveGenerator.GeneratePlays(s, 5, 1);

        // Path A: 11→10→5
        var a = new MoveEntryState(s, 5, 1);
        a.TryAdvanceFrom(11, new[] { 1, 5 });
        a.TryAdvanceFrom(10, new[] { 5, 1 });
        Assert.True(a.IsComplete);
        Assert.Contains(allPlays, p => p.Equals(a.CompletedPlay!.Value));

        // Path B: 11→6→5 (the ordering GeneratePlays did not emit)
        var b = new MoveEntryState(s, 5, 1);
        Assert.Equal(ClickOutcome.MoveCommitted, b.TryAdvanceFrom(11, new[] { 5, 1 }));
        Assert.Equal(ClickOutcome.PlayCompleted, b.TryAdvanceFrom(6, new[] { 1, 5 }));
        Assert.True(b.IsComplete);
        Assert.Contains(allPlays, p => p.Equals(b.CompletedPlay!.Value));

        Assert.Equal(a.CompletedPlay!.Value, b.CompletedPlay!.Value);
    }

    // ── Hit on the second move (both bar-entry orderings) ─────────

    [Fact]
    public void TryAdvanceFrom_BarEnterThenHit_NonEmittedOrdering_bar20_20to16_Completes()
    {
        var initial = BarEnterThenHit_5_4();
        var entry = new MoveEntryState(initial, 5, 4);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(25, new[] { 5, 4 })); // bar/20 (die 5)
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(20, new[] { 4, 5 })); // 20/16* hit

        Assert.True(entry.IsComplete);
        Assert.Equal(1, entry.Current.Points[16]);   // player landed on 16
        Assert.Equal(-1, entry.Current.Points[0]);   // opponent sent to bar
        var allPlays = MoveGenerator.GeneratePlays(initial, 5, 4);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_BarEnterThenHit_EmittedOrdering_bar21_21to16_Completes()
    {
        var initial = BarEnterThenHit_5_4();
        var entry = new MoveEntryState(initial, 5, 4);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(25, new[] { 4, 5 })); // bar/21 (die 4)
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(21, new[] { 5, 4 })); // 21/16* hit

        Assert.True(entry.IsComplete);
        Assert.Equal(-1, entry.Current.Points[0]);
        var allPlays = MoveGenerator.GeneratePlays(initial, 5, 4);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    // ── Edge cases: blocked / blot intermediates, doubles permutations ──

    [Fact]
    public void TryAdvanceFrom_BlockedIntermediate_OnlyOneOrderingLegal_StillWorks()
    {
        // Checker on 11, dice (5,1), opponent owns 10 (≥2). The die-1-first ordering
        // 11→10 is blocked, so only 11→6→5 is legal. Preferring die 1 still advances
        // via the only legal move (11→6).
        var s = new BoardState();
        s.Points[11] = 1;
        s.Points[10] = -2;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 5, 1);
        Assert.Contains(11, entry.LegalNextClicks);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(11, new[] { 1, 5 }));
        Assert.Equal(6, entry.AppliedMoves[0].ToPt); // 11→6 (die 5), not the blocked 11→10
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(6, new[] { 1, 5 }));

        var allPlays = MoveGenerator.GeneratePlays(s, 5, 1);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_BlotIntermediate_OrderingsDiffer_RemainDistinct()
    {
        // Checker on 11, dice (5,1), opponent BLOT on 10.
        //   11→10*(die 1, hit) → 5 (die 5)  — sends opponent to bar
        //   11→6 (die 5) → 5 (die 1)        — leaves the 10 blot untouched
        // Different final states ⇒ GeneratePlays keeps BOTH, and the two paths must
        // yield distinct (non-equal) completed plays.
        var s = new BoardState();
        s.Points[11] = 1;
        s.Points[10] = -1; // blot
        s.RecalcHighPoint();

        var allPlays = MoveGenerator.GeneratePlays(s, 5, 1);

        var hitPath = new MoveEntryState(s, 5, 1);
        hitPath.TryAdvanceFrom(11, new[] { 1, 5 }); // 11→10* (hit)
        hitPath.TryAdvanceFrom(10, new[] { 5, 1 }); // 10→5
        Assert.True(hitPath.IsComplete);
        Assert.Equal(-1, hitPath.Current.Points[0]); // opponent on bar
        Assert.Contains(allPlays, p => p.Equals(hitPath.CompletedPlay!.Value));

        var noHitPath = new MoveEntryState(s, 5, 1);
        noHitPath.TryAdvanceFrom(11, new[] { 5, 1 }); // 11→6
        noHitPath.TryAdvanceFrom(6, new[] { 1, 5 });  // 6→5
        Assert.True(noHitPath.IsComplete);
        Assert.Equal(-1, noHitPath.Current.Points[10]); // blot survives
        Assert.Contains(allPlays, p => p.Equals(noHitPath.CompletedPlay!.Value));

        // Genuinely different outcomes — must NOT collapse to one canonical play.
        Assert.NotEqual(hitPath.CompletedPlay!.Value, noHitPath.CompletedPlay!.Value);
    }

    [Fact]
    public void TryAdvanceFrom_Doubles_CombinedMove_InterleavedOrdering_Completes()
    {
        // Two checkers on 6, dice (2,2): 6/2(2) via 6→4→2 per checker. Enter in an
        // interleaved order (first checker all the way, then second).
        var s = new BoardState();
        s.Points[6] = 2;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 2, 2);
        entry.TryAdvanceFrom(6, new[] { 2 }); // 6→4 (checker A)
        entry.TryAdvanceFrom(4, new[] { 2 }); // 4→2 (checker A all the way)
        entry.TryAdvanceFrom(6, new[] { 2 }); // 6→4 (checker B)
        Assert.False(entry.IsComplete);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(4, new[] { 2 })); // 4→2 (checker B)

        Assert.True(entry.IsComplete);
        Assert.Equal(2, entry.Current.Points[2]);
        var allPlays = MoveGenerator.GeneratePlays(s, 2, 2);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    // ── Undo ──────────────────────────────────────────────────────

    [Fact]
    public void UndoLast_AfterCommit_RestoresPriorState()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 }); // 8/5

        entry.UndoLast();

        Assert.Empty(entry.AppliedMoves);
        for (int i = 0; i <= 25; i++)
            Assert.Equal(initial.Points[i], entry.Current.Points[i]);
    }

    [Fact]
    public void UndoLast_NoCommits_NoOp()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.UndoLast(); // should not throw
        Assert.Empty(entry.AppliedMoves);
    }

    [Fact]
    public void UndoLast_AfterMultipleCommits_RollsBackOnlyLast()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 }); // 8/5 (die 3)
        entry.TryAdvanceFrom(6, new[] { 3, 1 }); // 6/5 (die 1) → completes
        Assert.True(entry.IsComplete);

        entry.UndoLast();

        Assert.False(entry.IsComplete);
        Assert.Null(entry.CompletedPlay);
        Assert.Single(entry.AppliedMoves);
        Assert.Equal(8, entry.AppliedMoves[0].FrPt);
    }

    [Fact]
    public void UndoAll_AfterPartial_RestoresInitial()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 }); // 8/5

        entry.UndoAll();

        Assert.Empty(entry.AppliedMoves);
        Assert.False(entry.IsComplete);
        Assert.Null(entry.CompletedPlay);
        for (int i = 0; i <= 25; i++)
            Assert.Equal(initial.Points[i], entry.Current.Points[i]);
        Assert.Equal(initial.HighPointOccupied, entry.Current.HighPointOccupied);
    }

    [Fact]
    public void UndoAll_AfterComplete_RestoresInitialAndAllowsReplay()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 });
        entry.TryAdvanceFrom(6, new[] { 3, 1 });
        Assert.True(entry.IsComplete);

        entry.UndoAll();

        Assert.False(entry.IsComplete);
        // Should be able to play again.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(24, new[] { 3, 1 }));
    }

    // ── Tray-click bear-off-max (TryBearOffMax) ───────────────────
    //
    // The tray bears off the MAXIMUM number of checkers iff a unique reachable
    // complete play achieves that maximum (and it bears off ≥ 1). Ties for the
    // max, and positions where nothing can bear off, are no-ops.

    [Fact]
    public void TryBearOffMax_UniqueMax_CommitsIt_CompletesWithMaxBorneOff()
    {
        // Checkers on 2 and 1, dice (2,1). Bearing off both (2/0 + 1/0) clears the
        // board — 2 off. The rival completion 2/1 then 1/0(overshoot) bears off only
        // 1, so the max (2) is unique. Tray must commit the clear-the-board play.
        var initial = TwoCheckers(2, 1);
        var entry = new MoveEntryState(initial, 2, 1);

        Assert.Equal(2, OwnOnBoard(entry.Current)); // before

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryBearOffMax());
        Assert.True(entry.IsComplete);
        Assert.Equal(0, OwnOnBoard(entry.Current)); // both borne off → max = 2

        var allPlays = MoveGenerator.GeneratePlays(initial, 2, 1);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryBearOffMax_TieForMax_ReturnsIllegal_NoStateChange()
    {
        // Checkers on 6, 3, 2 with dice (6,1). die6 bears off the 6 (exact); die1
        // cannot bear off (point 1 empty, 6 isn't reachable-off by a 1). So every
        // completion bears off exactly one checker, but the die-1 move (3/2 vs 2/1)
        // leaves two DIFFERENT boards — a tie for the maximum ⇒ ambiguous ⇒ no-op.
        var s = new BoardState();
        s.Points[6] = 1; s.Points[3] = 1; s.Points[2] = 1;
        s.RecalcHighPoint();
        var entry = new MoveEntryState(s, 6, 1);

        var before = entry.Current.ToMop();
        Assert.Equal(ClickOutcome.Illegal, entry.TryBearOffMax());

        Assert.Empty(entry.AppliedMoves);
        Assert.False(entry.IsComplete);
        Assert.Equal(before, entry.Current.ToMop());
    }

    [Fact]
    public void TryBearOffMax_NoBearOffPossible_ReturnsIllegal_NoStateChange()
    {
        // Standard (3,1) opener: nothing is anywhere near bearing off.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);

        var before = entry.Current.ToMop();
        Assert.Equal(ClickOutcome.Illegal, entry.TryBearOffMax());

        Assert.Empty(entry.AppliedMoves);
        Assert.False(entry.IsComplete);
        Assert.Equal(before, entry.Current.ToMop());
    }

    [Fact]
    public void TryBearOffMax_PartialState_BearsOffUniqueRemainder_Completes()
    {
        // Checkers on 6 and 1, dice (6,1). Manually bear off the 6 (one-click advance
        // preferring die 6 → 6/0), leaving die 1 and the checker on 1. The tray then
        // bears off the unique remainder (1/0) and completes.
        var initial = TwoCheckers(6, 1);
        var entry = new MoveEntryState(initial, 6, 1);

        entry.TryAdvanceFrom(6, new[] { 6, 1 }); // manual 6/0 (die 6)
        Assert.Single(entry.AppliedMoves);
        Assert.Equal(1, OwnOnBoard(entry.Current)); // only the 1-checker remains

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryBearOffMax());
        Assert.True(entry.IsComplete);
        Assert.Equal(0, OwnOnBoard(entry.Current));

        var allPlays = MoveGenerator.GeneratePlays(initial, 6, 1);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryBearOffMax_Doubles_BearsOffMultiple_WhenUnique()
    {
        // Checker on 4 and two on 2, dice (2,2). The four 2s exactly clear the board
        // (4→2→0 consumes two, each 2-checker one), so the only completion bears off
        // all three. Unique max ⇒ tray clears the board.
        var s = new BoardState();
        s.Points[4] = 1; s.Points[2] = 2;
        s.RecalcHighPoint();
        var entry = new MoveEntryState(s, 2, 2);

        Assert.Equal(3, OwnOnBoard(entry.Current));
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryBearOffMax());
        Assert.True(entry.IsComplete);
        Assert.Equal(0, OwnOnBoard(entry.Current)); // 3 borne off

        var allPlays = MoveGenerator.GeneratePlays(s, 2, 2);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryBearOffMax_AlreadyComplete_ReturnsIllegal()
    {
        // Pass position: complete at construction with the empty play.
        var entry = new MoveEntryState(ClosedOutOnBar(), 3, 1);
        Assert.True(entry.IsComplete);
        Assert.Equal(ClickOutcome.Illegal, entry.TryBearOffMax());
    }

    [Fact]
    public void TryBearOffMax_OutlierComesHomeThenBearsOff_BearsOff()
    {
        // Outlier on the 8-pt plus a home checker on 1, dice (6,2). The outlier must
        // come home with one die and bear off with the other: 8/6 (die 2) 6/0 (die 6),
        // or equivalently 8/2 (die 6) 2/0 (die 2) — both reach the same final {1:1},
        // so the completion is unique and bears off exactly the one outlier. The home
        // checker on 1 cannot also bear off (it is never the highest while a die is
        // left), so the maximum is 1 and the tray commits it.
        var initial = TwoCheckers(8, 1);
        var entry = new MoveEntryState(initial, 6, 2);

        Assert.Equal(2, OwnOnBoard(entry.Current)); // before

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryBearOffMax());
        Assert.True(entry.IsComplete);
        Assert.Equal(1, OwnOnBoard(entry.Current)); // dropped by 1 — outlier borne off

        Assert.Equal(1, BearOffCount(entry.CompletedPlay!.Value)); // exactly one ToPt == 0
        var allPlays = MoveGenerator.GeneratePlays(initial, 6, 2);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryBearOffMax_OutlierComesHomeButDiceCantAlsoBearOff_ReturnsIllegal()
    {
        // Same shape of "outlier comes home", but dice (2,1) cannot bring-home-AND-
        // bear-off: 8/6 (die 2) lands the outlier on the edge, and die 1 only shuffles
        // (6/5) — no checker bears off, so the maximum is 0. Documents that the no-op
        // is dice-driven, not "the outlier blocks bear-off".
        var s = TwoCheckers(8, 6);
        var entry = new MoveEntryState(s, 2, 1);

        Assert.False(entry.IsComplete);
        var before = entry.Current.ToMop();

        Assert.Equal(ClickOutcome.Illegal, entry.TryBearOffMax());

        Assert.Empty(entry.AppliedMoves);
        Assert.False(entry.IsComplete);
        Assert.Equal(before, entry.Current.ToMop());
    }
}
