import asyncio
from copy import deepcopy

import pytest

from .test_doubles import CHICKEN, presentation, state
from .test_state import hand, ticks


def test_referee_penalty_once_and_ghosts_before_stop():
    async def run():
        gs = state()
        gs.game_status = "waiting_hand_action"
        p = hand(gs, 0, CHICKEN + [55])
        hand(gs, 1, [11])
        hand(gs, 3, [56])
        gs.player_list[1].discarded_ghosts = gs.player_list[3].discarded_ghosts = 1
        p.last_discarded_tile = 57
        gs._finalize_ready_after_discard(0, False)
        tick = gs.server_action_tick
        result = await gs.adjudicate(gs.round_index, 0, "false_win")
        assert result["penalty"] == 256 and [p.score for p in gs.player_list] == [-256,0,0,0]
        assert await gs.adjudicate(gs.round_index, 0, "false_win") == result
        assert p.record_counter.cuohe_times == 1 and gs.server_action_tick == tick
        p.last_discarded_tile = 58
        gs._finalize_ready_after_discard(0, False)
        assert gs.team_ghosts(2) == 1 and gs.check_hand_actions(0)[0] == ["cut"]
        gs.accept_self_draw(0)
        assert gs.game_status == "waiting_hand_action" and gs.action_dict[0] == ["cut"]
        assert gs._selected_winners([(0,"hu_first"), (2,"hu_third")]) == [(2,"hu_third")]
        gs.current_round = 8
        await gs._settle_hand(dict.fromkeys(range(4),0))
        assert int(p.score_history[-1]) == -256 and gs.team_boards[-1]["team_totals"] == [-256,0]
        assert len([t for t in ticks(gs) if t[:2]==["guangdong","penalty"]]) == 1
        with pytest.raises(ValueError):
            await gs.adjudicate(gs.round_index, 2, "forfeit")
    asyncio.run(run())


@pytest.mark.parametrize("both", [False, True])
def test_second_chicken_and_team_forfeit_at_board_end(both):
    async def run():
        gs=state()
        gs.game_status="waiting_hand_action"
        with pytest.raises(ValueError):
            await gs.adjudicate(gs.round_index, 0, "second_chicken")
        gs.chicken_teams.add(0)
        result=await gs.adjudicate(gs.round_index,2,"second_chicken")
        assert result["penalty"]==0 and "peida" in gs.player_list[2].tag_list
        await gs.adjudicate(gs.round_index,2,"forfeit")
        if both:
            await gs.adjudicate(gs.round_index,1,"forfeit")
        assert gs.game_status=="waiting_hand_action"
        assert await gs._settle_hand(dict.fromkeys(range(4),0))
        gs._finish_match()
        assert gs.team_result["reason"]=="forfeit"
        assert gs.team_result["winner_team_id"]==(None if both else 1)
        assert gs.team_result["match_points"]=={0:0,1:int(not both)}
    asyncio.run(run())


def test_referee_http_request_checks_game_and_board_identity():
    from fastapi import FastAPI
    from ...room.test_admin_rooms import request
    from ...webapi.admin_room import register_admin_room_routes

    async def run():
        gs = state()
        gs.game_status = "waiting_hand_action"
        gs.game_server.gamestate_manager.get_game_state_by_room_id = lambda room: gs if room == gs.room_id else None
        app = FastAPI()
        register_admin_room_routes(app, gs.game_server, proxy_secret="test-room-admin-secret")
        path = f"/admin/rooms/{gs.room_id}/ruling"
        body = dict(gamestate_id=gs.gamestate_id, round_index=gs.round_index, original_seat=1, decision="forfeit")
        assert (await request(app, "POST", path, body, signed=False))[0] == 403
        assert (await request(app, "POST", path, body | {"gamestate_id": "old"}))[0] == 409
        assert (await request(app, "POST", path, body | {"round_index": gs.round_index + 1}))[0] == 409
        assert not gs.forfeited_teams
        status, accepted = await request(app, "POST", path, body)
        assert status == 200 and accepted["success"] and gs.forfeited_teams == {1}
    asyncio.run(run())


def test_spectator_event_does_not_disclose_score_or_winning_draw():
    gs=state()
    gs.player_list[0].tag_list.append("gd_chicken")
    tick=["hu_self",0,16,["GD|concealed|1|门清", "GD|chicken|0|鸡和"],[0]*4,41,0,0]
    original=deepcopy(tick)
    assert gs.spectator_record_tick(tick)==["hu_self",0,0,["鸡和"],[0]*4,0,0,0]
    assert tick==original
    tile=["hu_first",1,8,["GD|pungs|4|碰碰和"],[0]*4,45,1,0,0]
    view=gs.spectator_record_tick(tile)
    assert view[3]==["非鸡和"] and view[5]==45
