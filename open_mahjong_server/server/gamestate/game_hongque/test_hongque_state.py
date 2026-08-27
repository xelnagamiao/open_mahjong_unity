import asyncio
from types import SimpleNamespace
from unittest.mock import patch

from server.gamestate.game_hongque.HongqueGameState import HongqueGameState
from server.gamestate.game_hongque import get_action as hongque_get_action
from server.gamestate.game_hongque.wait_action import ClaimWindow
from server.room.room_manager import RoomManager


def test_rule_debug_mode_is_disabled_by_default() -> None:
    room = {"room_id": "debug-guard", "player_list": []}

    assert HongqueGameState(None, room).Debug is False


def test_room_creation_exposes_actual_hongque_hand_count() -> None:
    async def scenario():
        connection = SimpleNamespace(
            user_id=101,
            username="host",
            current_room_id=None,
        )
        game_server = SimpleNamespace(
            players={"connection-1": connection},
            user_id_to_connection={},
            gamestate_manager=SimpleNamespace(
                is_user_in_active_game=lambda _user_id: False,
            ),
            match_manager=SimpleNamespace(
                is_user_in_queue=lambda _user_id: False,
                is_user_committed=lambda _user_id: False,
            ),
            db_manager=SimpleNamespace(
                get_user_settings=lambda _user_id: {"username": "host"},
            ),
        )
        manager = RoomManager(game_server)
        for configured_rounds, actual_hands in ((1, 4), (2, 8), (3, 12), (4, 16)):
            response = await manager.create_Hongque_room(
                "connection-1", "round-normalization", configured_rounds,
                "", 20, 5, True,
            )

            assert response.success is True
            assert response.room_info["game_round"] == actual_hands
            state = HongqueGameState(
                None, response.room_info, gamestate_id="round-count-test"
            )
            assert state.max_round == actual_hands

    asyncio.run(scenario())


def test_round_deal_discard_and_next_draw_stay_memory_only() -> None:
    asyncio.run(_exercise_round())


def test_claimed_tile_moves_from_river_into_open_meld() -> None:
    asyncio.run(_exercise_claim())


def test_multiple_discards_advance_players_and_wall() -> None:
    asyncio.run(_exercise_turn_progression())


def test_complete_hand_must_supplement_before_win_after_a_call() -> None:
    asyncio.run(_exercise_self_win_qualification())


def test_onlycut_after_claim_allows_kong_on_last_tile() -> None:
    asyncio.run(_exercise_onlycut_allows_kong_on_last_tile())


def test_multiple_ron_winners_and_nearest_winner_becomes_dealer() -> None:
    asyncio.run(_exercise_multiple_ron())


def test_self_win_builds_explainable_settlement_result() -> None:
    asyncio.run(_exercise_self_win_result())


def test_discard_and_following_draw_are_ordered_visible_events() -> None:
    asyncio.run(_exercise_discard_draw_event_batch())


def test_chi_claim_winner_discards_then_next_seat_draws() -> None:
    asyncio.run(_exercise_chi_claim_winner_discards_then_next_seat())


def test_chi_priority_by_discarder_relative_seat_flows_to_next_seat() -> None:
    asyncio.run(_exercise_chi_priority_by_discarder_relative_seat())


def test_two_smart_bots_competing_for_chi_draw_after_actual_winner() -> None:
    asyncio.run(_exercise_two_smart_bots_competing_for_chi())


def test_debug_double_ron_self_and_next_win_on_first_discard() -> None:
    asyncio.run(_exercise_debug_double_ron())


def test_debug_three_claimants_can_all_chi_peng_win_on_first_discard() -> None:
    asyncio.run(_exercise_debug_tactical_all_claims())


def test_debug_ones_nines_opening_hands() -> None:
    asyncio.run(_exercise_debug_ones_nines())


def test_head_bump_only_nearest_ron_wins() -> None:
    asyncio.run(_exercise_head_bump_ron())


def test_multi_ron_marks_all_winners() -> None:
    asyncio.run(_exercise_multi_ron_flag())


def test_kong_extends_open_meld_stepwise() -> None:
    asyncio.run(_exercise_kong_extends_open_meld())


def test_supplement_consumes_only_overtime_and_restarts_step_time() -> None:
    asyncio.run(_exercise_supplement_turn_clock())


def test_supplement_draws_from_wall_head_like_guobiao_kong() -> None:
    from server.gamestate.game_hongque.init_tiles import pop_supplement_tile

    state = SimpleNamespace(
        wall=["AX1", "AX2", "AX3", "AX4"],
        backward_tiles_list_type="double",
    )
    assert pop_supplement_tile(state) == "AX2"
    assert state.wall == ["AX1", "AX3", "AX4"]
    assert state.backward_tiles_list_type == "single"
    assert pop_supplement_tile(state) == "AX1"
    assert state.wall == ["AX3", "AX4"]
    assert state.backward_tiles_list_type == "double"

    last = SimpleNamespace(wall=["GX9"], backward_tiles_list_type="double")
    assert pop_supplement_tile(last) == "GX9"
    assert last.wall == []


def test_group_wait_can_self_win_and_has_score_hint() -> None:
    asyncio.run(_exercise_group_wait_self_win())


def test_group_wait_can_win_on_another_players_discard() -> None:
    asyncio.run(_exercise_group_wait_ron())


def test_default_hand_and_claim_clock_is_twenty_plus_five() -> None:
    room = {"room_id": "892", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="default-clock-test")
    state.current_player_index = 0
    state.phase = "turn"
    state._start_turn_clock()
    turn_snapshot = state.build_state(0)
    assert turn_snapshot["remaining_time"] == 20
    assert turn_snapshot["step_remaining"] == 5

    state.claim_options = {1: [{"id": "pass-test", "kind": "sequence"}]}
    state.phase = "claim"
    state._start_claim_clock()
    claim_snapshot = state.build_state(1)
    assert claim_snapshot["remaining_time"] == 20
    assert claim_snapshot["step_remaining"] == 5
    claim_deadline = max(state.claim_deadlines.values())
    assert 24.0 < claim_deadline - state.claim_started_at <= 25.0


def test_server_snapshot_leaves_tenpai_hint_scoring_to_client() -> None:
    room = {"room_id": "893", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="tenpai-hint-test")
    viewer = state.players[1]
    viewer.hand = ["AX1", "AX2"]
    viewer.melds = [{
        "kind": "sequence",
        "tiles": ["BX4", "BX5", "BX6"],
        "from_player": 2,
        "claimed_tile": "BX4",
    }]
    state.current_player_index = 0
    state.phase = "turn"

    snapshot = state.build_state(1)
    assert snapshot["waiting_tiles"] == []
    assert snapshot["waiting_hints"] == []


def test_round_result_records_score_and_round_history_for_reconnect() -> None:
    asyncio.run(_exercise_score_history_for_reconnect())


async def _exercise_score_history_for_reconnect() -> None:
    room = {"room_id": "895", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="score-history-test")
    state.current_round = 2
    winner = state.players[0]
    result = {
        "points": 31,
        "winning_hand": ["AX1", "AX2", "AX3"],
        "partition": [["AX1", "AX2", "AX3"]],
        "groups": [["AX1", "AX2", "AX3"]],
        "pair": [],
        "base": 3,
        "fans": [{"name": "测试", "value": 28, "count": 1, "total": 28}],
        "fan_total": 28,
    }
    await state._finish_round([(winner, result)], "self_draw")
    if state._round_task is not None:
        state._round_task.cancel()

    assert [player.score_history for player in state.players] == [["+31"], ["0"], ["0"], ["0"]]
    reconnect = state.build_state(0, sync_mode="reconnect")
    assert reconnect["players"][0]["score_history"] == ["+31"]
    assert reconnect["players"][0]["round_number_history"] == [2]


async def _exercise_round() -> None:
    user_ids = [101, 102, 103, 104]
    room = {
        "room_id": "123456",
        "room_rule": "hongque",
        "game_round": 1,
        "random_seed": 123,
        "player_list": user_ids,
        "player_settings": {user_id: {"username": f"P{user_id}"} for user_id in user_ids},
    }
    state = HongqueGameState(None, room, gamestate_id="test")
    await state._start_round()
    assert len(state.wall) == 81
    assert [len(player.hand) for player in state.players] == [12, 11, 11, 11]

    tile = state.players[0].hand[0]
    await state.submit_action(101, "discard", tile=tile, action_tick=state.action_tick)
    if state.phase == "claim":
        for player_index in list(state.claim_options):
            await state.submit_action(
                state.players[player_index].user_id,
                "pass",
                action_tick=state.action_tick,
            )

    assert state.phase == "turn"
    assert state.current_player_index == 1
    assert len(state.players[1].hand) == 12
    assert not hasattr(state, "game_record")
    snapshot = state.build_state(1)
    assert snapshot["hand"] == state.players[1].hand
    assert snapshot["room_id"] == 123456
    assert [player["user_id"] for player in snapshot["players"]] == user_ids
    assert all(player["profile_used"] == 1 for player in snapshot["players"])


async def _exercise_claim() -> None:
    room = {
        "room_id": "123456",
        "game_round": 1,
        "player_list": [101, 102, 103, 104],
        "player_settings": {},
    }
    state = HongqueGameState(None, room, gamestate_id="claim-test")
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX2", "AX3", "BX9"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    candidate = {
        "id": "call-0",
        "kind": "sequence",
        "priority": 2,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    state.claim_options = {1: [candidate]}
    state.claim_window = ClaimWindow(options=state.claim_options, pending=set())
    state.claim_window.submit(1, candidate)
    state.phase = "claim"

    await state._resolve_claims()

    assert state.players[0].discards == []
    assert state.players[1].melds[0]["tiles"] == ["AX1", "AX2", "AX3"]
    assert state.players[1].hand == ["BX9"]


async def _exercise_turn_progression() -> None:
    user_ids = [101, 102, 103, 104]
    room = {
        "room_id": "654321",
        "game_round": 1,
        "random_seed": 456,
        "player_list": user_ids,
        "player_settings": {user_id: {
            "username": f"P{user_id}",
            "profile_image_id": 1,
        } for user_id in user_ids},
    }
    state = HongqueGameState(None, room, gamestate_id="progress-test")
    await state._start_round()
    initial_wall = len(state.wall)

    for _ in range(12):
        current = state.players[state.current_player_index]
        await state.submit_action(
            current.user_id,
            "discard",
            tile=current.hand[0],
            action_tick=state.action_tick,
        )
        if state.phase == "claim":
            for player_index in list(state.claim_options):
                await state.submit_action(
                    state.players[player_index].user_id,
                    "pass",
                    action_tick=state.action_tick,
                )
        assert state.phase == "turn"

    assert len(state.wall) == initial_wall - 12
    assert sum(len(player.discards) for player in state.players) == 12


async def _exercise_self_win_qualification() -> None:
    room = {"room_id": "321", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="qualification-test")
    player = state.players[1]
    state.players[0].discards = ["AX1"]
    player.hand = ["AX2", "AX3"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    # 补 AX4 后可并入刚亮的顺子成杠和。
    state.wall = ["AX4"]
    candidate = {
        "id": "call-0",
        "kind": "sequence",
        "priority": 2,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    state.claim_options = {1: [candidate]}
    state.claim_window = ClaimWindow(options=state.claim_options, pending=set())
    state.claim_window.submit(1, candidate)
    state.phase = "claim"

    await state._resolve_claims()

    assert player.hand == []
    assert state.game_status == "onlycut_after_action"
    assert state.build_state(1)["legal_actions"] == ["supplement"]
    try:
        await state.submit_action(player.user_id, "win", action_tick=state.action_tick)
        raise AssertionError("onlycut 空手不得直接和牌")
    except ValueError:
        pass

    await state.submit_action(player.user_id, "supplement", action_tick=state.action_tick)
    assert player.hand == ["AX4"]
    assert state.game_status == "waiting_hand_action"
    assert "win" in state.build_state(1)["legal_actions"]
    await state.submit_action(player.user_id, "win", action_tick=state.action_tick)
    assert state.phase == "round_end"
    assert state.round_result["winners"][0]["groups"] == [["AX1", "AX2", "AX3", "AX4"]]
    if state._round_task:
        state._round_task.cancel()


async def _exercise_onlycut_allows_kong_on_last_tile() -> None:
    """亮牌后进入 onlycut：手上最后一张仍可加杠，这是相对摸牌后检查多出的一项。"""
    room = {"room_id": "322", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="onlycut-kong-test")
    player = state.players[1]
    state.players[0].discards = ["AX1"]
    player.hand = ["AX2", "AX3", "AX4"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    candidate = {
        "id": "call-0",
        "kind": "sequence",
        "priority": 2,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    state.claim_options = {1: [candidate]}
    state.claim_window = ClaimWindow(options=state.claim_options, pending=set())
    state.claim_window.submit(1, candidate)
    state.phase = "claim"
    state.wall = ["GX9"]

    await state._resolve_claims()

    assert player.hand == ["AX4"]
    assert state.game_status == "onlycut_after_action"
    actions, candidates = state._legal_turn_actions(player)
    assert "kong" in actions
    assert "win" not in actions
    assert "supplement" in actions
    assert any(item["kind"] == "kong" for item in candidates)

    kong = next(item for item in candidates if item["kind"] == "kong")
    await state.submit_action(
        player.user_id, "kong", candidate_id=kong["id"], action_tick=state.action_tick
    )
    assert player.hand == []
    assert player.melds[0]["tiles"] == ["AX1", "AX2", "AX3", "AX4"]
    assert state.game_status == "onlycut_after_action"

    await state.submit_action(player.user_id, "supplement", action_tick=state.action_tick)
    assert state.game_status == "waiting_hand_action"
    assert player.hand == ["GX9"]
    await state.cleanup_game_state()


async def _exercise_multiple_ron() -> None:
    room = {"room_id": "654", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="multi-ron-test")
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX2", "AX3"]
    state.players[3].hand = ["AX2", "AX3"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    ron = {"id": "ron", "kind": "win", "priority": 7, "tiles": ["AX1"], "hand_tiles": []}
    state.claim_options = {1: [ron], 3: [ron]}
    state.claim_window = ClaimWindow(options=state.claim_options, pending=set())
    state.claim_window.submit(1, ron)
    state.claim_window.submit(3, ron)
    state.phase = "claim"

    await state._resolve_claims()

    assert state.phase == "round_end"
    assert state.round_result["winner_indices"] == [1, 3]
    assert state.dealer_index == 1
    if state._round_task:
        state._round_task.cancel()


async def _exercise_self_win_result() -> None:
    room = {"room_id": "777", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="self-win-result-test")
    player = state.players[0]
    player.hand = ["AX1", "AX2", "AX3"]
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = ["GX9"]

    await state.submit_action(player.user_id, "win", action_tick=state.action_tick)

    winner = state.round_result["winners"][0]
    assert winner["player"] == 0
    assert winner["partition"] == [["AX1", "AX2", "AX3"]]
    assert winner["points"] == player.score
    assert winner["base"] == 5
    assert winner["fans"]
    if state._round_task:
        state._round_task.cancel()


async def _exercise_discard_draw_event_batch() -> None:
    room = {"room_id": "888", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="event-batch-test")
    current = state.players[0]
    current.hand = ["AX1"]
    current.drawn_tile = "AX1"
    state.players[1].hand = []
    state.players[2].hand = []
    state.players[3].hand = []
    state.wall = ["GX9"]
    state.current_player_index = 0
    state.phase = "turn"

    await state.submit_action(current.user_id, "discard", tile="AX1", action_tick=state.action_tick)

    assert [event["type"] for event in state.events] == ["discard", "draw"]
    assert state.events[0]["cut_class"] is True
    assert state.events[0]["id"] < state.events[1]["id"]
    assert state.build_state(0)["events"][1]["tile"] is None
    assert state.build_state(1)["events"][1]["tile"] == "GX9"


async def _exercise_tactical_claim_apply_execute() -> None:
    """虹雀战术鸣牌：申请帧立即广播，不等其他玩家回应；最终执行帧静默。"""
    room = {"room_id": "999", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="tactical-claim-test")
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX2", "AX3", "BX9"]
    state.players[3].hand = ["AX2", "AX3", "CY9"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    seq = {
        "id": "call-0",
        "kind": "sequence",
        "base_kind": "sequence",
        "priority": 4,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    # 玩家 3 有更高优先级的碰但暂时不回应：单家吃申请仍应立即亮相，随后可被碰抢断。
    peng = {
        "id": "call-1",
        "kind": "triplet",
        "base_kind": "triplet",
        "priority": 5,
        "hand_tiles": ["AX1", "AX1"],
        "tiles": ["AX1", "AX1", "AX1"],
    }
    state.claim_options = {1: [seq], 3: [peng]}
    state.claim_responses = {}
    state.phase = "claim"
    state._start_claim_clock()

    await state._handle_claim_action(state.players[1], "claim", "call-0")

    assert state.claim_responses[1]["action"] == "claim"
    apply_events = [event for event in state.events if event["type"] == "claim_apply"]
    assert apply_events, "亮牌申请应立即广播，而不是等所有玩家回应"
    assert apply_events[-1]["player"] == 1
    assert apply_events[-1]["kind"] == "sequence"
    assert apply_events[-1]["tile"] == "AX1"
    assert 3 not in state.claim_responses

    await state._handle_claim_action(state.players[3], "pass", None)

    assert state.phase == "turn"
    meld_events = [event for event in state.events if event["type"] == "sequence"]
    assert meld_events and meld_events[-1].get("silent") is True
    assert state.players[1].melds[0]["tiles"] == ["AX1", "AX2", "AX3"]
    assert state.players[1].hand == ["BX9"]


async def _exercise_tactical_grace_reask() -> None:
    """国标对齐：低优先级申请后，打断窗口重新询问主询问已 pass 的更高优先级玩家。"""
    room = {"room_id": "998", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="tactical-reask-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.05
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX2", "AX3", "BX9"]
    state.players[3].hand = ["AX1", "AX1", "CY9"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    seq = {
        "id": "call-0",
        "kind": "sequence",
        "base_kind": "sequence",
        "priority": 4,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    peng = {
        "id": "call-1",
        "kind": "triplet",
        "base_kind": "triplet",
        "priority": 5,
        "hand_tiles": ["AX1", "AX1"],
        "tiles": ["AX1", "AX1", "AX1"],
    }
    state.claim_options = {1: [seq], 3: [peng]}
    state.claim_responses = {3: {"action": "pass"}}  # 主询问阶段已 pass
    state.phase = "claim"
    state._start_claim_clock()

    await state._handle_claim_action(state.players[1], "claim", "call-0")

    assert state._claim_grace_active
    assert state._claim_applied == (1, seq)
    if state._claim_grace_task:
        await state._claim_grace_task
    # 打断窗口撤销了已 pass 玩家 3 的回应，允许其改选更高优先级的碰。
    assert 3 not in state.claim_responses
    assert 3 in state.claim_options

    await state._handle_claim_action(state.players[3], "claim", "call-1")
    if state._claim_grace_task:
        await state._claim_grace_task

    assert state.phase == "turn"
    assert state.players[3].melds[0]["kind"] == "triplet"
    assert state.players[3].hand == ["CY9"]


async def _exercise_closer_chi_interrupts_farther() -> None:
    """虹雀吃按出牌者相对位置分档：出牌者的下家(chi_first) > 对家(chi_second) > 上家(chi_third)。

    出牌者为 0 号：下家(1)=chi_first，上家(3)=chi_third，高档可打断低档申请
    （含主询问已 pass 者重询）。
    """
    room = {"room_id": "995", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="closer-chi-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.05
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX2", "AX3", "BX9"]  # 距离 1，更近
    state.players[3].hand = ["AX2", "AX3", "CY9"]  # 距离 3，更远
    state.last_discard = {"player": 0, "tile": "AX1"}
    seq = {
        "id": "call-0",
        "kind": "sequence",
        "base_kind": "sequence",
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    # 出牌者为 0：下家(1)=chi_first(4)，上家(3)=chi_third(2)。
    state.claim_options = {
        1: [dict(seq, id="call-1", priority=4)],
        3: [dict(seq, id="call-2", priority=2)],
    }
    state.claim_responses = {1: {"action": "pass"}}  # 近位玩家主询问已 pass
    state.phase = "claim"
    state._start_claim_clock()

    # 上家(3, chi_third)先申请吃
    await state._handle_claim_action(state.players[3], "claim", "call-2")
    assert state._claim_applied == (3, dict(seq, id="call-2", priority=2))
    if state._claim_grace_task:
        await state._claim_grace_task
    # 下家(1, chi_first)的 pass 在打断窗口被撤销重询（高档吃 > 低档吃）
    assert 1 not in state.claim_responses

    # 下家改选吃，替换上家申请并执行
    await state._handle_claim_action(state.players[1], "claim", "call-1")
    if state._claim_grace_task:
        await state._claim_grace_task

    assert state.phase == "turn"
    assert state.players[1].melds[0]["tiles"] == ["AX1", "AX2", "AX3"]
    assert state.players[1].hand == ["BX9"]
    assert state.players[3].melds == []


async def _exercise_tactical_ron_apply() -> None:
    """国标对齐：荣和申请帧提前发声（kind=win），结算帧静默。"""
    room = {"room_id": "997", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="tactical-ron-apply-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    discarder = state.players[0]
    winner = state.players[1]
    discarder.hand = ["AX3"]
    winner.hand = ["AX1", "AX2"]
    winner.melds = [{
        "kind": "sequence",
        "tiles": ["BX4", "BX5", "BX6"],
        "from_player": 2,
        "claimed_tile": "BX4",
    }]
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = ["GX9"]

    await state.submit_action(discarder.user_id, "discard", tile="AX3", action_tick=state.action_tick)
    assert state.phase == "claim"
    ron = next(option for option in state.claim_options[1] if option["kind"] == "win")
    await state.submit_action(
        winner.user_id,
        "claim",
        candidate_id=ron["id"],
        action_tick=state.action_tick,
    )

    apply_events = [event for event in state.events if event["type"] == "claim_apply"]
    assert apply_events and apply_events[-1]["kind"] == "win"
    assert apply_events[-1]["player"] == 1
    if state._claim_grace_task:
        await state._claim_grace_task

    assert state.phase == "round_end"
    assert state.round_result["winner_indices"] == [1]
    assert state.round_result["silent"] is True
    if state._round_task:
        state._round_task.cancel()


async def _exercise_tactical_grace_timeout() -> None:
    """国标对齐：打断窗口超时后，未回应的更高优先级玩家视为放弃，执行当前申请。"""
    room = {"room_id": "996", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="tactical-grace-timeout-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX1", "AX1", "BX9"]
    state.players[3].hand = ["AX1", "AX1", "CY9"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    peng = {
        "id": "call-0",
        "kind": "triplet",
        "base_kind": "triplet",
        "priority": 5,
        "hand_tiles": ["AX1", "AX1"],
        "tiles": ["AX1", "AX1", "AX1"],
    }
    state.claim_options = {3: [dict(peng, id="call-1")], 1: [dict(peng, id="call-2")]}
    state.claim_responses = {}
    state.phase = "claim"
    state._start_claim_clock()

    # 远位玩家先申请碰；近位玩家（更高优先级）未回应，窗口超时后按当前申请执行。
    await state._handle_claim_action(state.players[3], "claim", "call-1")
    assert state._claim_grace_active
    if state._claim_grace_task:
        await state._claim_grace_task
    assert state._claim_grace_timeout_task is not None
    if state._claim_grace_timeout_task:
        await state._claim_grace_timeout_task

    assert state.phase == "turn"
    assert state.players[3].melds[0]["kind"] == "triplet"
    assert state.players[3].hand == ["CY9"]


async def _exercise_chi_claim_winner_discards_then_next_seat() -> None:
    """座位流转不变量：谁吃谁出牌，出完牌后由 (winner+1)%4 摸牌。

    对家吃 -> 对家出 -> 下家战术鸣牌吃 -> 下家出 -> 对家（下家下一家）摸牌。
    """
    room = {"room_id": "994", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="seat-flow-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    p = state.players
    # 0=我 1=下家 2=对家 3=上家
    p[3].hand = ["AX1"]
    p[2].hand = ["AX2", "AX3", "BX9"]
    p[1].hand = ["BX7", "BX8", "CY9"]
    p[0].hand = ["EX1", "EX5", "EX9"]
    state.current_player_index = 3
    state.phase = "turn"
    state.wall = ["GX1", "GX2", "GX3", "GX4", "GX5", "GX6", "GX7", "GX8", "GX9",
                  "FY1", "FY2", "FY3", "FY4", "FY5", "FY6", "FY7", "FY8", "FY9"]

    await state.submit_action(p[3].user_id, "discard", tile="AX1", action_tick=state.action_tick)
    chi = next(c for c in state.claim_options[2] if c["kind"] == "sequence")
    await state.submit_action(p[2].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    assert state.current_player_index == 2

    await state.submit_action(p[2].user_id, "discard", tile="BX9", action_tick=state.action_tick)
    assert state.phase == "claim" and 1 in state.claim_options
    chi = next(c for c in state.claim_options[1] if c["kind"] == "sequence")
    await state.submit_action(p[1].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    assert state.current_player_index == 1

    await state.submit_action(p[1].user_id, "discard", tile="CY9", action_tick=state.action_tick)
    if state.phase == "claim":
        for pid in list(state.claim_options):
            if pid not in state.claim_responses:
                await state.submit_action(state.players[pid].user_id, "pass", action_tick=state.action_tick)
    draw = [event for event in state.events if event["type"] == "draw"]
    assert state.current_player_index == 2, f"下家打出后应轮到对家，实际 {state.current_player_index}"
    assert draw and draw[-1]["player"] == 2


async def _exercise_chi_priority_by_discarder_relative_seat() -> None:
    """出牌者相对位置分档：对家(2)出牌时，上家(3)是出牌者的下家=chi_first。

    下家(1)（出牌者的上家=chi_third）先申请，上家(chi_first)在打断窗口抢断获胜，
    上家打出后摸牌流转到 (上家+1)%4 = 0（我）。
    """
    room = {"room_id": "993", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="discarder-relative-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    p = state.players
    # 0=我 1=下家 2=对家(出牌方) 3=上家（对家的下家，吃档最高）
    p[3].hand = ["AX1"]
    p[2].hand = ["AX2", "AX3", "BX9"]
    p[1].hand = ["BX7", "BX8", "CY9"]
    p[0].hand = ["EX1", "EX5", "EX9"]
    state.current_player_index = 3
    state.phase = "turn"
    state.wall = ["GX1", "GX2", "GX3", "GX4", "GX5", "GX6", "GX7", "GX8", "GX9",
                  "FY1", "FY2", "FY3", "FY4", "FY5", "FY6", "FY7", "FY8", "FY9"]

    await state.submit_action(p[3].user_id, "discard", tile="AX1", action_tick=state.action_tick)
    chi = next(c for c in state.claim_options[2] if c["kind"] == "sequence")
    await state.submit_action(p[2].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)

    # 对家出 BX9：下家(1)（d=3, chi_third）与上家(3)（d=1, chi_first）都能吃。
    p[3].hand = ["BX7", "BX8", "EX9"]
    await state.submit_action(p[2].user_id, "discard", tile="BX9", action_tick=state.action_tick)
    assert state.phase == "claim" and 1 in state.claim_options and 3 in state.claim_options
    # 下家(1, chi_third)先申请：低档先亮相。
    chi = next(c for c in state.claim_options[1] if c["kind"] == "sequence")
    await state.submit_action(p[1].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    # 上家(3, chi_first)进入打断窗口（优先级更高），随后抢断获胜。
    assert 3 in state.claim_options and 3 not in state.claim_responses
    chi = next(c for c in state.claim_options[3] if c["kind"] == "sequence")
    await state.submit_action(p[3].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    assert state.current_player_index == 3
    assert p[3].melds and p[3].melds[0]["kind"] == "sequence"
    assert p[1].melds == []

    # 上家(3)打出后，摸牌应流转到 (3+1)%4 = 0（我）。
    await state.submit_action(p[3].user_id, "discard", tile="EX9", action_tick=state.action_tick)
    if state.phase == "claim":
        for pid in list(state.claim_options):
            if pid not in state.claim_responses:
                await state.submit_action(state.players[pid].user_id, "pass", action_tick=state.action_tick)
    draw = [event for event in state.events if event["type"] == "draw"]
    assert state.current_player_index == 0
    assert draw and draw[-1]["player"] == 0


async def _exercise_reported_chi_sequence() -> None:
    """反馈场景回归：上家(3)出牌，对家(2)/下家(1)/我(0)都能吃。

    按出牌者相对位置分档：我(0, 出牌者下家)=chi_first、下家(1, 出牌者对家)=chi_second、
    对家(2, 出牌者上家)=chi_third。对家先申请、下家后申请 → 下家获胜；
    我作为最高档在打断窗口内被等待（不再被直接抢走），未操作则窗口超时后下家执行，
    下家打出 → 摸牌流转到 (下家+1)%4 = 对家。
    """
    room = {"room_id": "991", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="reported-sequence-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    p = state.players
    # 0=我 1=下家 2=对家 3=上家
    p[3].hand = ["AX1"]                          # 上家出 AX1
    p[2].hand = ["AX2", "AX3", "BX9"]            # 对家可吃 AX1（chi_third）
    p[1].hand = ["AX2", "AX3", "CY9"]            # 下家可吃 AX1（chi_second）
    p[0].hand = ["AX2", "AX3", "EX9"]            # 我可吃 AX1（chi_first，最高）
    state.current_player_index = 3
    state.phase = "turn"
    state.wall = ["GX1", "GX2", "GX3", "GX4", "GX5", "GX6", "GX7", "GX8", "GX9",
                  "FY1", "FY2", "FY3", "FY4", "FY5", "FY6", "FY7", "FY8", "FY9"]

    await state.submit_action(p[3].user_id, "discard", tile="AX1", action_tick=state.action_tick)
    assert state.phase == "claim" and 0 in state.claim_options and 1 in state.claim_options
    assert 2 in state.claim_options
    # 对家(2, 最低档)先战鸣申请。
    chi = next(c for c in state.claim_options[2] if c["kind"] == "sequence")
    await state.submit_action(p[2].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    if state._claim_grace_task:
        await state._claim_grace_task
    # 下家(1, chi_second)后申请，覆盖对家。
    chi = next(c for c in state.claim_options[1] if c["kind"] == "sequence")
    await state.submit_action(p[1].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    if state._claim_grace_task:
        await state._claim_grace_task
    # 我(0, chi_first)仍在打断窗口等待集合中——不被直接抢走。
    assert 0 in state._claim_grace_pending()
    assert state._claim_grace_timeout_task is not None
    if state._claim_grace_timeout_task:
        await state._claim_grace_timeout_task
    if state._claim_grace_task:
        await state._claim_grace_task
    assert state.current_player_index == 1
    assert p[1].melds and p[1].hand == ["CY9"]
    assert p[2].melds == []

    # 下家打出后，摸牌必须是 (1+1)%4 = 2（对家）。
    await state.submit_action(p[1].user_id, "discard", tile="CY9", action_tick=state.action_tick)
    if state.phase == "claim":
        for pid in list(state.claim_options):
            if pid not in state.claim_responses:
                await state.submit_action(state.players[pid].user_id, "pass", action_tick=state.action_tick)
    draw = [event for event in state.events if event["type"] == "draw"]
    assert state.current_player_index == 2
    assert draw and draw[-1]["player"] == 2


async def _exercise_two_smart_bots_competing_for_chi() -> None:
    """合法唯一牌组：两名智能 AI 抢吃，只播放/执行赢家，随后由赢家下一家摸牌。"""
    room = {"room_id": "two-ai-chi", "game_round": 1, "player_list": [101, 2, 3, 104]}
    state = HongqueGameState(None, room, gamestate_id="two-ai-chi")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    state._schedule_bot_if_needed = lambda: None
    players = state.players
    # 0 打 AX3；AI 1 用 AX1+AX2，AI 2 用 AX4+AX5，牌张互不重复。
    players[0].hand = ["AX3"]
    players[1].hand = ["AX1", "AX2", "BX4", "BX5", "BX6", "CY9"]
    players[2].hand = ["AX4", "AX5", "BY1", "BY2", "BY3", "EX9"]
    players[3].hand = ["GX9"]
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = ["FY1", "FY2", "FY3", "FY4", "FY5"]

    async def inline_bot_cpu(game_state, func, *args, **kwargs):
        return func(*args, **kwargs)

    with patch.object(hongque_get_action, "run_room_bot_cpu", inline_bot_cpu), \
            patch.object(hongque_get_action, "BOT_ACTION_DELAY", 0.01):
        await state.submit_action(
            players[0].user_id, "discard", tile="AX3", action_tick=state.action_tick
        )
        bot_tasks = list(state._bot_claim_tasks.values())
        if bot_tasks:
            await asyncio.gather(*bot_tasks, return_exceptions=True)
        # 低档吃申请会按新 action_tick 为更高档机器人创建新的决策任务。
        while state.phase == "claim" and state._bot_claim_tasks:
            await asyncio.gather(
                *list(state._bot_claim_tasks.values()), return_exceptions=True
            )
        if state.phase == "claim" and state._claim_timeout_task is not None:
            await asyncio.gather(state._claim_timeout_task, return_exceptions=True)

    assert state.current_player_index == 1
    assert state.players[1].melds and state.players[1].melds[0]["tiles"] == ["AX1", "AX2", "AX3"]
    assert state.players[2].melds == []
    # 更高档机器人必须成为最终赢家；若它先提交，低档申请可不必亮相。
    assert 1 in state._claim_apply_broadcast
    assert set(state._claim_apply_broadcast) <= {1, 2}
    sequence_events = [event for event in state.events if event["type"] == "sequence"]
    assert sequence_events and sequence_events[-1]["player"] == 1
    assert sequence_events[-1].get("silent") is True

    await state.submit_action(
        players[1].user_id, "discard", tile="CY9", action_tick=state.action_tick
    )
    draw_events = [event for event in state.events if event["type"] == "draw"]
    assert state.current_player_index == 2
    assert draw_events and draw_events[-1]["player"] == 2
    await state.cleanup_game_state()


async def _exercise_debug_double_ron() -> None:
    """Debug 两家和牌：上家(3)首打 AX1，自家(0)与下家(1)同时荣和。"""
    room = {"room_id": "989", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="debug-double-ron")
    state.Debug = True
    state.debug_scenario = "double_ron"
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    await state._start_round()
    p = state.players
    # 庄家/首打固定为上家(3)，首打牌张为 AX1。
    assert state.dealer_index == 3
    assert state.current_player_index == 3
    assert "AX1" in p[3].hand
    # 上家不可天和（含重复牌，无法成和）。
    actions, _ = state._legal_turn_actions(p[3])
    assert "win" not in actions
    # 开局摸牌事件与覆盖后的手牌一致，避免客户端白牌/多一张。
    opening_draw = next(
        event for event in state.events
        if event["type"] == "draw" and event["player"] == 3
    )
    assert opening_draw["tile"] == p[3].drawn_tile
    assert p[3].drawn_tile in p[3].hand

    await state.submit_action(p[3].user_id, "discard", tile="AX1", action_tick=state.action_tick)
    assert state.phase == "claim"
    ron0 = next(c for c in state.claim_options[0] if c["kind"] == "win")
    ron1 = next(c for c in state.claim_options[1] if c["kind"] == "win")
    assert all(
        c["kind"] != "win" for c in state.claim_options.get(2, ())
    ), "对家不应有荣和选项"

    # 自家与下家同时荣和 → 两家和牌。
    await asyncio.gather(
        state.submit_action(p[0].user_id, "claim", candidate_id=ron0["id"], action_tick=state.action_tick),
        state.submit_action(p[1].user_id, "claim", candidate_id=ron1["id"], action_tick=state.action_tick),
    )
    if state._claim_timeout_task:
        await state._claim_timeout_task

    assert state.phase == "round_end"
    assert sorted(state.round_result["winner_indices"]) == [0, 1]
    assert state.round_result["multi_ron"] is True
    # 自家点确定（ready）→ 服务端播完中间面板后推进下一局，验证两家和牌不会卡死。
    await state.submit_action(p[0].user_id, "ready", action_tick=state.action_tick)
    if state._round_task:
        await asyncio.wait_for(state._round_task, timeout=40)
    assert state.current_round == 2
    if state._round_task:
        state._round_task.cancel()


async def _exercise_debug_tactical_all_claims() -> None:
    """玩家1打 AX1，三个真人都可吃碰和，服务端不得代替任何人操作。"""
    room = {"room_id": "987", "game_round": 1,
            "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="debug-tactical-all")
    state.Debug = True
    state.debug_scenario = "tactical_all_claims"
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.2
    await state._start_round()
    players = state.players

    assert state.dealer_index == 0
    assert state.current_player_index == 0
    assert "AX1" in players[0].hand

    await state.submit_action(
        players[0].user_id, "discard", tile="AX1", action_tick=state.action_tick
    )
    assert state.phase == "claim"
    assert set(state.claim_options) == {1, 2, 3}
    for player_index in (1, 2, 3):
        claim_kinds = {item["kind"] for item in state.claim_options[player_index]}
        assert {"sequence", "triplet", "win"} <= claim_kinds

    # 四个真人实例时没有机器人鸣牌任务，也没有服务器伪造的响应或动画。
    await asyncio.sleep(0.05)
    assert not state._bot_claim_tasks
    assert state.claim_responses == {}
    assert state._claim_apply_broadcast == {}
    assert state.phase == "claim"
    await state.cleanup_game_state()


async def _exercise_debug_ones_nines() -> None:
    """自家庄固定起手：1 色为主、下家 GY 顺、对家 AX 顺、上家 9 色为主。"""
    room = {"room_id": "986", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="debug-ones-nines")
    state.Debug = True
    state.debug_scenario = "ones_nines"
    await state._start_round()
    p = state.players
    assert state.dealer_index == 0
    assert state.current_player_index == 0
    assert p[0].hand == [
        "AY1", "BX1", "BY1", "CX1", "CY1", "DX1",
        "DY1", "EX1", "EY1", "FX1", "FY1", "GX9",
    ]
    assert p[0].drawn_tile == "GX9"
    assert p[1].hand == [
        "GX1", "GX5",
        "GY1", "GY2", "GY3", "GY4", "GY5", "GY6", "GY7", "GY8", "GY9",
    ]
    assert p[2].hand == [
        "AX1", "AX2", "AX3", "AX4", "AX5", "AX6", "AX7", "AX8", "AX9",
        "GX7", "GX8",
    ]
    assert p[3].hand == [
        "AY9", "BX9", "BY9", "CX9", "CY9", "DX9",
        "DY9", "EX9", "EY9", "FX9", "FY9",
    ]
    used = {tile for player in p for tile in player.hand}
    assert not used.intersection(state.wall)
    opening_draw = next(
        event for event in state.events
        if event["type"] == "draw" and event["player"] == 0
    )
    assert opening_draw["tile"] == "GX9"
    await state.cleanup_game_state()


async def _exercise_claim_upgrade_to_higher() -> None:
    """川麻模式：自己先碰（中优先级），别人也碰，自己仍可升级为和（高优先级）。"""
    room = {"room_id": "988", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="claim-upgrade-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    p = state.players
    # 0=我 1=下家 2=对家 3=上家
    p[3].hand = ["AX1", "AX2", "AX2", "BX1", "BX2", "BX3",
                 "CX1", "CX2", "CX3", "DX1", "DX2", "DX3"]  # 上家首打 AX1（含重复，不可和）
    p[0].hand = ["BX1", "CX1", "AX2", "AX3", "AX4",
                 "BX5", "BX6", "BX7", "CX5", "CX6", "CX7"]  # 我可碰 AX1 也可和 AX1
    p[1].hand = ["BX1", "CX1", "DX2", "DX3", "DX4",
                 "EX5", "EX6", "EX7", "FY1", "FY2", "FY4"]  # 下家可碰 AX1（FY 组不完整，不可和）
    p[2].hand = ["BX5", "BX6", "BX7", "CX1", "CX2", "CX3",
                 "DX1", "DX2", "DX3", "EX1", "EX1"]  # 对家无操作
    state.current_player_index = 3
    state.phase = "turn"
    state.wall = ["GX1", "GX2", "GX3", "GX4", "GX5", "GX6", "GX7", "GX8", "GX9",
                  "FY1", "FY2", "FY3", "FY4", "FY5", "FY6", "FY7", "FY8", "FY9"]

    await state.submit_action(p[3].user_id, "discard", tile="AX1", action_tick=state.action_tick)
    assert state.phase == "claim"
    assert any(c["kind"] == "triplet" for c in state.claim_options[0])
    assert any(c["kind"] == "win" for c in state.claim_options[0])
    assert any(c["kind"] == "triplet" for c in state.claim_options[1])

    # 我（自家）先碰（中优先级）。
    peng0 = next(c for c in state.claim_options[0] if c["kind"] == "triplet")
    await state.submit_action(p[0].user_id, "claim", candidate_id=peng0["id"], action_tick=state.action_tick)
    if state._claim_grace_task:
        await state._claim_grace_task
    # 下家也碰（别人执行中优先级操作）。
    peng1 = next(c for c in state.claim_options[1] if c["kind"] == "triplet")
    await state.submit_action(p[1].user_id, "claim", candidate_id=peng1["id"], action_tick=state.action_tick)
    if state._claim_grace_task:
        await state._claim_grace_task

    # 川麻模式：我仍可升级为和（更高优先级按钮重新弹出）。
    assert 0 in state._claim_upgrade_players
    win0 = next(c for c in state.claim_options[0] if c["kind"] == "win")
    await state.submit_action(p[0].user_id, "claim", candidate_id=win0["id"], action_tick=state.action_tick)
    if state._claim_grace_task:
        await state._claim_grace_task
    if state._claim_grace_timeout_task:
        await state._claim_grace_timeout_task

    assert state.phase == "round_end"
    assert state.round_result["winner_indices"] == [0]
    assert p[0].melds == []  # 荣和不改手牌，直接结算
    if state._round_task:
        state._round_task.cancel()


async def _exercise_head_bump_ron() -> None:
    """头跳：多家荣和时只保留距出牌者最近的一家，其余截和。"""
    room = {"room_id": "990", "game_round": 1, "player_list": [101, 102, 103, 104],
            "hepai_way": "head_bump"}
    state = HongqueGameState(None, room, gamestate_id="head-bump-test")
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX2", "AX3"]
    state.players[3].hand = ["AX2", "AX3"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    ron = {"id": "ron", "kind": "win", "priority": 7, "tiles": ["AX1"], "hand_tiles": []}
    state.claim_options = {1: [ron], 3: [ron]}
    state.claim_window = ClaimWindow(options=state.claim_options, pending=set())
    state.claim_window.submit(1, ron)
    state.claim_window.submit(3, ron)
    state.phase = "claim"

    await state._resolve_claims()

    assert state.phase == "round_end"
    assert state.round_result["winner_indices"] == [1]  # 距出牌者 1 最近，截和
    assert state.round_result["multi_ron"] is False
    if state._round_task:
        state._round_task.cancel()


async def _exercise_multi_ron_flag() -> None:
    """多家和：所有可荣和者全部结算，round_result 标记 multi_ron=True。"""
    room = {"room_id": "989", "game_round": 1, "player_list": [101, 102, 103, 104],
            "hepai_way": "multi_ron"}
    state = HongqueGameState(None, room, gamestate_id="multi-ron-flag-test")
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX2", "AX3"]
    state.players[3].hand = ["AX2", "AX3"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    ron = {"id": "ron", "kind": "win", "priority": 7, "tiles": ["AX1"], "hand_tiles": []}
    state.claim_options = {1: [ron], 3: [ron]}
    state.claim_window = ClaimWindow(options=state.claim_options, pending=set())
    state.claim_window.submit(1, ron)
    state.claim_window.submit(3, ron)
    state.phase = "claim"

    await state._resolve_claims()

    assert state.phase == "round_end"
    assert sorted(state.round_result["winner_indices"]) == [1, 3]
    assert state.round_result["multi_ron"] is True
    if state._round_task:
        state._round_task.cancel()


async def _exercise_kong_extends_open_meld() -> None:
    """明牌加杠：吃副露 3→4→5→6 逐步扩展，手牌只移除杠入张，不触发额外摸牌。"""
    room = {"room_id": "992", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="kong-step-test")
    p = state.players[0]
    p.melds = [{
        "kind": "sequence",
        "tiles": ["AX1", "AX2", "AX3"],
        "from_player": 1,
        "claimed_tile": "AX1",
    }]
    p.hand = ["AX4", "AX5", "AX6", "BX9", "CX9", "DX9", "EX9", "FX9", "GX9",
              "GY9", "BY1", "CY1", "DY1"]
    state.current_player_index = 0
    state.phase = "turn"
    initial_hand = len(p.hand)

    for expected_len in (4, 5, 6):
        actions, candidates = state._legal_turn_actions(p)
        assert "kong" in actions
        kong = [c for c in candidates if c["kind"] == "kong" and len(c["tiles"]) == expected_len]
        assert kong, f"缺少 {expected_len} 张杠候选: {candidates}"
        candidate = kong[0]
        added = len(candidate["hand_tiles"])
        state._apply_kong(p, candidate)
        assert len(p.melds[0]["tiles"]) == expected_len
        assert p.melds[0]["kind"] == "sequence"
        assert len(p.hand) == initial_hand - added
        initial_hand = len(p.hand)

    # 已到 6 张最长组，不再有杠候选。
    actions, candidates = state._legal_turn_actions(p)
    assert all(c["kind"] != "kong" for c in candidates)


async def _exercise_supplement_turn_clock() -> None:
    room = {
        "room_id": "889",
        "game_round": 1,
        "round_timer": 20,
        "step_timer": 5,
        "player_list": [101, 102, 103, 104],
    }
    state = HongqueGameState(None, room, gamestate_id="turn-clock-test")
    player = state.players[0]
    player.hand = ["AX1"]
    state.wall = ["GX9"]
    state.current_player_index = 0
    state.phase = "turn"
    state._start_turn_clock()
    state.turn_started_at -= 8
    state.turn_deadline -= 8

    await state.submit_action(player.user_id, "supplement", action_tick=state.action_tick)

    snapshot = state.build_state(0)
    assert player.remaining_time == 17  # 8 seconds used - 5 seconds step time
    assert snapshot["remaining_time"] == 17
    assert snapshot["step_remaining"] == 5
    assert snapshot["round_time"] == 20
    assert snapshot["step_time"] == 5


async def _exercise_group_wait_self_win() -> None:
    room = {"room_id": "890", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="group-wait-win-test")
    player = state.players[0]
    player.hand = ["AX1", "AX2", "AX3"]
    player.melds = [{
        "kind": "sequence",
        "tiles": ["BX4", "BX5", "BX6"],
        "from_player": 1,
        "claimed_tile": "BX4",
    }]
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = ["GX9"]

    snapshot = state.build_state(0)
    assert "win" in snapshot["legal_actions"]
    assert snapshot["win_hint"]["points"] > 0
    assert snapshot["win_hint"]["base"] > 0

    await state.submit_action(player.user_id, "win", action_tick=state.action_tick)

    assert state.phase == "round_end"
    assert state.round_result["winners"][0]["pair"] == []
    assert state.round_result["winners"][0]["groups"] == [
        ["AX1", "AX2", "AX3"],
        ["BX4", "BX5", "BX6"],
    ]
    if state._round_task:
        state._round_task.cancel()


async def _exercise_group_wait_ron() -> None:
    room = {"room_id": "891", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="group-wait-ron-test")
    discarder = state.players[0]
    winner = state.players[1]
    discarder.hand = ["AX3"]
    winner.hand = ["AX1", "AX2"]
    winner.melds = [{
        "kind": "sequence",
        "tiles": ["BX4", "BX5", "BX6"],
        "from_player": 2,
        "claimed_tile": "BX4",
    }]
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = ["GX9"]
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01

    await state.submit_action(discarder.user_id, "discard", tile="AX3", action_tick=state.action_tick)

    assert state.phase == "claim"
    ron = next(option for option in state.claim_options[1] if option["kind"] == "win")
    snapshot = state.build_state(1)
    assert snapshot["win_hint"]["points"] > 0
    await state.submit_action(
        winner.user_id,
        "claim",
        candidate_id=ron["id"],
        action_tick=state.action_tick,
    )
    # 战术鸣牌：申请后由后台任务开打断窗口，等待其完成后执行（荣和无更高竞争者在窗口内直接结算）。
    for player_index in list(state.claim_options):
        if player_index != 1 and player_index not in state.claim_responses:
            await state.submit_action(
                state.players[player_index].user_id,
                "pass",
                action_tick=state.action_tick,
            )

    assert state.phase == "round_end"
    assert state.round_result["winner_indices"] == [1]
    assert state.round_result["winners"][0]["pair"] == []
    if state._round_task:
        state._round_task.cancel()


def test_win_only_when_hand_is_the_last_tile() -> None:
    asyncio.run(_exercise_win_only_when_hand_is_last_tile())


def test_win_and_plain_kong_are_offered_separately() -> None:
    asyncio.run(_exercise_win_and_plain_kong_are_offered_separately())


def test_supplement_reverts_to_plain_kong_only() -> None:
    asyncio.run(_exercise_supplement_reverts_to_plain_kong_only())


def test_win_via_kong_finishes_round_without_moving_hand() -> None:
    asyncio.run(_exercise_win_via_kong_finishes_round())


def test_ron_cannot_use_kong_win_decomposition() -> None:
    asyncio.run(_exercise_ron_cannot_use_kong_win())


async def _exercise_win_only_when_hand_is_last_tile() -> None:
    room = {"room_id": "893", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="win-last-tile")
    player = state.players[0]
    player.hand = ["AX4"]
    player.melds = [{
        "kind": "sequence",
        "tiles": ["AX1", "AX2", "AX3"],
        "from_player": 1,
        "claimed_tile": "AX1",
    }]
    player.drawn_tile = "AX4"
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = []

    actions, candidates = state._legal_turn_actions(player)
    # 手牌只剩最后一张（自摸/补牌张）时只下发“和”，普通杠无独立意义。
    assert "win" in actions
    assert "kong" not in actions
    assert candidates == []


async def _exercise_win_and_plain_kong_are_offered_separately() -> None:
    room = {"room_id": "894", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="win-vs-kong")
    player = state.players[0]
    player.hand = ["AX4", "AX7", "BX7", "CX7"]
    player.melds = [{
        "kind": "sequence",
        "tiles": ["AX1", "AX2", "AX3"],
        "from_player": 1,
        "claimed_tile": "AX1",
    }]
    player.drawn_tile = "AX4"
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = []

    actions, candidates = state._legal_turn_actions(player)
    # 同一张 AX4 既能“杠”也能直接“和”（杠和并入和），两者分开下发。
    assert "win" in actions
    assert "kong" in actions
    assert any(candidate["kind"] == "kong" for candidate in candidates)


async def _exercise_supplement_reverts_to_plain_kong_only() -> None:
    room = {"room_id": "895", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="kong-win-supplement")
    player = state.players[0]
    player.hand = ["AX4", "AX7"]
    player.melds = [{
        "kind": "sequence",
        "tiles": ["AX1", "AX2", "AX3"],
        "from_player": 1,
        "claimed_tile": "AX1",
    }]
    player.drawn_tile = "AX7"
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = ["GX9"]

    actions, candidates = state._legal_turn_actions(player)
    # 补牌后手牌变两张，杠完剩余 AX7 不能和，只允许普通杠、不再有“和”。
    assert "kong" in actions
    assert "win" not in actions
    assert all(candidate["kind"] == "kong" for candidate in candidates)

    await state.submit_action(player.user_id, "supplement", action_tick=state.action_tick)
    # 补牌后服务端重新询问：回合仍属于该玩家，动作按新手牌重算。
    assert state.phase == "turn"
    assert state.current_player_index == 0
    actions_after, candidates_after = state._legal_turn_actions(player)
    assert "kong" in actions_after
    assert "win" not in actions_after
    assert player.hand == ["AX4", "AX7", "GX9"]


async def _exercise_win_via_kong_finishes_round() -> None:
    room = {"room_id": "896", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="win-via-kong-action")
    player = state.players[0]
    player.hand = ["AX4"]
    player.melds = [{
        "kind": "sequence",
        "tiles": ["AX1", "AX2", "AX3"],
        "from_player": 1,
        "claimed_tile": "AX1",
    }]
    player.drawn_tile = "AX4"
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = []

    actions, candidates = state._legal_turn_actions(player)
    assert "win" in actions
    await state.submit_action(player.user_id, "win", action_tick=state.action_tick)

    assert state.phase == "round_end"
    assert state.round_result["reason"] == "self_draw"
    winner = state.round_result["winners"][0]
    # 杠和并入普通和：手牌保持完整展示，明牌不实际移动，和牌拆解按扩展后的明牌计。
    assert winner["hand"] == ["AX4"]
    assert winner["melds"][0]["tiles"] == ["AX1", "AX2", "AX3"]
    assert winner["partition"] == []
    assert winner["groups"] == [["AX1", "AX2", "AX3", "AX4"]]
    # 摸最后一张墙牌自摸：海底只按自摸计算。
    assert any(fan["name"] == "海底" for fan in winner["fans"])
    if state._round_task:
        state._round_task.cancel()


async def _exercise_ron_cannot_use_kong_win() -> None:
    room = {"room_id": "898", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="ron-no-kong-win")
    discarder = state.players[0]
    winner = state.players[1]
    chi_player = state.players[3]
    discarder.hand = ["AX7"]
    winner.hand = ["AX1", "AX2", "AX3"]
    winner.melds = [{
        "kind": "sequence",
        "tiles": ["AX4", "AX5", "AX6"],
        "from_player": 2,
        "claimed_tile": "AX4",
    }]
    # 第三家有吃牌候选，保证弃牌询问窗开启。
    chi_player.hand = ["AX5", "AX6", "GX9"]
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = ["GX9"]
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01

    await state.submit_action(discarder.user_id, "discard", tile="AX7", action_tick=state.action_tick)

    # AX7 只能并入自家明牌（杠和）成和，不能荣和，因此询问窗不出现“和”。
    assert state.phase == "claim"
    options = state.claim_options.get(1, ())
    assert all(option.get("kind") != "win" for option in options)
    assert any(option.get("kind") == "sequence" for option in state.claim_options.get(3, ()))
