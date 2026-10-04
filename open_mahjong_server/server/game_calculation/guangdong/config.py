"""MIL 广东 2023 固定规则及原书七-2允许的推广赛事起和选项。"""

SUB_RULE = "guangdong/mil2023"
EDITION = "mil-guangdong-2023-om1"
TILES = tuple(suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10)) + tuple(range(41, 48))
GHOSTS = (55, 56, 57, 58)
PHYSICAL_TILES = TILES + GHOSTS
NUMBERS = frozenset(t for t in TILES if t < 40)
WINDS = frozenset(range(41, 45))
DRAGONS = frozenset(range(45, 48))
HONORS = WINDS | DRAGONS
ORPHANS = HONORS | frozenset((11, 19, 21, 29, 31, 39))
DEFAULT_CONFIG = {
    "edition": EDITION,
    "wildcards": True,
    "minimum_fan": 2,
    "minimum_score": 4,
    "horse_count": 4,
    "require_minimum_score": True,
}


def normalize_config(raw=None):
    if raw is None:
        return dict(DEFAULT_CONFIG)
    if not isinstance(raw, dict) or set(raw) - DEFAULT_CONFIG.keys():
        raise ValueError("广东 MIL 2023 配置含未知项目")
    result = dict(DEFAULT_CONFIG)
    for key, value in raw.items():
        if type(value) is not type(DEFAULT_CONFIG[key]):
            raise ValueError(f"广东配置 {key} 类型不正确")
        if key != "require_minimum_score" and value != DEFAULT_CONFIG[key]:
            raise ValueError(f"广东 MIL 2023 的 {key} 为固定规则")
        result[key] = value
    return result


def wall_tiles():
    """四种花鬼各一张，绝不走补花牌池。"""
    return [tile for tile in TILES for _ in range(4)] + list(GHOSTS)
