"""Duplicate v2 boundary rules, real game lifecycle, and ordinary Guobiao isolation."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from . import action_check
from .duplicate_rules import duplicate_rules_for
from .init_tiles import init_guobiao_tiles
from ..public.duplicate_wall import DuplicateFinished, configure_duplicate_state
from ..public.game_record_manager import init_game_record, init_game_round, end_game_record
from ..public.hand_slot_utils import clear_draw_slot
from ..public.test_duplicate_wall import game, wall_tiles
from ..public.test_duplicate_remaining_tiles import connected_game, messages


def set_parts(state, parts):
    """Arrange a late-hand wall without changing its production draw methods."""
    state.tiles_list.parts = [list(part) for part in parts]
    list.clear(state.tiles_list)
    list.extend(state.tiles_list, sum(state.tiles_list.parts, []))


@pytest.mark.parametrize("seat", range(4))
@pytest.mark.parametrize("supplement", [False, True])
def test_last_tile_can_be_drawn_and_other_seats_continue_until_empty_actor_draw(seat, supplement):
    state, _ = connected_game()
    parts = [[11, 12, 13], [21, 22, 23], [31, 32, 33], [41, 42, 43]]
    parts[seat] = [45]
    set_parts(state, parts)
    player = state.player_list[seat]
    if supplement:
        player.get_gang_tile(state.tiles_list, state)
    else:
        player.get_tile(state.tiles_list)
    assert player.hand_tiles[-1] == 45 and state.tiles_list.parts[seat] == []
    assert not getattr(state, "_duplicate_finished", False)
    other = state.player_list[(seat + 1) % 4]
    other.get_tile(state.tiles_list)
    before = list(player.hand_tiles)
    with pytest.raises(DuplicateFinished):
        player.get_tile(state.tiles_list)
    assert player.hand_tiles == before
    assert state.duplicate_exhausted_seat == seat


@pytest.mark.parametrize("seat", range(4))
def test_independent_supplement_tail_sequence_and_single_tile_fallback(seat):
    state, _ = connected_game()
    parts = [list(range(10 * i + 1, 10 * i + 9)) for i in range(4)]
    set_parts(state, parts)
    player = state.player_list[seat]
    other = state.player_list[(seat + 1) % 4]
    expected = list(parts[seat])
    for position in (2, 1, 2, 1):
        wanted = expected.pop(-position)
        player.get_gang_tile(state.tiles_list, state)
        assert player.hand_tiles[-1] == wanted
        other.get_gang_tile(state.tiles_list, state)
        # Forward draws never change either player's supplement cursor.
        assert state.tiles_list.parts[seat] == expected
        assert list(state.tiles_list) == sum(state.tiles_list.parts, [])
    player.get_tile(state.tiles_list)
    expected.pop(0)
    player.get_gang_tile(state.tiles_list, state)
    assert player.hand_tiles[-1] == expected.pop(-2)
    player.get_tile(state.tiles_list)
    expected.pop(0)
    assert len(expected) == 1
    player.get_gang_tile(state.tiles_list, state)
    assert player.hand_tiles[-1] == expected[0]
    cursor_before = list(state.tiles_list.supplement_positions)
    with pytest.raises(DuplicateFinished):
        player.get_gang_tile(state.tiles_list, state)
    assert state.tiles_list.supplement_positions == cursor_before


@pytest.mark.parametrize("seat", range(4))
def test_own_empty_wall_disables_flower_and_all_kongs_but_not_discard_or_ron(seat):
    state, _ = connected_game()
    parts = [[11, 12]] * 4
    parts[seat] = []
    set_parts(state, parts)
    player = state.player_list[seat]
    player.hand_tiles = [11] * 4 + [21, 51]
    player.combination_tiles = ["k21"]
    player.waiting_tiles = set()
    assert action_check.check_action_buhua(state, seat)[seat] == []
    assert action_check.check_action_hand_action(state, seat)[seat] == ["cut"]
    # A player may peng with an empty wall, but cannot declare a kong.
    state.current_player_index = (seat + 2) % 4
    player.hand_tiles = [11] * 3
    state.calculation_service.GB_tingpai_check.return_value = set()
    actions = action_check.check_action_after_cut(state, 11)
    assert "peng" in actions[seat] and "gang" not in actions[seat]


@pytest.mark.parametrize("action", ["buhua", "angang", "jiagang"])
def test_empty_wall_rejects_incoming_supplement_actions_before_hand_mutation(action):
    from .get_action import get_ai_action
    from .buhua_broadcast import perform_buhua_and_broadcast
    state, _ = connected_game()
    set_parts(state, [[], [11], [21], [31]])
    player = state.player_list[0]
    player.hand_tiles = [11] * 4 + [21, 51]
    player.combination_tiles = ["k21"]
    player.waiting_tiles = set()
    state.current_player_index = 0
    state.game_status = "waiting_hand_action"
    state.waiting_players_list = [0]
    state.action_dict = action_check.check_action_hand_action(state, 0)
    before = list(player.hand_tiles)
    asyncio.run(get_ai_action(state, 0, action, False, 11, 0, 11 if action == "angang" else 21))
    assert state.action_queues[0].empty() and player.hand_tiles == before
    with pytest.raises(ValueError, match="不能补花"):
        asyncio.run(perform_buhua_and_broadcast(state, 0))
    assert player.hand_tiles == before and player.huapai_list == []


@pytest.mark.parametrize("has_wall", [False, True])
def test_ordinary_sea_bottom_and_supplement_rules_are_unchanged(has_wall):
    state, _, _ = game("guobiao", "Guobiao", duplicate=False)
    state.tiles_list = [45] if has_wall else []
    state.player_list[0].hand_tiles = [11] * 4 + [21, 51]
    state.player_list[0].combination_tiles = ["k21"]
    state.player_list[0].waiting_tiles = set()
    actions = action_check.check_action_hand_action(state, 0)[0]
    assert (set(actions) & {"buhua", "angang", "jiagang"}) == ({"buhua", "angang", "jiagang"} if has_wall else set())
    state.calculation_service.GB_hepai_check.return_value = (8, ["测试番"])
    for kind, flag in (("dianhe", "last_cut"), ("handgot", "last_deal")):
        action_check.check_hepai(state, {i: [] for i in range(4)}, 45, 0, kind)
        assert (flag in state.calculation_service.GB_hepai_check.call_args.args[2]) == (not has_wall)


def test_supplement_and_normal_draw_on_empty_other_seat_do_not_end_game():
    state, _ = connected_game()
    set_parts(state, [[11, 12, 13], [], [21, 22], [31, 32]])
    state.player_list[0].get_gang_tile(state.tiles_list, state)
    assert state.player_list[0].hand_tiles[-1] == 12
    state.player_list[2].get_tile(state.tiles_list)
    assert state.player_list[2].hand_tiles[-1] == 21


@pytest.mark.parametrize("index", [0, 1, 2, 3])
@pytest.mark.parametrize("gang", [False, True])
def test_actual_calculator_awards_miaoshou_only_for_last_draw(index, gang):
    from ...game_calculation.game_calculation_service import GameCalculationService
    state, _ = connected_game()
    state.calculation_service = GameCalculationService()
    state.current_player_index = index
    player = state.player_list[index]
    player.hand_tiles = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 45]
    if gang:
        player.hand_tiles = player.hand_tiles[3:]
        player.combination_tiles = ["G11"]
    player.waiting_tiles = {45}
    player.huapai_list = []
    parts = [[45]] * 4
    parts[(index + 1) % 4] = []
    set_parts(state, parts)
    if gang:
        player.get_gang_tile(state.tiles_list, state)
    else:
        player.get_tile(state.tiles_list)
    actions = {i: [] for i in range(4)}
    action_check.check_hepai(state, actions, 45, index, "handgot", is_get_gang_tile=gang)
    assert "hu_self" in actions[index]
    assert "妙手回春" in state.result_dict["hu_self"][1]
    assert ("杠上开花" in state.result_dict["hu_self"][1]) == gang


@pytest.mark.parametrize("discarder", range(4))
@pytest.mark.parametrize("origin", ["draw", "chi", "peng", "gang"])
def test_next_empty_wall_makes_discard_ron_only_even_after_meld(discarder, origin):
    state, _ = connected_game()
    state.current_player_index = discarder
    parts = [[11, 12]] * 4
    parts[(discarder + 1) % 4] = []
    set_parts(state, parts)
    for player in state.player_list:
        player.hand_tiles = [11, 11, 11, 12, 13]
    state.player_list[discarder].combination_tiles = [] if origin == "draw" else ["s12"]
    clear_draw_slot(state.player_list[discarder])
    state.calculation_service.GB_tingpai_check.return_value = {11}
    state.calculation_service.GB_hepai_check.return_value = (8, ["海底捞月"])
    actions = action_check.check_action_after_cut(state, 11)
    for seat, allowed in actions.items():
        if seat == discarder:
            assert allowed == []
        else:
            assert any(action.startswith("hu_") for action in allowed)
            assert not set(allowed) & {"chi_left", "chi_mid", "chi_right", "peng", "gang"}
    assert all("last_cut" in call.args[2] for call in state.calculation_service.GB_hepai_check.call_args_list)


@pytest.mark.parametrize("seat", range(4))
@pytest.mark.parametrize("drawn,next_empty", [(False, True), (True, False), (True, True)])
def test_miaoshou_requires_actual_draw_and_next_player_empty(seat, drawn, next_empty):
    state, _ = connected_game()
    state.current_player_index = seat
    parts = [[45, 12]] * 4
    if next_empty:
        parts[(seat + 1) % 4] = []
    set_parts(state, parts)
    clear_draw_slot(state.player_list[seat])
    if drawn:
        state.player_list[seat].get_tile(state.tiles_list)
    state.calculation_service.GB_hepai_check.return_value = (8, ["测试番"])
    action_check.check_hepai(state, {i: [] for i in range(4)}, 45, seat, "handgot")
    ways = state.calculation_service.GB_hepai_check.call_args.args[2]
    assert ("last_deal" in ways) == (drawn and next_empty)


@pytest.mark.parametrize("sub_rule", ["guobiao/standard", "guobiao/xiaolin", "guobiao/kshen", "guobiao/lanshi", "guobiao/blood_battle"])
@pytest.mark.parametrize("use_flowers", [False, True])
def test_ordinary_guobiao_flower_setting_and_seed_reproduction(sub_rule, use_flowers):
    states = [game("guobiao", "Guobiao", sub_rule, duplicate=False, use_flowers=use_flowers)[0] for _ in range(2)]
    for state in states:
        state.master_seed = 123456789
        init_guobiao_tiles(state)
        all_tiles = list(state.tiles_list) + sum([p.hand_tiles for p in state.player_list], [])
        expected_flowers = use_flowers and sub_rule != "guobiao/lanshi"
        assert len(all_tiles) == (144 if expected_flowers else 136)
        assert any(tile > 50 for tile in all_tiles) == expected_flowers
        assert duplicate_rules_for(state) is None
    assert states[0].tiles_list == states[1].tiles_list
    assert [p.hand_tiles for p in states[0].player_list] == [p.hand_tiles for p in states[1].player_list]


@pytest.mark.parametrize("wall_type", ["manual", "seed", "key"])
@pytest.mark.parametrize("use_flowers", [False, True])
def test_record_preserves_full_wall_config_and_seed_but_live_never_does(wall_type, use_flowers, monkeypatch):
    state, _, room = game("guobiao", "Guobiao", duplicate=False, use_flowers=not use_flowers)
    tiles = wall_tiles("guobiao" if use_flowers else "guobiao/lanshi")
    wall = dict(id=1, key="DUP_test01234567", rule="guobiao", wall_type=wall_type,
                tiles=tiles, use_flowers=use_flowers, seed="0123456789abcdef", event_id=None)
    monkeypatch.setattr("server.database.duplicate_walls.load_duplicate_wall", lambda *args: wall)
    monkeypatch.setattr("server.database.duplicate_walls.reserve_duplicate_game", lambda *args: "dupgame0123456789")
    configure_duplicate_state(state, {**room, "duplicate_key": wall["key"]})
    assert state.use_flowers is use_flowers
    init_guobiao_tiles(state)
    init_game_record(state)
    init_game_round(state)
    title = state.game_record["game_title"]
    assert "duplicate_seed" not in title and "duplicate_tiles" not in title
    end_game_record(state)
    assert title["duplicate_tiles"] == tiles
    assert title["use_flowers"] is use_flowers and title["duplicate_rules_version"] == 2
    assert title.get("duplicate_seed") == (None if wall_type == "manual" else wall["seed"])
    from .boardcast import _build_game_start_payload_for_viewer
    from ...response import GameInfo
    live = GameInfo(**_build_game_start_payload_for_viewer(state, 1)).model_dump(exclude_none=True)
    assert live["use_flowers"] is use_flowers
    assert not set(live) & {"duplicate_key", "duplicate_seed", "duplicate_tiles", "duplicate_round_tiles", "seed", "master_seed"}


@pytest.mark.parametrize("round_count", [1, 4, 8, 12, 16])
@pytest.mark.parametrize("win_on_last", [False, True])
def test_real_loop_last_draw_can_win_or_continue_until_next_empty_draw(win_on_last, round_count, monkeypatch):
    state, module, _ = game("guobiao", "Guobiao", "guobiao/standard", use_flowers=False)
    state.duplicate_round_count = round_count
    state.max_round = max(1, round_count // 4)
    state._duplicate_round_tiles = tuple(state._duplicate_tiles for _ in range(round_count))
    state.run_hu_result_ready_phase = AsyncMock()
    monkeypatch.setattr(module, "liuju_ready_wait_seconds", lambda: 0)
    state.claim_protection = False
    state.calculation_service.GB_tingpai_check.return_value = set()
    state.spectator_manager.send_final_record_and_close = AsyncMock()
    sockets = []
    for player in state.player_list:
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
        sockets.append(socket)
    actual_init = module.init_guobiao_tiles
    def late_hand_init(gs):
        actual_init(gs)
        parts = [[11, 12], [21, 22], [31, 32], [41, 42]]
        parts[gs.player_list[1].original_player_index] = [21]
        set_parts(gs, parts)
    monkeypatch.setattr(module, "init_guobiao_tiles", late_hand_init)
    state.broadcast_ask_hand_action = AsyncMock()
    draws = []
    actual_broadcast = state.broadcast_do_action
    async def track_draw(**kwargs):
        if kwargs["action_list"] == ["deal_tile"]:
            draws.append(kwargs["action_player"])
        await actual_broadcast(**kwargs)
    state.broadcast_do_action = track_draw
    async def choose_action():
        if win_on_last and state.current_player_index == 1:
            state.hu_class = "hu_self"
            state.result_dict = {"hu_self": (8, ["测试番"])}
            state.game_status = "check_hepai"
        else:
            state.game_status = "deal_card"
    state.wait_action = choose_action
    asyncio.run(state.game_loop_chinese())
    assert draws == ([1] if win_on_last else [1, 2, 3, 0]) * round_count
    assert state.next_status == "match_end" and state.current_round == round_count
    assert len(state.game_record["game_round"]) == round_count
    assert len(state.game_record["game_title"]["duplicate_round_tiles"]) == round_count
    assert all(len(p.score_history) == round_count for p in state.player_list)
    assert [p.player_index for p in state.player_list] == [0, 1, 2, 3]
    ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[-1] == ["end"]
    assert ticks[-2][0] == ("hu_self" if win_on_last else "liuju")
    for socket in sockets:
        assert messages(socket, "do_action_info")[0]["duplicate_remaining_tiles"][1] == 0
        assert 0 in messages(socket, "game_end_info")[-1]["duplicate_remaining_tiles"]
        for call in socket.send_json.call_args_list:
            import json
            assert state.duplicate_key not in json.dumps(call.args[0], default=str)
    state.db_manager.store_guobiao_game_record.assert_called_once()
    state.game_server.room_manager.finish_custom_game_room.assert_not_awaited()
