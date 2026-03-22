import sys
sys.path.insert(0, r"D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgRLEngine\BgRLEngine")

from engine.state import BoardState
from engine.dice import generate_plays

state = BoardState.standard_setup()

for d1 in range(1, 7):
    for d2 in range(d1, 7):
        plays = generate_plays(state, d1, d2)
        non_empty = [p for p in plays if p.num_moves > 0]
        print(f"{d1}-{d2}: {len(non_empty)} plays")