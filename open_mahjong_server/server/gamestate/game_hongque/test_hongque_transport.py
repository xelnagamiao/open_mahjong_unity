"""Real loopback WebSockets, production gamestate router, four peers and reconnect.

Only account authentication is replaced by explicit fixture seat assignment.
No shared development service or database is modified.
"""
import asyncio
import json
from types import SimpleNamespace as NS

import pytest
from websockets.asyncio.client import connect
from websockets.asyncio.server import serve

from .test_hongque_reconnect import stage_fixture, STAGES
from .test_hongque_lifecycle import route_server
from ..gamestate_router import handle_gamestate_message


class WireSocket:
    def __init__(self, socket):
        self.socket = socket

    async def send_json(self, payload):
        await self.socket.send(json.dumps(payload))


async def through_marker(client, marker):
    packets = []
    async with asyncio.timeout(5):
        while True:
            packet = json.loads(await client.recv())
            if packet["type"] == marker:
                return packets
            packets.append(packet)


@pytest.mark.parametrize("stage", STAGES)
@pytest.mark.parametrize("seat", range(4))
def test_four_peer_socket_disconnect_restore_then_route_action(stage, seat):
    async def run():
        game, server = await stage_fixture(stage, dealer=seat if stage == "turn" else 0)
        route_server(game, server)
        peers = []

        async def handler(socket):
            uid = int(await socket.recv())  # Fixture-only authenticated identity.
            connection = NS(user_id=uid, websocket=WireSocket(socket), event_disconnecting=False)
            server.user_id_to_connection[uid] = connection
            server.players[str(uid)] = connection
            try:
                await game.player_reconnect(uid)
                await connection.websocket.send_json(dict(type="qa/connected"))
                async for raw in socket:
                    await handle_gamestate_message(server, str(uid), json.loads(raw), connection.websocket)
                    await connection.websocket.send_json(dict(type="qa/action"))
            finally:
                connection.event_disconnecting = True
                await game.player_disconnect(uid)

        async with serve(handler, "127.0.0.1", 0) as listener:
            url = f"ws://127.0.0.1:{listener.sockets[0].getsockname()[1]}"
            try:
                for player in game.players:
                    peer = await connect(url)
                    peers.append(peer)
                    await peer.send(str(player.user_id))
                    await through_marker(peer, "qa/connected")
                await peers[seat].close()
                async with asyncio.timeout(5):
                    while game.players[seat].online:
                        await asyncio.sleep(0.001)
                peers[seat] = await connect(url)
                client = peers[seat]
                await client.send(str(game.players[seat].user_id))
                packets = await through_marker(client, "qa/connected")
                restored = packets[0]
                assert restored["type"] == "gamestate/hongque/reconnect"
                state = restored["hongque_state"]
                assert state["hand"] == game.players[seat].hand
                assert state["you"] == seat and len(state["players"]) == 4
                assert all("hand" not in p for p in state["players"])
                if "discard" in state["legal_actions"]:
                    tile = game.players[seat].hand[-1]
                    action = dict(action="discard", tile=tile)
                elif "pass" in state["legal_actions"]:
                    action = dict(action="force_pass")
                elif game._ready_phase_active:
                    action = dict(action="ready")
                else:
                    action = dict(action="unknown")
                await client.send(json.dumps(dict(type="gamestate/hongque/action", gamestate_id=game.gamestate_id,
                    action_tick=state["action_tick"], **action)))
                packets = await through_marker(client, "qa/action")
                if action["action"] == "unknown":
                    assert packets[-1]["type"] == "tips" and not packets[-1]["success"]
                else:
                    assert not any(p["type"] == "tips" for p in packets)
                    if action["action"] == "discard":
                        assert tile not in game.players[seat].hand
                        assert any(e["type"] == "discard" for p in packets
                                   for e in p.get("hongque_state", {}).get("events", []))
                    elif action["action"] == "ready":
                        assert seat in game._ready_players
            finally:
                await asyncio.gather(*(peer.close() for peer in peers))
        await game.cleanup_game_state()
    asyncio.run(run())
