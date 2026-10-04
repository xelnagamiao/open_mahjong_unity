"""MIL 温州麻将 2024：实体/本牌校验及独立结算。

标准牌型由 ``game_calculation.jokers`` 统一求解。计分规则与求解器分离，
白板的固定本牌映射不改变手牌、牌墙或牌谱中的实体编码。

线上补则：财神不可用于吃碰杠；加杠原文未列杠分，记 0 分。
翻出的财神留在牌墙尾部，因此可进入玩家手牌的同种财神至多三张。
"""

from collections import Counter
from dataclasses import dataclass

from ..hand_structure import SIXTEEN_TILE_MAHJONG


RULE_VERSION = "mil-wenzhou-2024-om1"
EDITION = "mil-wenzhou-2024-om1"
SUB_RULE = "wenzhou/mil2024"
HAND_STRUCTURE = SIXTEEN_TILE_MAHJONG
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
HONORS = tuple(range(41, 48))
TILES = NUMBERS + HONORS
WHITE = 46
KONG_POINTS = {"ming": 1, "angang": 2, "jiagang": 0}


def normalize_config(raw=None):
    """MIL 温州采用固定馆规，不将平台补则误包装成可选规则。"""
    if raw is None:
        raw = {}
    if (not isinstance(raw, dict) or set(raw) - {"edition"}
            or raw.get("edition", EDITION) != EDITION):
        raise ValueError("MIL 温州麻将没有可选馆规")
    return {"edition": EDITION}


def _is_tile(tile):
    return type(tile) is int and tile in TILES


def natural_tile(tile, caishen):
    """固定本牌身份；财神自身在归位判定时仍是其本牌。"""
    if not _is_tile(tile) or not _is_tile(caishen):
        raise ValueError("非法实体牌或财神")
    return caishen if tile == WHITE else tile


@dataclass(frozen=True)
class DeclaredMeld:
    code: str
    kind: str
    physical: tuple[int, ...]
    logical: tuple[int, ...]
    concealed: bool

    def as_dict(self):
        return {"code": self.code, "kind": self.kind,
                "physical": list(self.physical), "logical": list(self.logical),
                "concealed": self.concealed}


def _code_tiles(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "skgG":
        raise ValueError("非法温州面子码")
    try:
        tile = int(code[1:])
    except ValueError as exc:
        raise ValueError("非法温州面子牌种") from exc
    if not _is_tile(tile):
        raise ValueError("面子牌种不在温州牌组中")
    if code[0] == "s":
        if tile not in NUMBERS or not 2 <= tile % 10 <= 8:
            raise ValueError("顺子码必须为数牌中张")
        return "sequence", (tile - 1, tile, tile + 1)
    if code[0] == "k":
        return "triplet", (tile,) * 3
    return "kong", (tile,) * 4


def parse_meld(value, *, caishen):
    """读取旧组合码或包含实体/逻辑数组的新副露记录。

    新记录的 code 描述逻辑面子；physical/logical 数组按同一位置配对，
    数组可以按实际展示顺序排列，不要求顺子升序。
    """
    if not _is_tile(caishen):
        raise ValueError("非法财神")
    if isinstance(value, str):
        kind, physical = _code_tiles(value)
        logical = tuple(natural_tile(t, caishen) for t in physical)
        # 旧牌谱只保存实体组合码；白板刻/杠按固定本牌恢复逻辑码。
        code = value[0] + str(logical[1] if kind == "sequence" else logical[0])
    elif isinstance(value, dict):
        code = value.get("code")
        kind, expected = _code_tiles(code)
        physical = value.get("physical")
        if not isinstance(physical, (list, tuple)) or len(physical) != len(expected):
            raise ValueError("副露实体牌数错误")
        physical = tuple(physical)
        if any(not _is_tile(t) for t in physical):
            raise ValueError("副露包含非法实体牌")
        supplied_logical = value.get("logical")
        if supplied_logical is None:
            logical = tuple(natural_tile(t, caishen) for t in physical)
        elif not isinstance(supplied_logical, (list, tuple)):
            raise ValueError("副露逻辑牌必须为数组")
        else:
            logical = tuple(supplied_logical)
        if (len(logical) != len(physical) or any(not _is_tile(t) for t in logical)
                or Counter(logical) != Counter(expected)):
            raise ValueError("副露逻辑牌与面子码不符")
        if any(natural_tile(p, caishen) != l for p, l in zip(physical, logical)):
            raise ValueError("副露不允许改变固定本牌身份")
    else:
        raise ValueError("副露必须为组合码或实体记录")
    if caishen in physical:
        raise ValueError("财神不参与吃碰杠")
    return DeclaredMeld(code, kind, physical, logical, code[0] == "G")


@dataclass(frozen=True)
class HandInput:
    hand: tuple[int, ...]
    melds: tuple[DeclaredMeld, ...]
    physical: tuple[int, ...]
    caishen: int


def _valid(hand, melds, caishen, *, complete):
    """无效外部输入返回 None；不让错误牌谱或协议包进入求解器。"""
    if (not isinstance(hand, (list, tuple)) or not isinstance(melds, (list, tuple))
            or not _is_tile(caishen) or len(melds) > HAND_STRUCTURE.meld_count):
        return None
    if len(hand) != HAND_STRUCTURE.concealed_tile_count(len(melds), complete=complete):
        return None
    if any(not _is_tile(t) for t in hand):
        return None
    try:
        parsed = tuple(parse_meld(value, caishen=caishen) for value in melds)
    except ValueError:
        return None
    physical = tuple(hand) + tuple(t for m in parsed for t in m.physical)
    count = Counter(physical)
    if any(n > (3 if t == caishen else 4) for t, n in count.items()):
        return None
    return HandInput(tuple(hand), parsed, physical, caishen)


def dealer_multiplier(repeats):
    if type(repeats) is not int or repeats not in range(4):
        raise ValueError("连庄次数必须为 0 至 3")
    return 2 * (repeats + 1)


def _seat(seat):
    if type(seat) is not int or seat not in range(4):
        raise ValueError("座位必须为 0 至 3")
    return seat


def _collect(player, points, dealer, repeats):
    _seat(player)
    _seat(dealer)
    factor = dealer_multiplier(repeats)
    changes = [0] * 4
    for opponent in range(4):
        if opponent == player:
            continue
        paid = points * (factor if dealer in (player, opponent) else 1)
        changes[opponent] -= paid
        changes[player] += paid
    return changes


def payments(winner, multiplier, *, dealer=0, repeats=0):
    """和牌者向其余三家收取分数；点和与自摸支付人数相同。"""
    if type(multiplier) is not int or multiplier not in (1, 2, 4):
        raise ValueError("和牌倍率必须为 1、2 或 4")
    return _collect(winner, multiplier, dealer, repeats)


def kong_payments(player, kind, *, dealer=0, repeats=0):
    """点杠 1、暗杠 2；加杠未另设分数，不能按点杠误收。"""
    if not isinstance(kind, str) or kind not in KONG_POINTS:
        raise ValueError("杠种必须为 ming、angang 或 jiagang")
    return _collect(player, KONG_POINTS[kind], dealer, repeats)


def caishen_payments(counts):
    """财神独立结算。PDF 例 0/0/1/2 -> -3/-3/1/5。"""
    if (not isinstance(counts, (list, tuple)) or len(counts) != 4
            or any(type(n) is not int or n not in range(4) for n in counts)
            or sum(counts) > 3):
        raise ValueError("四家财神计数非法或超过可摸的三张")
    total = sum(counts)
    return [4 * n - total for n in counts]


def _score_features(*, standard, eight_pairs, natural_standard, natural_eight_pairs,
                    caishen_count, self_draw):
    """比较所有成立的计分类别，不将另一种牌形的归位证明移植给八对。

    这里不拆牌：四个牌形布尔值由共用求解器分别证明。先证明类别的最高
    倍率，再仅为该类别请求一个拆分见证，避免温州的简单计分枚举所有鬼替代。
    """
    candidates = []
    if standard:
        factor = 2 if self_draw or natural_standard else 1
        reason = "自摸" if self_draw else "财神归位或无财" if natural_standard else "财神代牌"
        candidates.append((factor, 1, "standard", reason))
    if eight_pairs:
        factor = 4 if self_draw or natural_eight_pairs else 1
        reason = "八对自摸" if self_draw else "无财八对" if natural_eight_pairs else "带财八对"
        candidates.append((factor, 2, "eight_pairs", reason))
    if caishen_count >= 3:
        ordinary = standard or eight_pairs
        candidates.append((4 if ordinary else 2, 3, "three_caishen",
                           "三财神和牌型" if ordinary else "三财神非和牌型"))
    if not candidates:
        return None
    multiplier, _, shape, reason = max(candidates)
    name = {1: "软和", 2: "硬和", 4: "双番"}[multiplier]
    return {
        "rule_version": RULE_VERSION,
        "multiplier": multiplier,
        "fan_names": [name, reason],
        "shape": shape,
        "reason": reason,
        "hard": multiplier >= 2,
        "natural_win": bool(natural_standard or natural_eight_pairs),
        "standard": bool(standard),
        "eight_pairs": bool(eight_pairs),
        "caishen_count": caishen_count,
    }


def _evaluated_score(hand, melds, *, caishen, winning_tile, self_draw):
    valid = _valid(hand, melds, caishen, complete=True)
    if valid is None or type(self_draw) is not bool:
        return None
    if winning_tile is None:
        winning_tile = valid.hand[-1]
    if not _is_tile(winning_tile) or winning_tile not in valid.hand:
        return None
    from .solver_adapter import shape_flags
    flags = shape_flags(valid)
    score = _score_features(**flags, caishen_count=valid.hand.count(caishen), self_draw=self_draw)
    if score is None:
        return None
    return valid, score, winning_tile


def hand_multiplier(hand, melds=(), *, caishen, winning_tile=None, self_draw=False):
    """快判最高倍率；提示、过水与行动资格不生成任何拆分见证。"""
    evaluated = _evaluated_score(hand, melds, caishen=caishen,
                                winning_tile=winning_tile, self_draw=self_draw)
    return None if evaluated is None else evaluated[1]["multiplier"]


def score_hand(hand, melds=(), *, caishen, winning_tile=None, self_draw=False):
    """输入已包含和牌张；返回 JSON 可序列化结算或 None。

    副露使用 parse_meld 支持的实体记录。摸打来源限制和过水由状态机处理。
    """
    evaluated = _evaluated_score(hand, melds, caishen=caishen,
                                winning_tile=winning_tile, self_draw=self_draw)
    if evaluated is None:
        return None
    valid, score, winning_tile = evaluated
    from .solver_adapter import winning_witness
    score["winning_tile"] = winning_tile
    score["self_draw"] = self_draw
    score["caishen"] = caishen
    score["physical_hand"] = list(valid.hand)
    score["declared_melds"] = [meld.as_dict() for meld in valid.melds]
    score["witness"] = winning_witness(valid, score, winning_tile)
    return score


def is_complete(hand, melds=(), *, caishen):
    """快速和牌资格；三財独立牌型不强求普通面子结构。"""
    valid = _valid(hand, melds, caishen, complete=True)
    if valid is None:
        return False
    if valid.hand.count(caishen) >= 3:
        return True
    from .solver_adapter import ordinary_complete
    return ordinary_complete(valid)


def waiting_tiles(hand, melds=(), *, caishen):
    """返回实际可摸/和的实体牌，不返回白板的逻辑替代编码。"""
    valid = _valid(hand, melds, caishen, complete=False)
    if valid is None:
        return set()
    from .solver_adapter import structural_waits
    return set(structural_waits(valid))
