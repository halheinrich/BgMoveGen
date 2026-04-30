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
    public void Construction_NoSelectedSource()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.Null(entry.SelectedSource);
    }

    [Fact]
    public void Construction_LegalNextClicks_StandardOpener_3_1()
    {
        // Sources clickable as the first move of a (3,1) standard opener:
        //   24, 13, 8, 6 (each has at least one die-1 or die-3 destination available).
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

    // ── Source selection ──────────────────────────────────────────

    [Fact]
    public void TryAddClick_LegalSource_ReturnsSourceSelected()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(8));
        Assert.Equal(8, entry.SelectedSource);
    }

    [Fact]
    public void TryAddClick_LegalSource_SwapsLegalNextClicksToDestinations()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8);
        // From 8 with dice (3,1): destinations are 5 (die 3) and 7 (die 1).
        Assert.Contains(5, entry.LegalNextClicks);
        Assert.Contains(7, entry.LegalNextClicks);
        // Other-source FrPts should not appear.
        Assert.DoesNotContain(24, entry.LegalNextClicks);
        Assert.DoesNotContain(13, entry.LegalNextClicks);
    }

    [Fact]
    public void TryAddClick_PointWithNoOwnChecker_ReturnsIllegal()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.Equal(ClickOutcome.Illegal, entry.TryAddClick(10)); // empty
        Assert.Null(entry.SelectedSource);
    }

    [Fact]
    public void TryAddClick_OpponentPoint_ReturnsIllegal()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.Equal(ClickOutcome.Illegal, entry.TryAddClick(12)); // opponent
    }

    [Fact]
    public void TryAddClick_DifferentLegalSourceWhileSelected_ReplacesSelection()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8);
        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(13));
        Assert.Equal(13, entry.SelectedSource);
    }

    [Fact]
    public void TryAddClick_SameSourceWhileSelected_StaysSelected_NoCommit()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8);
        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(8));
        Assert.Equal(8, entry.SelectedSource);
        Assert.Empty(entry.AppliedMoves);
    }

    // ── Destination commit (3,1 opener) ───────────────────────────

    [Fact]
    public void TryAddClick_FullSequence_8to5_then_6to5_CompletesPlay()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);

        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(8));
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAddClick(5));
        Assert.Null(entry.SelectedSource);
        Assert.Single(entry.AppliedMoves);
        Assert.Equal(8, entry.AppliedMoves[0].FrPt);
        Assert.Equal(5, entry.AppliedMoves[0].ToPt);

        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(6));
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAddClick(5));

        Assert.True(entry.IsComplete);
        Assert.NotNull(entry.CompletedPlay);
        Assert.Equal(2, entry.CompletedPlay!.Value.Count);

        var allPlays = MoveGenerator.GeneratePlays(initial, 3, 1);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay.Value));
    }

    [Fact]
    public void TryAddClick_LegalDestination_AppliesMove_UpdatesCurrent()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8);
        entry.TryAddClick(5);
        Assert.Equal(2, entry.Current.Points[8]);
        Assert.Equal(1, entry.Current.Points[5]);
    }

    [Fact]
    public void TryAddClick_IllegalDestination_StateUnchanged()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8);
        // 4 is not a legal destination from 8 with dice (3,1) — would need die 4.
        // Not a legal source either (8 → 4 needs die 4, no own checker on 4).
        Assert.Equal(ClickOutcome.Illegal, entry.TryAddClick(4));
        Assert.Equal(8, entry.SelectedSource); // still selected
        Assert.Empty(entry.AppliedMoves);
        Assert.Equal(3, entry.Current.Points[8]); // unchanged
    }

    [Fact]
    public void TryAddClick_LastMoveOfPlay_ReturnsPlayCompleted_NotMoveCommitted()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8);
        entry.TryAddClick(5);
        entry.TryAddClick(6);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAddClick(5));
    }

    [Fact]
    public void TryAddClick_AfterComplete_ReturnsIllegal()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8); entry.TryAddClick(5);
        entry.TryAddClick(6); entry.TryAddClick(5);
        Assert.Equal(ClickOutcome.Illegal, entry.TryAddClick(13));
    }

    // ── (6,5) opener ──────────────────────────────────────────────

    [Fact]
    public void TryAddClick_LoversLeap_24to18_then_18to13_Completes()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 6, 5);

        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(24));
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAddClick(18));

        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(18));
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAddClick(13));

        Assert.True(entry.IsComplete);
        var allPlays = MoveGenerator.GeneratePlays(initial, 6, 5);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    // ── Doubles ───────────────────────────────────────────────────

    [Fact]
    public void TryAddClick_Doubles_FullPlay_ReachesCompletion()
    {
        // (6,6) standard opener — pick a play and click it through.
        // 24/18, 24/18, 13/7, 13/7.
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 6, 6);

        Assert.Equal(4, entry.IsComplete ? 0 : MoveGenerator.GeneratePlays(initial, 6, 6)[0].Count);

        entry.TryAddClick(24); entry.TryAddClick(18);
        entry.TryAddClick(24); entry.TryAddClick(18);
        entry.TryAddClick(13); entry.TryAddClick(7);
        Assert.False(entry.IsComplete);
        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(13));
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAddClick(7));

        Assert.True(entry.IsComplete);
        var allPlays = MoveGenerator.GeneratePlays(initial, 6, 6);
        Assert.Contains(allPlays, p => p.Equals(entry.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAddClick_Doubles_ChainSequence_RequiresIntermediateState()
    {
        // Single back checker that chains forward: 24→21→18→15→12 with dice (3,3,3,3).
        var s = new BoardState();
        s.Points[24] = 1;
        // Keep open lanes 21, 18, 15, 12 — opponent's other men below the action.
        s.Points[2] = -2;
        s.Points[1] = -2;
        // Pad to 15 player checkers with home-board points (well below the action).
        s.Points[6] = 5;
        s.Points[5] = 5;
        s.Points[4] = 4;
        s.Points[19] = -5; s.Points[17] = -3; s.Points[12] = -3;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 3, 3);
        // Must follow the chain in the only sequence that's individually legal:
        // From initial, only FrPt 24 / 6 / 5 / 4 are sources.
        Assert.Contains(24, entry.LegalNextClicks);
        // After 24→21, the 21-spot has the checker — chain is now from 21.
        entry.TryAddClick(24); entry.TryAddClick(21);
        Assert.Contains(21, entry.LegalNextClicks);
        // The chain destination from 21 with die 3 is 18.
        entry.TryAddClick(21);
        Assert.Contains(18, entry.LegalNextClicks);
    }

    // ── Bar entry ─────────────────────────────────────────────────

    [Fact]
    public void TryAddClick_OnBar_BarIsTheOnlyLegalSource()
    {
        var s = new BoardState();
        s.Points[25] = 1;
        s.Points[6] = 5; s.Points[8] = 3; s.Points[13] = 5; s.Points[24] = 1;
        s.Points[19] = -5; s.Points[17] = -3; s.Points[12] = -5; s.Points[1] = -2;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 3, 1);
        Assert.Equal(new HashSet<int> { 25 }, new HashSet<int>(entry.LegalNextClicks));
        Assert.Equal(ClickOutcome.Illegal, entry.TryAddClick(8));
    }

    [Fact]
    public void TryAddClick_BarEntry_CommitsToEntryPoint()
    {
        var s = new BoardState();
        s.Points[25] = 1;
        s.Points[6] = 5; s.Points[8] = 3; s.Points[13] = 5; s.Points[24] = 1;
        s.Points[19] = -5; s.Points[17] = -3; s.Points[12] = -5; s.Points[1] = -2;
        s.RecalcHighPoint();

        var entry = new MoveEntryState(s, 3, 1);
        entry.TryAddClick(25);
        // Entry destinations: 22 (die 3) and 24 (die 1).
        Assert.Contains(22, entry.LegalNextClicks);
        Assert.Contains(24, entry.LegalNextClicks);

        entry.TryAddClick(22);
        Assert.Equal(0, entry.Current.Points[25]);
        Assert.Equal(1, entry.Current.Points[22]);
    }

    [Fact]
    public void TryAddClick_BarEntry_ClosedOut_IsCompleteAtConstruction()
    {
        var entry = new MoveEntryState(ClosedOutOnBar(), 3, 1);
        Assert.True(entry.IsComplete);
        Assert.Equal(0, entry.CompletedPlay!.Value.Count);
    }

    // ── Bear off ──────────────────────────────────────────────────

    [Fact]
    public void TryAddClick_BearOff_DieExact_LegalDest0()
    {
        var s = BearOffPosition_HighFour();
        var entry = new MoveEntryState(s, 4, 1);
        entry.TryAddClick(4);
        Assert.Contains(0, entry.LegalNextClicks);
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAddClick(0));
        Assert.Equal(0, entry.AppliedMoves[0].ToPt);
        Assert.Equal(4, entry.Current.Points[4]); // 5 - 1 = 4
    }

    [Fact]
    public void TryAddClick_BearOff_OvershootFromHighest_Allowed()
    {
        var s = BearOffPosition_HighFour();
        var entry = new MoveEntryState(s, 5, 1);
        entry.TryAddClick(4);
        // 4 is the highest; die 5 overshoots — bear-off legal.
        Assert.Contains(0, entry.LegalNextClicks);
    }

    [Fact]
    public void TryAddClick_BearOff_NotFromHighest_DieMismatch_NoBearOffOption()
    {
        // Highest = 6; click 3 with dice (5,1). Die 5 only bear-offs from 6 (not 3).
        // From 3 with die 5 — overshoot not from highest. Die 1 → 3→2, not bear-off.
        var s = BearOffPosition_HighSixWithGap();
        var entry = new MoveEntryState(s, 5, 1);
        entry.TryAddClick(3);
        Assert.DoesNotContain(0, entry.LegalNextClicks);
    }

    // ── Hits ──────────────────────────────────────────────────────

    [Fact]
    public void TryAddClick_Hit_DestinationClickIsPositive_InternalEncodingIsNegative()
    {
        var s = SimpleHitPosition();
        var entry = new MoveEntryState(s, 3, 5);

        entry.TryAddClick(13);
        Assert.Contains(10, entry.LegalNextClicks); // hit destination clicks as positive
        entry.TryAddClick(10);

        var hit = entry.AppliedMoves[0];
        Assert.Equal(13, hit.FrPt);
        Assert.Equal(-10, hit.ToPt); // hit encoded internally as negative
        Assert.Equal(1, entry.Current.Points[10]);   // player landed on 10
        Assert.Equal(-1, entry.Current.Points[0]);   // opponent on bar
    }

    // ── Undo ──────────────────────────────────────────────────────

    [Fact]
    public void UndoLast_AfterCommit_RestoresPriorState()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);
        entry.TryAddClick(8);
        entry.TryAddClick(5);

        entry.UndoLast();

        Assert.Empty(entry.AppliedMoves);
        Assert.Null(entry.SelectedSource);
        for (int i = 0; i <= 25; i++)
            Assert.Equal(initial.Points[i], entry.Current.Points[i]);
    }

    [Fact]
    public void UndoLast_WithSourceSelectedOnly_ClearsSelection_NoRollback()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8);
        entry.UndoLast();
        Assert.Null(entry.SelectedSource);
        Assert.Empty(entry.AppliedMoves);
    }

    [Fact]
    public void UndoLast_NoCommitsNoSelection_NoOp()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.UndoLast(); // should not throw
        Assert.Null(entry.SelectedSource);
        Assert.Empty(entry.AppliedMoves);
    }

    [Fact]
    public void UndoLast_AfterMultipleCommits_RollsBackOnlyLast()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8); entry.TryAddClick(5);
        entry.TryAddClick(6); entry.TryAddClick(5);
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
        entry.TryAddClick(8); entry.TryAddClick(5);
        entry.TryAddClick(6);

        entry.UndoAll();

        Assert.Empty(entry.AppliedMoves);
        Assert.Null(entry.SelectedSource);
        Assert.False(entry.IsComplete);
        Assert.Null(entry.CompletedPlay);
        for (int i = 0; i <= 25; i++)
            Assert.Equal(initial.Points[i], entry.Current.Points[i]);
        Assert.Equal(initial.HighPointOccupied, entry.Current.HighPointOccupied);
    }

    [Fact]
    public void UndoAll_AfterComplete_RestoresInitialAndAllowsReplay()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);
        entry.TryAddClick(8); entry.TryAddClick(5);
        entry.TryAddClick(6); entry.TryAddClick(5);

        entry.UndoAll();

        // Should be able to play again.
        Assert.Equal(ClickOutcome.SourceSelected, entry.TryAddClick(24));
    }

    // ── Constrained mid-game / candidate-narrowing ────────────────

    [Fact]
    public void LegalNextClicks_PostFirstCommit_MatchesGeneratePlaysFiltered()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);
        entry.TryAddClick(8); entry.TryAddClick(5);

        // Expected: union of FrPts of "remaining" moves across plays containing (8,5).
        var allPlays = MoveGenerator.GeneratePlays(initial, 3, 1);
        var expected = new HashSet<int>();
        var first = new Move(8, 5);
        foreach (var p in allPlays)
        {
            // Multiset: does p contain (8,5)?
            bool contains = false;
            for (int i = 0; i < p.Count; i++)
                if (p[i].Equals(first)) { contains = true; break; }
            if (!contains) continue;

            for (int i = 0; i < p.Count; i++)
            {
                var m = p[i];
                if (m.Equals(first)) { contains = false; continue; } // skip exactly one copy
                expected.Add(m.FrPt);
            }
        }

        Assert.Equal(expected, new HashSet<int>(entry.LegalNextClicks));
    }

    [Fact]
    public void TryAddClick_ForcedSinglePlay_LegalNextClicksAreSingleton()
    {
        // Closed-out-bar with bar entry possible only on one die.
        var s = new BoardState();
        s.Points[25] = 1;
        // Block 5 of 6 entry points; leave 22 (die 3 entry) open.
        s.Points[24] = -2; s.Points[23] = -2;
        s.Points[21] = -2; s.Points[20] = -2; s.Points[19] = -2;
        s.Points[6] = 5;
        s.RecalcHighPoint();

        // Dice (3, 1): die 3 enters at 22. Die 1 enters at 24 — blocked.
        // Whether a second move is legal after entry depends on the rest of the position.
        var entry = new MoveEntryState(s, 3, 1);
        // Bar must be the (only) source.
        Assert.Equal(new HashSet<int> { 25 }, new HashSet<int>(entry.LegalNextClicks));
        entry.TryAddClick(25);
        Assert.Contains(22, entry.LegalNextClicks);
        Assert.DoesNotContain(24, entry.LegalNextClicks);
    }

    [Fact]
    public void Current_ReflectsAppliedMoves_MidPlay()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAddClick(8); entry.TryAddClick(5);

        // Standard: Points[8]=3, Points[5]=0.
        // After 8→5: Points[8]=2, Points[5]=1.
        Assert.Equal(2, entry.Current.Points[8]);
        Assert.Equal(1, entry.Current.Points[5]);
    }
}
