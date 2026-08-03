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


def test_supplement_consumes_only_overtime_and_restarts_step_time() -> None:
    asyncio.run(_exercise_supplement_turn_clock())


def test_single_number_pair_wait_can_self_win_and_has_score_hint() -> None:
    asyncio.run(_exercise_pair_wait_self_win())


def test_single_number_pair_wait_can_win_on_another_players_discard() -> None:
    asyncio.run(_exercise_pair_wait_ron())


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
    viewer.hand = ["BX5"]
    viewer.melds = [{
        "kind": "sequence",
        "tiles": ["AX1", "AX2", "AX3"],
        "from_player": 2,
        "claimed_tile": "AX1",
    }]
    state.current_player_index = 0
    state.phase = "turn"

    snapshot = state.build_state(1)
    hints = {hint["tile"]: hint for hint in snapshot["waiting_hints"]}
    assert "CY5" in hints
    assert hints["CY5"]["points"] > 0
    assert hints["CY5"]["base"] > 0


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


async def _exercise_pair_wait_self_win() -> None:
    room = {"room_id": "890", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="pair-wait-win-test")
    player = state.players[0]
    player.hand = ["BX5", "CY5"]
    player.melds = [{
        "kind": "sequence",
        "tiles": ["AX1", "AX2", "AX3"],
        "from_player": 1,
        "claimed_tile": "AX1",
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
    assert state.round_result["winners"][0]["pair"] == ["BX5", "CY5"]
    if state._round_task:
        state._round_task.cancel()


async def _exercise_pair_wait_ron() -> None:
    room = {"room_id": "891", "game_round": 1, "player_list": [101, 102, 103, 104]}
    state = HongqueGameState(None, room, gamestate_id="pair-wait-ron-test")
    discarder = state.players[0]
    winner = state.players[1]
    discarder.hand = ["CY5"]
    winner.hand = ["BX5"]
    winner.melds = [{
        "kind": "sequence",
        "tiles": ["AX1", "AX2", "AX3"],
        "from_player": 2,
        "claimed_tile": "AX1",
    }]
    state.current_player_index = 0
    state.phase = "turn"
    state.wall = ["GX9"]

    await state.submit_action(discarder.user_id, "discard", tile="CY5", action_tick=state.action_tick)

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
    for player_index in list(state.claim_options):
        if player_index != 1 and player_index not in state.claim_responses:
            await state.submit_action(
                state.players[player_index].user_id,
                "pass",
                action_tick=state.action_tick,
            )

    assert state.phase == "round_end"
    assert state.round_result["winner_indices"] == [1]
    assert state.round_result["winners"][0]["pair"] == ["BX5", "CY5"]
    if state._round_task:
        state._round_task.cancel()
