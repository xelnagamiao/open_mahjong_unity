import asyncio
import json
from unittest.mock import AsyncMock, patch

import pytest

from .test_state import make_state, hand
from . import lifecycle
from ...database.shared_record_metrics import analyze_shared_record_for_player
from ..public.game_record_manager import init_game_record, init_game_round


def settled_record(rotation, win):
    s=make_state()
    for p in s.player_list:p.original_player_index=(p.player_index+rotation)%4
    before={p.original_player_index:p.score for p in s.player_list}
    hand(s,0,[41,42,43,44,11,12,13,21,22,23,31,32,33,19],drawn=19)
    s.tiles_list=[19]+[18]*10+[21]
    init_game_record(s);init_game_round(s)
    async def run():
        assert await s.execute_special(0,'Ywind')
        await s._deal_supplement()
        assert s.egg_offer==21
        await s.resolve_egg(True)
        await s.execute_cut(0,{'TileId':21,'is_mo':True})
        if win:
            s.current_player_index=3
            await s._deal_normal()
            assert 'hu_self' in s.action_dict[0]
            s.accept_self_draw(0)
        else:
            s.draw_reason='wall_exhausted'
        s.current_round=s.max_round*4
        with patch.object(lifecycle.asyncio,'sleep',AsyncMock()):
            await s._settle_hand(before)
    asyncio.run(run())
    return s, before


@pytest.mark.parametrize('rotation',range(4))
@pytest.mark.parametrize('win',[True,False])
def test_egg_settlement_metrics_rotation_and_no_double_count(rotation,win):
    s,before=settled_record(rotation,win)
    record=json.loads(json.dumps(s.game_record,default=str))
    for p in s.player_list:
        metrics=analyze_shared_record_for_player(record,p.original_player_index)
        assert metrics['total_round_score']==p.score-before[p.original_player_index]
        assert metrics['win_count']==metrics['self_draw_count']==int(win and p.player_index==0)
        assert metrics['deal_in_count']==0
        assert metrics['fulu_round_count']==p.record_counter.fulu_times==0
        assert metrics['total_fan_score']==p.record_counter.win_score
        assert metrics['total_win_turn']==p.record_counter.win_turn
        assert metrics['total_rounds']==1
    assert sum(p.score for p in s.player_list)==sum(before.values())


@pytest.mark.parametrize('rotation',range(4))
def test_pung_then_ron_metrics(rotation):
    s=make_state()
    for p in s.player_list:p.original_player_index=(p.player_index+rotation)%4
    hand(s,1,[11,12,13,21,22,23,31,32,33,19,45,45,19])
    s.player_list[0].discard_tiles=[45]
    s.player_list[0].last_discarded_tile=45
    init_game_record(s);init_game_round(s)
    async def run():
        await s.execute_claim(1,'peng')
        await s.execute_cut(1,{'TileId':19})
        s.current_player_index=0;s.player_list[0].discard_tiles.append(19)
        p=s.player_list[1];p.passed_fan=-1
        allowed=s.check_discard_actions(19)
        assert 'hu_first' in allowed[1]
        await s.resolve_discard_responses({1:{'action_type':'hu_first'}},allowed)
        s.current_round=s.max_round*4
        await s._settle_hand(dict.fromkeys(range(4),0))
    asyncio.run(run())
    for p in s.player_list:
        metrics=analyze_shared_record_for_player(s.game_record,p.original_player_index)
        assert metrics['total_round_score']==p.score
        assert metrics['win_count']==int(p.player_index==1)
        assert metrics['deal_in_count']==int(p.player_index==0)
        assert metrics['fulu_round_count']==p.record_counter.fulu_times==int(p.player_index==1)
        assert metrics['total_fangchong_score']==p.record_counter.fangchong_score
        assert metrics['total_win_turn']==p.record_counter.win_turn
