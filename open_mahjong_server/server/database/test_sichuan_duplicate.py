"""Persistence failure boundaries complement the real local DB/HTTP checks."""
import json
from unittest.mock import MagicMock

import pytest

from .sichuan_duplicate import PairedMatchStore
from ..gamestate.public.sichuan_duplicate import new_match
from ..game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, make_board


def fixture(monkeypatch):
    db=MagicMock();conn=db._get_connection.return_value;cursor=conn.cursor.return_value.__enter__.return_value
    monkeypatch.setattr('server.database.sichuan_duplicate.ensure_tables',lambda c:None)
    data=new_match('00000000-0000-4000-8000-000000000001',SBR_DUPLICATE,{'open':'1','closed':'2'},[make_board('seed',1)],'seed')
    return db,conn,cursor,data,PairedMatchStore(db)


@pytest.mark.parametrize('today,stored,ok',[(24,99,True),(25,1,False),(1,100,False),(25,100,False)])
def test_create_quota_transaction_and_connection_release(monkeypatch,today,stored,ok):
    db,conn,cursor,data,store=fixture(monkeypatch)
    cursor.fetchone.side_effect=[(today,stored),(7,)]
    if ok:
        store.create(data,event_id='evt_test',owner_id=101,name='x'*100)
        assert data['wall_id']==7 and data['key'].startswith('dup_')
        conn.commit.assert_called_once();conn.rollback.assert_not_called()
        insert=next(c for c in cursor.execute.call_args_list if 'INSERT INTO duplicate_walls' in c.args[0])
        assert len(insert.args[1][4])==80
        assert len(json.loads(insert.args[1][-1])[0])==108
    else:
        with pytest.raises(ValueError,match='限额'):store.create(data,event_id='evt_test',owner_id=101,name='test')
        conn.rollback.assert_called_once();conn.commit.assert_not_called()
    db._put_connection.assert_called_once_with(conn)


@pytest.mark.parametrize('rowcount',[0,1])
def test_save_compare_and_swap_never_overwrites_stale_revision(monkeypatch,rowcount):
    db,conn,cursor,data,store=fixture(monkeypatch);data['wall_id']=7;data['revision']=4;cursor.rowcount=rowcount
    if rowcount:
        store.save(data,3);conn.commit.assert_called_once()
        assert cursor.execute.call_args_list[0].args[1][-1]==3
    else:
        with pytest.raises(ValueError,match='版本冲突'):store.save(data,3)
        assert cursor.execute.call_count==1
        conn.rollback.assert_called_once();conn.commit.assert_not_called()
    db._put_connection.assert_called_once_with(conn)


@pytest.mark.parametrize('failure',[False,True])
def test_restore_read_transaction_and_json_shapes(monkeypatch,failure):
    db,conn,cursor,data,store=fixture(monkeypatch)
    if failure:
        cursor.execute.side_effect=OSError('offline')
        with pytest.raises(OSError):store.unfinished()
        conn.rollback.assert_called_once()
    else:
        cursor.fetchall.return_value=[(data,),(json.dumps(data),)]
        assert store.unfinished()==[data,data];conn.commit.assert_called_once()
    db._put_connection.assert_called_once_with(conn)
