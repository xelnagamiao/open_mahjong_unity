"""MIL 红中2024第七、八节：对一项完整替代解释计番及零和支付。

形状求解由共用 jokers 包负责；本模块只消费完整逻辑解释。实体红中
是否存在与逻辑上被替成什么是不同事实，不能以替代后的牌去判“无红中”。
"""

from collections import Counter
from dataclasses import dataclass

RULE_VERSION = "mil-hongzhong-2024-om1"
SUB_RULE = "hongzhong/mil2024"
JOKER = 45
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
TILES = NUMBERS + (JOKER,)
FANS = {"平和": 0, "无红中": 1, "大对子": 1, "一条龙": 1,
        "杠上花": 1, "七对子": 2, "清一色": 2, "龙七对": 3}
CONFIG = {"rule_version": RULE_VERSION, "fan_cap": 4, "bird_count": 2,
          "self_draw_only": True, "joker_tile": JOKER}


@dataclass(frozen=True)
class Interpretation:
    """规则层所需的计分解释；所有牌值仍采用本项目的两位数编码。"""

    kind: str
    closed_tiles: tuple
    groups: tuple
    winning_logical_tile: int
    substitutions: tuple = ()


def normalize_config(raw=None):
    if raw is None:
        return dict(CONFIG)
    if not isinstance(raw, dict) or set(raw) - CONFIG.keys():
        raise ValueError("红中 MIL 2024 配置必须为标准规则对象")
    for key, value in raw.items():
        if type(value) is not type(CONFIG[key]) or value != CONFIG[key]:
            raise ValueError("红中 MIL 2024 不支持覆盖标准规则参数")
    return dict(CONFIG)


def meld_tiles(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "kgG":
        raise ValueError("红中麻将只允许数牌的碰与杠")
    try:
        tile = int(code[1:])
    except ValueError as exc:
        raise ValueError("无效的红中麻将副露") from exc
    if tile not in NUMBERS:
        raise ValueError("红中不能被碰或杠")
    return (tile,) * (3 if code[0] == "k" else 4)


def valid_tiles(hand, melds=(), *, complete=True):
    if not isinstance(hand, (tuple, list)) or not isinstance(melds, (tuple, list)):
        return False
    if len(melds) > 4 or len(hand) != 3 * (4 - len(melds)) + (2 if complete else 1):
        return False
    if any(type(t) is not int or t not in TILES for t in hand):
        return False
    try:
        physical = Counter(hand)
        physical.update(t for code in melds for t in meld_tiles(code))
        return max(physical.values(), default=0) <= 4
    except (ValueError, TypeError):
        return False


def score_interpretation(hand, melds, shape, *, replacement=False):
    """仅给完整且来自结构求解器的解释计分，不自行枚举癞子。"""
    names = set()
    if JOKER not in hand:
        names.add("无红中")
    if replacement:
        names.add("杠上花")
    all_tiles = tuple(shape.closed_tiles) + tuple(t for code in melds for t in meld_tiles(code))
    if all(t in NUMBERS for t in all_tiles) and len({t // 10 for t in all_tiles}) == 1:
        names.add("清一色")
    if shape.kind == "seven_pairs":
        if Counter(shape.closed_tiles)[shape.winning_logical_tile] == 4:
            names.add("龙七对")
        else:
            names.add("七对子")
    elif shape.kind == "standard":
        if all(len(set(group)) == 1 for group in shape.groups):
            names.add("大对子")
        sequences = {tuple(sorted(group)) for group in shape.groups if len(set(group)) == 3}
        if any({(s * 10 + 1, s * 10 + 2, s * 10 + 3),
                (s * 10 + 4, s * 10 + 5, s * 10 + 6),
                (s * 10 + 7, s * 10 + 8, s * 10 + 9)} <= sequences for s in (1, 2, 3)):
            names.add("一条龙")
    else:
        raise ValueError("红中麻将不支持的成和形状")
    if not names:
        names.add("平和")
    ordered = [name for name in FANS if name in names]
    raw = sum(FANS[name] for name in ordered)
    fan = min(4, raw)
    return {"is_win": True, "fan_names": ordered, "fan_values": {name: FANS[name] for name in ordered},
            "fan": fan, "raw_fan": raw, "base_score": 1 << fan, "rule_version": RULE_VERSION,
            "shape": shape.kind, "logical_hand": list(shape.closed_tiles),
            "winning_logical_tile": shape.winning_logical_tile,
            "joker_substitutions": [list(pair) for pair in shape.substitutions]}


def _seat(value):
    if type(value) is not int or value not in range(4):
        raise ValueError("无效座位")
    return value


def bird_hits(tiles):
    if not isinstance(tiles, (tuple, list)) or len(tiles) > 2:
        raise ValueError("扎鸟应为牌墙前端至多两张")
    if any(type(t) is not int or t not in TILES for t in tiles):
        raise ValueError("无效扎鸟牌")
    return sum(t == JOKER or t % 10 in (1, 5, 9) for t in tiles)


def win_payments(winner, fan, birds=()):
    _seat(winner)
    if type(fan) is not int or fan < 0:
        raise ValueError("无效番数")
    amount = (1 << min(4, fan)) + bird_hits(birds)
    return {i: amount * 3 if i == winner else -amount for i in range(4)}


def kong_payments(player, kind, *, payer=None, drawn=True):
    _seat(player)
    if kind not in ("direct", "added", "concealed") or type(drawn) is not bool:
        raise ValueError("无效杠类型")
    if kind == "direct" and (_seat(payer) == player):
        raise ValueError("点杠者不能是开杠者")
    changes = {i: 0 for i in range(4)}
    if kind == "added" and not drawn:
        return changes
    others = [payer] if kind == "direct" else [i for i in range(4) if i != player]
    amount = {"direct": 3, "added": 1, "concealed": 2}[kind]
    for i in others:
        changes[i] -= amount
        changes[player] += amount
    return changes


def reverse_kong_payments(ledger):
    changes = {i: 0 for i in range(4)}
    for event in ledger:
        for i in range(4):
            changes[i] -= event["changes"][i]
    return changes
