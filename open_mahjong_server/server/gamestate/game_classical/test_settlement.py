"""Exercise the real end-of-hand loop without sockets, timers, or a database."""

import asyncio
import importlib
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from .ClassicalGameState import ClassicalGameState
from ..public.round_end_timing import liuju_ready_wait_seconds

module = importlib.import_module(ClassicalGameState.__module__)


class NextHand(Exception):
    pass


def run_hand(monkeypatch, outcome, final=False):
    server = SimpleNamespace(user_id_to_connection={},
        gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock()))
    room = dict(room_id="classical-settlement-test", room_rule="classical", sub_rule="classical/standard",
                player_list=[101, 102, 103, 104], player_settings={}, game_round=1,
                round_timer=0, step_timer=0, room_type="custom", tips=False, random_seed=7,
                allow_spectator=False)
    game = ClassicalGameState(server, room, object(), Mock(), "classical-settlement-test")
    game.db_manager.store_classical_game_record.return_value = None
    game.current_round = 4 if final else 1
    initializations = 0

    def deal(state):
        nonlocal initializations
        initializations += 1
        if initializations > 1:
            raise NextHand
        state.hu_class = ""
        state.tiles_list = [42] * (state.dead_wall_count + 1)
        for p in state.player_list:
            p.hand_tiles = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42]

    async def act():
        game.hu_class = outcome
        # Distinct non-winning hand values make accidental draw transfers visible.
        game.player_list[0].combination_tiles = ["G45"]
        if outcome.startswith("hu_"):
            game.current_player_index = 1 if outcome == "hu_self" else 0
            game.result_dict = {outcome: (20, 40, ["test-fu"], ["test-fan"])}
            game.game_status = "END"
        else:
            game.game_status = "END" if outcome == "jiuzhongjiupai" else "deal_card"

    monkeypatch.setattr(module, "init_classical_tiles", deal)
    monkeypatch.setattr(module, "check_kokushi", lambda hand: False)
    monkeypatch.setattr(module, "check_jiuzhongjiupai", lambda hand: False)
    monkeypatch.setattr(module, "check_action_hand_action", lambda *a, **k: {})
    monkeypatch.setattr(module, "remember_local_record_detail", Mock())
    game.refresh_waiting_tiles = Mock()
    game.wait_action = act
    game.broadcast_game_start = AsyncMock()
    game.broadcast_ask_hand_action = AsyncMock()
    game.broadcast_do_action = AsyncMock()
    game.broadcast_shuhewei = AsyncMock()
    game.broadcast_game_end = AsyncMock()
    game.spectator_manager.send_final_record_and_close = AsyncMock()
    result = AsyncMock()
    ready = AsyncMock()
    monkeypatch.setattr(module, "broadcast_result", result)
    monkeypatch.setattr(module, "run_result_ready_phase", ready)
    monkeypatch.setattr(module.asyncio, "sleep", AsyncMock())
    if final:
        asyncio.run(game.game_loop_classical())
    else:
        with pytest.raises(NextHand):
            asyncio.run(game.game_loop_classical())
    return game, result, ready


@pytest.mark.parametrize("final", [False, True])
@pytest.mark.parametrize("outcome", ["liuju", "jiuzhongjiupai"])
def test_draw_has_no_shuhewei_payment_or_record(monkeypatch, outcome, final):
    game, result, ready = run_hand(monkeypatch, outcome, final)
    assert [p.score for p in game.player_list] == [0] * 4
    assert [p.score_history for p in game.player_list] == [["0"]] * 4
    game.broadcast_shuhewei.assert_not_awaited()
    actions = game.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert not any(a[0] == "shuhewei" for a in actions)
    assert result.await_args.kwargs["hu_class"] == outcome
    if final:
        assert game.next_status == "match_end"
        ready.assert_not_awaited()
    elif outcome == "liuju":
        assert ready.await_args.args[1] == liuju_ready_wait_seconds()


@pytest.mark.parametrize("outcome", ["hu_self", "hu_first", "hu_second", "hu_third"])
def test_wins_still_settle_winner_and_other_players(monkeypatch, outcome):
    game, result, ready = run_hand(monkeypatch, outcome)
    game.broadcast_shuhewei.assert_awaited_once()
    assert sum(p.score for p in game.player_list) == 0
    assert any(p.score != 0 for p in game.player_list)
    result.assert_not_awaited()
    assert ready.await_count == 1


def test_final_draw_packet_tells_clients_to_finish_match(monkeypatch):
    game, _, _ = run_hand(monkeypatch, "liuju", final=True)
    broadcaster = importlib.import_module("server.gamestate.game_classical.boardcast")
    send = AsyncMock()
    monkeypatch.setattr(broadcaster, "send_game_packet", send)
    game.game_server.user_id_to_connection = {
        p.user_id: SimpleNamespace(websocket=object()) for p in game.player_list
    }
    game.send_to_realtime_spectators = AsyncMock()
    asyncio.run(broadcaster.broadcast_result(game, hu_class="liuju",
        player_to_score={i: 0 for i in range(4)}, score_changes={i: 0 for i in range(4)}))
    assert send.await_count == 4
    for call in send.await_args_list:
        packet = call.args[2]["show_result_info"]
        assert packet["next_status"] == "match_end"
        assert packet["hu_class"] == "liuju"
        assert not any(packet["score_changes"].values())
