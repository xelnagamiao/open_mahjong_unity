"""Real ASGI WebSocket serialization, reconnect and subscription ownership."""
from copy import deepcopy
from types import SimpleNamespace as NS

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.testclient import TestClient

from .spectator import get_hub, handle_message
from .test_spectator import server_fixture
from .test_contract import validator


def test_websocket_reconnect_resumes_each_stream_and_rejects_stale_leave():
    server, pair = server_fixture()
    app = FastAPI()

    @app.websocket("/game/{connect_id}")
    async def endpoint(socket: WebSocket, connect_id: str):
        await socket.accept()
        connection = NS(user_id=900, websocket=socket)
        server.players[connect_id] = connection
        try:
            while True:
                await handle_message(server, connect_id, await socket.receive_json(), socket)
        except WebSocketDisconnect:
            pass
        finally:
            hub = get_hub(server)
            sub = hub.subscriptions.get(900)
            if sub and sub.connection is connection:
                await hub.remove(900, sub.id)
            server.players.pop(connect_id, None)

    check = validator("match-spectator-v2.schema.json")
    subscribe = dict(type="spectator/match_subscribe", protocol_version=2, match_id="test-match", request_id="first")
    with TestClient(app) as client:
        with client.websocket_connect("/game/one") as first:
            first.send_json(subscribe)
            accepted = first.receive_json()
            check.validate(accepted)
            old_subscription = accepted["subscription_id"]
            cursors = {}
            for _ in range(3):
                packet = first.receive_json()
                check.validate(packet)
                cursors[packet["stream_id"]] = dict(stream_epoch=packet["stream_epoch"], applied_seq=packet["at_seq"])
            first.send_json(dict(type="spectator/stream_ack", protocol_version=2, subscription_id=old_subscription, cursors=cursors))
        pair.states["open"].spectator_manager.round_ticks[1].append(dict(timestamp=104, tick=["c", 27, "T"]))
        with client.websocket_connect("/game/two") as second:
            second.send_json({**subscribe, "request_id": "resume", "resume": deepcopy(cursors)})
            accepted = second.receive_json()
            current = accepted["subscription_id"]
            assert current != old_subscription
            # Poll throttling may leave delivery to the next 1-second tick.
            appended = second.receive_json()
            check.validate(appended)
            assert appended["type"] == "spectator/stream_delta"
            assert appended["stream_id"] == "open"
            assert appended["events"][0]["start_node"] == 2
            second.send_json(dict(type="spectator/match_unsubscribe", protocol_version=2, subscription_id=old_subscription))
            error = second.receive_json()
            check.validate(error)
            assert error["code"] == "STALE_SUBSCRIPTION"
            second.send_json(dict(type="spectator/stream_resync", protocol_version=2, subscription_id=current, stream_id="closed"))
            snapshot = second.receive_json()
            check.validate(snapshot)
            assert snapshot["stream_id"] == "closed" and snapshot["at_seq"] == cursors["closed"]["applied_seq"]
            second.send_json(dict(type="spectator/match_unsubscribe", protocol_version=2, subscription_id=current))
            assert second.receive_json()["type"] == "spectator/match_unsubscribed"
        assert not get_hub(server).subscriptions
