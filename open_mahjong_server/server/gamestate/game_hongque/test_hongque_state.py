import asyncio

from .HongqueGameState import HongqueGameState


def test_round_deal_discard_and_next_draw_stay_memory_only() -> None:
    asyncio.run(_exercise_round())


def test_claimed_tile_moves_from_river_into_open_meld() -> None:
    asyncio.run(_exercise_claim())


def test_multiple_discards_advance_players_and_wall() -> None:
    asyncio.run(_exercise_turn_progression())


def test_complete_hand_can_win_immediately_after_a_call() -> None:
    asyncio.run(_exercise_self_win_qualification())


def test_multiple_ron_winners_and_nearest_winner_becomes_dealer() -> None:
    asyncio.run(_exercise_multiple_ron())


def test_self_win_builds_explainable_settlement_result() -> None:
    asyncio.run(_exercise_self_win_result())


def test_discard_and_following_draw_are_ordered_visible_events() -> None:
    asyncio.run(_exercise_discard_draw_event_batch())


def test_tactical_claim_apply_is_broadcast_before_other_players_respond() -> None:
    asyncio.run(_exercise_tactical_claim_apply_execute())


def test_tactical_claim_grace_reasks_main_ask_passer() -> None:
    asyncio.run(_exercise_tactical_grace_reask())


def test_closer_chi_interrupts_farther_chi_application() -> None:
    asyncio.run(_exercise_closer_chi_interrupts_farther())


def test_tactical_ron_apply_frame_and_silent_settlement() -> None:
    asyncio.run(_exercise_tactical_ron_apply())


def test_tactical_grace_timeout_executes_applied_claim() -> None:
    asyncio.run(_exercise_tactical_grace_timeout())


def test_chi_claim_winner_discards_then_next_seat_draws() -> None:
    asyncio.run(_exercise_chi_claim_winner_discards_then_next_seat())


def test_closer_chi_interrupt_winner_flows_to_next_seat() -> None:
    asyncio.run(_exercise_closer_chi_interrupt_winner_flows_to_next_seat())


def test_kong_extends_open_meld_stepwise() -> None:
    asyncio.run(_exercise_kong_extends_open_meld())


def test_supplement_consumes_only_overtime_and_restarts_step_time() -> None:
    asyncio.run(_exercise_supplement_turn_clock())


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
    assert 24.0 < state.claim_deadline - state.claim_started_at <= 25.0


def test_stable_tenpai_snapshot_contains_authoritative_score_hints() -> None:
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
    hints = {hint["tile"]: hint for hint in snapshot["waiting_hints"]}
    assert "AX3" in hints
    assert hints["AX3"]["points"] > 0
    assert hints["AX3"]["base"] > 0
    # 只有同数字两张的“对子待张”不构成听牌。
    assert "CY5" not in hints


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
        "priority": 1,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    state.claim_options = {1: [candidate]}
    state.claim_responses = {1: {"action": "claim", "candidate": candidate}}
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
    candidate = {
        "id": "call-0",
        "kind": "sequence",
        "priority": 1,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    state.claim_options = {1: [candidate]}
    state.claim_responses = {1: {"action": "claim", "candidate": candidate}}
    state.phase = "claim"

    await state._resolve_claims()

    assert player.hand == []
    assert state.build_state(1)["legal_actions"] == ["win"]
    await state.submit_action(player.user_id, "win", action_tick=state.action_tick)
    assert state.phase == "round_end"
    assert state.round_result["winners"][0]["groups"] == [["AX1", "AX2", "AX3"]]
    if state._round_task:
        state._round_task.cancel()


async def _exercise_multiple_ron() -> None:
    room = {"room_id": "654", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="multi-ron-test")
    state.players[0].discards = ["AX1"]
    state.players[1].hand = ["AX2", "AX3"]
    state.players[3].hand = ["AX2", "AX3"]
    state.last_discard = {"player": 0, "tile": "AX1"}
    ron = {"id": "ron", "kind": "win", "priority": 4, "tiles": ["AX1"], "hand_tiles": []}
    state.claim_options = {1: [ron], 3: [ron]}
    state.claim_responses = {
        1: {"action": "claim", "candidate": ron},
        3: {"action": "claim", "candidate": ron},
    }
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
        "priority": 1,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    # 玩家 3 也有可选亮牌，但暂时不回应：申请帧不应等待其回应。
    state.claim_options = {1: [seq], 3: [dict(seq, id="call-1")]}
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
        "priority": 1,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    peng = {
        "id": "call-1",
        "kind": "triplet",
        "base_kind": "triplet",
        "priority": 2,
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
    """虹雀近位优先：更近的吃按更高优先级处理，可打断远处吃的申请（含主询问已 pass 者重询）。"""
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
        "priority": 1,
        "hand_tiles": ["AX2", "AX3"],
        "tiles": ["AX1", "AX2", "AX3"],
    }
    state.claim_options = {1: [dict(seq, id="call-1")], 3: [dict(seq, id="call-2")]}
    state.claim_responses = {1: {"action": "pass"}}  # 近位玩家主询问已 pass
    state.phase = "claim"
    state._start_claim_clock()

    # 远位玩家先申请吃
    await state._handle_claim_action(state.players[3], "claim", "call-2")
    assert state._claim_applied == (3, dict(seq, id="call-2"))
    if state._claim_grace_task:
        await state._claim_grace_task
    # 近位玩家的 pass 在打断窗口被撤销重询（近位吃 > 远位吃）
    assert 1 not in state.claim_responses

    # 近位玩家改选吃，替换远位申请并执行
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
        "priority": 2,
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
    if state._claim_grace_task:
        await state._claim_grace_task
    assert state.current_player_index == 2

    await state.submit_action(p[2].user_id, "discard", tile="BX9", action_tick=state.action_tick)
    assert state.phase == "claim" and 1 in state.claim_options
    chi = next(c for c in state.claim_options[1] if c["kind"] == "sequence")
    await state.submit_action(p[1].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    if state._claim_grace_task:
        await state._claim_grace_task
    assert state.current_player_index == 1

    await state.submit_action(p[1].user_id, "discard", tile="CY9", action_tick=state.action_tick)
    if state.phase == "claim":
        for pid in list(state.claim_options):
            if pid not in state.claim_responses:
                await state.submit_action(state.players[pid].user_id, "pass", action_tick=state.action_tick)
    draw = [event for event in state.events if event["type"] == "draw"]
    assert state.current_player_index == 2, f"下家打出后应轮到对家，实际 {state.current_player_index}"
    assert draw and draw[-1]["player"] == 2


async def _exercise_closer_chi_interrupt_winner_flows_to_next_seat() -> None:
    """近位优先：远处下家先战鸣申请，更近的上家抢断获胜后，摸牌仍按胜者下一家流转。"""
    room = {"room_id": "993", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="closer-interrupt-seat-test")
    state.tactical_pre_grace_delay = 0.0
    state.tactical_grace_seconds = 0.01
    p = state.players
    # 0=我 1=下家(远 d=3) 2=对家(出牌方) 3=上家(近 d=1)
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
    if state._claim_grace_task:
        await state._claim_grace_task

    # 对家出 BX9：下家(1)与上家(3)都能吃，上家更近。
    p[3].hand = ["BX7", "BX8", "EX9"]
    await state.submit_action(p[2].user_id, "discard", tile="BX9", action_tick=state.action_tick)
    assert state.phase == "claim" and 1 in state.claim_options and 3 in state.claim_options
    # 下家先战鸣申请
    chi = next(c for c in state.claim_options[1] if c["kind"] == "sequence")
    await state.submit_action(p[1].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    if state._claim_grace_task:
        await state._claim_grace_task
    # 更近的上家抢断
    chi = next(c for c in state.claim_options[3] if c["kind"] == "sequence")
    await state.submit_action(p[3].user_id, "claim", candidate_id=chi["id"], action_tick=state.action_tick)
    if state._claim_grace_task:
        await state._claim_grace_task
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
    if state._claim_grace_task:
        await state._claim_grace_task
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


def test_wait_hint_marks_kong_win_self_draw_only() -> None:
    asyncio.run(_exercise_wait_hint_marks_kong_win_self_draw_only())


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


async def _exercise_wait_hint_marks_kong_win_self_draw_only() -> None:
    room = {"room_id": "897", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="kong-win-hint")
    viewer = state.players[3]
    viewer.hand = ["AX1", "AX2", "AX3"]
    viewer.melds = [{
        "kind": "sequence",
        "tiles": ["AX4", "AX5", "AX6"],
        "from_player": 1,
        "claimed_tile": "AX4",
    }]

    hints = state._wait_hints_for(viewer)
    ax7 = next(hint for hint in hints if hint["tile"] == "AX7")
    assert ax7["self_draw_only"] is True
    assert ax7["points"] > 0


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
    if state._claim_grace_task:
        await state._claim_grace_task
