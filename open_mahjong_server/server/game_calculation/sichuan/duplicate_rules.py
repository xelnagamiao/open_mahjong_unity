"""Versioned rules shared by paired rooms, play, records and settlement."""
from collections import Counter
import random

SBR_DUPLICATE = "sichuan/doubles_duplicate"
XUELIU_DUPLICATE = "sichuan/xueliu_doubles_duplicate"
DUPLICATE_SUB_RULES = frozenset((SBR_DUPLICATE, XUELIU_DUPLICATE))
ADMIN_ONLY_MESSAGE = "该房间只能在比赛场的管理后台创建"
BOARD_COUNTS = (1, 4, 8, 12, 16)
TILES = tuple(tile for suit in (1, 2, 3) for tile in range(suit * 10 + 1, suit * 10 + 10) for _ in range(4))
IMP_UPPER_BOUNDS = (0, 2, 5, 8, 11, 15, 19, 25, 31, 39)


def is_duplicate_sub_rule(sub_rule):
    return sub_rule in DUPLICATE_SUB_RULES


def rule_profile(sub_rule):
    if not is_duplicate_sub_rule(sub_rule):
        raise ValueError("不是川麻双打复式子规则")
    bloodflow = sub_rule == XUELIU_DUPLICATE
    return {
        "version": "xueliu-duplicate-v1" if bloodflow else "sbr2025-duplicate-v1",
        "format": "paired_tables_2v2", "sub_rule": sub_rule,
        "scoring": "raw_difference" if bloodflow else "imp",
        "tied_match": "extra_board_until_decided" if bloodflow else "draw",
        "wall_policy": "own_wall_standby" if bloodflow else "own_wall_then_retired_partner",
        "wall_policy_source": "platform_supplement" if bloodflow else "sbr2025",
        "initial_hand_count": 13, "initial_wall_count": 14,
        "supplement_draw": "front", "dealer_policy": "fixed_ESWN",
        "active_flower_pig_penalty": None if bloodflow else 12,
    }


def team_by_seat(table):
    if table not in ("open", "closed"):
        raise ValueError("复式桌号必须为开室或闭室")
    return [0, 1, 0, 1] if table == "open" else [1, 0, 1, 0]


def make_board(seed, number):
    tiles = list(TILES)
    random.Random(str(seed) + ":" + str(number)).shuffle(tiles)
    return {"number": number, "dealer": (number - 1) % 4,
            "hands": [tiles[i * 27:i * 27 + 13] for i in range(4)],
            "walls": [tiles[i * 27 + 13:(i + 1) * 27] for i in range(4)]}


def validate_board(board):
    if not isinstance(board, dict) or type(board.get("number")) is not int or board["number"] < 1:
        raise ValueError("复式副号无效")
    if type(board.get("dealer")) is not int or board["dealer"] != (board["number"] - 1) % 4:
        raise ValueError("复式庄位与副号不一致")
    for name, size in (("hands", 13), ("walls", 14)):
        groups = board.get(name)
        if not isinstance(groups, list) or len(groups) != 4 or any(
                not isinstance(g, list) or len(g) != size or any(type(t) is not int for t in g) for g in groups):
            raise ValueError("复式起手或牌墙数据不完整")
    if Counter(t for name in ("hands", "walls") for g in board[name] for t in g) != Counter(TILES):
        raise ValueError("复式牌组必须为 108 张川麻数牌")


def imp(points):
    if type(points) is not int or points < 0:
        raise ValueError("IMP 输入必须为非负整数副分")
    return next((i for i, upper in enumerate(IMP_UPPER_BOUNDS) if points <= upper), 10)


def score_board(open_scores, closed_scores, *, scoring):
    """Inputs are global team deltas, already including pair adjustments.

    Never subtract the opponent's zero-sum result a second time.
    """
    if scoring not in ("imp", "raw_difference"):
        raise ValueError("未知复式计分方式")
    if any(len(s) != 2 or any(type(n) is not int for n in s) or sum(s) != 0
           for s in (open_scores, closed_scores)):
        raise ValueError("复式两队副分必须守恒")
    raw = [open_scores[i] + closed_scores[i] for i in range(2)]
    positive = [max(0, n) for n in raw]
    return {"raw_difference": raw, "scores": [imp(n) for n in positive] if scoring == "imp" else positive}
