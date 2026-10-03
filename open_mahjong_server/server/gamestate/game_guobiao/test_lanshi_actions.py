"""蓝十复用生产动作队列与牌谱记录，检查牌实体、转移、截和与暗杠权限。"""
import asyncio
from unittest.mock import AsyncMock

import pytest

from . import wait_action as action_module
from .test_lanshi_v4_integration import state
from ..public.game_record_manager import init_game_record, init_game_round
from ...game_calculation.test_lanshi_v4 import tiles


def prepared(monkeypatch):
    gs=state(); gs.claim_protection=False; gs.tactical_call=False
    gs.round_index=gs.current_round=1
    init_game_record(gs); init_game_round(gs)
    outgoing=AsyncMock()
    monkeypatch.setattr(action_module,'broadcast_do_action',outgoing)
    monkeypatch.setattr(action_module,'broadcast_ask_other_action',AsyncMock())
    monkeypatch.setattr(action_module,'finalize_claim_protection',AsyncMock())
    return gs,outgoing


async def submit(gs,seat,actions,data):
    gs.action_dict={i:actions if i==seat else [] for i in range(4)}
    gs.action_queues[seat].put_nowait(data)
    await asyncio.wait_for(action_module.wait_action(gs),2)


@pytest.mark.parametrize('action,matching,expected,status,remaining',[
    ('chi_left',[21,22],'s22','onlycut_after_action',11),
    ('chi_mid',[22,24],'s23','onlycut_after_action',11),
    ('chi_right',[24,25],'s24','onlycut_after_action',11),
    ('peng',[23,23],'k23','onlycut_after_action',11),
    ('gang',[23,23,23],'g23','deal_card_after_gang',10),
])
def test_claim_consumes_only_own_tiles_and_records_action(monkeypatch,action,matching,expected,status,remaining):
    gs,sent=prepared(monkeypatch)
    gs.current_player_index=0; gs.game_status='waiting_action_after_cut'
    gs.player_list[0].discard_tiles=[23]
    filler=tiles('123456m789s11z')
    gs.player_list[1].hand_tiles=matching+filler[:13-len(matching)]
    asyncio.run(submit(gs,1,[action,'pass'],{'action_type':action}))
    assert gs.current_player_index==1 and gs.game_status==status
    assert gs.player_list[1].combination_tiles==[expected]
    assert len(gs.player_list[1].hand_tiles)==remaining
    assert gs.player_list[0].discard_tiles==[]
    assert gs.game_record['game_round']['round_index_1']['action_ticks']
    assert sent.await_count==1
    assert sent.await_args.kwargs['combination_target']==expected


@pytest.mark.parametrize('draw_slot',[False,True])
def test_concealed_kong_retains_true_owner_mask_and_records_mo_flag(monkeypatch,draw_slot):
    gs,sent=prepared(monkeypatch); p=gs.player_list[0]
    gs.current_player_index=0;gs.game_status='waiting_hand_action'
    p.hand_tiles=tiles('12345m2222p345s11z')
    if draw_slot:p.hand_tiles.remove(22);p.hand_tiles.append(22)
    p.has_draw_slot=draw_slot;p.last_drawn_tile=22 if draw_slot else None
    asyncio.run(submit(gs,0,['angang','cut'],{'action_type':'angang','target_tile':22}))
    assert gs.game_status=='deal_card_after_gang'
    assert p.combination_tiles==['G22'] and p.combination_mask==[[2,22]*4]
    assert len(p.hand_tiles)==10 and 22 not in p.hand_tiles and not p.has_draw_slot
    tick=gs.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert tick[0]=='ag' and tick[1]==22
    assert sent.await_args.kwargs['combination_mask']==[2,22]*4
    assert sent.await_args.kwargs['is_mo_gang'] is draw_slot


def test_added_kong_replaces_pung_without_replacing_other_meld(monkeypatch):
    gs,sent=prepared(monkeypatch);p=gs.player_list[0]
    gs.game_status='waiting_hand_action';gs.current_player_index=0
    p.hand_tiles=tiles('12345m2p11z')
    p.combination_tiles=['s35','k22'];p.combination_mask=[[1,34,0,35,0,36],[0,22,1,22,0,22]]
    asyncio.run(submit(gs,0,['jiagang','cut'],{'action_type':'jiagang','target_tile':22}))
    assert p.combination_tiles==['s35','g22']
    assert len(p.hand_tiles)==7 and 22 not in p.hand_tiles
    assert p.combination_mask[0]==[1,34,0,35,0,36]
    assert len(p.combination_mask[1])==8 and 3 in p.combination_mask[1][::2]
    assert gs.game_status=='deal_card_after_gang'
    assert gs.game_record['game_round']['round_index_1']['action_ticks'][-1][:2]==['jg',22]


@pytest.mark.parametrize('phase',['waiting_action_after_cut','waiting_action_qianggang'])
@pytest.mark.parametrize('winners',[(1,2,3),(2,3),(3,)])
def test_nearest_winner_takes_priority_for_discard_or_rob(monkeypatch,phase,winners):
    gs,_=prepared(monkeypatch);gs.game_status=phase;gs.current_player_index=0
    gs.player_list[0].discard_tiles=[23];gs.jiagang_tile=23
    gs.action_dict={i:[] for i in range(4)}
    for seat in winners:
        action={1:'hu_first',2:'hu_second',3:'hu_third'}[seat]
        gs.action_dict[seat]=[action,'pass']
        gs.result_dict[action]=(5,['抢杠和'] if 'qianggang' in phase else ['海底捞月'])
        gs.action_queues[seat].put_nowait({'action_type':action})
    asyncio.run(asyncio.wait_for(action_module.wait_action(gs),2))
    assert gs.hu_class=={1:'hu_first',2:'hu_second',3:'hu_third'}[min(winners)]
    assert gs.game_status=='check_hepai'
