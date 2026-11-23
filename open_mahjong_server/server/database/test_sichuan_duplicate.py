"""Persistence failure boundaries complement the real local DB/HTTP checks."""
import json
from unittest.mock import MagicMock

import pytest

from .sichuan_duplicate import attach_paired_wall
from ..gamestate.public.sichuan_duplicate import new_match
from ..game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, make_board


def fixture(monkeypatch):
    db=MagicMock();conn=db._get_connection.return_value;cursor=conn.cursor.return_value.__enter__.return_value
    monkeypatch.setattr('server.database.sichuan_duplicate.ensure_tables',lambda c:None)
    data=new_match('00000000-0000-4000-8000-000000000001',SBR_DUPLICATE,{'open':'1','closed':'2'},[make_board('seed',1)],'seed')
    return db, conn, cursor, data


@pytest.mark.parametrize('today,stored,ok',[(24,99,True),(25,1,False),(1,100,False),(25,100,False)])
def test_create_quota_transaction_and_connection_release(monkeypatch,today,stored,ok):
    db,conn,cursor,data=fixture(monkeypatch)
    cursor.fetchone.side_effect=[(today,stored),(7,)]
    if ok:
        attach_paired_wall(db, data,event_id='evt_test',owner_id=101,name='x'*100)
        assert data['wall_id']==7 and data['key'].startswith('dup_')
        conn.commit.assert_called_once();conn.rollback.assert_not_called()
        insert=next(c for c in cursor.execute.call_args_list if 'INSERT INTO duplicate_walls' in c.args[0])
        assert len(insert.args[1][5])==80
        assert len(json.loads(insert.args[1][-1])[0])==108
        assert set(data['table_record_ids']) == {'open', 'closed'}
        assert len(set(data['table_record_ids'].values())) == 2
        assert all('duplicate_games' not in c.args[0] for c in cursor.execute.call_args_list)
        assert all('sichuan_duplicate_matches' not in c.args[0] for c in cursor.execute.call_args_list)
    else:
        with pytest.raises(ValueError,match='限额'):attach_paired_wall(db, data,event_id='evt_test',owner_id=101,name='test')
        conn.rollback.assert_called_once();conn.commit.assert_not_called()
    db._put_connection.assert_called_once_with(conn)


@pytest.mark.parametrize('existing', [True, False])
def test_attach_saved_wall_reuses_source_without_using_another_quota(monkeypatch,existing):
    db,conn,cursor,data=fixture(monkeypatch)
    data.update(source_wall_id=55,key='dup_saved',wall_type='manual')
    cursor.fetchone.side_effect=[(25,100), (55,) if existing else None]
    if existing:
        attach_paired_wall(db, data,event_id='evt_test',owner_id=101,name='manual')
        assert data['wall_id']==55 and data['key']=='dup_saved'
        conn.commit.assert_called_once()
        assert not any('INSERT INTO duplicate_walls' in c.args[0] for c in cursor.execute.call_args_list)
        assert not any('SELECT id FROM sichuan_duplicate_matches' in c.args[0] for c in cursor.execute.call_args_list)
    else:
        with pytest.raises(ValueError): attach_paired_wall(db, data,event_id='evt_test',owner_id=101,name='manual')
        conn.rollback.assert_called_once()
        conn.commit.assert_not_called()
    db._put_connection.assert_called_once_with(conn)


@pytest.mark.parametrize('today,stored,ok', [(4,24,True),(5,0,False),(0,25,False)])
def test_personal_pair_uses_existing_personal_quota_and_shared_lock(monkeypatch,today,stored,ok):
    db,conn,cursor,data=fixture(monkeypatch)
    cursor.fetchone.side_effect=[(today,stored),(7,)]
    if ok:
        attach_paired_wall(db, data,event_id=None,owner_id=101,name='personal')
        assert data['scope']=='personal' and data['event_id'] is None
        conn.commit.assert_called_once()
        lock=cursor.execute.call_args_list[0]
        assert lock.args[1]==('duplicate:personal:101',)
        insert=next(c for c in cursor.execute.call_args_list if 'INSERT INTO duplicate_walls' in c.args[0])
        assert insert.args[1][1:4]==(101,None,'personal')
    else:
        with pytest.raises(ValueError,match='限额'):
            attach_paired_wall(db, data,event_id=None,owner_id=101,name='personal')
        conn.rollback.assert_called_once()
    db._put_connection.assert_called_once_with(conn)
