"""Validated four-player riichi options shared by room creation and calculation."""
import json
from copy import deepcopy
from functools import lru_cache
from pathlib import Path


@lru_cache(maxsize=1)
def catalog():
    return json.loads(Path(__file__).with_name('rule_options.json').read_text(encoding='utf-8'))


def normalize_riichi_config(raw=None):
    if raw is None:
        raw = {}
    if not isinstance(raw, dict):
        raise ValueError('日麻详细规则必须为配置对象')
    options = {item['key']: item for item in catalog()['options']}
    unknown = set(raw) - options.keys()
    if unknown:
        raise ValueError('未知日麻配置：' + ', '.join(sorted(unknown)))
    result = {}
    for key, item in options.items():
        value = raw.get(key, item['default'])
        if not any(type(value) is type(allowed) and value == allowed for allowed in item['values']):
            raise ValueError(f"{item['label']}的值无效：{value!r}")
        result[key] = value
    return result


def preset_room_config(preset_id):
    for preset in catalog()['presets']:
        if preset['id'] == preset_id:
            return {**deepcopy(preset['room']), 'sub_rule': 'riichi/standard',
                    'detailed_config': deepcopy(preset['values'])}
    raise ValueError(f'未知日麻预设：{preset_id}')


def validate_riichi_sub_rule(value):
    if value not in ('riichi/standard', 'riichi/langyong'):
        raise ValueError('不支持的立直麻将子规则')
    return value
