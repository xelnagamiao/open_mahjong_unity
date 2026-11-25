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
    deck = sorted(TILES, reverse=True) * 4
    for seat in range(4):
        for a in range(1, 7):
            for b in range(1, 7):
                hands, wall, marker, indicator, _ = deal(3, seat, wall=deck, dice=(a,b))
                assert len(wall) == 83 and len(hands[seat]) == 14
                assert all(len(hands[i]) == 13 for i in range(4) if i != seat)
                assert Counter(sum(hands, []) + wall) == Counter(deck)
                assert wall[marker] == indicator == deck[34 * ((seat+a+b-1) % 4)]
                start = (34 * ((seat+a+b-1) % 4) + 2 * min(a,b)) % len(deck)
                opened = deck[start:] + deck[:start]
                for offset in range(4):
                    indices = [base + 4*offset + i for base in (0,16,32) for i in range(4)] + [48+offset]
                    if offset == 0:
                        indices.append(52)
                    assert hands[(seat+offset) % 4] == [opened[i] for i in indices]


def test_wall_ends_in_actions_records_and_reconnects():
    s = SanhuaGameState()
    deck = sorted(TILES) * 4
    indicator_slot = 34 * ((s.dealer_index + 1) % 4)
    red_slot = deck.index(45)
    deck[red_slot], deck[indicator_slot] = deck[indicator_slot], deck[red_slot]
    s.initialize_round(wall=deck, dice=(1, 1))
    assert s.indicator == 45 and s.marker == 81
    s.start_game_recording()
    s.start_round_recording()
    header = s.game_record["game_round"]["round_index_1"]
    public_header = s.spectator_round_header(header)
    assert public_header["tiles_list"] == [0] * 83

    def ends(view):
        return view["wall_front_draws"], view["wall_back_draws"]

    assert ends(public_header["sanhua"]) == (0, 0)
    s.draw(0)
    s.draw(0, kong=True)
    window = s.draw(0, kong=True)  # The exposed red is automatically konged and replaced.
    s.open_action_window(window)
    assert s.marker == -1 and s.player_list[0].specials == [45]
    observed = [(p["action"], ends(p["game_info"]["sanhua"])) for p in s.outbound_payloads
                if p["player_index"] == 1 and "do_action_info" in p]
    assert observed == [("deal_tile", (1, 0)), ("deal_gang_tile", (1, 1)),
                        ("deal_gang_tile", (1, 2)), ("buhua", (1, 2)), ("deal_gang_tile", (1, 3))]
    assert len(s.tiles_list) == 83 - 1 - 3
    for seat in range(4):
        assert ends(s.restore_payloads(seat)[0]["game_info"]["sanhua"]) == (1, 3)
    assert ends(header["action_ticks"][-1][2]) == (1, 3)
    s.finish()
    s.game_status = "waiting_ready"
    s.advance_round_after_ready()
    s.initialize_round()
    assert ends(s.view()) == (0, 0)


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


@pytest.mark.parametrize("winner", range(4))
def test_orphans_fixed_payments_for_each_seat(winner):
    hand = list(ORPHANS) + [11]
    for payer in [None] + [seat for seat in range(4) if seat != winner]:
        result = rules.score(hand, [], 27, winner, payer, [["暗杠"] * 3] * 4, liable=payer)
        points = 540 if payer is None else 600
        expected = [-180] * 4
        if payer is not None:
            expected[payer] = -240
        expected[winner] = points
        assert result["points"] == points
        assert result["changes"] == expected
        assert result["gold"] is False


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
