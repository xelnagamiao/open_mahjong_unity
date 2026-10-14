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
JJ_IMP_UPPER_BOUNDS = (0, 9, 29, 59)


def is_duplicate_sub_rule(sub_rule):
    return sub_rule in DUPLICATE_SUB_RULES


def rule_profile(sub_rule):
    if not is_duplicate_sub_rule(sub_rule):
        raise ValueError("不是川麻双打复式子规则")
    bloodflow = sub_rule == XUELIU_DUPLICATE
    return {
        "version": "xueliu-duplicate-v3" if bloodflow else "sbr2025-duplicate-v1",
        "format": "paired_tables_2v2", "sub_rule": sub_rule,
        "scoring": "raw_difference" if bloodflow else "imp",
        "imp_scale": "jj" if bloodflow else "sbr",
        "board_score_cap": 80 if bloodflow else None,
        "tied_match": "extra_board_until_decided" if bloodflow else "draw",
        "wall_policy": "fixed8_then_common_ESWN" if bloodflow else "own_wall_then_retired_partner",
        "wall_policy_source": "jj" if bloodflow else "sbr2025",
        "wall_layout": "xueliu" if bloodflow else "standard",
        "initial_hand_count": 13, "initial_wall_count": 8 if bloodflow else 14,
        "initial_common_count": 24 if bloodflow else 0,
        "supplement_draw": "front", "dealer_policy": "fixed_ESWN",
        "active_flower_pig_penalty": None if bloodflow else 12,
    }


def team_by_seat(table):
    if table not in ("open", "closed"):
        raise ValueError("复式桌号必须为开室或闭室")
    return [0, 1, 0, 1] if table == "open" else [1, 0, 1, 0]


def make_board(seed, number, *, layout="standard"):
    tiles = list(TILES)
    random.Random(str(seed) + ":" + str(number)).shuffle(tiles)
    return board_from_tiles(tiles, number, layout=layout)


def board_from_tiles(tiles, number, *, layout="xueliu"):
    if layout not in ("standard", "xueliu"):
        raise ValueError("未知复式牌山布局")
    size = 21 if layout == "xueliu" else 27
    board = {"number": number, "dealer": (number - 1) % 4,
             "hands": [list(tiles[i * size:i * size + 13]) for i in range(4)],
             "walls": [list(tiles[i * size + 13:(i + 1) * size]) for i in range(4)]}
    if layout == "xueliu":
        board.update(wall_layout="xueliu", common_wall=list(tiles[84:]))
    validate_board(board)
    return board


def flatten_board(board):
    return [t for i in range(4) for t in board["hands"][i] + board["walls"][i]] + list(board.get("common_wall", []))


def validate_board(board):
    if not isinstance(board, dict) or type(board.get("number")) is not int or board["number"] < 1:
        raise ValueError("复式副号无效")
    if type(board.get("dealer")) is not int or board["dealer"] != (board["number"] - 1) % 4:
        raise ValueError("复式庄位与副号不一致")
    layout = board.get("wall_layout", "standard")
    if layout not in ("standard", "xueliu"):
        raise ValueError("未知复式牌山布局")
    for name, size in (("hands", 13), ("walls", 8 if layout == "xueliu" else 14)):
        groups = board.get(name)
        if not isinstance(groups, list) or len(groups) != 4 or any(
                not isinstance(g, list) or len(g) != size or any(type(t) is not int for t in g) for g in groups):
            raise ValueError("复式起手或牌墙数据不完整")
    common = board.get("common_wall", [])
    if not isinstance(common, list) or len(common) != (24 if layout == "xueliu" else 0) or any(type(t) is not int for t in common):
        raise ValueError("复式公共牌墙数据不完整")
    if Counter(flatten_board(board)) != Counter(TILES):
        raise ValueError("复式牌组必须为 108 张川麻数牌")


def imp(points, *, scale="sbr"):
    if type(points) is not int or points < 0:
        raise ValueError("IMP 输入必须为非负整数副分")
    if scale not in ("sbr", "jj"):
        raise ValueError("未知 IMP 换算表")
    bounds = JJ_IMP_UPPER_BOUNDS if scale == "jj" else IMP_UPPER_BOUNDS
    return next((i for i, upper in enumerate(bounds) if points <= upper), len(bounds))


def score_board(open_scores, closed_scores, *, scoring, imp_scale="sbr", board_score_cap=None):
    """Inputs are global team deltas, already including pair adjustments.

    Never subtract the opponent's zero-sum result a second time.
    """
    if scoring not in ("imp", "raw_difference"):
        raise ValueError("未知复式计分方式")
    if board_score_cap is not None and (type(board_score_cap) is not int or board_score_cap < 1):
        raise ValueError("副分封顶必须为正整数")
    if any(len(s) != 2 or any(type(n) is not int for n in s) or sum(s) != 0
           for s in (open_scores, closed_scores)):
        raise ValueError("复式两队副分必须守恒")
    raw = [open_scores[i] + closed_scores[i] for i in range(2)]
    positive = [max(0, n) for n in raw]
    small = [min(n, board_score_cap) if board_score_cap is not None else n for n in positive]
    large = [imp(n, scale=imp_scale) for n in positive]
    return {"raw_difference": raw, "small_scores": small, "imp_scores": large,
            "scores": list(large if scoring == "imp" else small)}


def score_totals(results, *, imp_scale):
    """Sum per-board conversions, including snapshots written before dual scoring."""
    small, large = [0, 0], [0, 0]
    for result in results:
        positive = [max(0, n) for n in result["raw_difference"]]
        board_small = result.get("small_scores", positive)
        board_imp = result.get("imp_scores")
        if board_imp is None:
            board_imp = [imp(n, scale=imp_scale) for n in positive]
        for team in (0, 1):
            small[team] += board_small[team]
            large[team] += board_imp[team]
    return {"small_scores": small, "imp_scores": large}
