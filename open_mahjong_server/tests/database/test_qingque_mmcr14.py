"""MMCR 14 目录与旧统计修复；数据库测试仅使用独立 schema。"""
import json
from pathlib import Path
import re

from psycopg2.extras import Json

from test_subrule_statistics import db
from test_subrule_realtime import replay, players, writer, prepare
from server.database.statistics_catalog import BY_SUB_RULE
from server.database.statistics_fans import fan_names
from server.database.subrule_fans import collect_subrule_fans
from server.database.statistics_fan_backfill import BACKUP_TABLE, MIGRATION_KEY as FULL_KEY
from server.database.get_rule_stats import get_rule_fans
from scripts.recalculate_qingque_fans import recalculate, MIGRATION_KEY

ROOT = Path(__file__).resolve().parents[3]


def test_catalog_matches_mmcr14_calculator_and_both_clients():
    names = fan_names('qingque/standard')
    calculator = ROOT / 'open_mahjong_unity/Assets/Scripts/GameScene/Calculation/CalculationScript/Qingque13/Qingque13Calc/QingqueFan.cs'
    expected = re.findall(r'\{ QingqueFan\.\w+, "([^"]+)" \}', calculator.read_text(encoding='utf-8'))
    assert len(expected) == 90
    assert list(names.values()) == expected
    assert names['shisanyao'] == '十三幺'
    assert not {'fengpaiwudui', 'fengpaisidui'} & names.keys()
    web = json.loads((ROOT / 'open_mahjong_web/shared/statisticsFans.json').read_text(encoding='utf-8'))
    assert web['qingque/standard'] == names
    unity = (ROOT / 'open_mahjong_unity/Assets/Scripts/UI/PlayerInfo/PlayerInfoRuleCatalog.cs').read_text(encoding='utf-8')
    entry = next(line for line in unity.splitlines() if '{"qingque/standard", new Dictionary' in line)
    assert dict(re.findall(r'\{"([^"]+)", "([^"]+)"\}', entry)) == names


def test_thirteen_orphans_is_counted_for_original_player_after_seat_rotation():
    record = replay(BY_SUB_RULE['qingque/standard'])
    record['game_round']['round_index_1']['action_ticks'][1][3] = ['十三幺', '门前清']
    assert collect_subrule_fans(record, 0, 'qingque/standard') == {'shisanyao': 1, 'menqianqing': 1}
    assert collect_subrule_fans(record, 1, 'qingque/standard') == {}


def seed(db):
    prepare(db)
    definition = BY_SUB_RULE['qingque/standard']
    record = replay(definition)
    record['game_title'].update(room_type='match', match_tier='elo')
    record['game_round']['round_index_1']['action_ticks'][1][3] = ['十三幺', '门前清']
    gid = writer(definition)(db, record, players(definition), 'match', '4/4_rank')
    assert gid
    db.sql("UPDATE game_player_metrics SET created_at='2020-01-01' WHERE game_id=%s", (gid,))
    db.sql('UPDATE game_player_metrics SET fan_stats=%s WHERE game_id=%s AND user_id=10000101',
           (Json({'menqianqing': 1, 'fengpaiwudui': 2, 'fengpaisidui': 3}), gid))
    db.sql("INSERT INTO scene_tier_fan_daily(stat_date,match_tier,rule,sub_rule,fan_field,fan_count) "
           "VALUES('2020-01-01','elo','qingque','qingque/standard','fengpaiwudui',99)")
    # 已完成旧全量升级，也必须允许本次专项修复。
    db.sql('INSERT INTO app_meta(meta_key,meta_value) VALUES(%s,%s)', (FULL_KEY, '{"status":"complete"}'))
    return gid


def test_recalculation_is_scoped_backed_up_atomic_and_once_only(db):
    gid = seed(db)
    before = db.sql("SELECT to_jsonb(m)-'fan_stats' FROM game_player_metrics m ORDER BY user_id")
    records = db.sql('SELECT * FROM game_records')
    db.sql("INSERT INTO scene_tier_fan_daily(stat_date,match_tier,rule,sub_rule,fan_field,fan_count) "
           "VALUES('2020-01-01','elo','sichuan','sichuan/standard','清一色',7)")
    conn = db._get_connection()
    preview = recalculate(conn)
    assert preview['changed'] == 1 and not preview['applied']
    assert db.sql('SELECT meta_value FROM app_meta WHERE meta_key=%s', (MIGRATION_KEY,)) == []
    assert db.sql("SELECT fan_count FROM scene_tier_fan_daily WHERE fan_field='fengpaiwudui'") == [(99,)]
    applied = recalculate(conn, apply=True)
    assert applied['changed'] == 1 and applied['applied'] and applied['scope'] == 'subset'
    assert db.sql("SELECT to_jsonb(m)-'fan_stats' FROM game_player_metrics m ORDER BY user_id") == before
    assert db.sql('SELECT * FROM game_records') == records
    assert get_rule_fans(db, 10000101, 'qingque', 'match') == {'shisanyao': 1, 'menqianqing': 1}
    conn.rollback()
    assert db.sql("SELECT fan_field,fan_count FROM scene_tier_fan_daily WHERE rule='qingque' ORDER BY fan_field") == [('menqianqing', 1), ('shisanyao', 1)]
    assert db.sql("SELECT fan_count FROM scene_tier_fan_daily WHERE rule='sichuan'") == [(7,)]
    assert db.sql(f"SELECT row_data->'fan_stats' FROM {BACKUP_TABLE} WHERE migration_key=%s AND table_name='game_player_metrics'", (MIGRATION_KEY,)) == [({'menqianqing': 1, 'fengpaiwudui': 2, 'fengpaisidui': 3},)]
    assert db.sql('SELECT meta_value FROM app_meta WHERE meta_key=%s', (FULL_KEY,)) == [('{"status":"complete"}',)]
    second = recalculate(conn, apply=True)
    assert second['status'] == 'already_complete' and second['scanned'] == 0


def test_missing_replay_rolls_back_without_completion_marker(db):
    gid = seed(db)
    db.sql("UPDATE game_replays SET record=record #- '{tables,main,payload}' WHERE game_id=%s", (gid,))
    result = recalculate(db._get_connection(), apply=True)
    assert result['status'] == 'blocked' and not result['applied']
    assert db.sql('SELECT meta_value FROM app_meta WHERE meta_key=%s', (MIGRATION_KEY,)) == []
    assert db.sql("SELECT fan_count FROM scene_tier_fan_daily WHERE fan_field='fengpaiwudui'") == [(99,)]
