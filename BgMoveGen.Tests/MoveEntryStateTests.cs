using BgMoveGen;
using BgDataTypes_Lib;

namespace BgMoveGen.Tests;

public class MoveEntryStateTests
{
    // ── Helpers ───────────────────────────────────────────────────

    private static BoardState ClosedOutOnBar()
    {
        var mop = new int[26];
        mop[25] = 1;
        for (int i = 19; i <= 24; i++) mop[i] = -2;
        mop[6] = 5;
        mop[1] = -2;
        mop[12] = -1; // pad opponent counts (need not be a legal game state)
        return BoardState.FromMop(mop);
    }

    private static BoardState BearOffPosition_HighFour()
    {
        // Highest = 4, in home board.
        var mop = new int[26];
        mop[4] = 5;
        mop[3] = 5;
        mop[2] = 5;
        mop[19] = -5;
        mop[17] = -5;
        mop[12] = -5;
        return BoardState.FromMop(mop);
    }

    private static BoardState BearOffPosition_HighSixWithGap()
    {
        // Highest = 6; nothing on 5; testing overshoot-not-from-highest illegality.
        var mop = new int[26];
        mop[6] = 5;
        mop[3] = 5;
        mop[1] = 5;
        mop[19] = -5;
        mop[17] = -5;
        mop[12] = -5;
        return BoardState.FromMop(mop);
    }

    private static BoardState SimpleHitPosition()
    {
        // Two-checker minimal: one player on 13, one opponent blot on 10.
        var mop = new int[26];
        mop[13] = 1;
        mop[10] = -1;
        return BoardState.FromMop(mop);
    }

    private static BoardState SingleCheckerOn(int point)
    {
        var mop = new int[26];
        mop[point] = 1;
        return BoardState.FromMop(mop);
    }

    private static BoardState BarEnterThenHit_5_4()
    {
        // Player on bar; opponent blot on 16. With dice (5,4):
        //   bar/20 (die 5) then 20/16* (die 4)   ← non-emitted ordering
        //   bar/21 (die 4) then 21/16* (die 5)   ← emitted ordering
        // Both hit the 16 blot and reach the same position, so GeneratePlays
        // emits the one play, as bar/21 21/16*.
        var mop = new int[26];
        mop[25] = 1;
        mop[16] = -1;
        return BoardState.FromMop(mop);
    }

    private static BoardState TwoCheckers(int a, int b)
    {
        var mop = new int[26];
        mop[a]++;
        mop[b]++;
        return BoardState.FromMop(mop);
    }

    /// <summary>
    /// <paramref name="completed"/> is one of the generator's candidates in
    /// the generator's own encoding — <see cref="MoveEntryState.CompletedPlay"/>'s
    /// contract, which is stronger than being the same play as one.
    /// </summary>
    private static void AssertIsGeneratedPlay(List<Play> allPlays, Play? completed)
    {
        Assert.NotNull(completed);
        Assert.Contains(allPlays, p => p.IsSameEncoding(completed!.Value));
    }

    private static int OwnOnBoard(BoardPosition position)
    {
        int total = 0;
        for (int i = 1; i <= 25; i++)
            if (position[i] > 0) total += position[i];
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

    /// <summary>
    /// No write through any interface <paramref name="exposed"/> implements
    /// succeeds: each writable collection interface it implements has every
    /// mutator throw <see cref="NotSupportedException"/>, and every other
    /// interface it implements is one with no mutators at all — so a cast
    /// finds no way to write it, and a new interface the type picks up fails
    /// here until it is checked.
    /// </summary>
    private static void AssertNoWriteSucceeds<T>(IEnumerable<T> exposed, T sample)
    {
        var checkedInterfaces = new HashSet<Type>();

        if (exposed is ICollection<T> collection)
        {
            Assert.True(collection.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => collection.Add(sample));
            Assert.Throws<NotSupportedException>(() => collection.Remove(sample));
            Assert.Throws<NotSupportedException>(() => collection.Clear());
            checkedInterfaces.Add(typeof(ICollection<T>));
        }
        if (exposed is IList<T> list)
        {
            Assert.Throws<NotSupportedException>(() => list.Insert(0, sample));
            Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => list[0] = sample);
            checkedInterfaces.Add(typeof(IList<T>));
        }
        if (exposed is ISet<T> set)
        {
            Assert.Throws<NotSupportedException>(() => set.Add(sample));
            Assert.Throws<NotSupportedException>(() => set.UnionWith([sample]));
            Assert.Throws<NotSupportedException>(() => set.IntersectWith([sample]));
            Assert.Throws<NotSupportedException>(() => set.ExceptWith([sample]));
            Assert.Throws<NotSupportedException>(() => set.SymmetricExceptWith([sample]));
            checkedInterfaces.Add(typeof(ISet<T>));
        }
        if (exposed is System.Collections.IList untyped)
        {
            Assert.True(untyped.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => untyped.Add(sample));
            Assert.Throws<NotSupportedException>(() => untyped.Insert(0, sample));
            Assert.Throws<NotSupportedException>(() => untyped.Remove(sample));
            Assert.Throws<NotSupportedException>(() => untyped.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => untyped.Clear());
            Assert.Throws<NotSupportedException>(() => untyped[0] = sample);
            checkedInterfaces.Add(typeof(System.Collections.IList));
        }

        Type[] withoutMutators =
        [
            typeof(IEnumerable<T>), typeof(System.Collections.IEnumerable),
            typeof(IReadOnlyCollection<T>), typeof(IReadOnlyList<T>), typeof(IReadOnlySet<T>),
            typeof(System.Collections.ICollection),
        ];
        foreach (var implemented in exposed.GetType().GetInterfaces())
            Assert.True(checkedInterfaces.Contains(implemented) || withoutMutators.Contains(implemented),
                $"{exposed.GetType()} implements {implemented}, which this check does not cover.");
    }

    [Fact]
    public void AppliedMoves_IsALiveView_ThatNoCastMakesWritable()
    {
        // halheinrich/backgammon#273: AppliedMoves handed out the live list
        // behind IReadOnlyList, and a cast back to List<Move> could write the
        // list IsComplete counts. It is a read-only view: held across a click
        // it reads the new move, as the list did, and no interface writes it.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        var moves = entry.AppliedMoves;
        entry.TryAdvanceFrom(8, new[] { 3, 1 });

        Assert.Equal(new Move(8, 5), Assert.Single(moves));
        Assert.IsNotType<List<Move>>(moves);
        AssertNoWriteSucceeds(moves, new Move(24, 23));

        Assert.Equal(new Move(8, 5), Assert.Single(entry.AppliedMoves));
        Assert.False(entry.IsComplete);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(6, new[] { 3, 1 }));
        Assert.Equal(2, moves.Count);
    }

    [Fact]
    public void LegalNextClicks_IsASnapshot_ThatNoCastMakesWritable()
    {
        // LegalNextClicks handed out the live set behind IReadOnlyCollection.
        // The set is rebuilt at each click, so it is a snapshot: one read
        // before a click keeps its points, a fresh read has the new ones, and
        // no interface writes either.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        var opening = entry.LegalNextClicks;
        Assert.Equal(new HashSet<int> { 24, 13, 8, 6 }, new HashSet<int>(opening));

        Assert.IsNotType<HashSet<int>>(opening);
        AssertNoWriteSucceeds(opening, 7);

        entry.TryAdvanceFrom(8, new[] { 3, 1 });   // 8/5; the 1 remains
        Assert.Equal(new HashSet<int> { 24, 13, 8, 6 }, new HashSet<int>(opening));
        Assert.Contains(5, entry.LegalNextClicks);
        Assert.DoesNotContain(5, opening);
    }

    [Fact]
    public void CurrentPosition_IsTheIntermediatePosition_AfterEachClick()
    {
        // Read after every click of a full doubles play, it is the start with
        // exactly the committed moves applied — the tests' own replay of
        // AppliedMoves — and after each undo it steps back the same way.
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 6, 6);
        Assert.Equal(BoardPosition.Standard, entry.CurrentPosition);

        foreach (int point in new[] { 24, 24, 13, 13 })
        {
            entry.TryAdvanceFrom(point, new[] { 6 });
            Assert.Equal(Replay.PositionAfter(initial, Play.Create([.. entry.AppliedMoves])), entry.CurrentPosition);
        }
        Assert.True(entry.IsComplete);

        while (entry.AppliedMoves.Count > 0)
        {
            entry.UndoLast();
            Assert.Equal(Replay.PositionAfter(initial, Play.Create([.. entry.AppliedMoves])), entry.CurrentPosition);
        }
        Assert.Equal(BoardPosition.Standard, entry.CurrentPosition);
    }

    [Fact]
    public void NoMemberHandsOutTheBoard_CurrentPositionIsAValue()
    {
        // The board the entry state assembles the play on is its own: no
        // public member returns a BoardState, and CurrentPosition is a
        // readonly struct, so what a caller reads can be read and never
        // written — a write cannot reach the entry state's board behind its
        // bookkeeping.
        var type = typeof(MoveEntryState);
        var members = type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                                      | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(members, m => m switch
        {
            System.Reflection.PropertyInfo p => p.PropertyType == typeof(BoardState),
            System.Reflection.MethodInfo mi => mi.ReturnType == typeof(BoardState),
            System.Reflection.FieldInfo f => f.FieldType == typeof(BoardState),
            _ => false,
        });

        var current = type.GetProperty(nameof(MoveEntryState.CurrentPosition))!;
        Assert.Equal(typeof(BoardPosition), current.PropertyType);
        Assert.Null(current.SetMethod);
        Assert.True(typeof(BoardPosition).IsValueType);
        Assert.Contains(typeof(BoardPosition).CustomAttributes,
            a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    }

    [Fact]
    public void ACompletedEntryOnNoGeneratedPosition_Throws_NamingThePosition()
    {
        // The acceptance rule keeps a generated position reachable, so no
        // click sequence gets here; the lookup behind CompletedPlay is asked
        // directly. Finding no generated play is a defect, not a fallback to
        // the literal clicks.
        var targets = new Dictionary<BoardPosition, Play> { [BoardPosition.Standard] = [] };
        var nowhere = BoardPosition.Nackgammon;

        var thrown = Assert.Throws<System.Diagnostics.UnreachableException>(
            () => MoveEntryState.GeneratedPlayReaching(targets, nowhere));

        Assert.Contains(nowhere.ToString(), thrown.Message);
        Assert.Equal(0, MoveEntryState.GeneratedPlayReaching(targets, BoardPosition.Standard).Count);
    }

    [Fact]
    public void Construction_CapturesInitialByCopy()
    {
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);

        initial.SetPosition(BoardPosition.Empty);

        Assert.Equal(BoardPosition.Standard, entry.CurrentPosition);
        entry.UndoAll();
        Assert.Equal(BoardPosition.Standard, entry.CurrentPosition);
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
        Assert.Equal(2, entry.CurrentPosition[8]);
        Assert.Equal(1, entry.CurrentPosition[5]);
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
        Assert.Equal(4, entry.CurrentPosition[3]);    // 5 - 1
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
    public void TryAdvanceFrom_SequenceCompletesPlay_AsAGeneratedPlay()
    {
        // Drive a full (3,1) play with one-click advances: 8/5 then 6/5.
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(8, new[] { 3, 1 }));
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(6, new[] { 3, 1 }));

        Assert.True(entry.IsComplete);
        var allPlays = MoveGenerator.GeneratePlays(initial, 3, 1);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
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
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
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
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryAdvanceFrom_BarEntry_AdvancesTheBarChecker()
    {
        var mop = new int[26];
        mop[25] = 1;
        mop[6] = 5; mop[8] = 3; mop[13] = 5; mop[24] = 1;
        mop[19] = -5; mop[17] = -3; mop[12] = -5; mop[1] = -2;
        var s = BoardState.FromMop(mop);

        var entry = new MoveEntryState(s, 3, 1);
        // Bar entries: 22 (die 3) and 24 (die 1). Prefer die 3 ⇒ enter on 22.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(25, new[] { 3, 1 }));
        Assert.Equal(0, entry.CurrentPosition[25]);
        Assert.Equal(1, entry.CurrentPosition[22]);
    }

    [Fact]
    public void TryAdvanceFrom_BearOff_AdvancingHomeCheckerBearsItOff()
    {
        var s = BearOffPosition_HighFour();
        var entry = new MoveEntryState(s, 4, 1);

        // From 4, die 4 bears off (ToPt 0); die 1 → 4/3. Prefer die 4 ⇒ bear off.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(4, new[] { 4, 1 }));
        Assert.Equal(0, entry.AppliedMoves[0].ToPt); // bore off
        Assert.Equal(4, entry.CurrentPosition[4]);    // 5 - 1
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
        Assert.Equal(0, entry.CurrentPosition[10]);
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
        Assert.Equal(2, entry.CurrentPosition[8]);
        Assert.Equal(1, entry.CurrentPosition[5]);
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
        Assert.Equal(1, entry.CurrentPosition[10]);   // player landed on 10
        Assert.Equal(-1, entry.CurrentPosition[0]);   // opponent on bar
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
        int n = MoveGenerator.SingleMoves(new BoardState(entry.CurrentPosition), 1, buf);
        for (int i = 0; i < n; i++) expected.Add(buf[i].FrPt);

        Assert.Equal(expected, new HashSet<int>(entry.LegalNextClicks));

        // Includes 5: advancing 8/5 then 5/4 yields 8/4, which GeneratePlays emits
        // as 8/7/4 — a move-list that does NOT contain the move 8/5.
        // State-based legality still admits 5 as a source.
        Assert.Contains(5, entry.LegalNextClicks);
    }

    [Fact]
    public void LegalNextClicks_OnBar_BarIsTheOnlySource_NonBarAdvanceIllegal()
    {
        var mop = new int[26];
        mop[25] = 1;
        mop[6] = 5; mop[8] = 3; mop[13] = 5; mop[24] = 1;
        mop[19] = -5; mop[17] = -3; mop[12] = -5; mop[1] = -2;
        var s = BoardState.FromMop(mop);

        var entry = new MoveEntryState(s, 3, 1);
        Assert.Equal(new HashSet<int> { 25 }, new HashSet<int>(entry.LegalNextClicks));
        // With a checker on the bar, no other point can advance.
        Assert.Equal(ClickOutcome.Illegal, entry.TryAdvanceFrom(8, new[] { 3, 1 }));
    }

    [Fact]
    public void TryAdvanceFrom_ForcedSingleEntryDie_EntersOnTheOnlyOpenPoint()
    {
        // Bar checker; 5 of 6 entry points blocked, leaving 22 (die-3 entry) open.
        var mop = new int[26];
        mop[25] = 1;
        mop[24] = -2; mop[23] = -2;
        mop[21] = -2; mop[20] = -2; mop[19] = -2;
        mop[6] = 5;
        var s = BoardState.FromMop(mop);

        var entry = new MoveEntryState(s, 3, 1);
        Assert.Equal(new HashSet<int> { 25 }, new HashSet<int>(entry.LegalNextClicks));

        // Die 1 entry (24) is blocked; preferring die 1 still enters via die 3 on 22.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(25, new[] { 1, 3 }));
        Assert.Equal(0, entry.CurrentPosition[25]);
        Assert.Equal(1, entry.CurrentPosition[22]);
    }

    [Fact]
    public void TryAdvanceFrom_Doubles_ChainSequence_TracksIntermediateState()
    {
        // Single back checker chains forward 24→21→18→… with dice (3,3,3,3).
        var mop = new int[26];
        mop[24] = 1;
        mop[2] = -2;
        mop[1] = -2;
        mop[6] = 5;
        mop[5] = 5;
        mop[4] = 4;
        mop[19] = -5; mop[17] = -3; mop[12] = -3;
        var s = BoardState.FromMop(mop);

        var entry = new MoveEntryState(s, 3, 3);
        Assert.Contains(24, entry.LegalNextClicks);

        entry.TryAdvanceFrom(24, new[] { 3 }); // 24→21
        Assert.Contains(21, entry.LegalNextClicks);

        entry.TryAdvanceFrom(21, new[] { 3 }); // 21→18
        Assert.Contains(18, entry.LegalNextClicks);
    }

    // ── Combined single-checker moves: both die orderings ─────────
    //
    // Regression: GeneratePlays emits one play per resulting position, so of the
    // equivalent die orderings (both intermediates open → same final square) it
    // keeps one. MoveEntryState must accept *either* physical path (the caller
    // picks the die via diePreference), and must complete as the generated play,
    // in its encoding, regardless of path taken.

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
        Assert.Equal(1, entry.CurrentPosition[5]);
        var allPlays = MoveGenerator.GeneratePlays(initial, 5, 1);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryAdvanceFrom_CombinedSingleChecker_NonEmittedOrdering_11to6to5_Completes()
    {
        // Same position, the OTHER ordering: 11→6 (die 5) → 5 (die 1).
        // GeneratePlays emits only 11/10/5 for this position; this path must
        // still be enterable and complete as the same generated play.
        var initial = SingleCheckerOn(11);
        var entry = new MoveEntryState(initial, 5, 1);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(11, new[] { 5, 1 }));
        Assert.Equal(6, entry.AppliedMoves[0].ToPt);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(6, new[] { 1, 5 }));

        Assert.True(entry.IsComplete);
        Assert.Equal(1, entry.CurrentPosition[5]);
        var allPlays = MoveGenerator.GeneratePlays(initial, 5, 1);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryAdvanceFrom_CombinedSingleChecker_BothOrderings_YieldTheSameGeneratedEncoding()
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
        // The identical generated encoding, whichever intermediate path the user took.
        Assert.True(viaTen.CompletedPlay!.Value.IsSameEncoding(viaSix.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_CombinedSingleChecker_FullBoard_5_1_BothOrderings()
    {
        // Full-board reconstruction of the reported 5-1 bug. A back checker on 11
        // can play 11/5 as a combined move; both intermediates (10 and 6) are open,
        // so GeneratePlays emits a single ordering.
        var mop = new int[26];
        mop[24] = 2;
        mop[13] = 4;
        mop[11] = 1;
        mop[8] = 3;
        mop[6] = 5;
        mop[19] = -5;
        mop[17] = -3;
        mop[12] = -5;
        mop[1] = -2;
        var s = BoardState.FromMop(mop);

        var allPlays = MoveGenerator.GeneratePlays(s, 5, 1);

        // Path A: 11→10→5
        var a = new MoveEntryState(s, 5, 1);
        a.TryAdvanceFrom(11, new[] { 1, 5 });
        a.TryAdvanceFrom(10, new[] { 5, 1 });
        Assert.True(a.IsComplete);
        AssertIsGeneratedPlay(allPlays, a.CompletedPlay);

        // Path B: 11→6→5 (the ordering GeneratePlays did not emit)
        var b = new MoveEntryState(s, 5, 1);
        Assert.Equal(ClickOutcome.MoveCommitted, b.TryAdvanceFrom(11, new[] { 5, 1 }));
        Assert.Equal(ClickOutcome.PlayCompleted, b.TryAdvanceFrom(6, new[] { 1, 5 }));
        Assert.True(b.IsComplete);
        AssertIsGeneratedPlay(allPlays, b.CompletedPlay);

        Assert.True(a.CompletedPlay!.Value.IsSameEncoding(b.CompletedPlay!.Value));
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
        Assert.Equal(1, entry.CurrentPosition[16]);   // player landed on 16
        Assert.Equal(-1, entry.CurrentPosition[0]);   // opponent sent to bar
        var allPlays = MoveGenerator.GeneratePlays(initial, 5, 4);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryAdvanceFrom_BarEnterThenHit_EmittedOrdering_bar21_21to16_Completes()
    {
        var initial = BarEnterThenHit_5_4();
        var entry = new MoveEntryState(initial, 5, 4);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(25, new[] { 4, 5 })); // bar/21 (die 4)
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(21, new[] { 5, 4 })); // 21/16* hit

        Assert.True(entry.IsComplete);
        Assert.Equal(-1, entry.CurrentPosition[0]);
        var allPlays = MoveGenerator.GeneratePlays(initial, 5, 4);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    // ── Edge cases: blocked / blot intermediates, doubles permutations ──

    [Fact]
    public void TryAdvanceFrom_BlockedIntermediate_OnlyOneOrderingLegal_StillWorks()
    {
        // Checker on 11, dice (5,1), opponent owns 10 (≥2). The die-1-first ordering
        // 11→10 is blocked, so only 11→6→5 is legal. Preferring die 1 still advances
        // via the only legal move (11→6).
        var mop = new int[26];
        mop[11] = 1;
        mop[10] = -2;
        var s = BoardState.FromMop(mop);

        var entry = new MoveEntryState(s, 5, 1);
        Assert.Contains(11, entry.LegalNextClicks);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(11, new[] { 1, 5 }));
        Assert.Equal(6, entry.AppliedMoves[0].ToPt); // 11→6 (die 5), not the blocked 11→10
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(6, new[] { 1, 5 }));

        var allPlays = MoveGenerator.GeneratePlays(s, 5, 1);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryAdvanceFrom_BlotIntermediate_OrderingsDiffer_RemainDistinct()
    {
        // Checker on 11, dice (5,1), opponent BLOT on 10.
        //   11→10*(die 1, hit) → 5 (die 5)  — sends opponent to bar
        //   11→6 (die 5) → 5 (die 1)        — leaves the 10 blot untouched
        // Different final states ⇒ GeneratePlays keeps BOTH, and the two paths must
        // yield distinct (non-equal) completed plays.
        var mop = new int[26];
        mop[11] = 1;
        mop[10] = -1; // blot
        var s = BoardState.FromMop(mop);

        var allPlays = MoveGenerator.GeneratePlays(s, 5, 1);

        var hitPath = new MoveEntryState(s, 5, 1);
        hitPath.TryAdvanceFrom(11, new[] { 1, 5 }); // 11→10* (hit)
        hitPath.TryAdvanceFrom(10, new[] { 5, 1 }); // 10→5
        Assert.True(hitPath.IsComplete);
        Assert.Equal(-1, hitPath.CurrentPosition[0]); // opponent on bar
        AssertIsGeneratedPlay(allPlays, hitPath.CompletedPlay);

        var noHitPath = new MoveEntryState(s, 5, 1);
        noHitPath.TryAdvanceFrom(11, new[] { 5, 1 }); // 11→6
        noHitPath.TryAdvanceFrom(6, new[] { 1, 5 });  // 6→5
        Assert.True(noHitPath.IsComplete);
        Assert.Equal(-1, noHitPath.CurrentPosition[10]); // blot survives
        AssertIsGeneratedPlay(allPlays, noHitPath.CompletedPlay);

        // Genuinely different outcomes — must NOT be the same play.
        Assert.False(s.IsSamePlay(hitPath.CompletedPlay!.Value, noHitPath.CompletedPlay!.Value));
    }

    [Fact]
    public void TryAdvanceFrom_Doubles_CombinedMove_InterleavedOrdering_Completes()
    {
        // Two checkers on 6, dice (2,2): 6/2(2) via 6→4→2 per checker. Enter in an
        // interleaved order (first checker all the way, then second).
        var mop = new int[26];
        mop[6] = 2;
        var s = BoardState.FromMop(mop);

        var entry = new MoveEntryState(s, 2, 2);
        entry.TryAdvanceFrom(6, new[] { 2 }); // 6→4 (checker A)
        entry.TryAdvanceFrom(4, new[] { 2 }); // 4→2 (checker A all the way)
        entry.TryAdvanceFrom(6, new[] { 2 }); // 6→4 (checker B)
        Assert.False(entry.IsComplete);
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(4, new[] { 2 })); // 4→2 (checker B)

        Assert.True(entry.IsComplete);
        Assert.Equal(2, entry.CurrentPosition[2]);
        var allPlays = MoveGenerator.GeneratePlays(s, 2, 2);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
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
        Assert.Equal(initial.ToPosition(), entry.CurrentPosition);
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
        Assert.Equal(initial.ToPosition(), entry.CurrentPosition);

        // The reset board plays on exactly as a fresh one would: the high
        // point it scans from came back with the position.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryAdvanceFrom(24, new[] { 3, 1 }));
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

        Assert.Equal(2, OwnOnBoard(entry.CurrentPosition)); // before

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryBearOffMax());
        Assert.True(entry.IsComplete);
        Assert.Equal(0, OwnOnBoard(entry.CurrentPosition)); // both borne off → max = 2

        var allPlays = MoveGenerator.GeneratePlays(initial, 2, 1);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryBearOffMax_TieForMax_ReturnsIllegal_NoStateChange()
    {
        // Checkers on 6, 3, 2 with dice (6,1). die6 bears off the 6 (exact); die1
        // cannot bear off (point 1 empty, 6 isn't reachable-off by a 1). So every
        // completion bears off exactly one checker, but the die-1 move (3/2 vs 2/1)
        // leaves two DIFFERENT boards — a tie for the maximum ⇒ ambiguous ⇒ no-op.
        var mop = new int[26];
        mop[6] = 1; mop[3] = 1; mop[2] = 1;
        var s = BoardState.FromMop(mop);
        var entry = new MoveEntryState(s, 6, 1);

        var before = entry.CurrentPosition;
        Assert.Equal(ClickOutcome.Illegal, entry.TryBearOffMax());

        Assert.Empty(entry.AppliedMoves);
        Assert.False(entry.IsComplete);
        Assert.Equal(before, entry.CurrentPosition);
    }

    [Fact]
    public void TryBearOffMax_NoBearOffPossible_ReturnsIllegal_NoStateChange()
    {
        // Standard (3,1) opener: nothing is anywhere near bearing off.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);

        var before = entry.CurrentPosition;
        Assert.Equal(ClickOutcome.Illegal, entry.TryBearOffMax());

        Assert.Empty(entry.AppliedMoves);
        Assert.False(entry.IsComplete);
        Assert.Equal(before, entry.CurrentPosition);
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
        Assert.Equal(1, OwnOnBoard(entry.CurrentPosition)); // only the 1-checker remains

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryBearOffMax());
        Assert.True(entry.IsComplete);
        Assert.Equal(0, OwnOnBoard(entry.CurrentPosition));

        var allPlays = MoveGenerator.GeneratePlays(initial, 6, 1);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryBearOffMax_Doubles_BearsOffMultiple_WhenUnique()
    {
        // Checker on 4 and two on 2, dice (2,2). The four 2s exactly clear the board
        // (4→2→0 consumes two, each 2-checker one), so the only completion bears off
        // all three. Unique max ⇒ tray clears the board.
        var mop = new int[26];
        mop[4] = 1; mop[2] = 2;
        var s = BoardState.FromMop(mop);
        var entry = new MoveEntryState(s, 2, 2);

        Assert.Equal(3, OwnOnBoard(entry.CurrentPosition));
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryBearOffMax());
        Assert.True(entry.IsComplete);
        Assert.Equal(0, OwnOnBoard(entry.CurrentPosition)); // 3 borne off

        var allPlays = MoveGenerator.GeneratePlays(s, 2, 2);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
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

        Assert.Equal(2, OwnOnBoard(entry.CurrentPosition)); // before

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryBearOffMax());
        Assert.True(entry.IsComplete);
        Assert.Equal(1, OwnOnBoard(entry.CurrentPosition)); // dropped by 1 — outlier borne off

        Assert.Equal(1, BearOffCount(entry.CompletedPlay!.Value)); // exactly one ToPt == 0
        var allPlays = MoveGenerator.GeneratePlays(initial, 6, 2);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
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
        var before = entry.CurrentPosition;

        Assert.Equal(ClickOutcome.Illegal, entry.TryBearOffMax());

        Assert.Empty(entry.AppliedMoves);
        Assert.False(entry.IsComplete);
        Assert.Equal(before, entry.CurrentPosition);
    }

    // ── One-click make-the-point (TryMakePoint) ───────────────────
    //
    // Click a DESTINATION point to land two own checkers on it. Make (fewest
    // sub-moves) is attempted before the move-one fallback. Own-occupied points are
    // advance sources, never make destinations, so they are rejected up front.

    [Fact]
    public void TryMakePoint_NonDoubles_MakesPoint_FromBothDice_Completes()
    {
        // Standard (3,1), click the empty 5-point: 8/5 (die 3) + 6/5 (die 1). Both
        // dice consumed ⇒ the play completes. Make is unique (5 is reachable in one
        // die only from 8-via-3 and 6-via-1).
        var initial = BoardState.Standard();
        var entry = new MoveEntryState(initial, 3, 1);

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryMakePoint(5));
        Assert.True(entry.IsComplete);
        Assert.Equal(2, entry.CurrentPosition[5]); // point made
        Assert.Equal(2, entry.CurrentPosition[8]); // 3 - 1
        Assert.Equal(4, entry.CurrentPosition[6]); // 5 - 1

        // The completed play is the generator's own candidate, whatever the click path.
        var allPlays = MoveGenerator.GeneratePlays(initial, 3, 1);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryMakePoint_HitAndCover_OverOpponentBlot_Completes()
    {
        // Own on 8 and 6, opponent blot on 5, dice (3,1). 8/5* hits and 6/5 covers;
        // the make lands the point over the blot, sending the opponent to the bar.
        var mop = new int[26];
        mop[8] = 1;
        mop[6] = 1;
        mop[5] = -1; // opponent blot
        var s = BoardState.FromMop(mop);
        var entry = new MoveEntryState(s, 3, 1);

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryMakePoint(5));
        Assert.True(entry.IsComplete);
        Assert.Equal(2, entry.CurrentPosition[5]);  // two own checkers cover the point
        Assert.Equal(-1, entry.CurrentPosition[0]); // opponent sent to the bar

        // The arrival onto the blot is generated as a hit (negative ToPt) — the sign
        // encoding stays internal; the click was the positive point 5.
        Assert.Contains(entry.AppliedMoves, m => m.ToPt == -5);

        var allPlays = MoveGenerator.GeneratePlays(s, 3, 1);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryMakePoint_Doubles_TwoSubMoveMake_LeavesTwoDice_ThenCompletes()
    {
        // Two back checkers on 24, dice (4,4). Making the 20-point is the minimal
        // (two-sub-move) placement 24/20 24/20, leaving two 4s for the user.
        var mop = new int[26];
        mop[24] = 2;
        var s = BoardState.FromMop(mop);
        var entry = new MoveEntryState(s, 4, 4);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryMakePoint(20));
        Assert.False(entry.IsComplete);
        Assert.Equal(2, entry.AppliedMoves.Count); // exactly two sub-moves
        Assert.Equal(2, entry.CurrentPosition[20]); // point made
        Assert.Equal(0, entry.CurrentPosition[24]); // both back checkers advanced

        // Two 4s remain — the play then completes via one-click advances.
        entry.TryAdvanceFrom(20, new[] { 4 }); // 20/16
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(20, new[] { 4 })); // 20/16
        Assert.True(entry.IsComplete);

        var allPlays = MoveGenerator.GeneratePlays(s, 4, 4);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryMakePoint_Doubles_ThreeSubMoveMake_LeavesOneDie()
    {
        // Own on 12 (= P+d) and 14 (= P+2d), dice (2,2), make the 10-point. No
        // two-sub-move make exists (only one checker is one die from 10), so the
        // minimal make is three sub-moves: 12/10 plus 14/12/10. One die left over.
        var mop = new int[26];
        mop[12] = 1;
        mop[14] = 1;
        var s = BoardState.FromMop(mop);
        var entry = new MoveEntryState(s, 2, 2);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryMakePoint(10));
        Assert.False(entry.IsComplete);
        Assert.Equal(3, entry.AppliedMoves.Count); // three sub-moves
        Assert.Equal(2, entry.CurrentPosition[10]); // point made
        Assert.Equal(0, entry.CurrentPosition[14]); // outlier brought all the way in
        Assert.Equal(0, entry.CurrentPosition[12]);

        // One 2 remains — completes with a final advance off the made point.
        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryAdvanceFrom(10, new[] { 2 }));
        Assert.True(entry.IsComplete);

        var allPlays = MoveGenerator.GeneratePlays(s, 2, 2);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryMakePoint_Doubles_FourSubMoveMake_SingleConfig_Completes()
    {
        // Two checkers on 8 (= P+2d for d=2), dice (2,2), make the 4-point. The only
        // make is the four-sub-move {2,2} config 8/6/4 8/6/4 — no shorter make exists
        // (no checker is one or two dice from 4 except via 8), and the rival {1,3}
        // config (a checker on 6 and one on 10) is absent. Single config ⇒ commits.
        var mop = new int[26];
        mop[8] = 2;
        var s = BoardState.FromMop(mop);
        var entry = new MoveEntryState(s, 2, 2);

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryMakePoint(4));
        Assert.True(entry.IsComplete);
        Assert.Equal(4, entry.AppliedMoves.Count); // four sub-moves, all dice consumed
        Assert.Equal(2, entry.CurrentPosition[4]);  // point made
        Assert.Equal(0, entry.CurrentPosition[8]);

        var allPlays = MoveGenerator.GeneratePlays(s, 2, 2);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryMakePoint_Unmakeable_OneCheckerLands_MoveOneFallback()
    {
        // A single checker on 8, dice (3,1), click the empty 5-point. The point is
        // unmakeable (one checker cannot put two on it), so the move-one fallback
        // commits the single sub-move that lands on 5: 8/5 (die 3). Die 1 remains.
        var mop = new int[26];
        mop[8] = 1;
        var s = BoardState.FromMop(mop);
        var entry = new MoveEntryState(s, 3, 1);

        // Only die 3 reaches 5 in one step, so the landing is unique.
        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryMakePoint(5));
        Assert.Single(entry.AppliedMoves);
        Assert.Equal(8, entry.AppliedMoves[0].FrPt);
        Assert.Equal(5, entry.AppliedMoves[0].ToPt); // die 3, single landing on the point
        Assert.Equal(1, entry.CurrentPosition[5]);
        Assert.False(entry.IsComplete);
    }

    [Fact]
    public void TryMakePoint_OwnOccupiedPoint_ReturnsIllegal_NoStateChange()
    {
        // Standard (3,1): 6 holds own checkers — an advance source, never a make
        // destination. Rejected without inspecting reachability.
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        var before = entry.CurrentPosition;

        Assert.Equal(ClickOutcome.Illegal, entry.TryMakePoint(6));
        Assert.Empty(entry.AppliedMoves);
        Assert.Equal(before, entry.CurrentPosition);
    }

    [Fact]
    public void TryMakePoint_UnmakeableAndUnreachable_ReturnsIllegal()
    {
        // Standard (3,1): the 1-point is below the reach floor — the nearest own
        // checker (6) covers at most 4 pips (3+1), landing on 2 — so no checker
        // reaches 1 by any single- or combined-die path, and it cannot be made.
        // Neither make nor land-one has a candidate. (The 4-point would now land via
        // the combined 8/7/4, so the genuinely-unreachable point moved to 1.)
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        var before = entry.CurrentPosition;

        Assert.Equal(ClickOutcome.Illegal, entry.TryMakePoint(1));
        Assert.Empty(entry.AppliedMoves);
        Assert.Equal(before, entry.CurrentPosition);
    }

    [Fact]
    public void TryMakePoint_AfterComplete_ReturnsIllegal()
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        entry.TryAdvanceFrom(8, new[] { 3, 1 });
        entry.TryAdvanceFrom(6, new[] { 3, 1 });
        Assert.True(entry.IsComplete);

        Assert.Equal(ClickOutcome.Illegal, entry.TryMakePoint(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(-1)]
    [InlineData(26)]
    public void TryMakePoint_OutOfRange_ReturnsIllegal(int point)
    {
        var entry = new MoveEntryState(BoardState.Standard(), 3, 1);
        Assert.Equal(ClickOutcome.Illegal, entry.TryMakePoint(point));
        Assert.Empty(entry.AppliedMoves);
    }

    // ── Land-one fallback via a combined (multi-die) path ─────────
    //
    // The destination can't be made and has no single-die landing, but one checker
    // reaches it by walking two dice. The make-first/else-land-one search lands that
    // checker via the minimal combined path. Modelled on the live repro XGID
    // aAB-CCCA--a-b----cBc-b-ba-:…:62 (only legal hit 18/16 16/10*).

    private static BoardState CombinedHitRepro(int blotPoint)
    {
        // Own checker on 18; opponent anchor on 12 blocks the 18/12 (die-6) start, so
        // the only route to 10 is 18/16 (die 2) then 16/10 (die 6). blotPoint carries
        // the opponent blot (10 for the hit case, 0 to leave the point empty).
        var mop = new int[26];
        mop[18] = 1;
        mop[12] = -2;        // opponent anchor — blocks 18→12
        if (blotPoint != 0) mop[blotPoint] = -1;
        return BoardState.FromMop(mop);
    }

    [Fact]
    public void TryMakePoint_CombinedPathHit_LandsViaTwoDice_Completes()
    {
        // Repro: blot on 10, dice (6,2). No make (one checker), no single-die landing
        // on 10, but 18/16 16/10* lands it. Without the combined-path fallback this
        // no-ops; with it the play completes (both dice consumed) on the hit.
        var s = CombinedHitRepro(blotPoint: 10);
        var entry = new MoveEntryState(s, 6, 2);

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryMakePoint(10));
        Assert.True(entry.IsComplete);
        Assert.Equal(1, entry.CurrentPosition[10]); // own checker landed
        Assert.Equal(-1, entry.CurrentPosition[0]); // opponent blot sent to the bar

        var allPlays = MoveGenerator.GeneratePlays(s, 6, 2);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryMakePoint_CombinedPathEmptyPoint_LandsViaTwoDice_Completes()
    {
        // Same geometry, point 10 empty (no blot): 18/16 16/10 lands one checker with
        // no hit. Empty point and opponent blot are symmetric for the fallback.
        var s = CombinedHitRepro(blotPoint: 0);
        var entry = new MoveEntryState(s, 6, 2);

        Assert.Equal(ClickOutcome.PlayCompleted, entry.TryMakePoint(10));
        Assert.True(entry.IsComplete);
        Assert.Equal(1, entry.CurrentPosition[10]); // own checker landed
        Assert.Equal(0, entry.CurrentPosition[0]);  // nothing hit

        var allPlays = MoveGenerator.GeneratePlays(s, 6, 2);
        AssertIsGeneratedPlay(allPlays, entry.CompletedPlay);
    }

    [Fact]
    public void TryMakePoint_CombinedPathUnreachable_ReturnsIllegal()
    {
        // Repro position, click the 24-point: the lone checker on 18 only moves toward
        // home, so 24 is unreachable by any path. Neither make nor land-one applies.
        var s = CombinedHitRepro(blotPoint: 10);
        var entry = new MoveEntryState(s, 6, 2);
        var before = entry.CurrentPosition;

        Assert.Equal(ClickOutcome.Illegal, entry.TryMakePoint(24));
        Assert.Empty(entry.AppliedMoves);
        Assert.Equal(before, entry.CurrentPosition);
    }

    [Fact]
    public void TryMakePoint_LandOne_PrefersSingleDieOverCombinedPath()
    {
        // Own on 12 (one die from 10 via the 2) and on 18 (reaches 10 only by a
        // combined two-die path), dice (6,2). The point is unmakeable — putting both
        // on 10 needs three die-steps but only two dice are available — so the
        // fallback lands one checker, and the minimal-depth (single-die 12/10) landing
        // is preferred over the deeper 18-combined one, leaving the larger die.
        var mop = new int[26];
        mop[12] = 1;
        mop[18] = 1;
        var s = BoardState.FromMop(mop);
        var entry = new MoveEntryState(s, 6, 2);

        Assert.Equal(ClickOutcome.MoveCommitted, entry.TryMakePoint(10));
        Assert.Single(entry.AppliedMoves);
        Assert.Equal(12, entry.AppliedMoves[0].FrPt); // depth-1 landing chosen
        Assert.Equal(10, entry.AppliedMoves[0].ToPt); // die 2
        Assert.Equal(1, entry.CurrentPosition[10]);
        Assert.Equal(0, entry.CurrentPosition[12]);
        Assert.False(entry.IsComplete); // the 6 remains
    }
}
