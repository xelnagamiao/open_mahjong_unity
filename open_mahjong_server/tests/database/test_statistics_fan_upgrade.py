"""首次升级的全范围、事务、并发和重复启动行为；仅使用独立测试 schema。"""
import ast
import asyncio
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import threading
from types import SimpleNamespace
from unittest.mock import Mock

import psycopg2
from psycopg2.extras import Json
import pytest

from test_subrule_statistics import db
from test_subrule_realtime import replay, players, writer, prepare
from test_statistics_fan_catalog import seed_historical_fans
from server.database import statistics_fan_backfill as upgrade
from server.database.daily_aggregator import current_stat_date
from server.database.statistics_catalog import RULES
from server.database.subrule_fans import collect_subrule_fans


def marker(db):
    rows = db.sql('SELECT meta_value,updated_at FROM app_meta WHERE meta_key=%s', (upgrade.MIGRATION_KEY,))
    return rows[0] if rows else None


def test_first_startup_repairs_every_enabled_subrule_and_skips_future_scans(db, monkeypatch):
    prepare(db)
    expected = []
    for definition in RULES:
        if not definition['basic_stats']:
            continue
        record = replay(definition)
        mode = definition['custom_modes'][0]
        gid = writer(definition)(db, record, players(definition), 'custom', mode)
        assert gid
        if definition['fan_stats']:
            expected.extend((gid, 10000101 + i, collect_subrule_fans(record, i, definition['sub_rule']))
                            for i in range(definition['player_count']))
    db.sql('UPDATE game_player_metrics SET fan_stats=NULL')
    # 本机旧版曾单独处理川麻；其 v1 标记不能让生产全量升级被跳过。
    db.sql('INSERT INTO app_meta(meta_key,meta_value) VALUES(%s,%s)',
           ('statistics_fans_catalog_v1', Json({'status': 'complete', 'by_sub_rule': {'sichuan/standard': 4}})))
    before = db.sql("SELECT to_jsonb(m)-'fan_stats' FROM game_player_metrics m ORDER BY game_id,user_id")
    replays = db.sql('SELECT * FROM game_records ORDER BY game_id')
    participants = db.sql('SELECT * FROM game_player_records ORDER BY game_id,user_id')
    report = upgrade.run_startup_fan_upgrade(db)
    assert report['status'] == 'complete' and report['changed'] == len(expected)
    assert set(report['by_sub_rule']) == {r['sub_rule'] for r in RULES if r['fan_stats']}
    actual = db.sql('SELECT game_id,user_id,fan_stats FROM game_player_metrics WHERE fan_stats IS NOT NULL ORDER BY game_id,user_id')
    assert sorted(actual) == sorted(expected)
    assert db.sql("SELECT to_jsonb(m)-'fan_stats' FROM game_player_metrics m ORDER BY game_id,user_id") == before
    assert db.sql('SELECT * FROM game_records ORDER BY game_id') == replays
    assert db.sql('SELECT * FROM game_player_records ORDER BY game_id,user_id') == participants
    first_marker = marker(db)
    assert json.loads(first_marker[0])['scope'] == 'all'
    collector = Mock(side_effect=AssertionError('completed startup must not scan records'))
    monkeypatch.setattr(upgrade, 'collect_subrule_fans', collector)
    second = upgrade.run_startup_fan_upgrade(db)
    assert second['status'] == 'already_complete' and second['scanned'] == 0
    collector.assert_not_called()
    assert marker(db) == first_marker


def test_subset_apply_does_not_mark_other_rules_complete(db):
    seed_historical_fans(db, 'sichuan/standard')
    seed_historical_fans(db, 'hongque/v1.6')
    subset = upgrade.backfill(db._get_connection(), apply=True, sub_rules=['sichuan/standard'])
    assert subset['scope'] == 'subset' and subset['changed'] == 4
    assert marker(db) is None
    assert db.sql("SELECT COUNT(fan_stats) FROM game_player_metrics WHERE sub_rule='hongque/v1.6'") == [(0,)]
    full = upgrade.run_startup_fan_upgrade(db)
    assert full['changed'] == 4 and full['by_sub_rule'] == {'hongque/v1.6': 4}
    assert marker(db)


def test_first_upgrade_repairs_missing_and_stale_daily_fans_without_touching_open_day(db):
    seed_historical_fans(db)
    # 每玩家计数已经正确，旧日统计仍缺失或错误时也需要刷新。
    upgrade.backfill(db._get_connection(), apply=True, sub_rules=['sichuan/standard'])
    db.sql("UPDATE scene_tier_fan_daily SET fan_count=99")
    db.sql("INSERT INTO scene_tier_fan_daily VALUES('2019-01-01','elo','sichuan','残余番种',8,'sichuan/standard')")
    db.sql("INSERT INTO scene_tier_fan_daily VALUES(%s,'elo','sichuan','未结束统计日',7,'sichuan/standard')", (current_stat_date(),))
    db.sql("DELETE FROM app_meta WHERE meta_key='stats_catchup_through'")
    report = upgrade.run_startup_fan_upgrade(db)
    assert report['changed'] == 0 and report['daily_groups'] == 2
    assert db.sql("SELECT fan_count FROM scene_tier_fan_daily WHERE fan_field='清一色'") == [(1,)]
    assert db.sql("SELECT COUNT(*) FROM scene_tier_fan_daily WHERE fan_field='残余番种'") == [(0,)]
    assert db.sql("SELECT fan_count FROM scene_tier_fan_daily WHERE fan_field='未结束统计日'") == [(7,)]


def test_failed_startup_rolls_back_and_can_retry_after_record_is_repaired(db):
    gid, record = seed_historical_fans(db)
    db.sql('UPDATE game_records SET record=NULL WHERE game_id=%s', (gid,))
    with pytest.raises(RuntimeError, match='历史番种升级失败'):
        upgrade.run_startup_fan_upgrade(db)
    assert marker(db) is None
    assert db.sql('SELECT COUNT(fan_stats) FROM game_player_metrics') == [(0,)]
    assert db.sql('SELECT to_regclass(%s)', (upgrade.BACKUP_TABLE,)) == [(None,)]
    db.sql('UPDATE game_records SET record=%s WHERE game_id=%s', (Json(record), gid))
    assert upgrade.run_startup_fan_upgrade(db)['changed'] == 4
    assert marker(db)


def test_daily_write_failure_rolls_back_metrics_backups_and_completion(db):
    seed_historical_fans(db)
    db.sql('ALTER TABLE scene_tier_fan_daily ADD CONSTRAINT fail_daily CHECK(fan_count<0)')
    with pytest.raises(psycopg2.errors.CheckViolation):
        upgrade.run_startup_fan_upgrade(db)
    assert marker(db) is None
    assert db.sql('SELECT COUNT(fan_stats) FROM game_player_metrics') == [(0,)]
    assert db.sql('SELECT to_regclass(%s)', (upgrade.BACKUP_TABLE,)) == [(None,)]


def test_force_rerun_preserves_original_backup(db):
    gid, _ = seed_historical_fans(db)
    upgrade.run_startup_fan_upgrade(db)
    before = db.sql(f'SELECT * FROM {upgrade.BACKUP_TABLE} ORDER BY table_name,row_key')
    db.sql("UPDATE game_player_metrics SET fan_stats='{}' WHERE game_id=%s", (gid,))
    assert upgrade.backfill(db._get_connection(), apply=True, force=True)['changed'] == 1
    assert db.sql(f'SELECT * FROM {upgrade.BACKUP_TABLE} ORDER BY table_name,row_key') == before


def test_two_startups_wait_for_one_transaction_and_second_skips(db, monkeypatch):
    seed_historical_fans(db)
    conn = db._get_connection()
    schema = db.sql('SELECT current_schema()')[0][0]
    other = psycopg2.connect(os.environ['SUBRULE_TEST_DATABASE_URL'], options='-c search_path=' + schema)
    entered, release, waiting = threading.Event(), threading.Event(), threading.Event()
    original = upgrade._is_complete
    second_checks = 0

    def checked(cursor, selected):
        nonlocal second_checks
        result = original(cursor, selected)
        if cursor.connection is other:
            second_checks += 1
            waiting.set()
        return result

    def progress(_):
        entered.set()
        assert release.wait(10), 'test migration release timed out'

    monkeypatch.setattr(upgrade, '_is_complete', checked)
    try:
        with ThreadPoolExecutor(max_workers=2) as executor:
            first = executor.submit(upgrade.backfill, conn, apply=True, progress=progress)
            assert entered.wait(10)
            second = executor.submit(upgrade.backfill, other, apply=True)
            assert waiting.wait(10)
            release.set()
            assert first.result(timeout=15)['status'] == 'complete'
            assert second.result(timeout=15)['status'] == 'already_complete'
            assert second_checks == 2
    finally:
        release.set()
        other.close()


def test_empty_database_marks_complete_and_startup_failure_never_yields_ready(db, monkeypatch):
    assert upgrade.run_startup_fan_upgrade(db)['scanned'] == 0
    assert marker(db)
    # 只编译实际 lifespan，避免导入 server.py 时启动真实聊天/游戏管理器。
    path = Path(__file__).resolve().parents[2] / 'server/server.py'
    function = next(node for node in ast.parse(path.read_text(encoding='utf-8')).body
                    if isinstance(node, ast.AsyncFunctionDef) and node.name == 'lifespan')
    function.decorator_list = []
    module = ast.fix_missing_locations(ast.Module(body=[function], type_ignores=[]))
    fake_db = SimpleNamespace(init_database=Mock())
    from server.match import qingque_session
    checkpoint_recovery = Mock()
    monkeypatch.setattr(qingque_session, 'recover_checkpoints', checkpoint_recovery)
    env = dict(asyncio=asyncio, FastAPI=object, db_manager=fake_db,
               __name__='server.server', __package__='server')
    exec(compile(module, str(path), 'exec'), env)
    def fail(manager):
        assert manager is fake_db and fake_db.init_database.called
        checkpoint_recovery.assert_called_once_with(fake_db, True)
        raise RuntimeError('upgrade failed before accepting requests')
    monkeypatch.setattr(upgrade, 'run_startup_fan_upgrade', fail)
    async def start():
        async with __import__('contextlib').asynccontextmanager(env['lifespan'])(None):
            pytest.fail('failed upgrade must not yield a ready application')
    with pytest.raises(RuntimeError, match='before accepting requests'):
        asyncio.run(start())
