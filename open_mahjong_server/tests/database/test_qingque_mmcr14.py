"""MMCR 14 番种目录和换位计数。"""
import json
from pathlib import Path
import re

from test_subrule_realtime import replay
from server.database.statistics_catalog import BY_SUB_RULE
from server.database.statistics_fans import fan_names
from server.database.subrule_fans import collect_subrule_fans
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
