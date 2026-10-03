from collections import Counter
import random

import pytest

from . import rules as r


READY = [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45, 45, 19]


def test_ordinary_hand_has_no_wildcard():
    assert r.waits(READY) == {19}
    assert r.score(READY + [19], winning_tile=19)["fan"] == 2
    # One-bamboo cannot complete a pair of 9m in an ordinary hand.
    assert r.score(READY + [31], winning_tile=31) is None


def test_terminal_and_three_suit_declaration_requirements():
    no_terminal = [12,13,14,22,23,24,32,33,34,15,15,15,19]
    assert r.waits(no_terminal, declaration=True) == {19}
    missing_terminal = [12,13,14,22,23,24,32,33,34,16,16,16,18]
    assert not r.waits(missing_terminal, declaration=True)
    assert not r.score(missing_terminal + [18], winning_tile=18)
    missing_terminal = [12,13,22,23,24,32,33,34,16,16,16,28,28]
    assert r.waits(missing_terminal) == {11}
    assert not r.waits(missing_terminal, declaration=True)
    assert r.score(missing_terminal+[11], winning_tile=11)
    assert not r.shapes([11,12,13,14,15,16,21,22,23,25,25,25,29,29])


def test_requires_triplet_dragon_pair_or_seven_pairs():
    assert not r.score([11,12,13,14,15,16,21,22,23,31,32,33,19,19], winning_tile=19)
    assert r.score([11,12,13,14,15,16,21,22,23,31,32,33,45,45], winning_tile=45)
    shanpon = [11,12,13,21,22,23,31,32,33,19,19,29,29]
    assert r.waits(shanpon, declaration=True) == {19,29}


@pytest.mark.parametrize("hand,win,melds",[
    ([25,27,19,19],26,["k15","k16","k39"]),
    ([21,22,23,24,25,16,16],23,["k15","k39"]),
    ([21,22,23,24],24,["k15","k39","k16"]),
])
def test_pdf_page10_examples_1_2_3(hand,win,melds):
    detail = r.score(hand+[win], melds, winning_tile=win)
    assert detail["fan_names"] == ["夹/单吊/单和"]


def test_pdf_page10_piao_singleton_is_not_counted_twice():
    detail = r.score([19,19], ["k24","k29","k39","k38"], winning_tile=19)
    assert detail["fan"] == 2 and detail["fan_names"] == ["飘和"]
    assert r.payments(0, detail["fan"])[0] == {0:48,1:-16,2:-16,3:-16}


def test_pdf_page10_seven_pairs_baozhuang_40_and_luxury_definition():
    hand = [24,24,19,28,28,29,29,37,37,38,38,39,39]
    detail = r.score(hand+[19], winning_tile=19)
    assert detail["fan_names"] == ["七对"]
    assert r.payments(1,3,discarder=2,bao_seen=False)[0] == {0:0,1:40,2:-40,3:0}
    quad = [11]*4+[22]*2+[33]*2+[19]*2+[29]*2+[39]*2
    assert r.score(quad,winning_tile=11)["fan_names"] == ["豪华七对"]
    assert r.score(quad,winning_tile=39)["fan_names"] == ["七对"]


@pytest.mark.parametrize("winner,discarder",[(w,d) for w in range(4) for d in [None,*range(4)] if w!=d])
def test_payment_cap_and_zero_sum(winner,discarder):
    delta, parts = r.payments(winner,99,discarder=discarder)
    assert sum(delta.values()) == 0 and delta[winner] == 192
    assert len(parts)==3 and all(p["score"]==64 for p in parts)
    if discarder is not None:
        delta,_ = r.payments(winner,99,discarder=discarder,bao_seen=False)
        assert delta[discarder]==-192


@pytest.mark.parametrize("kind,tile,amount",[("direct",11,1),("added",21,2),("concealed",31,4),
    ("concealed",44,2),("special",None,1),("special_added",31,1)])
def test_kong_payments(kind,tile,amount):
    assert r.kong_payments(2,kind,tile)=={0:-amount,1:-amount,2:3*amount,3:-amount}


def test_bao_types_and_represented_scoring_do_not_change_physical_hand():
    assert r.score(READY+[19],winning_tile=19,bao="chong")["fan_names"][-1]=="冲宝"
    hand=READY+[47]
    detail=r.score(hand,winning_tile=47,bao="mo")
    assert detail["represented_win"]==19 and detail["fan"]==3
    assert hand==READY+[47]
    assert not r.score([12]*14,winning_tile=12,bao="mo")


def test_special_kongs_preserve_three_distinct_logical_and_physical_supply():
    codes=r.initial_special_candidates([11,21,31,31,41,42,45,46])
    assert "Cyao:11,21,31:11,21,31" in codes
    assert "Cwind:41,42,31:41,42,43" in codes
    assert "Cdragon:45,46,31:45,46,47" in codes
    code="Cwind:41,42,31:41,42,43"
    added=r.add_special(code,31,44)
    meld=r.parse_meld(added)
    assert meld.physical==(41,42,31,31) and meld.logical==(41,42,43,44)
    assert not any(c.startswith("Cwind") for c in r.initial_special_candidates([41,42,31],[code]))
    assert len(r.initial_special_candidates([31,31,31])) >= 4


def test_special_is_one_meld_and_fulfils_logical_suits():
    code="Cyao:31,31,31:11,21,31"
    hand=[14,15,16,17,18,19,45,45,45,47,47]
    detail=r.score(hand,[code],winning_tile=47)
    assert detail and "门清" not in detail["fan_names"]
    assert not r.valid_hand(hand,[code,code],complete=True)


@pytest.mark.parametrize("code",["",None,"Cbogus:11,21,31:11,21,31","Cyao:11,21:11,21",
    "Cyao:11,21,22:11,21,31","Cwind:41,41,31:41,41,42","Cwind:31,31,31:41,42,45",
    "Cyao:31,31,31,31,31:11,21,31,11,21","n00"])
def test_invalid_melds_fail_closed(code):
    with pytest.raises(ValueError): r.parse_meld(code)
    assert not r.waits(READY,[code])


@pytest.mark.parametrize("hand",[[11]*14,[True]*14,[0]*14,[11]*12,READY+[99]])
def test_invalid_physical_inputs(hand):
    assert not r.shapes(hand)


def test_independent_recursive_oracle_seed_2024():
    # Independent complete-hand recursion, not the production cached splitter.
    def parts(tiles,left):
        if not tiles: return left==0
        if left<=0: return False
        tile=min(tiles)
        for group in ((tile,)*3,(tile,tile+1,tile+2)):
            if group[1]!=tile and (tile>=40 or tile%10>7): continue
            remaining=list(tiles)
            try:
                for t in group: remaining.remove(t)
            except ValueError: continue
            if parts(remaining,left-1): return True
        return False
    rng=random.Random(2024)
    for _ in range(400):
        hand=rng.sample([t for t in r.TILES for _ in range(4)],14)
        if not r._qualifies_tiles(hand,()): continue
        counts=Counter(hand)
        shape=all(c%2==0 for c in counts.values())
        for pair,count in counts.items():
            if count<2: continue
            rest=list(hand);rest.remove(pair);rest.remove(pair)
            # Presence of a natural triplet is necessary but not sufficient;
            # filter all-sequence hands separately with a dragon pair.
            if pair in r.DRAGONS:
                shape |= parts(rest,4)
            else:
                for pung,n in Counter(rest).items():
                    if n>=3:
                        reduced=list(rest)
                        for j in range(3): reduced.remove(pung)
                        shape |= parts(reduced,3)
        assert bool(r.shapes(hand))==shape,hand
