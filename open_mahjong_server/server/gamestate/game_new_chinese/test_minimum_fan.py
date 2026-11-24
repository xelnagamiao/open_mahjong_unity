import asyncio
from unittest.mock import AsyncMock

import pytest

from ...game_calculation.new_chinese import rules
from ...room.new_chinese_room import NewChineseRoomValidator
from ..test_room_lifecycle import USERS, make_server
from .NewChineseGameState import NewChineseGameState
from .actions import claim_actions, ready_cuts, wait_context
from .bot import choose
from .state_machine import Phase as P
from .test_new_chinese import move, prior_discards, start, table


DRAGON = [11, 12, 13, 15, 16, 17, 18, 19, 22, 22]
SEVEN = [11, 11, 14, 14, 22, 22, 25, 25, 33, 33, 36, 36, 41]
PUNG_CODES = ["k11", "k24", "k33", "k37"]
EXACT = [11, 12, 13, 13, 13, 21, 22, 23, 25, 25, 27, 27, 27]


@pytest.mark.parametrize("exchange,configured,effective", [
    (False, 0, 0), (False, 17, 17), (False, 2_147_483_647, 2_147_483_647),
    (True, 0, 15), (True, 250, 15),
])
def test_custom_event_restored_and_recorded_threshold(exchange, configured, effective):
    async def run():
        server = make_server(USERS)
        manager = server.room_manager
        manager._broadcast_room_info = AsyncMock()
        server.db_manager.get_user_settings.return_value = {"username": "南家"}
        server.db_manager.get_event.return_value = {"status": "active"}
        server.players[str(USERS[0])].current_room_id = None
        config = dict(hepai_limit=configured, detailed_config={"exchange_three": exchange})
        room = await manager.create_NewChinese_room(str(USERS[0]), room_name="新中式", **config)
        event = await manager.create_empty_event_room("event", "new_chinese",
                                 dict(room_name="比赛", game_round=4, **config), broadcast=False)
        for result in (room, event):
            assert result.success, result.message
            assert result.room_info["hepai_limit"] == effective
            data = dict(result.room_info, player_list=USERS)
            s = NewChineseGameState(room_data=data)
            s.initialize_round()
            s.start_game_recording()
            assert s.game_record["game_title"]["hepai_limit"] == effective
            start(s, s.opening_window())
            for packet in s.restore_payloads(0):
                if packet.get("game_info"):
                    assert packet["game_info"]["hepai_limit"] == effective
    asyncio.run(run())


def test_showdown_config_round_trips_and_rejects_non_boolean():
    normalized = NewChineseRoomValidator(room_name="新中式").detailed_config
    assert normalized["showdown"] is False
    enabled = NewChineseRoomValidator(room_name="新中式", detailed_config={"showdown": True}).detailed_config
    assert enabled["showdown"] is True
    with pytest.raises(ValueError):
        NewChineseRoomValidator(room_name="新中式", detailed_config={"showdown": "true"})


@pytest.mark.parametrize("bad", [True, -1, 2_147_483_648, 5.5, "15"])
def test_invalid_room_fan_limit(bad):
    with pytest.raises(ValueError):
        NewChineseRoomValidator(room_name="新中式", hepai_limit=bad)


def test_partial_waits_apply_to_declaration_discard_draw_tongbao_and_mobao():
    s = table(exchange=True)
    p = s.player_list[1]
    p.hand_tiles, p.combination_tiles = [*DRAGON, 45], ["k41"]
    p.has_draw_slot = True
    assert "riichi_cut" in s.turn(1)["actions"][1]
    assert s.candidate_cuts[45] == [14]
    assert rules.waits(DRAGON, p.combination_tiles, structural=True) == {14, 17}
    p.declared = True
    for tile, allowed in ((14, True), (17, False)):
        p.hand_tiles = [*DRAGON, tile]
        actions = s.turn(1)["actions"][1]
        assert ("hu_self" in actions) == allowed
        assert choose(s, 1, actions)["action_type"] == ("hu_self" if allowed else "cut")
        if not allowed:
            with pytest.raises(ValueError, match="和牌未成立"):
                s.settle(1, tile, "draw")
        p.hand_tiles = list(DRAGON)
        p.has_draw_slot = False
        prior_discards(s, 2, tile)
        assert ("hu" in claim_actions(s, 2, tile)[1]) == allowed
        assert bool(s.quote(1, tile, "rob", 2)) == allowed
        s.bao = tile
        assert bool(s.quote(1, tile, "seen")) == allowed
        s.bao = 0
        p.has_draw_slot = True
    p.hand_tiles, s.bao = [*DRAGON, 45], 45
    assert s.quote(1, 45, "draw")["represented"] == 14
    s.settle(1, 45, "draw")
    assert s.deferred_hu_settlements[-1]["fan_details"]["represented"] == 14


@pytest.mark.parametrize("hand,codes,grade", [(SEVEN, [], 3), ([41], PUNG_CODES, 2)])
def test_zero_fan_special_shape_settles_after_everyone_declines_exchange(hand, codes, grade, monkeypatch):
    s = table(exchange=True)
    now = 0
    monkeypatch.setattr(s, "_timing_now", lambda: now)
    window = move(s, start(s, s.opening_window()), {})
    now = window["deadline"]
    move(s, window, {})
    assert s.hepai_limit == 15
    p = s.player_list[1]
    p.hand_tiles, p.combination_tiles, p.has_draw_slot = [*hand, 41], list(codes), True
    p.declared = True
    window = start(s, s.turn(1))
    assert "hu_self" in window["actions"][1]
    move(s, window, {1: "hu_self"})
    detail = s.deferred_hu_settlements[-1]["fan_details"]
    assert detail["fan"] == 0 and detail["base"] == grade


@pytest.mark.parametrize("hand,codes,grade", [(SEVEN, [], 3), ([41], PUNG_CODES, 2)])
@pytest.mark.parametrize("source", sorted(rules.BASE))
def test_zero_fan_special_shapes_only_exempt_when_exchange_is_on(hand, codes, grade, source):
    assert rules.score(hand, codes, 41, source)["fan"] == 0
    assert rules.score(hand, codes, 41, source, hepai_limit=1) is None
    quote = rules.score(hand, codes, 41, source, exchange_three=True)
    assert quote["fan"] == 0 and quote["base"] == grade
    for exchange, expected in ((True, {41}), (False, set())):
        assert rules.waits(hand, codes, hepai_limit=15, exchange_three=exchange) == expected
        assert set(ready_cuts([*hand, 45], codes, hepai_limit=15, exchange_three=exchange).get(45, [])) == expected


def test_exactly_fifteen_and_pickup_source_are_checked_without_base_points():
    assert rules.score(EXACT, [], 13, "self_draw", exchange_three=True)["fan"] == 15
    assert rules.score(EXACT, [], 13, "self_draw", hepai_limit=15)["fan"] == 15
    assert rules.score(EXACT, [], 13, "self_draw", hepai_limit=16) is None
    assert rules.score(DRAGON, ["k41"], 14, "black", hepai_limit=30) is None
    assert 14 in rules.waits(DRAGON, ["k41"], hepai_limit=30)
    assert 14 not in rules.waits(DRAGON, ["k41"], hepai_limit=30, self_draw=False)


def test_threshold_precedes_highest_payout_selection_over_legal_decompositions():
    hand = [11, 11, 12, 12, 12, 13, 13, 13, 14, 14]
    plain = rules.score(hand, ["k16"], 11, "black")
    assert (plain["fan"], plain["base"], plain["points"]) == (85, 2, 210)
    gated = rules.score(hand, ["k16"], 11, "black", hepai_limit=100)
    assert (gated["fan"], gated["base"], gated["points"]) == (100, 0, 206)


@pytest.mark.parametrize("limit,can_declare", [(15, True), (20, True), (21, False)])
def test_chow_and_declare_requires_a_qualifying_wait(limit, can_declare):
    s = table()
    s.hepai_limit = limit
    p = s.player_list[1]
    p.hand_tiles, p.combination_tiles = [11, 13, 15, 16, 17, 18, 19, 22, 22, 45], ["k41"]
    prior_discards(s, 0, 12)
    choices = claim_actions(s, 0, 12)[1]
    assert "chi_mid" in choices
    assert ("chi_mid_ting" in choices) == can_declare
    if can_declare:
        s.machine.transition(P.TURN)
        window = start(s, s.window(P.RESPONSE, 0, 12, {1: choices}))
        move(s, window, {1: "chi_mid_ting"})
        assert s.must_declare == 1
        assert s.candidate_cuts == {45: [14]}


def test_conditional_fans_count_on_actual_winning_tile_and_projection_sees_discard():
    s = table()
    p = s.player_list[1]
    s.hepai_limit = rules.VALUES["绝张"]
    p.hand_tiles, p.combination_tiles = [*DRAGON, 17], ["k41"]
    prior_discards(s, 0, 17, 17)
    assert 17 in ready_cuts(p.hand_tiles, p.combination_tiles, **wait_context(s, 1))[17]
    assert not rules.score(DRAGON, ["k41"], 17, "self_draw", hepai_limit=s.hepai_limit, visible=2)
    assert rules.score(DRAGON, ["k41"], 17, "self_draw", hepai_limit=s.hepai_limit, visible=3)
    assert rules.score(DRAGON, ["k41"], 17, "self_draw", hepai_limit=5, after_kong=True)
