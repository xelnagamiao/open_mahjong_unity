"""Declaration responses must finish before the deposit reaches any table viewer."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .boardcast import broadcast_game_start
from .test_double_riichi import make_game
from .test_execution_paths import result
from .wait_action import _execute_cut, wait_action
from ..public.claim_protection import end_claim_protection_interval
from ..public.outbound_pipe import close_outbound_pipes, drain_viewer
from ..verifier.record_sim.decoder import accumulate_score_changes_from_tick


@pytest.mark.parametrize("protected", [False, True])
@pytest.mark.parametrize("outcome", ["none", "pass", "timeout", "chi_mid", "peng", "gang", "ron", "multi_ron"])
def test_accepted_deposit_follows_discard_and_responses_for_every_viewer(monkeypatch, protected, outcome):
    async def run():
        game, _ = make_game(0)
        game.room_id = 824002
        game.claim_protection = protected
        game.claim_protect_delay = .02
        game.claim_meld_followup_gap = .01
        game.claim_meld_post_gap = .01
        wire = {seat: [] for seat in range(4)}
        for seat, player in enumerate(game.player_list):
            async def send(payload, viewer=seat):
                wire[viewer].append(payload)
            game.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(
                websocket=SimpleNamespace(send_json=send))
        game.send_to_realtime_spectators = AsyncMock()
        tile = 15 if outcome == "chi_mid" else 47
        game.player_list[0].hand_tiles[-1] = tile
        game.player_list[1].hand_tiles = (
            [14, 16] if outcome == "chi_mid" else [47, 47, 47]
        ) + [21, 22, 23, 24, 26, 27, 31, 32, 33, 41]
        response = "hu_first" if outcome in ("ron", "multi_ron") else (
            outcome if outcome in ("chi_mid", "peng", "gang") else "pass")
        actions = {0: [], 1: [] if outcome == "none" else [response, "pass"], 2: [], 3: []}
        if outcome == "multi_ron":
            actions[2] = ["hu_second", "pass"]
        game.result_dict = {"hu_first": result(8000), "hu_second": result(8000)}
        monkeypatch.setattr("server.gamestate.game_riichi.wait_action.check_action_after_cut",
                            lambda *_: {seat: list(acts) for seat, acts in actions.items()})
        response_task = None
        try:
            assert await _execute_cut(game, 0, tile, True, None, True)
            if outcome != "none":
                assert game.player_list[0].score == 25000
                assert game.riichi_sticks == 0
                assert not any(msg["type"].endswith("/riichi_accepted") for msg in wire[0])
                await broadcast_game_start(game)
                pending = next(msg["game_info"] for msg in wire[0] if msg["type"].endswith("/game_start"))
                assert pending["players_info"][0]["riichi_accepted"] is False
                game.step_time = 0
                for player in game.player_list:
                    player.remaining_time = 0 if outcome == "timeout" else 10
                if outcome != "timeout":
                    async def respond():
                        await asyncio.sleep(.001)
                        for seat, action in [(1, response)] + ([(2, "hu_second")] if outcome == "multi_ron" else []):
                            await game.action_queues[seat].put({"action_type": action, "target_tile": tile})
                            game.action_events[seat].set()
                    response_task = asyncio.create_task(respond())
                await asyncio.wait_for(wait_action(game), 2)
                if response_task is not None:
                    await response_task
            await asyncio.gather(*(drain_viewer(game, seat) for seat in range(4)))
            paid = outcome not in ("ron", "multi_ron")
            for messages in wire.values():
                accepted = [msg for msg in messages if msg["type"].endswith("/riichi_accepted")]
                assert len(accepted) == int(paid)
                if paid:
                    info = accepted[0]["refresh_player_tag_list_info"]
                    assert info["riichi_accepted_player_index"] == 0
                    assert info["player_to_score"] == {0: 24000, 1: 25000, 2: 25000, 3: 25000}
                    assert info["riichi_sticks"] == 1
                    cut = next(i for i, msg in enumerate(messages)
                               if msg.get("do_action_info", {}).get("action_list") == ["cut"])
                    assert cut < messages.index(accepted[0])
                    if outcome in ("chi_mid", "peng", "gang"):
                        meld = next(i for i, msg in enumerate(messages)
                                    if msg.get("do_action_info", {}).get("action_list") == [outcome])
                        assert meld < messages.index(accepted[0])
            assert game.player_list[0].score == (24000 if paid else 25000)
            assert game.riichi_sticks == int(paid)
            assert sum(player.score for player in game.player_list) + game.riichi_sticks * 1000 == 100000
            ticks = game.game_record["game_round"]["round_index_1"]["action_ticks"]
            assert sum(tick[0] == "riichi" for tick in ticks) == int(paid)
            recorded = None
            for tick in ticks:
                recorded = accumulate_score_changes_from_tick(recorded, tick, [0, 1, 2, 3])
            assert (recorded or [0] * 4) == ([-1000, 0, 0, 0] if paid else [0] * 4)
            if paid:
                await broadcast_game_start(game)
                snapshot = wire[0][-1]["game_info"]
                assert snapshot["players_info"][0]["riichi_accepted"] is True
                assert snapshot["players_info"][0]["score"] == 24000
                assert snapshot["riichi_sticks"] == 1
            assert not game._cp_active
        finally:
            if response_task is not None and not response_task.done():
                response_task.cancel()
            close_outbound_pipes(game)
            end_claim_protection_interval(game)
    asyncio.run(run())
