"""按实际保存入口验证：全部子规则独立写入、失败回滚、无后续回填依赖。"""
from importlib import import_module
from types import SimpleNamespace

import pytest

from test_subrule_statistics import db  # 一次性 PostgreSQL schema
from server.database.statistics_catalog import RULES
from server.database.subrule_fans import collect_subrule_fans
from server.database.get_rule_stats import get_custom_rule_history


SAMPLES = {
    'guobiao': ('平和', 'pinghe'), 'riichi': ('平和', 'pinfu'),
    'qingque': ('和牌', 'hepai'), 'classical': ('自摸', 'zimo'),
    'sichuan': ('清一色', '清一色'), 'changsha': ('清一色', '清一色'),
    'taiwan': ('清一色', '清一色'), 'hongkong': ('清一色', '清一色'),
    'shanghai': ('清一色（3番）', '清一色'), 'hongque': ('清顺*3', '清顺'),
    'zhongyong': ('清一色', '清一色'), 'hongzhong': ('七对子', '七对子'),
}


def replay(definition):
    rule = definition['rule']
    count = definition['player_count']
    seats = list(range(1, count)) + [0]  # 换位，不能将当前座位当初始席位。
    label, _ = SAMPLES.get(rule, ('平和', '平和'))
    tick = (['hu_riichi', seats[0], 'hu_self', 8, 30, [label], [0] * count]
            if rule == 'riichi' else ['hu_self', seats[0], 8, [label], [0] * count])
    return {'game_title': {'rule': rule, 'sub_rule': definition['sub_rule'], 'room_type': 'custom',
                           **{f'p{i}_uid': 10000101 + i for i in range(count)}},
            'game_round': {'round_index_1': {'seats': seats, 'start_player_index': seats[0],
                                            'action_ticks': [['reset', seats[0]], tick, ['end']]}}}


@pytest.mark.parametrize('definition', RULES, ids=lambda row: row['sub_rule'])
def test_every_subrule_fan_profile_is_independent(definition):
    record = replay(definition)
    fans = collect_subrule_fans(record, 0, definition['sub_rule'])
    if not definition['fan_stats']:
        assert fans is None
    else:
        key = SAMPLES[definition['rule']][1]
        assert fans[key] == (3 if definition['rule'] == 'hongque' else 1)
        assert not any(collect_subrule_fans(record, 1, definition['sub_rule']).values())


def test_counts_and_display_annotations_do_not_mix_with_fan_values():
    definition = next(r for r in RULES if r['rule'] == 'hongque')
    record = replay(definition)
    record['game_round']['round_index_1']['action_ticks'][1][3] = ['清顺*3', '清刻×2', '清顺|12']
    assert collect_subrule_fans(record, 0, 'hongque/v1.6') == {'清顺': 4, '清刻': 2}
    record['game_round']['round_index_1']['action_ticks'][1][3] = ['正字（门风）', '清一色（3番）']
    assert collect_subrule_fans(record, 0, 'hongkong/new16') == {'正字（门风）': 1, '清一色': 1}
    record['game_round']['round_index_1']['action_ticks'][1][3].append('错和')
    assert collect_subrule_fans(record, 0, 'hongkong/new16') == {}


def test_changsha_initial_hu_is_counted_without_bird_or_dice_labels():
    record = replay(next(r for r in RULES if r['rule'] == 'changsha'))
    rd = record['game_round']['round_index_1']
    rd['action_ticks'][1][3] += ['鸟牌:一万,二万', '扎鸟倍数:x4']
    rd['initial_hu_events'] = [{'seat': 1, 'fans': ['四喜', '骰子:1,2', '起手胡']},
                               {'seat': 2, 'fans': ['板板胡']}]
    assert collect_subrule_fans(record, 0, 'changsha/classic_double_bird') == {'清一色': 1, '四喜': 1}


def writer(definition):
    family = definition['rule']
    if definition['sub_rule'] == 'zhongyong/nanque':
        family = 'jiandan'
    elif family not in {'guobiao', 'riichi', 'qingque', 'classical', 'sichuan', 'changsha', 'hongque'}:
        family = 'taiwan'
    return getattr(import_module(f'server.database.{family}.store_{family}'), f'store_{family}_game_record')


def players(definition):
    return [SimpleNamespace(user_id=10000101 + i, username=f'test{i}', score=24 if i == 0 else -8,
            original_player_index=i, record_counter=SimpleNamespace(rank_result=i+1))
            for i in range(definition['player_count'])]


def prepare(db):
    from server.database.player_recent_records import ensure_schema
    conn = db._get_connection()
    with conn.cursor() as cur:
        ensure_schema(cur)
    conn.commit()


def test_real_record_writers_publish_all_subrules_without_backfill(db, monkeypatch):
    prepare(db)
    for definition in RULES:
        if not definition['basic_stats']:
            continue
        record = replay(definition)
        mode = definition['custom_modes'][0].replace('_sanma', '')
        gid = writer(definition)(db, record, players(definition), 'custom', mode)
        assert gid, definition['sub_rule']
        rows = db.sql('SELECT sub_rule,fan_stats FROM game_player_metrics WHERE game_id=%s ORDER BY user_id', (gid,))
        assert len(rows) == definition['player_count']
        assert all(row[0] == definition['sub_rule'] for row in rows)
        assert rows[0][1] == collect_subrule_fans(record, 0, definition['sub_rule'])
        stats = get_custom_rule_history(db, 10000101, definition['key'])
        assert len(stats) == 1 and stats[0]['total_games'] == 1
        assert ('fan_stats' in stats[0]) == definition['fan_stats']
    # 第二次启动仅聚合，不再扫描原始牌谱。
    from server.database import daily_aggregator
    db.sql("UPDATE app_meta SET meta_value='2026-01-01' WHERE meta_key='stats_catchup_through'")
    aggregated = []
    monkeypatch.setattr(daily_aggregator, 'run_catchup_aggregation', lambda *a, **kw: aggregated.append(kw))
    daily_aggregator.run_startup_stats_restore(db)
    assert aggregated
    assert db.sql('SELECT COUNT(DISTINCT sub_rule) FROM game_player_metrics') == [(sum(row['basic_stats'] for row in RULES),)]
    columns = 'sub_rule,user_id,total_rounds,win_count,self_draw_count,fan_stats,match_type'
    before = db.sql(f'SELECT {columns} FROM game_player_metrics ORDER BY sub_rule,user_id')
    # 重复正常结构初始化和启动聚合，不改写逐局指标。
    from server.database.statistics_schema import ensure_statistics_schema
    with db._get_connection().cursor() as cur:
        ensure_statistics_schema(cur)
    db._get_connection().commit()
    daily_aggregator.run_startup_stats_restore(db)
    assert db.sql(f'SELECT {columns} FROM game_player_metrics ORDER BY sub_rule,user_id') == before


@pytest.mark.parametrize('family', ['guobiao', 'riichi', 'qingque', 'classical', 'sichuan', 'changsha', 'hongque', 'taiwan', 'zhongyong'])
def test_metrics_failure_rolls_back_record_and_players_then_retry_succeeds(db, monkeypatch, family):
    prepare(db)
    definition = next(r for r in RULES if r['rule'] == family)
    if family == 'zhongyong':
        definition = next(r for r in RULES if r['sub_rule'] == 'zhongyong/nanque')
    from server.database import record_metrics as metrics
    original = metrics._build_metrics_row
    def fail_second(*args, **kwargs):
        if args[1] == 10000102:
            raise ValueError('simulate fan-parser failure after first player write')
        return original(*args, **kwargs)
    record = replay(definition)
    monkeypatch.setattr(metrics, '_build_metrics_row', fail_second)
    assert writer(definition)(db, record, players(definition), 'custom', '1/4') is None
    for table in ('game_records', 'game_player_records', 'game_player_metrics', 'player_recent_records', 'riichi_player_game_stats'):
        assert db.sql(f'SELECT COUNT(*) FROM {table}') == [(0,)], table
    monkeypatch.setattr(metrics, '_build_metrics_row', original)
    assert writer(definition)(db, record, players(definition), 'custom', '1/4')
    assert db.sql('SELECT COUNT(*) FROM game_records') == [(1,)]
    assert db.sql('SELECT COUNT(*) FROM game_player_metrics') == [(definition['player_count'],)]
