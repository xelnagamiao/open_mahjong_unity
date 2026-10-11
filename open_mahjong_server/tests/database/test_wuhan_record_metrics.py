import asyncio
from unittest.mock import AsyncMock,patch

import pytest

from test_subrule_statistics import db
from test_subrule_realtime import prepare
from server.gamestate.game_wuhan.test_flow import make_state,indicator,hand,draw,PURE
from server.gamestate.public.game_record_manager import init_game_record,init_game_round
from server.database.taiwan.store_taiwan import store_taiwan_game_record
from server.database.get_rule_stats import get_custom_rule_history


@pytest.mark.parametrize('rotation',range(4))
@pytest.mark.parametrize('win',[True,False])
def test_real_wuhan_bonus_record_roundtrip_and_rotation(db,rotation,win):
    prepare(db)
    s=make_state();indicator(s);s.current_round=4
    for i,p in enumerate(s.player_list):
        p.original_player_index=(i+rotation)%4
        p.user_id=10000101+p.original_player_index
    p=hand(s,0,PURE,['s12']);p.huapai_list=[37,37,37,38,38,38,45]
    for q in s.player_list[1:]:q.huapai_list=[45]
    init_game_record(s);init_game_round(s)
    async def run():
        await s._check_bonus(0)
        if win:
            draw(p,15);s.result_dict['hu_self']=s.score_candidate(0,'self_draw');s.accept_self_draw(0)
        with patch('server.gamestate.game_wuhan.WuhanGameState.asyncio.sleep',new=AsyncMock()):
            await s._settle_hand(dict.fromkeys(range(4),0))
    asyncio.run(run())
    gid=store_taiwan_game_record(db,s.game_record,s.player_list,'custom','1/4')
    assert gid
    for p in s.player_list:
        rows=db.sql('SELECT total_round_score,win_count,self_draw_count,deal_in_count,fan_stats '
                    'FROM game_player_metrics WHERE game_id=%s AND user_id=%s',(gid,p.user_id))
        winner=int(win and p.player_index==0)
        assert rows==[(p.score,winner,winner,0,None)]
        stats=get_custom_rule_history(db,p.user_id,'wuhan')
        assert len(stats)==1 and stats[0]['sub_rule']=='wuhan/mil2025'
        assert stats[0]['total_round_score']==p.score and 'fan_stats' not in stats[0]
