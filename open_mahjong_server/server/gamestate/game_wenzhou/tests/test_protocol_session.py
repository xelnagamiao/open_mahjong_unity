"""Transport authority, visibility and queue timing without real services."""

import asyncio
import copy
from types import SimpleNamespace

import pytest

from ..actions import empty_actions
from ..get_action import handle_action
from ..state_machine import Phase as P
from .helpers import STANDARD, SocketRecorder, fixture_state, make_state


def waiting_state(*, actor=0, actions=None, phase=P.TURN):
    state = make_state()
    state.initialize_round()
    state.machine.transition(P.TURN)
    if phase != P.TURN:
        state.machine.transition(phase)
    window = dict(status=phase.value, player=actor, tile=11 if phase == P.RESPONSE else None,
                  actions=actions or {**empty_actions(), actor: ["cut"]})
    state.open_action_window(window)
    return state, window


def test_start_payload_only_contains_self_hand_and_public_indicator():
    state = make_state()
    state.initialize_round()
    for viewer in range(4):
        payload = state.build_game_start_payload(viewer)
        assert payload["type"] == "gamestate/wenzhou/game_start"
        info = payload["game_info"]
        assert info["self_hand_tiles"] == state.player_list[viewer].hand_tiles
        assert info["wenzhou_waits"] == {}
        for index, player in enumerate(info["players_info"]):
            assert player["hand_tiles_count"] == len(state.player_list[index].hand_tiles)
            assert player["hand_tiles"] == (state.player_list[index].hand_tiles if index == viewer else None)
        meta = info["wenzhou_info"]
        assert meta["indicator"] == state.caishen
        assert meta["base_hand_tiles"] == 16
        assert meta["rule_version"] == "mil-wenzhou-2024-om1"
        assert not {"caishen_counts", "caishen_changes", "ledger", "score_details"}.intersection(meta)
        assert "master_seed" not in info and "master_seed_hex" not in info


def test_concealed_kong_is_hidden_in_snapshots_and_action_but_revealed_at_end():
    state = make_state()
    state.initialize_round()
    state.caishen = 12
    owner = state.player_list[0]
    state._add_meld(owner, "G12", [2, 46] * 4)
    state.machine.transition(P.TURN)
    event = dict(action="angang", player=0, tile=46, meld_code="G12", combination_mask=[2, 46] * 4,
                 is_mo_gang=False, gang_score_changes={0: 12, 1: -4, 2: -4, 3: -4})
    payloads = state.emit_visible_action_payloads(event)
    for viewer, payload in enumerate(payloads):
        public = payload["game_info"]["players_info"][0]
        if viewer == 0:
            assert public["combination_tiles"] == ["G12"]
            assert public["combination_mask"] == [[2, 46] * 4]
            assert payload["do_action_info"]["combination_mask"] == [2, 46] * 4
        else:
            assert public["combination_tiles"] == ["G0"]
            assert public["combination_mask"] == [[2, 0] * 4]
            assert payload["do_action_info"]["combination_mask"] == [2, 0] * 4
            assert payload["tile"] == 0 and payload["meld_code"] == "G0"
    state.end_draw()
    ended = state.build_final_settlement_payload(2)["game_info"]
    assert ended["players_info"][0]["combination_tiles"] == ["G12"]
    assert all(p["hand_tiles"] is not None for p in ended["players_info"])
    assert "caishen_counts" in ended["wenzhou_info"]


def test_drawn_tile_hidden_from_other_players_and_physical_white_stays_white():
    state = make_state()
    state.initialize_round()
    state.caishen = 12
    payloads = state.emit_visible_action_payloads(dict(action="deal_tile", player=2, tile=46))
    for viewer, payload in enumerate(payloads):
        assert payload["tile"] == (46 if viewer == 2 else None)
        assert payload["do_action_info"]["deal_tile"] == (46 if viewer == 2 else None)
        assert payload["game_info"]["wenzhou_info"]["white_natural"] == 12


def test_response_payload_sent_only_to_players_with_actions():
    state, window = waiting_state(phase=P.RESPONSE,
        actions={0: [], 1: ["peng", "pass"], 2: [], 3: ["hu", "pass"]})
    assert [p["player_index"] for p in state.outbound_payloads] == [1, 3]
    for payload in state.outbound_payloads:
        assert payload["type"] == "gamestate/wenzhou/ask_other_action"
        assert payload["ask_other_action_info"]["cut_tile"] == 11
        assert payload["ask_other_action_info"]["action_tick"] == window["action_tick"]


def test_pending_restore_has_same_window_tick_and_hides_others():
    state, window = waiting_state()
    payloads = state.restore_payloads(2)
    assert len(payloads) == 2
    assert payloads[1]["action_tick"] == window["action_tick"]
    assert payloads[1]["action_list"] == []
    assert payloads[0]["game_info"]["players_info"][0]["hand_tiles"] is None
    # The ask adds its current exact clock; the immutable rule snapshot agrees.
    assert payloads[0]["game_info"]["wenzhou_info"] == {
        key:value for key,value in payloads[1]["game_info"]["wenzhou_info"].items()
        if not key.startswith("clock_")}


def test_restore_ended_and_ready_does_not_apply_scores_twice():
    state, _ = waiting_state()
    state.settle_kong(1, "ming")
    state.end_draw()
    first = state.restore_payloads(0)
    scores = [p.score for p in state.player_list]
    assert len(first) == 2 and first[-1]["type"].endswith("show_result")
    assert state.build_pending_action_payload(0) is None
    state.machine.transition(P.READY)
    second = state.restore_payloads(0)
    assert len(second) == 3 and second[-1]["type"].endswith("ready_status")
    assert [p.score for p in state.player_list] == scores
    assert all(len(p.score_history) == 1 for p in state.player_list)


def test_reconnect_recognizes_identity_and_restores_without_redealing():
    async def run():
        state, window = waiting_state()
        socket = SocketRecorder()
        state.game_server = SimpleNamespace(user_id_to_connection={101: SimpleNamespace(websocket=socket)})
        state.player_list[0].tag_list = ["offline"]
        before = [list(p.hand_tiles) for p in state.player_list], list(state.tiles_list), list(state.dice)
        await state.player_reconnect(101)
        assert len(socket.messages) == 2
        assert "offline" not in state.player_list[0].tag_list
        assert socket.messages[-1]["action_tick"] == window["action_tick"]
        await state.player_reconnect(999)
        assert len(socket.messages) == 2
        assert before == ([p.hand_tiles for p in state.player_list], state.tiles_list, state.dice)
    asyncio.run(run())


def test_realtime_spectator_restore_uses_authorized_view_and_ignores_invalid_slots():
    async def run():
        state, _ = waiting_state()
        await state.send_realtime_spectator_snapshot(777, 0)
        socket = SocketRecorder()
        state.game_server = SimpleNamespace(user_id_to_connection={777: SimpleNamespace(websocket=socket)})
        for viewer in [-1, 4]:
            await state.send_realtime_spectator_snapshot(777, viewer)
        await state.send_realtime_spectator_snapshot(778, 0)
        assert socket.messages == []
        await state.send_realtime_spectator_snapshot(777, 3)
        assert len(socket.messages) == 2
        info = socket.messages[0]["game_info"]
        assert info["self_hand_tiles"] == state.player_list[3].hand_tiles
        assert info["players_info"][0]["hand_tiles"] is None
    asyncio.run(run())


def test_opening_new_window_clears_stale_queue_before_broadcast():
    async def run():
        state, _ = waiting_state()
        await state.action_queues[0].put({"action_type": "hu_self"})
        state.action_events[0].set()
        window = dict(status=P.TURN.value, player=0, actions={**empty_actions(), 0: ["cut"]})
        previous_tick = state.server_action_tick
        state.open_action_window(window)
        assert state.action_queues[0].empty() and not state.action_events[0].is_set()
        assert state.server_action_tick == previous_tick + 1
    asyncio.run(run())


def test_queue_accepts_one_legal_reply_and_rejects_duplicate_or_wrong_seat():
    async def run():
        state, _ = waiting_state()
        player = state.player_list[0]
        reply = dict(TileId=player.hand_tiles[-1], cutIndex=len(player.hand_tiles)-1, cutClass=True)
        await state.submit_action(0, "cut", **reply)
        assert state.action_events[0].is_set()
        with pytest.raises(ValueError):
            await state.submit_action(0, "cut", **reply)
        for invalid in [-1, 4, True, 1]:
            with pytest.raises(ValueError):
                await state.submit_action(invalid, "cut", **reply)
        response = await state.wait_action(timeout=0)
        assert response[0]["TileId"] == reply["TileId"]
        assert state.waiting_players_list == [] and state.action_dict[0] == []
    asyncio.run(run())


def test_timeout_cut_uses_last_physical_tile_and_does_not_mark_held_cut_as_drawn():
    async def run():
        state, _ = waiting_state()
        response = (await state.wait_action(timeout=0))[0]
        assert response["action_type"] == "cut" and response["is_timeout_action"]
        assert response["TileId"] == state.player_list[0].hand_tiles[-1]
        assert response["cutClass"] is True
        state.player_list[0].has_draw_slot = False
        state.open_action_window(dict(status=P.TURN.value, player=0, actions={**empty_actions(), 0: ["cut"]}))
        response = (await state.wait_action(timeout=0))[0]
        assert response["cutClass"] is False
    asyncio.run(run())


def test_timeout_response_passes_and_ready_keeps_time_bank():
    async def run():
        state, _ = waiting_state(phase=P.RESPONSE, actions={**empty_actions(), 1: ["hu", "pass"]})
        response = await state.wait_action(timeout=0)
        assert response[1]["action_type"] == "pass"
        state.end_draw()
        state.player_list[0].remaining_time = 8
        await state.run_round_ready_phase(timeout=0)
        assert state.machine.phase == P.READY and state.live_pending_window is None
        assert not any(state.action_dict.values())
        assert state.player_list[0].remaining_time == 8
    asyncio.run(run())


def test_finished_match_skips_ready_wait_for_humans():
    async def run():
        state, _ = waiting_state()
        state.end_draw()
        state.match_finishing = True
        await state.run_round_ready_phase(timeout=10)
        assert state.waiting_players_list == []
    asyncio.run(run())


def socket_server(state, *, user=101):
    return SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: state),
                           players={"connection": SimpleNamespace(user_id=user)})


def test_network_handler_checks_socket_user_and_tick_not_user_supplied_seat():
    async def run():
        state, _ = waiting_state()
        socket = SocketRecorder()
        player = state.player_list[0]
        message = dict(type="gamestate/wenzhou/cut_tile", gamestate_id=state.gamestate_id,
                       action_tick=state.server_action_tick, player_index=3,
                       TileId=player.hand_tiles[-1], cutIndex=len(player.hand_tiles)-1, cutClass=True)
        await handle_action(socket_server(state), "connection", message, socket)
        assert state.action_queues[0].qsize() == 1
        assert all(state.action_queues[i].empty() for i in (1, 2, 3))
        await handle_action(socket_server(state), "connection", message, socket)
        assert socket.messages[-1]["type"] == "tips" and socket.messages[-1]["success"] is False
    asyncio.run(run())


@pytest.mark.parametrize("tick", [None, -1, True, "1"])
def test_stale_network_actions_resync_instead_of_mutating(tick):
    async def run():
        state, window = waiting_state()
        socket = SocketRecorder()
        await handle_action(socket_server(state), "connection", {"action_tick": tick}, socket)
        assert all(queue.empty() for queue in state.action_queues.values())
        assert len(socket.messages) == 1 and socket.messages[0]["action_tick"] == window["action_tick"]
    asyncio.run(run())


def test_network_handler_ignores_unknown_state_connection_or_nonparticipant():
    async def run():
        state, _ = waiting_state()
        socket = SocketRecorder()
        for server, connection in [(socket_server(None), "connection"), (socket_server(state), "unknown"),
                                   (socket_server(state, user=999), "connection")]:
            await handle_action(server, connection, {}, socket)
        state.room_rule = "guobiao"
        await handle_action(socket_server(state), "connection", {}, socket)
        assert socket.messages == []
    asyncio.run(run())


def test_after_rotation_total_score_keys_use_seats_and_deltas_use_original_identity():
    state=fixture_state({1:STANDARD},actor=1)
    for index,player in enumerate(state.player_list):
        player.score=100*(index+1)
    state.settle_win(1,"self_draw",43)
    state.apply_deferred_score_changes()
    state.machine.transition(P.READY)
    state.advance_round_after_ready()
    state.initialize_round()
    assert [p.original_player_index for p in state.player_list] == [1,2,3,0]
    state.machine.transition(P.TURN)
    state.end_draw()
    info=state.build_final_settlement_payload(0)["show_result_info"]
    # Mirrors the existing Unity score resolver's documented key contract:
    # total lookup prefers current seat, delta lookup prefers original index.
    for player in state.player_list:
        assert info["player_to_score"][player.player_index] == player.score
        assert info["score_changes"][player.original_player_index] == 0


@pytest.mark.parametrize("spectator",[False,True])
def test_restore_drains_actual_outbound_fifo_before_snapshot(spectator):
    from ...public.outbound_pipe import schedule_viewer_send
    async def run():
        state,_=waiting_state()
        socket=SocketRecorder()
        uid=777 if spectator else 101
        state.game_server=SimpleNamespace(user_id_to_connection={uid:SimpleNamespace(websocket=socket)})
        gate=asyncio.Event()
        async def prior_send():
            await gate.wait()
            await socket.send_json({"type":"prior_queued_action"})
        earlier=schedule_viewer_send(state,0,prior_send)
        restored=asyncio.create_task(state.send_realtime_spectator_snapshot(uid,0) if spectator
                                    else state.player_reconnect(uid))
        await asyncio.sleep(0)
        assert not restored.done() and socket.messages == []
        gate.set()
        await asyncio.wait_for(restored,1)
        assert earlier.done()
        assert [message["type"] for message in socket.messages] == [
            "prior_queued_action","gamestate/wenzhou/game_start","gamestate/wenzhou/broadcast_hand_action"]
    asyncio.run(run())
