from collections import Counter

import pytest
from ...game_calculation.jokers import ORPHANS, TILES
from ...game_calculation.sanhua import rules
from ...game_calculation.sanhua.wall import deal, special_tiles
from ...room.new_chinese_room import NewChineseRoomValidator
from .SanhuaGameState import SanhuaGameState
from .bot import choose


def state():
    table = SanhuaGameState()
    table.initialize_round()
    return table


def test_opening_stack_and_indicator_identity():
    deck = [t for t in TILES for _ in range(4)]
    for seat in range(4):
        for a in range(1, 7):
            for b in range(1, 7):
                hands, wall, marker, indicator, _ = deal(3, seat, wall=deck, dice=(a,b))
                assert len(wall) == 83 and len(hands[seat]) == 14
                assert all(len(hands[i]) == 13 for i in range(4) if i != seat)
                assert Counter(sum(hands, []) + wall) == Counter(deck)
                assert wall[marker] == indicator == deck[34 * ((seat+a+b-1) % 4)]
                assert hands[seat][-1] == deck[(34*((seat+a+b-1)%4)+2*min(a,b)+52)%136]


@pytest.mark.parametrize("face,expected", [(19,(19,11)),(29,(29,21)),(39,(39,31)),(44,(44,41)),
                                           (45,(46,47)),(46,(46,47)),(47,(47,46))])
def test_indicator(face, expected):
    assert special_tiles(face) == expected


def test_room_contract():
    room = NewChineseRoomValidator(room_name="测试", sub_rule=rules.SUB_RULE)
    assert room.hepai_limit == 4 and room.detailed_config == {"rule_version": rules.VERSION}
    with pytest.raises(ValueError):
        NewChineseRoomValidator(room_name="测试", sub_rule=rules.SUB_RULE, hepai_limit=8)


def test_pair_payments():
    hand = [11,12,13,21,22,23,34,35,36,12,12]
    table = [["开口番","暗杠"], ["开口番"], ["暗杠"], []]
    def price(payer):
        return rules.score(hand, ["s16"], 27, 0, payer, table)
    assert price(None)["changes"] == [30,-10,-20,0]
    assert price(1)["changes"] == [10,-10,0,0]
    assert price(3) is None


def test_orphans_fixed_even_with_gold_and_liability():
    hand = list(ORPHANS) + [11]
    for payer in (None,1,2,3):
        result = rules.score(hand, [], 27, 0, payer, [["暗杠"]*3]*4, rob=payer is not None)
        assert result["changes"] == [600,-200,-200,-200]


def test_auto_cut_draws_only_and_full_record():
    s = state()
    for p in s.player_list:
        p.user_id = 0
    s.start_game_recording()
    s.start_round_recording()
    w = s.open_action_window(s.opening_window())
    for _ in range(220):
        if s.round_settlement is not None:
            break
        responses = {i:choose(s,i,a) for i,a in w["actions"].items() if a}
        for i, d in responses.items():
            if d["action_type"] == "cut":
                hand = s.player_list[i].hand_tiles
                expected = next(tile for tile in reversed(hand) if tile in s.cut_tiles(i))
                assert d["TileId"] == expected and d["cutClass"] == (hand[-1] == expected)
        w = s.apply_action_results(w,responses)
    assert s.ended_by == "wall" and s.round_changes == [0]*4
    s.finalize_round_recording()
    assert s.build_final_settlement_payload(0)["game_info"]["sub_rule"] == rules.SUB_RULE
    assert s.game_record["game_round"]["round_index_1"]["sanhua"]["indicator"] == s.indicator
