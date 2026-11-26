import asyncio
import threading
from copy import deepcopy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, patch

import pytest

from .spectator import MatchFeed, MatchSpectatorHub, PublishedStream, handle_message
from .test_format import records
from ..gamestate.public.sichuan_duplicate import PairedMatch, new_match
from ..gamestate.public.spectator_manager import SpectatorManager
from ..game_calculation.sichuan.duplicate_rules import make_board


def server_fixture(delay=0):
    pair = PairedMatch(new_match("test-match", "sichuan/doubles_duplicate", {}, [make_board("v2-fixture", 1)], "s"))
    pair.data.update(status="running", wall_source="training")
    source = records()
    for table in ("open", "closed"):
        state = NS(gamestate_id="g-" + table, room_id=table, player_list=[NS(user_id=100+i) for i in range(4)],
                   spectator_enabled=True, duplicate_match=pair, duplicate_key=None)
        spec = state.spectator_manager = SpectatorManager(state, delay=delay)
        spec.game_title = deepcopy(source[table]["record"]["game_title"])
        # Live titles must not include final wall material.
        spec.game_title.pop("duplicate_round_tiles")
        rd = deepcopy(source[table]["record"]["game_round"]["round_index_1"])
        ticks = rd.pop("action_ticks")
        rd["board_instance_id"] = pair.data["board_instances"][0]["board_instance_id"]
        spec.round_headers = {1: {"timestamp": 100, "data": rd}}
        spec.round_ticks = {1: [{"timestamp": 101+i, "tick": t} for i, t in enumerate(ticks)]}
        spec.current_round_index = 1
        pair.states[table] = state
    manager = NS(sichuan_duplicate_matches={"test-match": pair}, user_id_to_game_state={}, remove_spectator_from_all_games=AsyncMock())
    server = NS(gamestate_manager=manager, match_manager=NS(blocks_spectator=lambda _: False), players={})
    return server, pair


def test_stream_resume_snapshot_dedupe_and_retention_boundary():
    stream = PublishedStream("open", {"n": 0})
    epoch = stream.epoch
    for n in range(1, 520):
        stream.publish({"n": n}, {"n": n})
    assert stream.packet({"stream_epoch": epoch, "applied_seq": 0})["type"].endswith("snapshot")
    delta = stream.packet({"stream_epoch": epoch, "applied_seq": 517})
    assert [e["n"] for e in delta["events"]] == [518, 519]
    assert stream.packet({"stream_epoch": epoch, "applied_seq": 519}) is None
    assert stream.packet({"stream_epoch": epoch, "applied_seq": True})["type"].endswith("snapshot")
    stream.reset({"n": 1})
    assert stream.epoch != epoch


def test_delayed_snapshot_and_incremental_boundary_have_no_gap_or_future_data():
    _, pair = server_fixture(delay=10)
    feed = MatchFeed(pair)
    streams = asyncio.run(feed.refresh(111))
    body = streams["open"].body
    assert body["released_head"]["node"] == 1
    assert "duplicate_round_tiles" not in str(body)
    cursor = {"stream_epoch": streams["open"].epoch, "applied_seq": streams["open"].seq}
    streams = asyncio.run(feed.refresh(112))
    packet = streams["open"].packet(cursor)
    assert packet["events"][0]["start_node"] == 1
    assert len(packet["events"][0]["ticks"]) == 1
    assert streams["closed"].body["released_head"]["node"] == 2


def test_independent_table_progress_and_ask_filtering():
    _, pair = server_fixture()
    pair.states["closed"].spectator_manager.round_ticks[1][-1]["timestamp"] = 200
    pair.states["open"].spectator_manager.round_ticks[1].append({"timestamp": 103, "tick": ["ask_hand", 1, "c"]})
    feed = MatchFeed(pair)
    asyncio.run(feed.refresh(150))
    assert feed.streams["open"].body["released_head"]["node"] == 2
    assert feed.streams["closed"].body["released_head"]["node"] == 1
    assert feed.streams["match"].body["result"] is None


def test_replaced_board_invalidates_table_epoch():
    _, pair = server_fixture()
    feed = MatchFeed(pair)
    asyncio.run(feed.refresh(150))
    epoch = feed.streams["open"].epoch
    pair.states["open"].spectator_manager.round_headers[1]["data"]["board_instance_id"] = "new-attempt"
    pair.states["closed"].spectator_manager.round_headers[1]["data"]["board_instance_id"] = "new-attempt"
    pair.data["board_instances"][0]["board_instance_id"] = "new-attempt"
    asyncio.run(feed.refresh(150))
    assert feed.streams["open"].epoch != epoch


@pytest.mark.parametrize("reason", ["player", "other_game", "queue", "formal"])
def test_match_admission_checks_both_tables_and_match_policy(reason):
    server, pair = server_fixture()
    hub = MatchSpectatorHub(server)
    uid = 900
    if reason == "player":
        pair.data["members"] = {"closed": [{"user_id": uid}]}
    elif reason == "other_game":
        server.gamestate_manager.user_id_to_game_state[uid] = object()
    elif reason == "queue":
        server.match_manager.blocks_spectator = lambda _: True
    else:
        pair.data["wall_source"] = "key"
    with pytest.raises(PermissionError):
        hub.authorize(uid, "test-match", ["open", "closed"])


def test_subscription_is_atomic_and_stale_unsubscribe_cannot_remove_rejoin():
    async def run():
        server, _ = server_fixture()
        hub = server.gamestate_manager.match_spectator_hub = MatchSpectatorHub(server)
        conn = NS(user_id=900, websocket=NS(send_json=AsyncMock()))
        first = await hub.subscribe(conn, {"match_id": "test-match"})
        second = await hub.subscribe(conn, {"match_id": "test-match"})
        await hub.remove(900, first.id)
        assert hub.subscriptions[900] is second
        types = [c.args[0]["type"] for c in conn.websocket.send_json.call_args_list]
        assert types[:4] == ["spectator/match_accepted"] + ["spectator/stream_snapshot"] * 3
        await hub.remove(900)
        assert first.task.done() and second.task.done()
    asyncio.run(run())


def test_idle_poll_and_private_actions_do_not_rebuild_released_history():
    async def run():
        _, pair = server_fixture(delay=10)
        feed = MatchFeed(pair)
        with patch.object(feed, "_build_streams", wraps=feed._build_streams) as build:
            first = await feed.refresh(120)
            source = pair.states["open"].spectator_manager
            source.record_tick(["ask_hand", 0, "cut"], timestamp=111)
            source.record_tick(["c", 27, "T"], timestamp=115)
            assert await feed.refresh(124) is first
            assert build.call_count == 1
            updated = await feed.refresh(125)
            assert updated["open"].seq == first["open"].seq + 1
            assert updated["closed"].seq == first["closed"].seq
            assert await feed.refresh(150) is updated
            assert build.call_count == 2
    asyncio.run(run())


def test_concurrent_polls_publish_once_and_leave_the_game_loop_free():
    async def run():
        _, pair = server_fixture()
        feed = MatchFeed(pair)
        await feed.refresh(103)
        previous = feed.streams
        pair.states["open"].spectator_manager.record_tick(["c", 27, "T"], timestamp=104)
        loop = asyncio.get_running_loop()
        started, release = asyncio.Event(), threading.Event()
        build = feed._build_streams

        def paused_build(*args):
            loop.call_soon_threadsafe(started.set)
            assert release.wait(3)
            return build(*args)

        with patch.object(feed, "_build_streams", side_effect=paused_build) as worker:
            first = asyncio.create_task(feed.refresh(105))
            try:
                await asyncio.wait_for(started.wait(), 2)
                second = asyncio.create_task(feed.refresh(105))
                await asyncio.sleep(0)
                assert feed.streams is previous
            finally:
                release.set()
            a, b = await asyncio.gather(first, second)
            assert a is b and a["open"].seq == previous["open"].seq + 1
            assert worker.call_count == 1
    asyncio.run(run())


def test_delivery_that_overlaps_finalization_still_sends_the_final_generation():
    async def run():
        server, pair = server_fixture()
        hub = MatchSpectatorHub(server)
        connection = NS(user_id=900, websocket=NS(send_json=AsyncMock()))
        sub = await hub.subscribe(connection, {"match_id": "test-match"})
        source = pair.states["open"].spectator_manager
        source.record_tick(["c", 27, "T"], timestamp=104)
        await sub.feed.refresh(110)
        paused, release = asyncio.Event(), asyncio.Event()

        async def slow_send(packet):
            paused.set()
            await release.wait()

        connection.websocket.send_json.side_effect = slow_send
        delivery = asyncio.create_task(hub.deliver(sub))
        try:
            await asyncio.wait_for(paused.wait(), 2)
            pair.data.update(status="completed", final_records={t: x["record"] for t, x in records().items()})
            for record in pair.data["final_records"].values():
                record["game_round"]["round_index_1"]["board_instance_id"] = pair.data["board_instances"][0]["board_instance_id"]
            await sub.feed.refresh(120)
            release.set()
            assert not await delivery
            assert await hub.deliver(sub)
            for key, stream in sub.feed.streams.items():
                assert sub.sent[key] == {"stream_epoch": stream.epoch, "applied_seq": stream.seq}
        finally:
            release.set()
            await hub.remove(900)
    asyncio.run(run())


def test_final_record_waits_for_both_delayed_tails_and_does_not_repeat():
    _, pair = server_fixture(delay=10)
    pair.data.update(status="completed", final_records={t: x["record"] for t, x in records().items()})
    # Archived records carry the same durable identity as their live headers.
    for rd in pair.data["final_records"].values():
        rd["game_round"]["round_index_1"]["board_instance_id"] = pair.data["board_instances"][0]["board_instance_id"]
    pair.states["closed"].spectator_manager.round_ticks[1][-1]["timestamp"] = 200
    feed = MatchFeed(pair)
    asyncio.run(feed.refresh(150))
    assert not feed.complete
    asyncio.run(feed.refresh(211))
    assert feed.complete
    seq = feed.streams["match"].seq
    asyncio.run(feed.refresh(212))
    assert feed.streams["match"].seq == seq
    assert "final_streams" in feed.streams["match"].body

def test_existing_training_spectator_can_finish_after_both_rooms_close():
    server, pair = server_fixture()
    hub = MatchSpectatorHub(server)
    pair.data["status"] = "completed"
    feed = MatchFeed(pair)
    server.gamestate_manager.sichuan_duplicate_matches.clear()
    admitted = NS(feed=feed)
    assert hub.authorize(9001, pair.data["id"], ["open"], subscription=admitted) == (pair, ["open"])
    with pytest.raises(PermissionError, match="MATCH_UNAVAILABLE"):
        hub.authorize(9002, pair.data["id"], ["open"])
    with pytest.raises(PermissionError, match="MATCH_UNAVAILABLE"):
        hub.authorize(9001, "other", ["open"], subscription=admitted)


def test_incremental_append_and_round_open_avoid_full_match_rebuild_and_match_full_output():
    from . import spectator as spectator_mod
    from .format import build_match, identity
    from .test_contract import validator

    _, pair = server_fixture(delay=0)
    board2 = make_board("v2-fixture", 2)
    pair.data["planned_boards"] = 2
    pair.data["boards"].append(board2)
    b2_id = identity("board", "test-match", 2, "initial")
    pair.data["board_instances"].append({"board_instance_id": b2_id, "board_no": 2, "attempt": 1})

    feed = MatchFeed(pair)
    asyncio.run(feed.refresh(110))
    closed_before = feed.streams["closed"].body
    closed_seq_before = feed.streams["closed"].seq
    open_cursor = {"stream_epoch": feed.streams["open"].epoch, "applied_seq": feed.streams["open"].seq}
    match_cursor = {"stream_epoch": feed.streams["match"].epoch, "applied_seq": feed.streams["match"].seq}

    open_spec = pair.states["open"].spectator_manager
    with patch.object(spectator_mod, "build_match", side_effect=AssertionError("should stay incremental")):
        # 1) Append actions on existing round 1 (including a recorded draw).
        open_spec.record_tick(["c", 27, "T"], timestamp=120)
        board1 = pair.data["boards"][0]
        open_spec.record_tick(["d", board1["walls"][2][0], "dw", 2, 0, 2], timestamp=121)
        asyncio.run(feed.refresh(122))
        assert feed.streams["closed"].seq == closed_seq_before
        assert feed.streams["closed"].body is closed_before

        # 2) Open round 2 on open table and append its first tick.
        hands2 = deepcopy(board2["hands"])
        hands2[0].append(board2["walls"][0][0])
        rd2_header = {
            "round_index": 2, "current_round": 2, "seats": [0, 1, 2, 3],
            "dealer_index": 0, "start_player_index": 0, "duplicate_wall_layout": "standard",
            "board_instance_id": b2_id,
            **{f"p{i}_tiles": h for i, h in enumerate(hands2)},
        }
        open_spec.round_headers[2] = {"timestamp": 130, "data": rd2_header}
        open_spec.round_ticks[2] = [
            {"timestamp": 131, "tick": ["c", hands2[0][-1], "T"]},
            {"timestamp": 132, "tick": ["d", board2["walls"][1][0], "dw", 1, 0, 1]},
        ]
        open_spec.current_round_index = 2
        asyncio.run(feed.refresh(135))

    # Validate emitted deltas and current snapshot against the V2 spectator schema.
    check = validator("match-spectator-v2.schema.json")
    open_delta = feed.streams["open"].packet(open_cursor)
    assert [e["kind"] for e in open_delta["events"]] == ["actions_appended", "round_opened"]
    for pkt in (open_delta, feed.streams["open"].packet(None), feed.streams["match"].packet(match_cursor)):
        check.validate({"protocol_version": 2, "success": True, "match_id": "test-match", "subscription_id": "s1", **pkt})

    # Cross-check incremental result against a fresh full build_match().
    sources, _ = feed._collect_full_sources(135, False)
    full_bundle = build_match(sources, match_id="test-match", snapshot=feed._last_snapshot, complete=False)
    inc_table = feed.streams["open"].body["table_record"]
    full_table = full_bundle["tables"]["open"]
    assert inc_table["payload"] == full_table["payload"]
    assert inc_table["round_meta"] == full_table["round_meta"]
    assert len(inc_table["source_sha256"]) == 64
    full_catalog = [{k: v for k, v in b.items() if k != "wall_definition"} for b in full_bundle["board_catalog"]]
    assert feed.streams["match"].body["board_catalog"] == full_catalog
