import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_state import state, turn, act, PLAIN
from .state_machine import Phase as P


def test_added_kong_keeps_original_provider_and_pung_history():
    s=turn([12,13,14,21,22,23,31,32,33,41,18])
    p=s.player_list[0]
    p.combination_tiles=['k18'];p.combination_mask=[[1,18,0,18,0,18]]
    p.meld_sources=[2];p.pung_melds={0};p.pung_claimed=True
    s.open_action_window(s.begin_turn(0))
    act(s,action_type='jiagang',target_tile=18);act(s)
    assert p.combination_tiles==['g18'] and p.meld_sources==[2]
    assert p.pung_melds=={0} and p.pung_claimed
    assert p.combination_mask==[[3,18,1,18,0,18,0,18]]
    assert s.round_changes==[10,0,-10,0] and s.kong_chain_payer==2


def test_fourth_concealed_kong_forces_contract_win_before_replacement():
    s=turn([41,41,41,41,22]);p=s.player_list[0]
    p.combination_tiles=['k11','k24','k37'];p.meld_sources=[2,2,2]
    p.combination_mask=[[1,t,0,t,0,t] for t in (11,24,37)]
    p.pung_claimed=True;s.establish_contracts(0)
    wall=list(s.tiles_list)
    s.open_action_window(s.begin_turn(0))
    act(s,action_type='angang',target_tile=41);act(s)
    win=s.deferred_hu_settlements[0]
    assert s.machine.phase==P.END and s.tiles_list==wall
    assert win['forced'] and win['source']=='self_draw' and '全求独钓' in win['fan_names']
    assert win['score_changes']==[win['points']*3,0,-win['points']*3,0]
    assert p.hand_tiles==[22] and p.won_tiles==[]
    packet=s.build_final_settlement_payload(0)['show_result_info']
    assert packet['skip_hand_reveal'] is True and packet['hepai_player_hand']==[22]
    assert packet['recycle_discard'] is False and s.build_pending_action_payload(0) is None


def test_contract_self_draw_promotes_triplets_and_charges_partner():
    s=turn([41,41,41,22,22]);p=s.player_list[0]
    p.combination_tiles=['k11','k24','k37'];p.meld_sources=[2,2,2]
    p.pung_claimed=True;p.huapai_list=[51]
    s.establish_contracts(0);s.open_action_window(s.begin_turn(0))
    act(s,action_type='hu_self')
    win=s.deferred_hu_settlements[0]
    assert '全求独钓' in win['fan_names'] and '碰碰和' not in win['fan_names']
    assert win['score_changes']==[win['points']*3,0,-win['points']*3,0]


@pytest.mark.parametrize('contract',[False,True])
def test_pure_suit_ron_external_liability_and_contract_precedence(contract):
    s=turn(PLAIN[:-1]+[18],actor=2);p=s.player_list[0]
    p.hand_tiles=[12,13,14,18];p.combination_tiles=['k11','g15','k17']
    p.pung_melds={0,1,2};p.pung_claimed=True;p.huapai_list=[51]
    p.meld_sources=[1,1,1] if contract else [1,2,3]
    s.establish_contracts(0)
    act(s,action_type='cut',TileId=18,cutClass=True);act(s,{0:'hu'})
    win=s.deferred_hu_settlements[0]
    assert '清一色' in win['fan_names']
    amount=win['points']*(1 if contract else 3)
    assert win['score_changes']==([amount,-amount,0,0] if contract else [amount,0,-amount,0])


def test_fourth_pung_external_liability_yields_to_existing_contract():
    s=turn();p=s.player_list[0]
    p.hand_tiles=[22];p.combination_tiles=['k11','g14','k17','k18']
    p.pung_melds={0,1,2,3};p.pung_claimed=True
    s.contracts[0]={1};s.machine.phase=P.RESPONSE
    s.forced_claim_win(0,2,18,'peng')
    win=s.deferred_hu_settlements[0]
    assert win['forced'] and {'清一色','全求独钓'}.issubset(win['fan_names'])
    assert win['score_changes']==[win['points'],-win['points'],0,0]


def test_last_wall_discard_can_win_and_empty_wall_disables_kong():
    s=turn(PLAIN[:-1]+[18]);s.tiles_list=[22]
    s.player_list[2].hand_tiles=PLAIN[:-1]
    act(s,action_type='cut',TileId=18,cutClass=True);act(s)
    assert not s.tiles_list and s.current_player_index==1
    assert 'angang' not in s.action_dict[1] and 'jiagang' not in s.action_dict[1]
    act(s,action_type='cut',TileId=22,cutClass=True);act(s,{2:'hu'})
    assert s.round_settlement['winners']==[2]


def test_reconnect_ready_and_spectator_restore_full_protocol():
    async def run():
        s=turn();s.end_draw();s.machine.transition(P.READY)
        p=s.player_list[1];p.tag_list=['offline']
        s.send_payload_to_player=AsyncMock()
        await s.player_reconnect(p.user_id)
        assert 'offline' not in p.tag_list
        delivered=[c.args[1]['type'].split('/')[-1] for c in s.send_payload_to_player.await_args_list]
        assert delivered==['game_start','show_result','ready_status']
        ws=SimpleNamespace(send_json=AsyncMock())
        s.game_server=SimpleNamespace(user_id_to_connection={999:SimpleNamespace(websocket=ws)})
        await s.send_realtime_spectator_snapshot(999,1)
        assert ws.send_json.await_count==3
        await s.send_realtime_spectator_snapshot(999,4)
        assert ws.send_json.await_count==3
    asyncio.run(run())


def test_timeout_flower_discard_and_ready_are_valid_actions():
    async def run():
        s=turn(PLAIN[:-1]+[45]);s._timing_now=lambda:100
        s.action_clocks[0].start(1)
        responses=await s.wait_action()
        assert responses[0]['action_type']=='buhua'
        s.apply_action_results(s.live_pending_window,responses)
        s=turn();s._timing_now=lambda:100;s.action_clocks[0].start(1)
        responses=await s.wait_action()
        assert responses[0]['TileId']==22 and responses[0]['cutClass'] is True
        s.apply_action_results(s.live_pending_window,responses)
        s.end_draw();s.flush_outbound_payloads=AsyncMock()
        await s.run_round_ready_phase(timeout=0)
        assert not s.waiting_players_list
    asyncio.run(run())


def test_actual_async_bot_match_lifecycle(monkeypatch):
    from . import bot
    from ..public.ai import pacing
    from ..game_guobiao import buhua_broadcast
    monkeypatch.setattr(bot,'wait_bot_delay',AsyncMock())
    monkeypatch.setattr(pacing,'bot_delay',lambda _:0)
    monkeypatch.setattr(buhua_broadcast,'HAND_SETTLE_GAP_SEC',0)
    async def run():
        s=state(19)
        for i,p in enumerate(s.player_list):p.user_id=i+1
        await asyncio.wait_for(s.run_game_loop(),90)
        assert s.machine.phase==P.FINISHED and s.current_round==4
        assert len(s.game_record['game_round'])==4 and sum(p.score for p in s.player_list)==0
        assert any(p.get('type')=='gamestate/nanjing/game_end' for p in s.outbound_payloads)
        assert all(t.done() or t.cancelling() for t in s.bot_tasks)
    asyncio.run(run())


def test_kong_chain_win_is_paid_by_original_exposed_kong_provider():
    s=turn();p=s.player_list[0]
    p.after_kong=True;p.draw_kind='kong';s.kong_chain_actor=0;s.kong_chain_payer=2;s.kong_chain_count=2
    s.open_action_window(s.begin_turn(0));act(s,action_type='hu_self')
    win=s.deferred_hu_settlements[0]
    assert '杠开' in win['fan_names'] and win['score_changes']==[win['points']*3,0,-win['points']*3,0]


@pytest.mark.parametrize('data',[
    dict(action_type='cut',TileId=True),dict(action_type='cut',TileId=99),
    dict(action_type='cut',TileId=22,cutIndex=30),dict(action_type='cut',TileId=22,cutIndex=True),
    dict(action_type='cut',TileId=22,cutClass='false'),dict(action_type='cut',TileId=22,cutIndex=0),
    dict(action_type='angang',target_tile='22'),dict(action_type='chi_left'),
])
def test_malformed_moves_leave_the_board_and_window_unchanged(data):
    s=turn();before=(list(s.player_list[0].hand_tiles),s.server_action_tick,len(s.domain_events))
    with pytest.raises(ValueError):act(s,**data)
    assert (s.player_list[0].hand_tiles,s.server_action_tick,len(s.domain_events))==before


def test_cut_identity_fallback_and_drawn_only_tile():
    s=turn(PLAIN[:-1]+[18])
    with pytest.raises(ValueError):s._cut_identity(0,dict(TileId=18,cutClass=False))
    assert s._cut_identity(0,dict(TileId=18))==(18,13,True)
    assert s._cut_identity(0,dict(TileId=11))==(11,0,False)
    assert s._cut_identity(0,dict(TileId=12,cutIndex=1))==(12,1,False)


def test_phase_and_win_guards_prevent_invalid_or_duplicate_settlement():
    s=turn()
    with pytest.raises(RuntimeError):s.initialize_round()
    with pytest.raises(RuntimeError):s.advance_round_after_ready()
    with pytest.raises(RuntimeError):s.machine.transition(P.FINISHED)
    with pytest.raises(ValueError):s._ledger('invalid',0,[1,0,0,0])
    for winners,source,payer in [([], 'self_draw', None),([1], 'self_draw', None),([0], 'discard', 0),([1], 'discard',0),([1], 'rob_kong',0),([1,1], 'discard',0)]:
        with pytest.raises(ValueError):s.settle_wins(winners,source,22,payer=payer)
    assert not s.round_changes==[1,0,0,0] and not s.deferred_hu_settlements
    act(s,action_type='hu_self')
    before=list(s.round_changes)
    with pytest.raises(ValueError):s.settle_wins([0],'self_draw',22)
    with pytest.raises(ValueError):s.end_draw()
    assert s.round_changes==before and not s.is_match_end()


@pytest.mark.parametrize('overrides',[dict(sub_rule='nanjing/other'),dict(use_flowers=False),dict(tactical_call=True)])
def test_state_rejects_invalid_saved_room_configs(overrides):
    from .NanjingGameState import NanjingGameState
    with pytest.raises(ValueError):NanjingGameState(room_data={**NanjingGameState._default_room_data(),**overrides})


def test_custom_wall_requires_all_physical_tiles():
    from ...game_calculation.nanjing.rules import PHYSICAL
    s=state()
    with pytest.raises(ValueError):s.initialize_round(wall=list(PHYSICAL[:-1]))
    s=state();s.initialize_round(wall=list(PHYSICAL))
    assert [len(p.hand_tiles) for p in s.player_list]==[14,13,13,13]


@pytest.mark.parametrize('rotation',[0,1,2,3])
def test_self_draw_remainder_uses_match_start_seats_after_dealer_rotation(rotation):
    s=state();s.player_list=s.player_list[rotation:]+s.player_list[:rotation]
    for i,p in enumerate(s.player_list):p.player_index=i
    seats={p.original_player_index:p.player_index for p in s.player_list}
    changes=s._payments(seats[0],67,responsible=[seats[1],seats[3]])
    by_original={p.original_player_index:changes[p.player_index] for p in s.player_list}
    assert by_original=={0:201,1:-101,2:0,3:-100}
