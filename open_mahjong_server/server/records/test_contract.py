"""Executable wire/file contracts; jsonschema is a test-only dependency."""
import asyncio
from copy import deepcopy
import json
from pathlib import Path
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import jsonschema
import pytest

from .format import build_match, validate_match
from .spectator import MatchSpectatorHub, handle_message
from .test_format import records
from .test_spectator import server_fixture
from .wall_alignment import RecordConflict

HERE = Path(__file__).parent


def test_shared_canonical_vectors():
    from .format import digest
    path = HERE.parents[2] / "open_mahjong_unity/Assets/Tests/Fixtures/MatchCanonical.json"
    for entry in json.loads(path.read_text(encoding="utf8")):
        assert digest(entry["value"]) == entry["sha256"]


def validator(name):
    schema = json.loads((HERE / name).read_text(encoding="utf8"))
    jsonschema.Draft202012Validator.check_schema(schema)
    return jsonschema.Draft202012Validator(schema)


@pytest.mark.parametrize("layout", ["standard", "xueliu"])
@pytest.mark.parametrize("complete", [False, True])
def test_archive_schema_matches_produced_records(layout, complete):
    check = validator("match-record-v2.schema.json")
    source = records(layout)
    check.validate(build_match(source, complete=complete))
    check.validate(build_match({"open": source["open"]}, complete=complete))
    solo = deepcopy(source["open"])
    solo["record"]["game_title"] = {"rule": "sichuan", "sub_rule": "sichuan/standard"}
    check.validate(build_match({"main": solo}))


def test_protocol_accept_snapshot_append_resync_ack_and_leave():
    async def run():
        server, pair = server_fixture()
        check = validator("match-spectator-v2.schema.json")
        socket = NS(send_json=AsyncMock())
        connection = NS(user_id=900, websocket=socket)
        server.players["client"] = connection
        hub = server.gamestate_manager.match_spectator_hub = MatchSpectatorHub(server)
        subscribe = {"type": "spectator/match_subscribe", "protocol_version": 2, "match_id": "test-match", "request_id": "r1"}
        check.validate(subscribe)
        await handle_message(server, "client", subscribe, socket)
        sub = hub.subscriptions[900]
        for call in socket.send_json.call_args_list:
            check.validate(call.args[0])
        socket.send_json.reset_mock()
        pair.states["open"].spectator_manager.round_ticks[1].append({"timestamp": 104, "tick": ["c", 27, "T"]})
        await sub.feed.refresh(200)
        await hub.deliver(sub)
        assert socket.send_json.call_count == 1
        check.validate(socket.send_json.call_args.args[0])
        for command in [
            {"type": "spectator/stream_ack", "cursors": deepcopy(sub.sent)},
            {"type": "spectator/stream_resync", "stream_id": "closed"},
            {"type": "spectator/match_unsubscribe"},
        ]:
            command.update(protocol_version=2, subscription_id=sub.id)
            check.validate(command)
            await handle_message(server, "client", command, socket)
        for call in socket.send_json.call_args_list:
            check.validate(call.args[0])
        assert not hub.subscriptions
    asyncio.run(run())


def test_header_change_with_new_ticks_is_not_lost():
    _, pair = server_fixture()
    from .spectator import MatchFeed
    feed = MatchFeed(pair)
    stream = asyncio.run(feed.refresh(200))["open"]
    cursor = {"stream_epoch": stream.epoch, "applied_seq": stream.seq}
    spec = pair.states["open"].spectator_manager
    spec.game_title["future_extension"] = {"verified": True}
    spec.round_ticks[1].append({"timestamp": 105, "tick": ["c", 27, "T"]})
    streams = asyncio.run(feed.refresh(201))
    event = streams["open"].packet(cursor)["events"][0]
    assert event["kind"] == "table_available"
    assert event["body"]["table_record"]["payload"]["game_title"]["future_extension"] == {"verified": True}


@pytest.mark.parametrize("mutation", ["revision", "table_set", "duplicate_round", "missing_payload"])
def test_cross_field_corruption_is_rejected(mutation):
    record = build_match(records())
    if mutation == "revision": record["record_revision"] = True
    elif mutation == "table_set": record["tables"].pop("closed")
    elif mutation == "missing_payload": record["tables"]["open"]["record_state"] = "missing"
    else:
        table = record["tables"]["open"]
        table["payload"]["game_round"]["round_index_2"] = deepcopy(table["payload"]["game_round"]["round_index_1"])
        table["round_meta"]["round_index_2"] = deepcopy(table["round_meta"]["round_index_1"])
    with pytest.raises(RecordConflict): validate_match(record)
