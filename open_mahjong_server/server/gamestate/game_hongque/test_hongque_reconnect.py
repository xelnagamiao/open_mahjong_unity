"""Restore every externally visible stage, then continue through the real action path.

The packet fixtures are also consumed by Unity's windowless MainScene validation.
"""
import asyncio
import copy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .HongqueGameState import HongqueGameState
from .state_machine import HongqueStatus

STAGES = ("turn", "claim", "tactical", "onlycut", "supplement", "kong",
          "self_draw", "ron", "multi_ron", "draw", "game_end")


class PacketSocket:
    def __init__(self):
        self.packets = []

    async def send_json(self, payload):
        self.packets.append(copy.deepcopy(payload))


async def stage_fixture(stage, *, dealer=0, game_round=8):
    connections = {uid: NS(user_id=uid, websocket=PacketSocket(), event_disconnecting=False)
                   for uid in range(11000001, 11000005)}
    server = NS(user_id_to_connection=connections, match_manager=None,
                gamestate_manager=NS(cleanup_game_state_complete=AsyncMock()))
    game = HongqueGameState(server, dict(room_id=12345, player_list=list(connections),
        game_round=game_round, round_timer=300, step_timer=30, tips=False,
        pointer_tips=False, random_seed=7), gamestate_id=f"qa-{stage}-{dealer}")
    game.dealer_index = dealer
    game.tactical_grace_seconds = 0.01
    if stage in ("claim", "tactical", "onlycut", "supplement", "ron", "multi_ron"):
        game.Debug = True
        game.debug_scenario = "tactical_all_claims"
    await game._start_round()
    if game.Debug:
        await game.submit_action(game.players[0].user_id, "discard", tile="AX1")
        if stage != "claim":
            kind = "win" if stage in ("ron", "multi_ron") else "sequence"
            candidate = next(c for c in game.claim_options[1] if c["kind"] == kind)
            await game.submit_action(game.players[1].user_id, "claim", candidate_id=candidate["id"])
            if stage == "multi_ron":
                for seat in (2, 3):
                    candidate = next(c for c in game.claim_options[seat] if c["kind"] == "win")
                    await game.submit_action(game.players[seat].user_id, "claim", candidate_id=candidate["id"])
            if stage != "tactical":
                await pass_pending(game)
                if stage == "supplement":
                    await game.submit_action(game.players[1].user_id, "supplement")
    elif stage == "kong":
        player = game.players[dealer]
        player.hand = ["AX4", "BY9"]
        player.drawn_tile = "BY9"
        player.melds = [{"kind": "sequence", "tiles": ["AX1", "AX2", "AX3"],
                         "from_player": (dealer+3)%4, "claimed_tile": "AX1"}]
        candidate = game._legal_turn_actions(player)[1][0]
        await game.submit_action(player.user_id, "kong", candidate_id=candidate["id"])
    elif stage == "self_draw":
        player = game.players[dealer]
        player.hand = ["AX1", "AX2", "AX3", "BX4", "BX5", "BX6",
                       "CX4", "CX5", "CX6", "DX4", "DX5", "DX6"]
        player.drawn_tile = "DX6"
        await game.submit_action(player.user_id, "win")
    elif stage in ("draw", "game_end"):
        await game._finish_round([], "draw")
    if game._round_task:
        game._round_task.cancel()
        await asyncio.gather(game._round_task, return_exceptions=True)
    if stage == "game_end":
        game._transition(HongqueStatus.END)
        game.current_round = game.max_round + 1
    return game, server


async def pass_pending(game):
    while game.phase == "claim":
        pending = sorted(game.claim_window.pending)
        if not pending:
            await game.wait_action()
            continue
        for seat in pending:
            if game.phase == "claim" and seat in game.claim_window.pending:
                await game.submit_action(game.players[seat].user_id, "force_pass")


@pytest.mark.parametrize("stage", STAGES)
@pytest.mark.parametrize("seat", range(4))
def test_disconnect_reconnect_preserves_private_snapshot_and_continues(stage, seat):
    async def run():
        game, server = await stage_fixture(stage, dealer=seat if stage == "turn" else 0)
        player = game.players[seat]
        expected = game.build_state(seat, sync_mode="reconnect")
        tick = game.action_tick
        socket = server.user_id_to_connection[player.user_id].websocket
        try:
            await game.player_disconnect(player.user_id)
            assert not player.online
            socket.packets.clear()
            await game.player_reconnect(player.user_id)
            restored = socket.packets[0]
            assert restored["type"] == "gamestate/hongque/reconnect"
            state = restored["hongque_state"]
            for key in ("hand", "pending_draw", "last_discard", "legal_actions", "candidates",
                        "phase", "game_status", "action_tick", "round_result"):
                assert state[key] == expected[key], key
            assert state["players"][seat]["online"] is True
            assert all("hand" not in p for p in state["players"])
            assert len(state["players"]) == 4 and state["hand"] == player.hand
            assert game.action_tick == tick
            if game._ready_phase_active:
                assert socket.packets[1]["type"] == "gamestate/hongque/ready_status"
                await game.submit_action(player.user_id, "ready", action_tick=tick)
                assert seat in game._ready_players
            elif "discard" in state["legal_actions"]:
                tile = player.hand[-1]
                await game.submit_action(player.user_id, "discard", tile=tile, action_tick=tick)
                assert tile not in player.hand and game.action_tick > tick
            elif "pass" in state["legal_actions"]:
                await game.submit_action(player.user_id, "force_pass", action_tick=tick)
                assert seat not in game.claim_window.pending if game.claim_window else True
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_unknown_presence_does_not_change_game_or_broadcast():
    async def run():
        game, server = await stage_fixture("turn")
        try:
            for connection in server.user_id_to_connection.values():
                connection.websocket.packets.clear()
            await game.player_disconnect(-999)
            await game.player_reconnect(-999)
            assert all(not c.websocket.packets for c in server.user_id_to_connection.values())
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("stage", ("turn", "claim", "tactical", "onlycut", "supplement", "ron", "multi_ron"))
def test_replay_starts_with_exactly_one_complete_deck(stage):
    async def run():
        game, _ = await stage_fixture(stage)
        try:
            header = game.game_record["game_round"]["round_index_1"]
            tiles = list(header["tiles_list"])
            for seat in range(4):
                tiles.extend(header[f"p{seat}_tiles"])
            assert len(tiles) == len(set(tiles)) == 126
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("scenario", ("double_ron", "tactical_all_claims", "ones_nines"))
def test_every_debug_scenario_preserves_the_unique_deck(scenario):
    async def run():
        game, _ = await stage_fixture("turn")
        try:
            game.Debug = True
            game.debug_scenario = scenario
            await game._start_round()
            tiles = game.wall + [tile for player in game.players for tile in player.hand]
            assert len(tiles) == len(set(tiles)) == 126
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


async def export_client_fixtures(output):
    """Export actual packets and records for HongqueRuntimeValidation, without a DB."""
    import json
    from pathlib import Path
    from .boardcast import state_packet
    folder = Path(output)
    cases, replays = [], []
    replay_stages = {"turn", "claim", "tactical", "onlycut", "supplement", "ron", "multi_ron"}
    for stage in STAGES:
        for seat in range(4):
            game, server = await stage_fixture(stage, dealer=seat if stage == "turn" else 0)
            try:
                restore = copy.deepcopy(state_packet(game, seat, sync_mode="reconnect"))
                socket = server.user_id_to_connection[game.players[seat].user_id].websocket
                if stage in replay_stages - {"turn"}:
                    for duplicate in (False, True):
                        cases.append(dict(name=f"live-{stage}-seat-{seat}-duplicates-{duplicate}",
                            restore=copy.deepcopy(restore), livePackets=copy.deepcopy(socket.packets), duplicateEvents=duplicate))
                if stage in replay_stages:
                    replays.append(dict(name=f"replay-{stage}-seat-{seat}", seat=seat,
                        record=copy.deepcopy(game.game_record), players=[dict(user_id=p.user_id,
                        username=p.username, title_used=1, profile_used=1, character_used=1, voice_used=1)
                        for p in game.players]))
                extra = {}
                if stage in ("turn", "onlycut", "supplement", "kong") and "discard" in restore["hongque_state"]["legal_actions"]:
                    socket.packets.clear()
                    await game.submit_action(game.players[seat].user_id, "discard", tile=game.players[seat].hand[-1])
                    extra = dict(nextPackets=copy.deepcopy(socket.packets),
                                 after=copy.deepcopy(state_packet(game, seat, sync_mode="reconnect")))
                for warm in (False, True):
                    case = dict(name=f'{"warm" if warm else "cold"}-{stage}-seat-{seat}',
                                restore=copy.deepcopy(restore), **extra)
                    if warm:
                        case["initial"] = copy.deepcopy(restore)
                        case["initial"]["type"] = "gamestate/hongque/game_start"
                    cases.append(case)
                cases.append(dict(name=f"spectator-{stage}-seat-{seat}", restore=copy.deepcopy(restore), spectator=True))
            finally:
                await game.cleanup_game_state()
    normal_names = {f"cold-turn-seat-{seat}" for seat in range(4)} | {
        "live-ron-seat-0-duplicates-False", "live-multi_ron-seat-0-duplicates-False",
        "warm-self_draw-seat-0",
    }
    replays.extend(dict(copy.deepcopy(case), name=case["name"] + "-collapsed", showCardsMode=False)
                   for case in list(replays) if case["name"].startswith("replay-multi_ron-"))
    normal_speed = [dict(copy.deepcopy(case), timeScale=1)
                    for case in cases if case["name"] in normal_names]
    for destination, fixtures in ((folder, cases), (folder / "replay-run", replays),
                                  (folder / "normal-time-run", normal_speed)):
        destination.mkdir(parents=True, exist_ok=True)
        (destination / "runtime-fixtures.json").write_text(
            json.dumps(fixtures, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
    return len(cases), len(replays)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Export authoritative Hongque client validation fixtures")
    parser.add_argument("--output", required=True, help="Local validation directory, normally under .om_workspace")
    print(asyncio.run(export_client_fixtures(parser.parse_args().output)))
