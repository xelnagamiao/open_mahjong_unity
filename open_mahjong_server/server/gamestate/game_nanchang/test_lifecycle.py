import asyncio
from copy import deepcopy
import pytest

from .test_state import state,NORMAL
from .state_machine import Phase as P


def test_nonbot_round_ready_timeout_and_completed_match_have_finite_waits():
    async def check():
        s=state();s.end_draw()
        await asyncio.wait_for(s.run_round_ready_phase(timeout=0),1)
        assert s.machine.phase==P.READY and not s.waiting_players_list
        assert all(not actions for actions in s.action_dict.values())
        s.finalize_round_recording()
        s.advance_round_after_ready();s.initialize_round();s.begin_turn(0);s.current_round=4;s.end_draw()
        assert s.match_finishing
        await asyncio.wait_for(s.run_round_ready_phase(timeout=60),1)
        assert not s.waiting_players_list
        with pytest.raises(RuntimeError):s.advance_round_after_ready()
    asyncio.run(check())


def test_cut_timeout_avoids_jing_when_peida_and_consumes_bank():
    async def check():
        s=state();p=s.player_list[0];p.hand_tiles=NORMAL[:-1]+[45];p.has_draw_slot=True;p.tag_list=['peida']
        p.remaining_time=0;s.step_time=0
        w=s.open_action_window(s.begin_turn(0))
        results=await asyncio.wait_for(s.wait_action(timeout=0),1)
        assert results[0]['TileId']!=45 and results[0]['is_timeout_action']
        s.apply_action_results(w,results)
        assert 45 in p.hand_tiles and p.remaining_time==0
    asyncio.run(check())


@pytest.mark.parametrize('source,payer,tile,winner',[
    ('rob_kong',1,41,0),('self_draw',1,41,0),('self_draw',None,42,0),
    ('self_draw',None,41,1),('discard',1,41,0),('self_draw',None,41,True),
])
def test_settlement_source_identity_guards_preserve_score(source,payer,tile,winner):
    s=state();p=s.player_list[0];p.hand_tiles=NORMAL.copy();p.has_draw_slot=True
    before=deepcopy((s.round_changes,s.deferred_hu_settlements,[p.score for p in s.player_list]))
    with pytest.raises(ValueError):s.settle_win(winner,source,tile,payer=payer)
    assert (s.round_changes,s.deferred_hu_settlements,[p.score for p in s.player_list])==before
    assert s.round_settlement is None


def test_already_scored_hand_rejects_second_win_or_draw():
    s=state();p=s.player_list[0];p.hand_tiles=NORMAL.copy();p.has_draw_slot=True
    s.settle_win(0,'self_draw',41)
    before=deepcopy(s.round_changes)
    with pytest.raises(ValueError):s.settle_win(0,'self_draw',41)
    with pytest.raises(ValueError):s.end_draw()
    assert s.round_changes==before and len(s.deferred_hu_settlements)==1


def test_protocol_time_bank_remains_integer_after_fractional_action_receipts():
    s=state()
    for p,time in zip(s.player_list,[12.125,3.0,0.1,0]):p.remaining_time=time
    for viewer in range(4):
        players=s.build_game_start_payload(viewer)['game_info']['players_info']
        assert [p['remaining_time'] for p in players]==[13,3,1,0]
        assert all(type(p['remaining_time']) is int for p in players)
    s.end_draw()
    for viewer in range(4):
        assert all(type(s._player_view(i,viewer)['remaining_time']) is int for i in range(4))


def test_ready_snapshot_carries_its_own_action_tick_through_response_codec():
    from ...response import Response
    s=state();s.end_draw();s.machine.transition(P.READY)
    s.server_action_tick=57
    s.action_dict={i:['ready'] for i in range(4)}
    for viewer in range(4):
        packet=Response.model_validate(s.build_ready_status_payload(viewer)).model_dump()
        assert packet['ready_status_info']['action_tick']==57
        assert not any(packet['ready_status_info']['player_to_ready'].values())


@pytest.mark.parametrize('scores,ranks,points',[
    ([30,10,-10,-30],[1,2,3,4],[4,3,2,1]),
    ([10,10,-10,-10],[1,1,3,3],[3.5,3.5,1.5,1.5]),
    ([0,0,0,0],[1,1,1,1],[2.5]*4),
])
def test_competition_ties_share_match_points_in_record_and_protocol(scores,ranks,points):
    from ...response import Response
    s=state();s.start_game_recording()
    for p,score in zip(s.player_list,scores):p.score=score
    s.persist_game_record()
    assert [p.record_counter.rank_result for p in s.player_list]==ranks
    assert [s.game_record['game_title']['nanchang_final_result'][i]['competition_points'] for i in range(4)]==points
    for payload in s.emit_game_end_payloads():
        final=Response.model_validate(payload).model_dump()['game_end_info']['player_final_data']
        assert [final[str(i)]['rank'] for i in range(4)]==ranks
        assert [final[str(i)]['competition_points'] for i in range(4)]==points
