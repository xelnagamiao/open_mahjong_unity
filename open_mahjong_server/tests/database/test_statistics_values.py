"""Regression: fan, base points and actual payments are different quantities."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from server.database.shared_record_metrics import analyze_shared_record_for_player
from server.database.statistics_catalog import RULES
from server.database.statistics_values import sichuan_win_fan


def record(sub="sichuan/standard", names=None, base=8):
    return {"game_title": {"rule": sub.split('/')[0], "sub_rule": sub,
                           **{f"p{i}_uid": 10000101+i for i in range(4)}},
        "game_round": {"round_index_1": {"seats": [2, 3, 0, 1], "action_ticks": [
            ["reset", 0], ["c", 11],
            ["hu_first", 3, base, ["清一色", "七对"] if names is None else names,
             [-8, 0, 0, 8], 11, 0, 0, 1], ["end"]]}}}


@pytest.mark.parametrize("sub", [r['sub_rule'] for r in RULES if r['rule']=='sichuan' and 'xueliu' not in r['sub_rule']])
@pytest.mark.parametrize("names,base,expected", [(["平和"],1,0),(["大对子"],2,1),
    (["清一色"],4,2),(["清一色","七对"],8,4),(["清一色","杠*2","根×2"],8,6)])
def test_blood_battle_fan_is_uncapped_and_independent_of_seat_and_payment(sub,names,base,expected):
    data = record(sub,names,base)
    winner = analyze_shared_record_for_player(data,1)
    payer = analyze_shared_record_for_player(data,2)
    assert winner['win_count'] == payer['deal_in_count'] == 1
    assert winner['total_fan_score'] == payer['total_fangchong_score'] == expected
    assert winner['total_round_score'] == 8


@pytest.mark.parametrize("sub", [r['sub_rule'] for r in RULES if r['rule']=='sichuan' and 'xueliu' in r['sub_rule']])
def test_blood_flow_keeps_historical_linear_values(sub):
    data = record(sub,["清碰","暗杠"],14)
    assert analyze_shared_record_for_player(data,1)['total_fan_score'] == 14


def test_saved_fan_does_not_change_positional_ticks_or_guess_new_fans():
    from server.gamestate.public.game_record_manager import player_action_record_hu
    data = record(names=['future fan'])
    rd = data['game_round']['round_index_1']
    with pytest.raises(ValueError,match='Unknown historical'):
        sichuan_win_fan(data['game_title'],rd,rd['action_ticks'][2],2)
    state = SimpleNamespace(game_record=data,round_index=1,player_action_tick=4)
    player_action_record_hu(state,'hu_self',8,['future fan'],0,[27,-9,-9,-9],hepai_tile=11,statistics_fan=7)
    assert rd['action_ticks'][-1] == ['hu_self',0,8,['future fan'],[27,-9,-9,-9],11]
    assert rd['win_fan_by_tick'] == {'4':7}
    assert sichuan_win_fan(data['game_title'],rd,rd['action_ticks'][-1],4) == 7


@pytest.mark.parametrize('mode', ['standard','xueliu','xueliu_exchange'])
def test_real_settlement_records_fan_before_payment_conversion(mode):
    from server.gamestate.game_sichuan.test_action_window import make_state
    from server.gamestate.public.lifecycle import cancel_auxiliary_tasks
    async def run():
        state,_ = make_state(mode, room_id='920014')
        state.pending_win = {'type':'zimo'}
        state.sichuan_hu_results = {0:{'fan':4,'fan_list':['清一色','七对'],'hepai_tile':11}}
        # Keep the state in an ongoing hand and suppress only presentation delays.
        with patch('asyncio.sleep',new=AsyncMock()):
            await state._settle_win()
        rd = state.game_record['game_round'][f'round_index_{state.round_index}']
        at = next(i for i,t in enumerate(rd['action_ticks']) if t[0]=='hu_self')
        assert rd['win_fan_by_tick'][str(at)] == 4
        assert rd['action_ticks'][at][2] == (8 if mode=='standard' else 4)
        assert state.player_list[0].record_counter.win_score == 4
        await cancel_auxiliary_tasks(state)
    asyncio.run(run())


def test_shenyang_loss_counts_payers_payment_not_winners_total_income():
    data=record('shenyang/mil2025',['平和'],2)
    tick=data['game_round']['round_index_1']['action_ticks'][2]
    tick[0]='hu_third'
    tick[4]=[-8,-2,-2,12]
    assert analyze_shared_record_for_player(data,1)['total_fan_score']==12
    assert analyze_shared_record_for_player(data,2)['total_fangchong_score']==8


def test_multi_ron_uses_each_winners_fan_and_the_explicit_discarder():
    data=record()
    rd=data['game_round']['round_index_1']
    rd['action_ticks'][2][4]=[0,0,0,0]
    rd['action_ticks'].insert(3,['hu_second',1,2,['大对子'],[0,0,0,0],11,1,0,1])
    data['game_title']['blood_battle']=True
    payer=analyze_shared_record_for_player(data,2)
    assert payer['deal_in_count']==2 and payer['total_fangchong_score']==5
    assert analyze_shared_record_for_player(data,1)['total_fan_score']==4
    assert analyze_shared_record_for_player(data,3)['total_fan_score']==1
