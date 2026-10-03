"""本规则的离线适配单测；不启动服务、真实房间、数据库或 Unity。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_state import make_state, hand, draw, HAND
from . import bot
from .result import broadcast_result as terminal_result
from ..game_taiwan.boardcast import send_reconnect_game_state
from ..gamestate_manager import GameStateManager
from ...room.tuidao_room import create_tuidao_room, handle_create_tuidao_room, CONFIG
from ...room.tuidao_room import TuidaoRoomValidator
from ...room.room_manager import RoomManager


class Socket:
    def __init__(self): self.messages = []
    async def send_json(self, message): self.messages.append(message)


def room_manager():
    player = SimpleNamespace(user_id=101, username="测试", current_room_id=None)
    manager = SimpleNamespace(
        rooms={}, room_passwords={},
        game_server=SimpleNamespace(players={"unit": player},
            db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "测试"})),
        _reject_room_entry_conflicts=lambda *_: None,
        _normalize_event_id=lambda value: value,
        _validate_event_for_room=lambda *_: None,
        _generate_room_id=lambda: "123456",
        _apply_event_fields=lambda room, event: room.update(event_id=event),
        _broadcast_room_info=AsyncMock(),
    )
    return manager


def test_room_creation_pins_profile_and_preserves_common_settings():
    manager = room_manager()
    result = asyncio.run(create_tuidao_room(manager, "unit", room_name=" 推倒和 ", gameround=3,
        password="unit-secret", count_tips=True, pointer_tips=False, event_id="unit-event"))
    assert result.success
    room = manager.rooms["123456"]
    assert room["room_rule"] == "guangdong" and room["sub_rule"] == "guangdong/tuidao_mil2024"
    assert room["game_round"] == 3 and room["room_name"] == "推倒和"
    assert room["detailed_config"] == CONFIG
    assert room["count_tips"] and not room["pointer_tips"]
    assert not room["use_flowers"] and not room["claim_protection"]
    assert room["event_id"] == "unit-event"
    assert manager.room_passwords["123456"] == "unit-secret"
    assert "password" not in room


@pytest.mark.parametrize("overrides", [
    {"gameround": 0}, {"sub_rule": "tuidao/local"},
    {"detailed_config": {"wildcards": True}}, {"room_name": " "},
])
def test_bad_rooms_are_rejected_before_mutation(overrides):
    manager = room_manager()
    result = asyncio.run(create_tuidao_room(manager, "unit", **({"room_name": "推倒和", "gameround": 1} | overrides)))
    assert not result.success and not manager.rooms
    assert manager.game_server.players["unit"].current_room_id is None


def test_router_handler_serializes_version_and_fixed_defaults():
    manager = room_manager(); socket = Socket()
    asyncio.run(handle_create_tuidao_room(SimpleNamespace(room_manager=manager), "unit",
        {"roomname": "推倒和"}, socket))
    result = socket.messages[0]
    assert result["success"] and result["room_info"]["detailed_config"] == CONFIG


def test_room_index_registration_including_duplicate_guards():
    state = make_state()
    room = {"instance_id": "unit-instance"}
    manager = GameStateManager(SimpleNamespace(room_manager=SimpleNamespace(
        rooms={state.room_id: room}, active_game_room_ids={})))
    manager.register_game(state, room)
    assert manager.get_game_state_by_room_id(state.room_id) is state
    assert manager.room_id_to_TuidaoGameState[state.room_id] is state
    assert all(manager.get_game_state_by_user_id(p.user_id) is state for p in state.player_list)
    assert room["active_gamestate_id"] == state.gamestate_id
    with pytest.raises(ValueError): manager.register_game(state, room)


def test_reconnect_preserves_score_ready_lock_and_private_hand():
    state = make_state(); player = hand(state, 0, HAND, ready=True)
    draw(player, 19); player.tag_list.append("declared_ready"); player.score = 6
    concealed = hand(state, 1, [22,23,24,31,32,33,41,41,41,47], ["G11"])
    concealed.combination_mask = [[0,11]*4]
    socket = Socket()
    state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
    state.game_status = "waiting_hand_action"
    state.action_dict = {0: ["cut"], 1: [], 2: [], 3: []}
    state.waiting_players_list = [0]
    asyncio.run(send_reconnect_game_state(state, player))
    info = socket.messages[0]["game_info"]
    assert info["detailed_config"] == CONFIG and info["room_rule"] == "guangdong"
    assert info["players_info"][0]["hand_tiles"] == HAND+[19]
    assert info["players_info"][0]["score"] == 6
    assert "declared_ready" in info["players_info"][0]["tag_list"]
    assert all("hand_tiles" not in p for p in info["players_info"][1:])
    ask = socket.messages[1]["ask_hand_action_info"]
    assert ask["action_list"] == ["cut"]
    assert 19 not in ask["forbidden_cut_tiles"] and 11 in ask["forbidden_cut_tiles"]


def test_declining_self_draw_does_not_import_replacement_bonus_into_pass_water():
    state = make_state(); player = hand(state, 0, HAND); draw(player, 28)
    state.last_draw_after_kong = True; player.normal_draw_count = 2
    state.result_dict["hu_self"] = state.score_candidate(0, "self_draw")
    state.enter_water(0)
    assert player.passed_fan == -1
    asyncio.run(state.execute_cut(0, {"TileId": 28, "cutIndex": 13, "cutClass": True}))
    detail = state.score_candidate(0, "discard", 28, ignore_pass=True)
    assert player.passed_fan == detail["fan"] < state.result_dict.get("hu_self", {"fan": 100})["fan"]


@pytest.mark.parametrize("available,status,expected", [
    (["hu_self", "cut"], "waiting_hand_action", "hu_self"),
    (["hu_first", "pass"], "waiting_action_after_cut", "hu_first"),
    (["gang", "pass"], "waiting_action_after_cut", "gang"),
    (["peng", "pass"], "waiting_action_after_cut", "pass"),
    (["pass"], "waiting_action_qianggang", "pass"),
])
def test_bot_obeys_available_priority_actions(available, status, expected):
    state = make_state()
    with patch.object(bot, "_wait_until_actionable", new=AsyncMock(return_value=True)), \
         patch.object(bot, "bot_action_is_current", return_value=True), \
         patch.object(bot, "submit_bot_action", new=AsyncMock()) as send:
        asyncio.run(bot.tuidao_bot_action.__wrapped__(state, 0, available, status))
    assert send.await_args.args[3] == expected


def test_ready_bot_discards_drawn_slot_even_with_duplicate_values():
    state = make_state(); player = hand(state, 0, HAND, ready=True); draw(player, 11)
    with patch.object(bot, "_wait_until_actionable", new=AsyncMock(return_value=True)), \
         patch.object(bot, "bot_action_is_current", return_value=True), \
         patch.object(bot, "submit_bot_action", new=AsyncMock()) as send:
        asyncio.run(bot.tuidao_bot_action.__wrapped__(state, 0, ["cut"], "waiting_hand_action"))
    assert send.await_args.args[3:7] == ("cut", True, 11, 13)


def test_stale_bot_window_never_submits():
    state = make_state()
    with patch.object(bot, "_wait_until_actionable", new=AsyncMock(return_value=True)), \
         patch.object(bot, "bot_action_is_current", return_value=False), \
         patch.object(bot, "submit_bot_action", new=AsyncMock()) as send:
        asyncio.run(bot.tuidao_bot_action.__wrapped__(state, 0, ["hu_self"], "waiting_hand_action"))
    send.assert_not_awaited()


def test_terminal_reveal_all_hands_and_reconnect_without_repeating_payment():
    state = make_state(); sockets = []
    for i, player in enumerate(state.player_list):
        hand(state, i, HAND)
        socket = Socket(); sockets.append(socket)
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
    draw(state.player_list[0], 28)
    state.game_status = "END"
    state.pending_winners = [{"index": 0, "detail": state.score_candidate(0, "self_draw"),
        "payer": None, "source": "self_draw", "hu_class": "hu_self", "tile": 28}]
    state.current_round = 4
    asyncio.run(state._settle_hand({i:0 for i in range(4)}))
    result = sockets[0].messages[-1]["show_result_info"]
    assert result["revealed_hands"] == {i: list(p.hand_tiles) for i,p in enumerate(state.player_list)}
    scores = [p.score for p in state.player_list]
    ticks = state.server_action_tick
    asyncio.run(state.player_reconnect(101))
    assert sockets[0].messages[-1]["show_result_info"] == result
    assert state.server_action_tick == ticks and [p.score for p in state.player_list] == scores
    state._reset_hand_runtime()
    assert state._terminal_result is None


def test_draw_reveals_all_hands_and_keeps_instant_kong_payments():
    state = make_state(); socket = Socket()
    state.game_server.user_id_to_connection[101] = SimpleNamespace(websocket=socket)
    for i in range(4): hand(state,i,HAND)
    state.player_list[0].score = 6
    for p in state.player_list[1:]: p.score = -2
    asyncio.run(terminal_result(state, hu_class="liuju", player_to_score={i:p.score for i,p in enumerate(state.player_list)},
        score_changes={i:0 for i in range(4)}, next_status="round_end_by_ready"))
    info = socket.messages[0]["show_result_info"]
    assert set(info["revealed_hands"]) == {0,1,2,3}
    assert info["player_to_score"] == {0:6,1:-2,2:-2,3:-2}


def test_timeout_after_ready_keeps_locked_hand_and_makes_tsumogiri():
    state = make_state(); player = hand(state,0,HAND,ready=True); draw(player,19)
    asyncio.run(state.execute_timeout_cut(0))
    assert player.hand_tiles == HAND and player.discard_tiles[-1] == 19


def test_claim_clock_recovers_three_second_limit_with_elapsed_time():
    state = make_state(); player = state.player_list[1]
    assert state.claim_clock(player) == (0,3)
    state._ask_delivered_at = {1: 100.0}
    with patch("server.gamestate.game_tuidao.TuidaoGameState.time.time", return_value=101.2):
        assert state.claim_clock(player, reconnecting=True) == (0,2)
    with patch("server.gamestate.game_tuidao.TuidaoGameState.time.time", return_value=104.0):
        assert state.claim_clock(player, reconnecting=True) == (0,0)


@pytest.mark.parametrize("flowers", [False, True])
def test_event_room_uses_same_fixed_edition_and_rejects_flowers(flowers):
    manager = room_manager()
    manager.room_validators = {"guangdong": TuidaoRoomValidator}
    result = asyncio.run(RoomManager.create_empty_event_room(manager, "unit-event", "guangdong",
        {"use_flowers": flowers, "game_round": 2, "tips": True}, broadcast=False))
    assert result.success is not flowers
    if not flowers:
        assert result.room_info["detailed_config"] == CONFIG
        assert result.room_info["sub_rule"] == "guangdong/tuidao_mil2024"
    else:
        assert not manager.rooms


def test_authoritative_kong_targets_hide_shape_changing_ready_kongs():
    state = make_state()
    player = hand(state,0,[11]*3+[12]*3+[13]*3+[21,22,23,47],ready=True)
    draw(player,11)
    assert "angang" not in state.check_hand_actions(0)[0]
    assert state.build_private_hand_action_info(0)["kong_candidates"]["angang"] == []
    player = hand(state,0,[11]*3+[22,23,24,31,32,33,41,41,41,47],ready=True)
    draw(player,11)
    assert "angang" in state.check_hand_actions(0)[0]
    assert state.build_private_hand_action_info(0)["kong_candidates"]["angang"] == [11]
