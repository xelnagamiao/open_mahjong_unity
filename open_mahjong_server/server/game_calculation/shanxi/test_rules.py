from collections import Counter
import pytest
from . import rules as sx

BASE = [11,12,13,21,22,23,31,32,33,17,18,19]


@pytest.mark.parametrize("tile", sx.TILES)
def test_point_gates_on_actual_winning_tile(tile):
    hand = BASE + [tile, tile]
    if max(Counter(hand).values()) > 4:
        return
    for self_draw, threshold in ((True,3),(False,6)):
        result = sx.score(hand, winning_tile=tile, declared_ready=True, self_draw=self_draw)
        assert bool(result) == (sx.tile_points(tile) >= threshold)
        if result:
            assert result["tile_points"] == sx.tile_points(tile)
    assert sx.score(hand, winning_tile=tile, declared_ready=False) is None


def test_flush_dragon_and_seven_pairs_are_not_exponentiated():
    hand = [11,12,13,14,15,16,17,18,19,16,17,18,19,19]
    result = sx.score(hand, winning_tile=19, declared_ready=True)
    assert result["base_score"] == 49
    assert result["fan_names"][:2] == ["一条龙", "清一色"]


def test_luxury_seven_pairs_requires_winning_fourth_copy():
    hand = [19]*4 + [21,21,22,22,23,23,24,24,29,29]
    assert sx.score(hand, winning_tile=19, declared_ready=True)["pattern_points"] == 40
    assert sx.score(hand, winning_tile=29, declared_ready=True)["pattern_points"] == 20
    assert sx.score(hand, ["G19"], winning_tile=19, declared_ready=True) is None


def test_thirteen_orphans_is_present_in_the_books_score_table():
    hand = list(sx.ORPHANS) + [47]
    result = sx.score(hand, winning_tile=47, declared_ready=True)
    assert result["base_score"] == 70
    assert sx.waits(list(sx.ORPHANS)) == sx.ORPHANS


@pytest.mark.parametrize("melds", [["s12"],["k00"],["g99"],[None],["k11"]*5])
def test_invalid_public_melds_are_rejected(melds):
    assert not sx.shapes(BASE+[47,47],melds)


def test_known_dead_high_wait_cannot_enable_ready():
    assert sx.ready_waits(BASE+[47]) == {47}
    assert not sx.ready_waits(BASE+[47], public_tiles=[47]*3)
    assert not sx.ready_waits(BASE+[12])  # 只有低点听口
    assert sx.ready_waits(BASE+[16]) == {16,19}
    assert not sx.shapes([True]+BASE+[47])
    assert not sx.waits([47]*5+BASE[:8])


def test_all_seven_honor_types_count_as_one_book_suit():
    hand = [41,41,41,42,42,42,43,43,43,45,45,45,47,47]
    assert sx.score(hand, winning_tile=47, declared_ready=True)["base_score"] == 30


@pytest.mark.parametrize("winner,payer,ready,expected", [
    (0,1,True,[36,-12,-12,-12]),
    (1,0,True,[-12,26,-7,-7]),
    (1,2,True,[-7,21,-7,-7]),
    (0,1,False,[36,-36,0,0]),
    (1,0,False,[-26,26,0,0]),
    (1,2,False,[0,21,-21,0]),
    (0,None,False,[72,-24,-24,-24]),
    (1,None,False,[-24,52,-14,-14]),
])
def test_rulebook_ron_and_self_draw_examples(winner,payer,ready,expected):
    result = sx.payments(winner,base_score=7,discarder=payer,discarder_ready=ready)
    assert list(result["total"].values()) == expected


def test_rulebook_page_ten_full_table_kong_liability():
    result=sx.payments(0,base_score=7,discarder=1,discarder_ready=False,
                       kongs=[(0,13,False),(1,12,True),(2,19,False)])
    assert list(result["total"].values()) == [45,-72,27,0]
    assert result["liable_payer"] == 1


def test_rulebook_page_eleven_kong_example_and_draw():
    assert list(sx.payments(1,base_score=7,discarder=0,kongs=[(1,13,True)])["total"].values()) == [-44,44,0,0]
    assert sx.payments(kongs=[(1,47,True)])["total"] == dict.fromkeys(range(4),0)


@pytest.mark.parametrize("winner",range(4))
@pytest.mark.parametrize("dealer",range(4))
def test_all_seat_payment_variants_conserve_points(winner,dealer):
    for payer in [None]+[p for p in range(4) if p!=winner]:
        for ready in [False,True]:
            result=sx.payments(winner,base_score=49,dealer=dealer,discarder=payer,
                discarder_ready=ready,kongs=[(i,41+i,i%2==0) for i in range(4)])
            assert sum(result["total"].values()) == 0
            assert sum(result["win"].values()) == 0
            assert sum(result["kong"].values()) == 0
