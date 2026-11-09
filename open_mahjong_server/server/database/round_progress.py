"""One completed-hand contract for every replay, independent of dealer rotation.

current_round is the rule's scheduled progress; round_index identifies an actual
deal, including repeats. Neither is a count of completed hands. Only an ended
round_index_* entry contributes to completed_hands.
"""
import re

from .statistics_catalog import BY_SUB_RULE, normalize_match_type

VERSION = 1
_ROUND_KEY = re.compile(r"round_index_([1-9][0-9]*)\Z")


def ended_rounds(record):
    rounds = record.get('game_round') or {}
    if not isinstance(rounds, dict):
        raise ValueError('牌谱 game_round 须为对象')
    result, seen = [], set()
    for key, rd in rounds.items():
        match = _ROUND_KEY.fullmatch(str(key))
        if not match:
            continue
        index = int(match[1])
        if not isinstance(rd, dict):
            raise ValueError(f'牌谱小局结构无效: {key}')
        declared = rd.get('round_index')
        if declared is not None and (type(declared) is not int or declared != index):
            raise ValueError(f'牌谱实际小局索引冲突: {key}')
        if index in seen:
            raise ValueError(f'牌谱实际小局索引重复: {index}')
        seen.add(index)
        ticks = rd.get('action_ticks')
        if not isinstance(ticks, list):
            raise ValueError(f'牌谱小局缺少动作列表: {key}')
        if any(isinstance(tick, list) and tick and tick[0] == 'end' for tick in ticks):
            result.append((index, rd))
    return sorted(result, key=lambda row: row[0])


def completed_hand_count(record):
    return len(ended_rounds(record))


def participant_ids(record):
    title = record.get('game_title') or {}
    result = []
    for index in range(8):
        value = title.get(f'p{index}_uid')
        if value is None:
            continue
        if isinstance(value, bool):
            raise ValueError('牌谱玩家身份无效')
        try:
            parsed = int(value)
        except (TypeError, ValueError) as exc:
            raise ValueError('牌谱玩家身份无效') from exc
        if str(parsed) != str(value):
            raise ValueError('牌谱玩家身份须为整数')
        result.append(parsed)
    return result


def round_progress(record, match_type=None):
    title = record.get('game_title') or {}
    definition = BY_SUB_RULE.get(title.get('sub_rule'), {})
    ids = participant_ids(record)
    count = len(ids) or title.get('player_count') or definition.get('player_count', 4)
    if type(count) is not int or count not in (1, 2, 3, 4):
        raise ValueError('牌谱玩家人数无效')
    if title.get('player_count') is not None and title['player_count'] != count:
        raise ValueError('牌谱人数与实际席位冲突')
    if definition.get('player_count') and ids and len(ids) != definition['player_count']:
        raise ValueError('牌谱席位数与子规则冲突')
    completed = ended_rounds(record)
    raw_limit = title.get('max_round')
    limit = raw_limit if type(raw_limit) is int and raw_limit > 0 else None
    mode = match_type or title.get('match_type')
    if limit is None:
        match = re.fullmatch(r'([1-9][0-9]*)/[34](?:_sanma)?(?:_rank)?', mode or '')
        if match:
            limit = int(match[1])
    unit, planned = 'circles', limit * count if limit is not None else None
    if title.get('rule') == 'fuzhou' and (
            title.get('rule_version') or (title.get('detailed_config') or {}).get('rule_version')) == 'fuzhou-user-20261014-v1':
        # The retired v1 schedule used six dealer advances per configured circle.
        planned = limit * 6 if limit is not None else None
    if title.get('unlimited_rounds') or (mode or '').startswith('unlimited/'):
        unit, planned = 'unlimited', None
    elif type(title.get('duplicate_round_count')) is int and title['duplicate_round_count'] > 0:
        unit, planned = 'hands', title['duplicate_round_count']
    elif title.get('rule') == 'hongque':
        unit, planned = 'hands', limit
        # Older metadata sometimes encoded wind circles for Hongque. It has no
        # dealer repeats: a completed 8/12/16-hand replay cannot have a 4-hand limit.
        if len(completed) in (4, 8, 12, 16) and (planned is None or len(completed) > planned):
            planned = len(completed)
    current = max((rd.get('current_round', 0) for _, rd in completed
                   if type(rd.get('current_round')) is int), default=0)
    rounds = record.get('game_round') or {}
    return dict(version=VERSION, player_count=count, limit_unit=unit,
                planned_hands=planned, completed_hands=len(completed),
                started_hands=sum(bool(_ROUND_KEY.fullmatch(str(key))) for key in rounds),
                last_round_index=completed[-1][0] if completed else 0,
                progress_round=current)


def apply_round_progress(record, match_type=None, room_type=None):
    """Normalize metadata in place; keep legacy progress fields for old clients."""
    title = record.setdefault('game_title', {})
    progress = round_progress(record, match_type)
    mode = normalize_match_type(match_type or title.get('match_type'), title.get('sub_rule'))
    if title.get('rule') == 'hongque' and progress['planned_hands'] in (4, 8, 12, 16):
        title['max_round'] = progress['planned_hands']
        ranked = (room_type or title.get('room_type')) == 'match' or (mode or '').endswith('_rank')
        mode = f"{progress['planned_hands']}/4" + ('_rank' if ranked else '')
    title.update(player_count=progress['player_count'], round_progress=progress)
    if mode:
        title['match_type'] = mode
    return mode


def reward_human_ids(record, indexed_ids):
    """Guests are human seats. Registration is checked separately in users."""
    ids = participant_ids(record)
    count = len(ids)
    if (count not in (2, 3, 4) or len(set(ids)) != count or any(uid <= 10 for uid in ids)
            or sorted(ids) != sorted(indexed_ids) or len(indexed_ids) != count):
        return []
    title = record.get('game_title') or {}
    if title.get('rule') == 'free':
        return []
    definition = BY_SUB_RULE.get(title.get('sub_rule'), {})
    if definition.get('player_count', title.get('player_count', count)) != count:
        return []
    if title.get('player_count') is not None and title['player_count'] != count:
        return []
    return sorted(ids)
