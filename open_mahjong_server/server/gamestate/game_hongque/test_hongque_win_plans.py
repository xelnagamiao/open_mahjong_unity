"""Selectable settlements use actual legal kongs, packets and replay records."""
import asyncio
import copy

import pytest

from .action_check import check_action_after_cut
from .boardcast import state_packet
from .record import code_to_id
from .rules import classify_meld
from .scoring import all_win_results
from .state_machine import HongqueStatus
from .test_hongque_reconnect import pass_pending, stage_fixture
from .wait_action import _timeout_hand_action
from .win_plans import self_draw_plans


HAND = "AX7 EX7 EY7 FX7 DY7".split()
MELDS = [{"kind": "triplet", "tiles": text.split(), "from_player": index + 1,
          "claimed_tile": text.split()[0]} for index, text in enumerate((
    "CX1 CY1 DX1 DY1", "FY7 GX7 GY7"))]
FLAGS = dict(self_draw=True, before_first_discard=False, wall_empty=False, allow_kong_win=True)


async def win_state(hand=HAND, melds=MELDS, seat=0):
    game, server = await stage_fixture("turn", dealer=seat)
    for player in game.players:
        player.hand = []
        player.melds = []
        player.discards = []
        player.drawn_tile = None
    player = game.players[seat]
    player.hand = list(hand)
    player.melds = copy.deepcopy(melds)
    player.drawn_tile = hand[-1] if hand else None
    game.players[(seat + 1) % 4].discards = ["GX9"]
    game.wall = ["BY9", "CY9"]
    game.events = []
    for connection in server.user_id_to_connection.values():
        connection.websocket.packets.clear()
    return game, server


def shape(groups):
    return tuple(sorted(tuple(sorted(group)) for group in groups))


def test_prediction_retains_lower_scores_and_continues_beyond_first_win():
    original = copy.deepcopy((HAND, MELDS))
    plans = all_win_results(HAND, MELDS, **FLAGS)
    assert [(p["base"], p["fan_total"], p["points"]) for p in plans] == [
        (9, 17, 153), (6, 18, 108), (6, 17, 102)]
    assert len({shape(p["groups"]) for p in plans}) == len(plans)
    for plan in plans:
        remaining = list(HAND)
        melds = copy.deepcopy(MELDS)
        for step in plan["kong_steps"]:
            assert len(step["hand_tiles"]) == 1
            tile = step["hand_tiles"][0]
            previous = melds[step["meld_index"]]["tiles"]
            assert set(step["tiles"]) == set(previous + [tile])
            assert classify_meld(step["tiles"]) is not None
            remaining.remove(tile)
            melds[step["meld_index"]]["tiles"] = step["tiles"]
        assert shape(plan["groups"]) == shape(plan["partition"] + [m["tiles"] for m in melds])
    assert (HAND, MELDS) == original
    assert all_win_results(HAND, MELDS, **{**FLAGS, "self_draw": False}) == []


def test_prediction_does_not_skip_illegal_intermediate_kongs():
    assert all_win_results("AX2 AX4".split(), [{"tiles": "AX1 AX3 AX5".split()}], **FLAGS) == []
    plans = all_win_results("AX4 AX5 BX4".split(), [
        {"tiles": "AX1 AX2 AX3".split()}, {"tiles": "BX1 BX2 BX3".split()}], **FLAGS)
    assert plans and {step["meld_index"] for step in plans[0]["kong_steps"]} == {0, 1}


@pytest.mark.parametrize("points", [153, 108, 102])
def test_selected_score_applies_every_kong_before_result_and_records_it(points):
    async def run():
        game, server = await win_state()
        try:
            player = game.players[0]
            before = copy.deepcopy(player.melds)
            plans = [p for p in game.build_state(0)["candidates"] if p["kind"] == "win"]
            selected = next(plan for plan in plans if plan["points"] == points)
            await game.submit_action(player.user_id, "win", candidate_id=selected["id"], action_tick=game.action_tick)
            result = game.round_result["winners"][0]
            assert result["points"] == points
            assert result["winning_tile"] == "DY7"
            assert shape(result["groups"]) == shape(selected["groups"])
            assert result["hand"] == player.hand
            assert result["melds"] == player.melds
            assert not player.hand if points == 153 else bool(player.hand)
            packets = server.user_id_to_connection[player.user_id].websocket.packets
            updates = [p["hongque_state"] for p in packets if p["type"] == "gamestate/hongque/update"]
            events = [e for update in updates for e in update["events"]]
            kongs = [e for e in events if e["type"] == "kong"]
            assert len(kongs) == len(selected["kong_steps"])
            assert [e["type"] for e in events] == ["kong"] * len(kongs) + ["self_draw"]
            assert [e["id"] for e in events] == sorted({e["id"] for e in events})
            for event in kongs:
                index = event["meld_index"]
                assert len(event["tiles"]) == len(before[index]["tiles"]) + 1
                before[index]["tiles"] = event["tiles"]
            ticks = game.game_record["game_round"]["round_index_1"]["action_ticks"]
            recorded = [tick for tick in ticks if tick[0] == "jg"]
            assert [(tick[1], tick[3]) for tick in recorded] == [
                (code_to_id(step["hand_tiles"][0]), step["meld_index"]) for step in selected["kong_steps"]]
            hu = next(tick for tick in ticks if tick[0] == "hu_self")
            assert hu[2] == points
            assert hu[5] == code_to_id("DY7")
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_bad_or_stale_selection_never_falls_back_and_legacy_win_uses_best():
    async def run():
        game, _ = await win_state()
        try:
            player = game.players[0]
            before = copy.deepcopy((player.hand, player.melds, game.action_tick))
            with pytest.raises(ValueError, match="候选"):
                await game.submit_action(player.user_id, "win", candidate_id="win-invalid")
            kong = next(c for c in game.build_state(0)["candidates"] if c["kind"] == "kong")
            with pytest.raises(ValueError, match="候选"):
                await game.submit_action(player.user_id, "win", candidate_id=kong["id"])
            assert (player.hand, player.melds, game.action_tick) == before
            first = self_draw_plans(game, player)[0]
            with pytest.raises(ValueError):
                await game.submit_action(player.user_id, "win", candidate_id=first["id"], action_tick=game.action_tick - 1)
            await game.submit_action(player.user_id, "win")
            assert game.round_result["winners"][0]["points"] == 153
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_consumed_draw_does_not_turn_remaining_hand_last_tile_into_winning_tile():
    async def run():
        game, _ = await win_state()
        try:
            player = game.players[0]
            player.drawn_tile = "AX7"
            plan = next(p for p in self_draw_plans(game, player) if p["points"] == 108)
            await game.submit_action(player.user_id, "win", candidate_id=plan["id"])
            winner = game.round_result["winners"][0]
            assert winner["hand"] and "AX7" not in winner["hand"]
            assert winner["winning_tile"] == "AX7"
            ticks = game.game_record["game_round"]["round_index_1"]["action_ticks"]
            assert next(t for t in ticks if t[0] == "hu_self")[5] == code_to_id("AX7")
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_ron_exposes_all_direct_shapes_and_honors_selected_lower_score():
    async def run():
        game, server = await win_state("AX1 AX2 AX3 AX4 AX5".split(), [])
        try:
            game.current_player_index = 1
            game.players[1].discards.append("AX6")
            game.last_discard = {"player": 1, "tile": "AX6"}
            game.phase = "resolving"
            await game._open_claim_window()
            wins = [c for c in game.build_state(0)["candidates"] if c["kind"] == "win"]
            assert len(wins) > 1 and wins[0]["points"] > wins[-1]["points"]
            assert all(not c["kong_steps"] for c in wins)
            assert wins[0]["id"] == "ron"
            selected = wins[-1]
            await game.submit_action(game.players[0].user_id, "claim", candidate_id=selected["id"])
            await pass_pending(game)
            winner = game.round_result["winners"][0]
            assert game.round_result["reason"] == "ron"
            assert winner["points"] == selected["points"]
            assert shape(winner["groups"]) == shape(selected["groups"])
            assert winner["hand"] == "AX1 AX2 AX3 AX4 AX5 AX6".split()
            assert winner["winning_tile"] == "AX6"
            public = server.user_id_to_connection[game.players[1].user_id].websocket.packets
            applications = [e for p in public for e in p.get("hongque_state", {}).get("events", [])
                            if e["type"] == "claim_apply"]
            assert applications and all(e["tiles"] == ["AX6"] for e in applications)
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("onlycut", [False, True])
@pytest.mark.parametrize("next_action", ["win", "supplement"])
def test_last_tile_plain_kong_when_supplement_available_then_user_chooses(onlycut, next_action):
    async def run():
        game, _ = await win_state(["AX4"], [{"tiles": "AX1 AX2 AX3".split(), "kind": "sequence"}])
        try:
            player = game.players[0]
            if onlycut:
                game.state_machine.status = HongqueStatus.ONLYCUT_AFTER_ACTION
            candidate = next(c for c in game.build_state(0)["candidates"] if c["kind"] == "kong")
            await game.submit_action(player.user_id, "kong", candidate_id=candidate["id"])
            assert player.hand == []
            assert game.build_state(0)["legal_actions"] == ["win", "supplement"]
            await game.submit_action(player.user_id, next_action)
            if next_action == "win":
                assert game.phase == "round_end"
                assert game.round_result["winners"][0]["hand"] == []
                assert game.round_result["winners"][0]["winning_tile"] is None
                ticks = game.game_record["game_round"]["round_index_1"]["action_ticks"]
                assert next(t for t in ticks if t[0] == "hu_self")[5] == 0
            else:
                assert game.phase == "turn" and player.hand == ["CY9"] and player.supplements == 1
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


async def legal_kong_win_fixture():
    """A complete 126-tile deal followed only by real discard/call/draw/win."""
    from .record import snapshot_round_header
    from .tile import full_deck

    game, server = await stage_fixture("turn")
    winner = game.players[1]
    hand = "AX2 AX3 AX4 BX1 BX2 BX3 CX1 CX2 CX3 DY1 EY1".split()
    reserved = set(hand + ["AX1", "FY1"])
    pool = [code for code in full_deck() if code not in reserved]
    for player in game.players:
        player.hand = []
        player.melds = []
        player.discards = []
        player.drawn_tile = None
    game.players[0].hand = [pool.pop() for _ in range(11)] + ["AX1"]
    game.players[0].drawn_tile = "AX1"
    winner.hand = hand
    for player in game.players[2:]:
        player.hand = [pool.pop() for _ in range(11)]
    game.wall = list(pool)
    game.wall.insert(1, "FY1")  # First supplement uses the second tile in the final stack.
    game.events = []
    game.last_discard = None
    snapshot_round_header(game)
    initial = copy.deepcopy(state_packet(game, 0, sync_mode="round_start"))
    for connection in server.user_id_to_connection.values():
        connection.websocket.packets.clear()
    await game.submit_action(game.players[0].user_id, "discard", tile="AX1")
    option = next(c for c in game.claim_options[1]
                  if c["kind"] == "sequence" and set(c["tiles"]) == {"AX1", "AX2", "AX3"})
    await game.submit_action(winner.user_id, "claim", candidate_id=option["id"])
    await pass_pending(game)
    assert game.current_player_index == 1 and game.game_status == "onlycut_after_action"
    await game.submit_action(winner.user_id, "supplement")
    assert winner.drawn_tile == "FY1"
    plans = self_draw_plans(game, winner)
    plan = next(plan for plan in plans if plan["kong_steps"])
    await game.submit_action(winner.user_id, "win", candidate_id=plan["id"])
    return game, server, initial


def test_real_deal_claim_supplement_kong_win_record_replays_to_authoritative_state():
    from server.gamestate.public.game_record_manager import jsonable_game_record
    from server.gamestate.verifier.record_sim import RecordSim

    async def run():
        game, _, _ = await legal_kong_win_fixture()
        try:
            record = jsonable_game_record(game.game_record)
            header = record["game_round"]["round_index_1"]
            deck = header["tiles_list"] + [tile for seat in range(4) for tile in header[f"p{seat}_tiles"]]
            assert len(deck) == len(set(deck)) == 126
            assert [len(header[f"p{seat}_tiles"]) for seat in range(4)] == [12, 11, 11, 11]
            ticks = header["action_ticks"]
            assert any(tick[0] == "jg" for tick in ticks)
            replay = RecordSim(record)
            frames = replay.apply_all("round_index_1")
            jumped = RecordSim(record).goto_action("round_index_1", len(ticks))
            assert jumped["players"] == frames[-1]["players"]
            winner = game.players[1]
            assert sorted(replay.players[1].tile_list) == sorted(code_to_id(code) for code in winner.hand)
            assert game.round_result["winners"][0]["winning_tile"] == "FY1"
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("onlycut", [False, True])
@pytest.mark.parametrize("empty", [False, True])
@pytest.mark.parametrize("wall_empty", [False, True])
def test_no_supplement_last_tile_requires_kong_win_and_empty_hand_can_finish(onlycut, empty, wall_empty):
    async def run():
        game, _ = await win_state([] if empty else ["AX4"], [
            {"tiles": ("AX1 AX2 AX3 AX4" if empty else "AX1 AX2 AX3").split(), "kind": "sequence"}])
        try:
            player = game.players[0]
            if onlycut:
                game.state_machine.status = HongqueStatus.ONLYCUT_AFTER_ACTION
            if wall_empty:
                game.wall = []
            else:
                player.supplements = 2
            actions = game.build_state(0)["legal_actions"]
            assert "win" in actions and "kong" not in actions and "supplement" not in actions
            if empty:
                await _timeout_hand_action(game, player)
            else:
                await game.submit_action(player.user_id, "win")
            assert game.phase == "round_end" and player.hand == []
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())
