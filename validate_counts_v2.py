import sys
sys.path.insert(0, r"D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgRLEngine\BgRLEngine")

from engine.state import BoardState
from engine.dice import generate_plays, _apply_move

state = BoardState.standard_setup()

for d1 in range(1, 7):
    for d2 in range(d1, 7):
        plays = generate_plays(state, d1, d2)
        
        # Dedup by final board state instead of (source, dest) pairs
        seen = set()
        unique = []
        for play in plays:
            if play.num_moves == 0:
                continue
            # Apply all moves to get final board state
            s = state.copy()
            for m in play.moves:
                s = _apply_move(s, m)
            key = tuple(s.points) + (s.bar_player, s.bar_opponent)
            if key not in seen:
                seen.add(key)
                unique.append(play)
        
        print(f"{d1}-{d2}: {len(unique)} plays")