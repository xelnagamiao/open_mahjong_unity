"""Independent invariants: suit permutation/rank reflection cannot alter points."""
from itertools import permutations
from dataclasses import replace
import pytest
from .rules import score_hand,waiting_tiles,WinContext
from .test_rules import HAND

@pytest.mark.parametrize('suits',list(permutations((1,2,3))))
@pytest.mark.parametrize('reflect',[False,True])
def test_suit_permutations_and_rank_reflection_preserve_all_five_groups(suits,reflect):
    def tile(t):return suits[t//10-1]*10+(10-t%10 if reflect else t%10)
    def meld(m):return m[0]+str(tile(int(m[1:])))
    examples=[
        (HAND,[],WinContext()),
        (HAND[:-2]+[39,39],[],WinContext()),
        (HAND[:12]+[39]*3+[29,29],[],WinContext()),
        (HAND[:-2]+[39,39],[],WinContext(youjin=2)),
        ([23,23],['k11','k21','k31','k18','k28'],WinContext()),
        ([11,12,13,14,15,16,17,18,19,11,11,11,14,14,14,19,19],[],WinContext()),
    ]
    for hand,melds,context in examples:
        original=score_hand(hand,melds,jin=39,context=context)
        transformed=score_hand([tile(t) for t in hand],[meld(m) for m in melds],jin=tile(39),context=context)
        assert original is not None and transformed is not None
        assert original['points']==transformed['points']
        assert {r['id'] for r in original['items']}=={r['id'] for r in transformed['items']}
        assert {tile(t) for t in waiting_tiles(hand[:-1],melds,jin=39)}==set(waiting_tiles([tile(t) for t in hand[:-1]],[meld(m) for m in melds],jin=tile(39)))
