import asyncio
from copy import deepcopy
from dataclasses import replace
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from ...database.subrule_fans import collect_subrule_fans
from ...game_calculation.guangdong import config, rules
from ...game_calculation.guangdong.scoring import Context
from ...room.guangdong_room import GuangdongRoomValidator
from ..game_taiwan import boardcast
from ..public.game_record_manager import init_game_record, init_game_round
from ..verifier.unity_sim import UnitySim
from .doubles import GuangdongDoublesGameState
from .test_flow import ActionSocket
from .test_state import HAND, hand, ticks

CHICKEN = [11, 12, 13, 14, 15, 16, 22, 23, 24, 36, 37, 38, 41]
PROFILE = config.DOUBLES_SUB_RULE


def state(**options):
    server = SimpleNamespace(user_id_to_connection={}, gamestate_manager=SimpleNamespace(
        cleanup_game_state_complete=AsyncMock()), room_manager=SimpleNamespace())
    room = dict(room_id="398623", player_list=[101, 102, 103, 104], round_timer=20, step_timer=5,
                game_round=2, tips=True, room_type="custom", allow_spectator=False, sub_rule=PROFILE,
                team_by_seat=[0, 1, 0, 1], random_seed=87)
    gs = GuangdongDoublesGameState(server, room | options, None, None, "gd-doubles-test")
    for i, p in enumerate(gs.player_list):
        p.player_index = p.original_player_index = i
        server.user_id_to_connection[p.user_id] = SimpleNamespace(websocket=ActionSocket(gs, p.user_id))
    gs.tiles_list = [19, 19, 19]
    init_game_record(gs)
    init_game_round(gs)
    return gs


@pytest.fixture(autouse=True)
def presentation(monkeypatch):
    import server.gamestate.game_guangdong.doubles as module
    monkeypatch.setattr(module, "hu_result_ready_pre_panel_seconds", lambda: 0)
    monkeypatch.setattr(module, "liuju_ready_wait_seconds", lambda: 0)
    monkeypatch.setattr(module, "sichuan_settle_hu_panel_wait_seconds", lambda *args, **kwargs: 0)


@pytest.mark.parametrize("wild", [False, True])
@pytest.mark.parametrize("self_draw", [False, True])
def test_chicken_exception_and_scoring(wild, self_draw):
    tiles = list(CHICKEN) + [41]
    if wild:
        tiles[0] = 55
    ctx = Context(self_draw=self_draw, doubles=True)
    detail = rules.score(tiles, context=ctx)
    assert detail["chicken"] and detail["fan"] == 1 + self_draw
    assert detail["base_score"] == (1 + self_draw) * (1 if wild else 2)
    assert detail["fan_names"].count("GD|chicken|0|鸡和") == 1
    assert rules.score(tiles, context=replace(ctx, chicken_available=False)) is None
    single = rules.score(tiles, context=replace(ctx, doubles=False))
    assert bool(single) == (self_draw and not wild)
    high = rules.score(HAND + [45], context=replace(ctx, chicken_available=False))
    assert high and not high["chicken"]
    assert not rules.score(CHICKEN[3:] + [41], ["k11"], context=ctx)
    assert rules.score(CHICKEN[3:] + [41], ["G11"], context=ctx)["chicken"]


def test_private_csharp_context_teams_and_stop_after_ghost():
    gs = state()
    p = gs.player_list[0]
    for index, tile in [(0,55), (2,56)]:
        gs.player_list[index].last_discarded_tile = tile
        gs._finalize_ready_after_discard(index, False)
    assert gs.local_context(0)["local_request"]["context"]["discarded_ghosts"] == 2
    p.tag_list.append("peida")
    p.last_discarded_tile = 57
    gs._finalize_ready_after_discard(0, False)
    assert gs.team_ghosts(2) == 2 and gs.team_ghosts(1) == 0
    assert gs.local_context(0)["local_request"]["context"]["blocked"]
    gs._reset_hand_runtime()
    assert not gs.chicken_teams and gs.team_ghosts(2) == 0 and "peida" not in p.tag_list


@pytest.mark.parametrize("gameround", [1, 2, 3, 4])
def test_room_profile(gameround):
    args = dict(room_name="广东双打", sub_rule=PROFILE, game_round=gameround, round_timer=10, step_timer=4)
    room = GuangdongRoomValidator(**args)
    assert room.detailed_config["horse_count"] == 0 and room.detailed_config["edition"] == config.DOUBLES_EDITION
    with pytest.raises(ValueError):
        GuangdongRoomValidator(**dict(args, game_round=2, detailed_config={"horse_count": 4}))


def test_same_team_double_ron_privacy_and_continuation():
    async def run():
        gs = state()
        hand(gs, 1, CHICKEN); hand(gs, 3, CHICKEN)
        gs.player_list[0].discard_tiles.append(41)
        allowed = gs.check_discard_actions(41)
        assert "hu_first" in allowed[1] and "hu_third" in allowed[3]
        await gs.resolve_discard_responses({3: {"action_type": "hu_third"}, 1: {"action_type": "hu_first"}}, allowed)
        assert gs.game_status == "doubles_win"
        await gs.confirm_wins()
        assert gs.game_status == "deal_card" and [i["index"] for i in gs.win_events] == [1]
        assert gs.player_list[0].discard_tiles == [] and gs.player_list[0].discard_origin_tiles == [41]
        assert "peida" not in gs.player_list[3].tag_list and gs.chicken_teams == {1}
        assert gs.score_candidate(3, "discard", 41) is None
        assert all(p.score == 0 for p in gs.player_list)
        assert "hu_first" not in gs.check_discard_actions(41)[1]
        for p in gs.player_list:
            msg = gs.game_server.user_id_to_connection[p.user_id].websocket.messages[-1]["show_result_info"]
            assert msg["hepai_tile"] == 41 and msg["suppress_hand_reveal"] and msg["defer_score_settlement"]
            assert not any(k in msg for k in ("hu_fan","hu_score","hepai_player_hand","revealed_hands","player_to_score"))
        await gs._deal_normal()
        assert gs.current_player_index == 2
        assert gs.next_dealer() == 1
    asyncio.run(run())


def test_freeze_ghosts_kongs_and_statistics():
    async def run():
        gs = state()
        gs.current_round = 8
        gs.player_list[0].discarded_ghosts = gs.player_list[2].discarded_ghosts = 1
        await gs._pay_kong(1, "direct", 29, payer=3)
        p = hand(gs, 0, CHICKEN + [41]); p.last_drawn_tile = 41
        await boardcast.broadcast_game_start(gs)
        sims = []
        for player in gs.player_list:
            sim = UnitySim(viewer_user_id=player.user_id)
            start = gs.game_server.user_id_to_connection[player.user_id].websocket.messages[-1]
            sim.apply(start)
            assert sim.gsm["selfIndex"] == player.player_index
            sims.append(sim)
        gs.accept_self_draw(0); await gs.confirm_wins()
        first = gs.win_events[0]
        assert first["detail"]["coefficient"] == 8
        for viewer in range(4):
            packet = gs.game_server.user_id_to_connection[gs.player_list[viewer].user_id].websocket.messages[-1]
            sim = sims[viewer]
            sim.apply(packet)
            position = sim.pos_of(0)
            assert "gd_chicken" in sim.gsm["player_to_info"][position]["tag_list"]
            assert sim.game3d[position]["win_marker"] == (41 if viewer == 0 else 0)
            assert sim.end_result["show_result"] is False and sim.end_result["hu_fan"] is None
            info = packet["show_result_info"]
            assert info["hepai_tile"] == (41 if viewer == 0 else 0)
            snap = gs.player_view_fields(p, viewer)
            assert snap["hand_tiles_count"] == 13 and (snap["hand_tiles"] is None) == (viewer != 0)
            await boardcast.send_reconnect_game_state(gs, gs.player_list[viewer])
            snapshot = gs.game_server.user_id_to_connection[gs.player_list[viewer].user_id].websocket.messages[-1]
            sim.apply(snapshot)
            assert sim.game3d[position]["win_marker"] == (41 if viewer == 0 else 0)
        await gs._pay_kong(3, "concealed", 35)
        gs.player_list[2].discarded_ghosts += 1
        hand(gs, 2, HAND)
        gs.current_player_index = 1
        gs.player_list[1].discard_tiles = [45]
        offered = gs.check_discard_actions(45)
        await gs.resolve_discard_responses({2: {"action_type": "hu_first"}}, offered)
        await gs.confirm_wins()
        assert gs.game_status == "END" and gs.win_events[-1]["detail"]["coefficient"] == 16
        assert first["detail"]["coefficient"] == 8 and gs.valid_kongs == 2
        await gs._settle_hand(dict.fromkeys(range(4),0))
        scores = [p.score for p in gs.player_list]
        await gs._settle_hand(dict.fromkeys(range(4),0))
        assert [p.score for p in gs.player_list] == scores and sum(scores) == 0
        assert gs.game_record["game_title"]["sub_rule"] == PROFILE
        assert collect_subrule_fans(gs.game_record,0,PROFILE) == {"门清":1, "自摸":1, "鸡和":1}
        assert collect_subrule_fans(gs.game_record,2,PROFILE)["碰碰和"] == 1
        assert not [tick for tick in ticks(gs) if tick[:2] == ["guangdong","horses"]]
    asyncio.run(run())


def test_unfollowed_kongs_are_void_and_draw_repeats_dealer():
    async def run():
        gs = state(); gs.current_round = 7; gs.run_hu_result_ready_phase = AsyncMock()
        await gs._pay_kong(0, "direct", 25, payer=1)
        await gs._settle_hand(dict.fromkeys(range(4),0))
        assert gs.next_dealer() == 0 and all(p.score == 0 for p in gs.player_list)
        gs._reset_hand_runtime()
        await gs._pay_kong(0, "direct", 25, payer=1)
        p = hand(gs, 2, CHICKEN + [41]); p.last_drawn_tile = 41
        gs.accept_self_draw(2); await gs.confirm_wins()
        await gs._pay_kong(0, "concealed", 24)
        await gs._settle_hand(dict.fromkeys(range(4),0))
        assert gs.valid_kongs == 1 and gs.next_dealer() == 2
        assert [p.score - gs.win_events[0]["changes"][i] for i,p in enumerate(gs.player_list)] == [2,-2,0,0]
        gs.current_round=8
        assert gs.next_dealer() == 1
    asyncio.run(run())
