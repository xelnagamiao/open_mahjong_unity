"""支持表的番种能力必须贯穿字典、牌谱计数和历史修复。"""
import ast
import json
from pathlib import Path

import pytest

from test_subrule_statistics import db
from test_subrule_realtime import replay, players, writer, prepare
from server.database.statistics_catalog import RULES, BY_SUB_RULE
from server.database.statistics_fans import fan_names, HONGQUE_NAMES
from server.database.subrule_fans import collect_subrule_fans
from server.database.get_rule_stats import get_rule_history
from server.database.statistics_fan_backfill import backfill, BACKUP_TABLE


@pytest.mark.parametrize('definition', [r for r in RULES if r['fan_stats']], ids=lambda r: r['sub_rule'])
def test_every_enabled_rule_has_complete_zero_catalog_and_readable_fields(definition):
    names = fan_names(definition['sub_rule'])
    assert names and len(set(names.values())) == len(names)
    record = replay(definition)
    tick = record['game_round']['round_index_1']['action_ticks'][1]
    tick[5 if definition['rule'] == 'riichi' else 3] = list(names.values())
    counts = collect_subrule_fans(record, 0, definition['sub_rule'])
    assert {key for key, value in counts.items() if value} == set(names)
    assert all(value == 1 for value in counts.values())
    assert not any(collect_subrule_fans(record, 1, definition['sub_rule']).values())


def test_hongque_catalog_covers_every_scoring_pattern_and_counts_achievements():
    path = Path(__file__).resolve().parents[2] / 'server/gamestate/game_hongque/scoring.py'
    tree = ast.parse(path.read_text(encoding='utf-8'))
    direct = {node.args[0].value for node in ast.walk(tree) if isinstance(node, ast.Call)
              and isinstance(node.func, ast.Name) and node.func.id == '_entry'
              and node.args and isinstance(node.args[0], ast.Constant)}
    assert direct <= set(HONGQUE_NAMES)
    assert {'双同刻', '三同刻', '四同刻', '双同顺', '三同顺', '四同顺'} <= set(HONGQUE_NAMES)
    record = replay(BY_SUB_RULE['hongque/v1.6'])
    record['game_round']['round_index_1']['action_ticks'][1][3] = ['双同顺*2|4', '清顺*4|4', '门清|1']
    assert collect_subrule_fans(record, 0, 'hongque/v1.6') == {'双同顺': 2, '清顺': 4, '门清': 1}


@pytest.mark.parametrize('filename,sub_rule', [
    ('old_style.py', 'hongkong/qingzhang'), ('modern16.py', 'hongkong/new16')])
def test_hongkong_catalog_covers_names_emitted_by_the_scorer(filename, sub_rule):
    path = Path(__file__).resolve().parents[2] / 'server/game_calculation/hongkong' / filename
    tree = ast.parse(path.read_text(encoding='utf-8'))

    def names(node):
        if isinstance(node, ast.Constant) and isinstance(node.value, str):
            return {node.value}
        if isinstance(node, ast.IfExp):
            return names(node.body) | names(node.orelse)
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            return {a + b for a in names(node.left) for b in names(node.right)}
        if isinstance(node, ast.Subscript) and isinstance(node.value, ast.Dict):
            return set().union(*(names(value) for value in node.value.values))
        return set()

    emitted = set()
    for call in ast.walk(tree):
        if isinstance(call, ast.Call) and isinstance(call.func, ast.Name) \
                and call.func.id in ('add', 'Fan') and len(call.args) > 1:
            emitted.update(names(call.args[1]))
    assert len(emitted) > 20
    assert emitted <= set(fan_names(sub_rule))
    if sub_rule == 'hongkong/new16':
        assert {'大鸡糊', '大鸭糊', '小三色6连刻', '暗四归4', '嚦咕·嚦咕嚦咕'} <= set(fan_names(sub_rule))
        assert not {'嚦咕·暗一色5步高', '嚦咕·大鸡糊', '嚦咕·底'} & set(fan_names(sub_rule))


def test_qiaoma_counts_patterns_without_flower_or_cap_payment_annotations():
    record = replay(BY_SUB_RULE['shanghai/qiaoma'])
    record['game_round']['round_index_1']['action_ticks'][1][3] = [
        '清一色（3番）', '碰碰和（2番）', '花数（5花）', '底花（1花）', '三番封顶']
    assert collect_subrule_fans(record, 0, 'shanghai/qiaoma') == {'清一色': 1, '碰碰和': 1}
    assert '十三将' not in fan_names('changsha/classic_double_bird')
    assert '十三将' in fan_names('changsha/exchange_six')
    assert '根' in fan_names('sichuan/standard')
    assert '根' not in fan_names('sichuan/xueliu_doubles')


def test_workbook_and_generated_web_catalog_agree_for_every_rule():
    from scripts.validate_rule_support_table import validate, ROOT
    assert validate()['番种统计'] == 27
    web_rules = json.loads((ROOT / 'open_mahjong_web/shared/statisticsRules.json').read_text(encoding='utf-8'))['rules']
    assert web_rules == RULES
    web_fans = json.loads((ROOT / 'open_mahjong_web/shared/statisticsFans.json').read_text(encoding='utf-8'))
    assert web_fans == {r['sub_rule']: fan_names(r['sub_rule']) for r in RULES if r['fan_stats']}


def seed_historical_fans(db, sub='sichuan/standard'):
    prepare(db)
    definition = BY_SUB_RULE[sub]
    record = replay(definition)
    record['game_title'].update(room_type='match', match_tier='elo')
    gid = writer(definition)(db, record, players(definition), 'match', '4/4_rank')
    assert gid
    db.sql('UPDATE game_player_metrics SET fan_stats=NULL,created_at=%s WHERE game_id=%s', ('2020-01-01', gid))
    return gid, record


def test_historical_fan_repair_preserves_replays_values_modes_and_is_idempotent(db):
    gid, record = seed_historical_fans(db)
    before = db.sql('SELECT to_jsonb(m)-\'fan_stats\' FROM game_player_metrics m ORDER BY user_id')
    replay_before = db.sql('SELECT * FROM game_records')
    participants = db.sql('SELECT * FROM game_player_records ORDER BY user_id')
    conn = db._get_connection()
    assert backfill(conn)['changed'] == 4
    assert db.sql('SELECT COUNT(fan_stats) FROM game_player_metrics') == [(0,)]
    result = backfill(conn, apply=True)
    assert result['changed'] == 4 and result['applied']
    assert db.sql('SELECT to_jsonb(m)-\'fan_stats\' FROM game_player_metrics m ORDER BY user_id') == before
    assert db.sql('SELECT * FROM game_records') == replay_before
    assert db.sql('SELECT * FROM game_player_records ORDER BY user_id') == participants
    history = get_rule_history(db, 10000101, 'sichuan', 'match')
    assert [(row['mode'], row['fan_stats']) for row in history] == [('4/4_rank', {'清一色': 1})]
    conn.rollback()
    assert backfill(conn, apply=True)['changed'] == 0
    assert db.sql(f"SELECT COUNT(*) FROM {BACKUP_TABLE} WHERE table_name='game_player_metrics'") == [(4,)]
    assert db.sql("SELECT fan_count FROM scene_tier_fan_daily WHERE sub_rule='sichuan/standard' AND fan_field='清一色'") == [(1,)]


def test_failed_historical_repair_rolls_back_all_changes(db):
    gid, record = seed_historical_fans(db)
    db.sql('UPDATE game_records SET record=NULL WHERE game_id=%s', (gid,))
    result = backfill(db._get_connection(), apply=True)
    assert result['errors'] and not result['applied']
    assert db.sql('SELECT COUNT(fan_stats) FROM game_player_metrics') == [(0,)]
    assert db.sql('SELECT to_regclass(%s)', (BACKUP_TABLE,)) == [(None,)]
