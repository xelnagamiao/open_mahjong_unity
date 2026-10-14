"""Regression: fan, base points and actual payments are different quantities."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest
from psycopg2.extras import Json

from test_subrule_statistics import db
from server.database.shared_record_metrics import analyze_shared_record_for_player
from server.database.statistics_catalog import RULES
from server.database.statistics_values import sichuan_win_fan
from server.database.scene_stats import record_game_metrics
from server.database.daily_aggregator import _write_scene_statistics
from scripts.backfill_statistics_values import backfill, MIGRATION_KEY, BACKUP_TABLE


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


def seed_metrics(db, bad=False):
    data = record(names=['unknown'] if bad else None)
    if bad:
        # Write a valid row first, then replace the saved record with damaged evidence.
        good = record()
    else:
        good = data
    db.sql("INSERT INTO game_records(game_id,record,created_at) VALUES('fanfix',%s,'2020-01-02 05:00+08')",(Json(data),))
    players = [SimpleNamespace(user_id=10000101+i,username=f'test-{i}',original_player_index=i,
        score=0,record_counter=SimpleNamespace(rank_result=i+1)) for i in range(4)]
    for p in players:
        db.sql("""INSERT INTO game_player_records(game_id,user_id,original_player_index)
            VALUES('fanfix',%s,%s)""",(p.user_id,p.original_player_index))
    record_game_metrics(db,'fanfix',good,players,dict(rule='sichuan',sub_rule='sichuan/standard',room_type='custom',match_type='1/4'))
    db.sql("""UPDATE game_player_metrics SET total_fan_score=98,total_fangchong_score=99,
        created_at='2020-01-02 05:00+08' WHERE game_id='fanfix'""")
    conn=db._get_connection()
    from datetime import date
    with conn.cursor() as cur:
        _write_scene_statistics(cur,date(2020,1,2))
    conn.commit()
    return data


def test_full_backfill_dry_run_atomic_apply_backups_daily_and_idempotence(db):
    seed_metrics(db)
    before=db.sql('SELECT to_jsonb(m) FROM game_player_metrics m ORDER BY user_id')
    original=db.sql('SELECT record FROM game_records')
    preview=backfill(db._get_connection(),batch_size=1)
    assert preview['changed']==4 and preview['status']=='preview'
    assert db.sql('SELECT to_jsonb(m) FROM game_player_metrics m ORDER BY user_id')==before
    assert db.sql('SELECT to_regclass(%s)',(BACKUP_TABLE,))==[(None,)]
    result=backfill(db._get_connection(),apply=True,batch_size=1)
    assert result['metrics_updated']==4 and result['daily_updated']==1
    assert db.sql('SELECT total_fan_score,total_fangchong_score FROM game_player_metrics ORDER BY user_id')==[(0,0),(4,0),(0,4),(0,0)]
    assert db.sql('SELECT total_fan_score,total_fangchong_score FROM scene_daily_stats')==[(4,4)]
    assert db.sql(f'SELECT COUNT(*) FROM {BACKUP_TABLE}')==[(5,)]
    assert db.sql('SELECT record FROM game_records')==original
    for old,new in zip(before,db.sql('SELECT to_jsonb(m) FROM game_player_metrics m ORDER BY user_id')):
        assert {k:v for k,v in old[0].items() if k not in ('total_fan_score','total_fangchong_score')}=={
            k:v for k,v in new[0].items() if k not in ('total_fan_score','total_fangchong_score')}
    assert backfill(db._get_connection(),apply=True)['status']=='already_complete'
    again=backfill(db._get_connection(),apply=True,force=True)
    assert again['changed']==again['metrics_updated']==again['daily_updated']==0
    assert db.sql(f'SELECT COUNT(*) FROM {BACKUP_TABLE}')==[(5,)]


def test_missing_fan_evidence_rolls_back_without_marking_complete(db):
    seed_metrics(db,bad=True)
    result=backfill(db._get_connection(),apply=True)
    assert result['status']=='blocked' and result['error_count']==4
    assert db.sql('SELECT DISTINCT total_fan_score FROM game_player_metrics')==[(98,)]
    assert db.sql('SELECT meta_value FROM app_meta WHERE meta_key=%s',(MIGRATION_KEY,))==[]
    assert db.sql('SELECT to_regclass(%s)',(BACKUP_TABLE,))==[(None,)]


def test_late_aggregate_failure_rolls_back_metrics_and_backups(db):
    seed_metrics(db)
    from scripts import backfill_statistics_values as migration
    original=migration._refresh_values
    def fail(cursor,table,*args,**kwargs):
        if table=='scene_daily_stats':
            raise RuntimeError('injected aggregate failure')
        return original(cursor,table,*args,**kwargs)
    with patch.object(migration,'_refresh_values',side_effect=fail), pytest.raises(RuntimeError,match='injected'):
        backfill(db._get_connection(),apply=True)
    assert db.sql('SELECT DISTINCT total_fan_score FROM game_player_metrics')==[(98,)]
    assert db.sql('SELECT meta_value FROM app_meta WHERE meta_key=%s',(MIGRATION_KEY,))==[]
    assert db.sql('SELECT to_regclass(%s)',(BACKUP_TABLE,))==[(None,)]


def test_backfill_refreshes_legacy_snapshot_without_overwriting_other_fields(db):
    data = record('qingque/standard',['门前清'],3.60616)
    rd = data['game_round']['round_index_1']
    rd['action_ticks'][2][0]='hu_third'
    rd['action_ticks'][2][4]=[-39,0,0,39]
    db.sql("INSERT INTO game_records(game_id,record) VALUES('qingque',%s)",(Json(data),))
    players=[SimpleNamespace(user_id=10000101+i,username='test',original_player_index=i,
        score=0,record_counter=SimpleNamespace(rank_result=i+1)) for i in range(4)]
    for p in players:
        db.sql("INSERT INTO game_player_records(game_id,user_id,original_player_index) VALUES('qingque',%s,%s)",
               (p.user_id,p.original_player_index))
    record_game_metrics(db,'qingque',data,players,dict(rule='qingque',sub_rule='qingque/standard',room_type='custom',match_type='1/4'))
    db.sql("""CREATE TABLE qingque_history_stats(user_id bigint,rule text,mode text,total_fan_score int,
        total_fangchong_score int,total_games int);
        INSERT INTO qingque_history_stats VALUES(10000102,'qingque/standard','1/4',999,999,123)""")
    result=backfill(db._get_connection(),apply=True)
    assert result['legacy_updated']==1
    assert db.sql('SELECT total_fan_score,total_fangchong_score,total_games FROM qingque_history_stats')==[(4,0,123)]
    assert db.sql(f'SELECT row_data->>\'total_fan_score\' FROM {BACKUP_TABLE} WHERE table_name=\'qingque_history_stats\'')==[('999',)]


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


def test_daily_backup_retains_distinct_rows_even_with_nullable_duplicate_dimensions(db):
    seed_metrics(db)
    db.sql("""INSERT INTO scene_daily_stats(stat_date,room_type,rule,sub_rule,game_type,
        total_fan_score,total_fangchong_score) SELECT stat_date,room_type,rule,sub_rule,game_type,777,888
        FROM scene_daily_stats""")
    result=backfill(db._get_connection(),apply=True)
    assert result['daily_updated']==2
    assert db.sql(f"SELECT COUNT(*) FROM {BACKUP_TABLE} WHERE table_name='scene_daily_stats'")==[(2,)]
    assert db.sql('SELECT DISTINCT total_fan_score,total_fangchong_score FROM scene_daily_stats')==[(4,4)]
