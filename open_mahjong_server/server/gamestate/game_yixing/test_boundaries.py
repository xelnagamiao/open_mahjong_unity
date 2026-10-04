"""Failure atomicity, exceptional lifecycle paths, and rulebook edge cases."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_flow import state,turn,act,start,river,PLAIN,OTHER,physical
from .state_machine import Phase as P
from .actions import legal_cuts,turn_actions
from .YixingGameState import YixingGameState
from ...game_calculation.yixing.rules import TILES,FLOWERS,score_hand
from ...room.yixing_room import YixingRoomValidator


@pytest.mark.parametrize('config',[{'sub_rule':'yixing/unknown'},{'claim_protection':True},{'use_flowers':False},{'use_flowers':1}])
def test_wrong_rule_profile_rejected(config):
    with pytest.raises(ValueError): YixingGameState(room_data={**YixingGameState._default_room_data(),**config})


def test_deck_is_fixed_144_and_opening_cannot_repeat():
    s=state(); assert s.use_flowers is True
    wall=[t for t in TILES for _ in range(4)]+list(FLOWERS)
    with pytest.raises(ValueError): s.initialize_round(wall=wall[:-1])
    s.initialize_round(wall=wall)
    assert len(s.tiles_list)==91 and sum(len(p.hand_tiles) for p in s.player_list)==53
    s.open_action_window(s.opening_window())
    with pytest.raises(RuntimeError): s.initialize_round()


@pytest.mark.parametrize('winner,source,tile,payer',[(True,'self_draw',28,None),(4,'self_draw',28,None),(0,'unknown',28,None),
    (0,'self_draw',28,1),(1,'self_draw',28,None),(0,'self_draw',11,None),(1,'discard',28,None),
    (1,'discard',28,True),(1,'discard',28,1),(1,'discard',28,0),(1,'rob_kong',28,0)])
def test_invalid_settlement_is_atomic(winner,source,tile,payer):
    s=turn()
    def snapshot():
        return deepcopy([{k:v for k,v in vars(p).items() if k!='record_counter'} for p in s.player_list])
    before=snapshot()
    with pytest.raises(ValueError): s.settle_win(winner,source,tile,payer=payer)
    assert s.round_settlement is None
    assert snapshot()==before


def test_self_draw_cannot_be_used_as_a_response_or_without_draw_slot():
    s=turn();s.player_list[1].hand_tiles=PLAIN[:-1]
    river(s,0,28)
    with pytest.raises(ValueError): s.settle_win(0,'self_draw',28)
    s.player_list[1].is_hu=True
    assert not s.can_win(1,'discard',28)
    assert not s.can_win(0,'wrong',28)
    s.player_list[0].has_draw_slot=False
    assert not s.can_win(0,'self_draw',28)


def test_unqualified_open_hand_cannot_settle_and_second_settlement_rejected():
    s=turn(PLAIN[3:]);s.player_list[0].combination_tiles=['s12']
    with pytest.raises(ValueError): s.settle_win(0,'self_draw',28)
    s=turn();s.settle_win(0,'self_draw',28)
    with pytest.raises(ValueError): s.settle_win(0,'self_draw',28)
    with pytest.raises(ValueError): s.end_draw()


def test_fixed_pattern_shared_bottom_is_shown_exactly_once():
    # Pure + menqing = 8 + 4 - 1 = 11; no flower/kong extras.
    s=turn([11,12,13,12,13,14,14,15,16,17,18,19,15,15])
    s.settle_win(0,'self_draw',15)
    result=s.build_final_settlement_payload(0)['show_result_info']
    assert s.round_changes==[33,-11,-11,-11]
    assert 'YX|shared_bottom|-1|重复底花' in s.deferred_hu_settlements[0]['fan_ids']
    assert '重复底花（-1花）' in s.deferred_hu_settlements[0]['fan_names']
    assert result['yixing_fan_details']['base_flowers']==11


@pytest.mark.parametrize('seat,data',[(True,{}),(-1,{}),(0,None),(0,{'action_type':'angang','target_tile':28}),
    (0,{'action_type':'jiagang','target_tile':True})])
def test_bad_action_payload_rejected_before_mutation(seat,data):
    s=turn()
    with pytest.raises(ValueError): s.validate_response(seat,data,['angang','jiagang'])


def test_illegal_hand_cut_of_only_drawn_copy_and_held_flower():
    s=turn(PLAIN[:-1]+[47]);p=s.player_list[0]
    with pytest.raises(ValueError): s._cut_identity(0,dict(TileId=47,cutClass=False))
    assert s.legal_discard_tiles(0)==set(p.hand_tiles)
    p.hand_tiles[0]=51
    assert not legal_cuts(s,0)


def test_drawing_from_empty_wall_and_four_sea_passes_record_draw():
    s=state();start(s)
    s.machine.phase=P.RESPONSE
    s.tiles_list=[28];s.open_action_window(s.draw_for(0))
    for _ in range(4): act(s,s.current_player_index,'pass')
    s.finalize_round_recording()
    ticks=next(iter(s.game_record['game_round'].values()))['action_ticks']
    assert sum(t[:2]==['yixing','last_choice'] for t in ticks)==4
    assert any(t[0]=='liuju' for t in ticks)
    assert ticks[-1]==['end']
    s=turn();s.tiles_list=[];s.draw_for(0)
    assert s.round_settlement['source']=='draw'
    state().finalize_round_recording()  # no round started: safe no-op


def test_cancelled_game_loop_cancels_owned_bot_tasks(monkeypatch):
    async def run():
        s=state();task=asyncio.create_task(asyncio.sleep(100));s.bot_tasks.add(task)
        s.start_game_recording=lambda: (_ for _ in ()).throw(asyncio.CancelledError())
        with pytest.raises(asyncio.CancelledError): await s.run_game_loop()
        await asyncio.gather(task,return_exceptions=True)
        assert task.cancelled()
    asyncio.run(run())


def test_explicit_fixed_false_flags_roundtrip():
    room=YixingRoomValidator(room_name='宜兴',round_timer=0,step_timer=0,claim_protection=False,
        open_cuohe=False,tactical_call=False,tian_di_ren_he=False,use_flowers=True)
    assert room.use_flowers and not room.claim_protection


def test_dice_seating_record_identity_survives_dealer_rotation():
    s=state(19)
    s.start_game_recording()
    title=s.game_record['game_title']
    assert title['player_entry_order']==[101,102,103,104]
    assert [p.original_player_index for p in s.player_list]==list(range(4))
    assert [title[f'p{i}_uid'] for i in range(4)]==[p.user_id for p in s.player_list]
    s.initialize_round();s.open_action_window(s.opening_window())
    s.end_draw();s.machine.transition(P.READY)
    s.next_dealer=1;s.advance_round_after_ready()
    for p in s.player_list:
        assert title[f'p{p.original_player_index}_uid']==p.user_id


def test_chow_can_use_flower_replacements_to_escape_food_swap_restrictions():
    s=turn()
    s.player_list[1].hand_tiles=[14,15,13,13,13,16,16,16,16,51,52,53,54]
    s.tiles_list=[19,18,17,11,12,21]
    river(s,0,13)
    assert 'chi_right' in s.action_dict[1]
    act(s,1,'chi_right')
    for _ in range(4):
        assert s.action_dict[1]==['buhua']
        act(s,1,'buhua')
    assert s.machine.phase==P.DISCARD_ONLY
    assert not ({13,16} & legal_cuts(s,1))
    assert set(s.player_list[1].huapai_list)=={51,52,53,54}


def test_chow_is_not_offered_when_every_remaining_tile_would_be_forbidden():
    s=turn();p=s.player_list[1]
    p.hand_tiles=[14,15,13,13,13,16,16]
    p.combination_tiles=['k21','k31']
    river(s,0,13)
    assert 'chi_right' not in s.action_dict[1]


def test_explicit_cut_identity_and_non_play_phase_are_validated():
    s=turn()
    assert s._cut_identity(0,dict(TileId=11,cutIndex=0))==(11,0,False)
    s.live_pending_window={'status':P.START.value,'action_tick':s.server_action_tick,'actions':{}}
    with pytest.raises(ValueError,match='当前阶段'):s.apply_action_results(s.live_pending_window,{})


def test_no_cut_is_offered_while_a_flower_still_needs_replacement():
    s=turn();s.player_list[0].hand_tiles[0]=51
    assert turn_actions(s,0,discard_only=True)[0]==[]


def test_late_timeout_and_delayed_response_do_not_hang_or_repeat_a_win_record():
    async def run():
        s=turn();s.waiting_players_list=[]
        assert s._build_timeout_action(0)['action_type']=='cut'
        s=turn();s.player_list[0].remaining_time=3
        async def respond():
            await asyncio.sleep(.025)
            await s.submit_action(0,'hu_self')
        task=asyncio.create_task(respond())
        responses=await asyncio.wait_for(s.wait_action(),1)
        await task
        assert responses[0]['action_type']=='hu_self'
        s=state();start(s);s.player_list[0].hand_tiles=list(PLAIN)
        s.open_action_window(s.begin_turn(0));act(s,0,'hu_self')
        before=deepcopy(s.game_record)
        s.record_visible_action(s.domain_events[-1])
        assert s.game_record==before
    asyncio.run(run())


def test_view_adapter_accepts_non_snapshot_frames_and_absent_observer_connections():
    async def run():
        s=turn()
        assert s._adapt({'type':'test'},2)=={'type':'test','player_index':2}
        s.game_server=SimpleNamespace(user_id_to_connection={})
        await s.send_realtime_spectator_snapshot(999,1)
        s.game_server.user_id_to_connection[999]=SimpleNamespace(websocket=None)
        await s.send_realtime_spectator_snapshot(999,1)
    asyncio.run(run())


def test_cached_shape_helpers_reject_invalid_input_and_predeal_restore_has_no_ask():
    from ...game_calculation.yixing.rules import _shapes,_waits
    assert _shapes((11,),(),False)==()
    assert _waits((11,),(),False)==frozenset()
    s=state()
    restored=s.restore_payloads(0)
    assert len(restored)==1 and restored[0]['type'].endswith('/game_start')


def test_multiple_ready_replies_are_preserved_without_spending_clock():
    async def run():
        s=turn();s.settle_win(0,'self_draw',28)
        s.open_action_window(s._window(P.READY,0,actions={i:['ready'] for i in range(4)}))
        banks=[p.remaining_time for p in s.player_list]
        for i in range(4):await s.submit_action(i,'ready')
        replies=await asyncio.wait_for(s.wait_action(),1)
        assert set(replies)==set(range(4))
        assert all(reply['action_type']=='ready' for reply in replies.values())
        assert banks==[p.remaining_time for p in s.player_list]
    asyncio.run(run())


def test_cancelled_expired_window_without_legal_actions_does_not_mutate_tiles():
    async def run():
        s=turn(step_timer=0,round_timer=0)
        before=list(s.player_list[0].hand_tiles)
        # Simulate teardown clearing the offer before the waiting task is cancelled.
        s.action_dict[0]=[]
        waiting=asyncio.create_task(s.wait_action())
        await asyncio.sleep(.01)
        waiting.cancel()
        with pytest.raises(asyncio.CancelledError):await waiting
        assert s.player_list[0].hand_tiles==before
        assert not s.action_queues[0].qsize()
    asyncio.run(run())
