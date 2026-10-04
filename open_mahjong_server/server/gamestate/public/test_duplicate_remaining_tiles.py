"""Wire-level coverage for Guobiao duplicate personal-wall counters."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from ..game_guobiao.buhua_broadcast import perform_buhua_and_broadcast
from ..game_guobiao.init_tiles import init_guobiao_tiles
from .claim_protection import begin_claim_protection_interval
from .duplicate_wall import DuplicateFinished, duplicate_remaining_tile_counts, finish_duplicate_game
from .game_record_manager import init_game_record, init_game_round
from .test_duplicate_wall import SUBRULES, game


def connected_game(sub_rule="guobiao/standard", *, duplicate=True):
    state, _, _ = game("guobiao", "Guobiao", sub_rule, duplicate=duplicate)
    init_guobiao_tiles(state)
    init_game_record(state)
    init_game_round(state)
    sockets = []
    for seat, player in enumerate(state.player_list):
        socket = SimpleNamespace(send_json=AsyncMock())
        sockets.append(socket)
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
        player.record_counter.rank_result = seat + 1
    return state, sockets


def messages(socket, field):
    return [call.args[0][field] for call in socket.send_json.call_args_list if field in call.args[0]]


@pytest.mark.parametrize("sub_rule", SUBRULES)
def test_start_and_real_reconnect_send_counts_by_original_seat(sub_rule):
    state, sockets = connected_game(sub_rule)
    part_size = 34 if sub_rule == "guobiao/lanshi" else 36
    expected = [part_size - 14, *([part_size - 13] * 3)]
    asyncio.run(state.broadcast_game_start())
    for seat, socket in enumerate(sockets):
        info = messages(socket, "game_info")[-1]
        assert info["duplicate_remaining_tiles"] == expected
        assert info["tile_count"] == sum(expected)
        assert "master_seed" not in info and "tiles" not in info
        assert all("hand_tiles" not in p for p in info["players_info"] if p["original_player_index"] != seat)

    state.player_list[2].get_tile(state.tiles_list)
    expected[2] -= 1
    asyncio.run(state.player_reconnect(state.player_list[3].user_id))
    assert messages(sockets[3], "game_info")[-1]["duplicate_remaining_tiles"] == expected
    # Earlier payloads are snapshots, not shared mutable counters.
    assert messages(sockets[3], "game_info")[0]["duplicate_remaining_tiles"][2] == expected[2] + 1


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("seat", range(4))
@pytest.mark.parametrize("gang", [False, True])
def test_real_draw_and_kong_supplement_payloads_only_reduce_actor_wall(sub_rule, seat, gang):
    state, sockets = connected_game(sub_rule)
    expected = duplicate_remaining_tile_counts(state)
    player = state.player_list[seat]
    if gang:
        player.get_gang_tile(state.tiles_list, state)
    else:
        player.get_tile(state.tiles_list)
    expected[seat] -= 1
    action = "deal_gang_tile" if gang else "deal_tile"
    asyncio.run(state.broadcast_do_action(action_list=[action], action_player=seat, deal_tile=player.hand_tiles[-1]))
    for viewer, socket in enumerate(sockets):
        info = messages(socket, "do_action_info")[-1]
        assert info["duplicate_remaining_tiles"] == expected
        assert info.get("deal_tile") == (player.hand_tiles[-1] if viewer == seat else None)
        assert "tiles" not in info and "master_seed" not in info


@pytest.mark.parametrize("instant", [False, True])
def test_real_flower_replacement_broadcast_sends_authoritative_counts(instant, monkeypatch):
    state, sockets = connected_game()
    monkeypatch.setattr("server.gamestate.game_guobiao.buhua_broadcast.HAND_SETTLE_GAP_SEC", 0)
    seat = next(i for i, p in enumerate(state.player_list) if max(p.hand_tiles) >= 51)
    expected = duplicate_remaining_tile_counts(state)
    expected[seat] -= 1
    asyncio.run(perform_buhua_and_broadcast(state, seat, huapai_before_draw=instant, instant=instant))
    for socket in sockets:
        payloads = messages(socket, "do_action_info")
        assert len(payloads) == (1 if instant else 2)
        assert all(info["duplicate_remaining_tiles"] == expected for info in payloads)
        assert "deal_buhua_tile" in payloads[-1]["action_list"]


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("gang", [False, True])
def test_exhaustion_sends_terminal_zero_without_seed_or_local_record(sub_rule, gang):
    state, sockets = connected_game(sub_rule)
    expected = duplicate_remaining_tile_counts(state)
    player = state.player_list[1]
    with pytest.raises(DuplicateFinished) as done:
        while True:
            if gang:
                player.get_gang_tile(state.tiles_list, state)
            else:
                player.get_tile(state.tiles_list)
    expected[1] = 0
    asyncio.run(finish_duplicate_game(state, done.value))
    for socket in sockets:
        info = messages(socket, "game_end_info")[-1]
        assert info["duplicate_remaining_tiles"] == expected
        assert "master_seed" not in info and "record_detail" not in info


def test_protected_cut_and_deferred_meld_retain_generated_count_snapshots():
    async def run():
        state, sockets = connected_game()
        state.claim_protection = True
        state.claim_protect_delay = 10
        state.claim_meld_followup_gap = 0.01
        state.claim_meld_post_gap = 0
        cut_counts = duplicate_remaining_tile_counts(state)
        begin_claim_protection_interval(state, {0: [], 1: ["gang"], 2: [], 3: []}, 0)
        await state.broadcast_do_action(action_list=["cut"], action_player=0, cut_tile=11)
        assert not messages(sockets[2], "do_action_info")
        assert state._cp_pending_cut[2]["duplicate_remaining_tiles"] == cut_counts

        state.player_list[1].get_gang_tile(state.tiles_list, state)
        meld_counts = duplicate_remaining_tile_counts(state)
        await state.broadcast_do_action(action_list=["gang"], action_player=1, combination_target="k11")
        # Advance the wall before the protected viewers' deferred meld sends.
        state.player_list[1].get_tile(state.tiles_list)
        await asyncio.gather(*state._outbound_tails.values())
        for socket in sockets:
            cut, meld = messages(socket, "do_action_info")
            assert cut["duplicate_remaining_tiles"] == cut_counts
            assert meld["duplicate_remaining_tiles"] == meld_counts
            assert meld_counts != duplicate_remaining_tile_counts(state)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
def test_ordinary_start_action_reconnect_and_end_omit_duplicate_counts(sub_rule):
    async def run():
        state, sockets = connected_game(sub_rule, duplicate=False)
        await state.broadcast_game_start()
        state.player_list[1].get_tile(state.tiles_list)
        await state.broadcast_do_action(action_list=["deal_tile"], action_player=1, deal_tile=state.player_list[1].hand_tiles[-1])
        await state.player_reconnect(state.player_list[0].user_id)
        await state.broadcast_game_end()
        assert duplicate_remaining_tile_counts(state) is None
        for socket in sockets:
            for field in ("game_info", "do_action_info", "game_end_info"):
                assert messages(socket, field)
                assert all("duplicate_remaining_tiles" not in info for info in messages(socket, field))
    asyncio.run(run())


@pytest.mark.parametrize("exhausted", [False, True])
def test_duplicate_end_follows_queued_action_even_when_websocket_yields(exhausted):
    """A queued old count must reach the wire before the terminal snapshot."""
    from ..game_guobiao.boardcast import _build_do_action_payload, _deliver_do_action_payload_to_viewer
    from .outbound_pipe import close_outbound_pipes, schedule_viewer_send

    async def run():
        state, sockets = connected_game()
        release_action = asyncio.Event()
        old_counts = duplicate_remaining_tile_counts(state)
        old_payload = _build_do_action_payload(state, ["gang"], 1, 0)

        async def queued_action():
            await release_action.wait()
            await _deliver_do_action_payload_to_viewer(state, 0, old_payload)

        schedule_viewer_send(state, 0, queued_action)
        # Model a real websocket write yielding after it accepts a message.
        async def network_yield(payload):
            await asyncio.sleep(0)
        sockets[0].send_json.side_effect = network_yield
        original_end = state.broadcast_game_end

        async def ending():
            release_action.set()
            await original_end()
        state.broadcast_game_end = ending
        state.game_server.gamestate_manager.cleanup_game_state_complete.side_effect = lambda **kwargs: close_outbound_pipes(state)

        if exhausted:
            with pytest.raises(DuplicateFinished) as done:
                while True:
                    state.player_list[1].get_tile(state.tiles_list)
            await finish_duplicate_game(state, done.value)
        else:
            # Successful single-hand completion also uses this shared broadcast.
            state.hu_class = "hu_self"
            state.game_status = "END"
            await state.broadcast_game_end()
            close_outbound_pipes(state)

        wire = [call.args[0] for call in sockets[0].send_json.call_args_list]
        assert [message["type"] for message in wire] == ["gamestate/guobiao/do_action", "gamestate/guobiao/game_end"]
        assert wire[0]["do_action_info"]["duplicate_remaining_tiles"] == old_counts
        final_counts = duplicate_remaining_tile_counts(state)
        assert wire[-1]["game_end_info"]["duplicate_remaining_tiles"] == final_counts
        if exhausted:
            assert final_counts[1] == 0
        for socket in sockets:
            assert messages(socket, "game_end_info")[-1]["duplicate_remaining_tiles"] == final_counts
    asyncio.run(run())


@pytest.mark.parametrize("flush_mode", ["pending", "same_task", "concurrent"])
def test_duplicate_terminal_waits_for_all_reserved_protected_cut_sends(flush_mode):
    from ..game_guobiao.boardcast import _send_do_action_payload_to_viewer
    from .claim_protection import flush_protected_cut
    from .outbound_pipe import close_outbound_pipes

    async def run():
        state, sockets = connected_game()
        state.claim_protection = True
        state.claim_protect_delay = 10
        original_counts = duplicate_remaining_tile_counts(state)
        begin_claim_protection_interval(state, {0: [], 1: ["gang"], 2: [], 3: []}, 0)
        await state.broadcast_do_action(action_list=["cut"], action_player=0, cut_tile=11)
        assert not messages(sockets[2], "do_action_info")

        cut_started = asyncio.Event()
        release_cut = asyncio.Event()
        flush_task = None
        if flush_mode == "same_task":
            await flush_protected_cut(state, _send_do_action_payload_to_viewer)
        elif flush_mode == "concurrent":
            async def hold_first_protected_cut(payload):
                if "do_action_info" in payload:
                    cut_started.set()
                    await release_cut.wait()
            sockets[2].send_json.side_effect = hold_first_protected_cut
            flush_task = asyncio.create_task(flush_protected_cut(state, _send_do_action_payload_to_viewer))
            await asyncio.wait_for(cut_started.wait(), timeout=2)
            # Every protected cut now reserves its own FIFO immediately. A slow
            # viewer must still prevent terminal cleanup from cancelling its cut.
            assert 2 in state._outbound_tails and 3 in state._outbound_tails
            assert not state._outbound_tails[2].done()

        with pytest.raises(DuplicateFinished) as done:
            while True:
                state.player_list[1].get_tile(state.tiles_list)

        end_started = asyncio.Event()
        original_end = state.broadcast_game_end
        async def ending():
            end_started.set()
            await original_end()
        state.broadcast_game_end = ending
        state.game_server.gamestate_manager.cleanup_game_state_complete.side_effect = lambda **kwargs: close_outbound_pipes(state)
        end_task = asyncio.create_task(finish_duplicate_game(state, done.value))
        await asyncio.wait_for(end_started.wait(), timeout=2)
        if flush_mode == "concurrent":
            assert not any(messages(socket, "game_end_info") for socket in sockets)
            release_cut.set()
            await asyncio.wait_for(flush_task, timeout=2)
        await asyncio.wait_for(end_task, timeout=2)

        for socket in sockets:
            wire = [call.args[0] for call in socket.send_json.call_args_list]
            assert [message["type"] for message in wire] == ["gamestate/guobiao/do_action", "gamestate/guobiao/game_end"]
            assert wire[0]["do_action_info"]["duplicate_remaining_tiles"] == original_counts
            assert wire[-1]["game_end_info"]["duplicate_remaining_tiles"][1] == 0
    asyncio.run(run())
