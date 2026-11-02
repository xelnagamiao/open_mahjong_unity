"""Haidi pong must produce another claim window, while every kong stays forbidden."""
import asyncio

import pytest

from .test_doubles import make_state, WIN
from .test_duplicate import run_async
from .test_action_window import reply_on_ask, submit
from .action_check import check_action_after_cut, check_action_after_peng, check_action_hand_action, refresh_waiting_tiles
from .wait_action import wait_action
from .duplicate_wall import SichuanDuplicateWall
from ...game_calculation.sichuan.duplicate_rules import make_board
from ..public.outbound_pipe import drain_viewer
from ..public.lifecycle import cancel_auxiliary_tasks


@pytest.mark.parametrize('mode',['standard','doubles','duplicate'])
@run_async
async def test_haidi_peng_cut_then_hu_and_no_kong(mode):
    s,_=make_state()
    s.claim_protection=False;s.tactical_call=False;s.current_player_index=0
    if mode=='standard':
        s.is_doubles=False;s.is_team_game=False;s.sub_rule='sichuan/standard'
    if mode=='duplicate':
        wall=SichuanDuplicateWall(s,make_board('haidi-regression',1))
        wall.parts=[[],[],[],[]];wall.clear();s.tiles_list=wall
    else:
        s.tiles_list=[]
    for p in s.player_list:
        p.hand_tiles=list(WIN[:-1]);p.waiting_tiles=set();p.dingque_suit=3
    s.player_list[1].hand_tiles=[11,11,11,12,13,14,21,22,23,25,26,27,29]
    for p in s.player_list:refresh_waiting_tiles(s,p.player_index)
    s.player_list[0].discard_tiles=[11]
    s.game_status='waiting_action_after_cut'
    s.action_dict=check_action_after_cut(s,11)
    assert 'peng' in s.action_dict[1] and 'gang' not in s.action_dict[1]
    for i,available in s.action_dict.items():
        if available:reply_on_ask(s,i,'peng' if i==1 else 'pass')
    try:
        await s.broadcast_ask_other_action()
        for i in range(4):await drain_viewer(s,i)
        await asyncio.wait_for(wait_action(s),2)
        assert s.game_status=='onlycut_after_action' and s.current_player_index==1
        s.action_dict=check_action_after_peng(s,1);s.game_status='waiting_hand_action'
        await s.broadcast_ask_hand_action();await drain_viewer(s,1)
        await submit(s,1,'cut',TileId=25,cutClass=False)
        await asyncio.wait_for(wait_action(s),2)
        assert s.game_status=='waiting_action_after_cut' and 'hu' in s.action_dict[2]
        for p in s.player_list:p.waiting_tiles=set()
        s.player_list[1].hand_tiles=[11]*4+[12,13,14,21,22,23,25]
        s.player_list[1].combination_tiles=['k25']
        assert not set(check_action_hand_action(s,1)[1]) & {'angang','jiagang'}
    finally:
        await cancel_auxiliary_tasks(s)
