"""
日麻内部牌 id 与 mahjong 库编码互转

内部牌 id 约定：
- 11-19 万 / 21-29 饼 / 31-39 条 / 41-44 东南西北 / 45-47 中白发
- 105 / 205 / 305 分别代表赤 5 万 / 赤 5 饼 / 赤 5 条，结算归一化为普通 5m/5p/5s 并单独计入 aka dora 计数。

mahjong 库使用 34 编号：
- 0-8 万（1m..9m） 9-17 饼（1p..9p） 18-26 条（1s..9s） 27-33 字牌（东 27, 南 28, 西 29, 北 30, 白 31, 发 32, 中 33）

内部字牌对应：
- 41 东(27) 42 南(28) 43 西(29) 44 北(30) 45 中(33) 46 白(31) 47 发(32)
"""
from typing import Dict, List, Tuple

from mahjong.tile import TilesConverter


def _normalize(tile: int) -> int:
    if tile == 105:
        return 15
    if tile == 205:
        return 25
    if tile == 305:
        return 35
    return tile


def tile_id_to_34(tile: int) -> int:
    """内部 id -> mahjong 34 编码"""
    tile = _normalize(tile)
    if 11 <= tile <= 19:
        return tile - 11
    if 21 <= tile <= 29:
        return 9 + (tile - 21)
    if 31 <= tile <= 39:
        return 18 + (tile - 31)
    if tile == 41:
        return 27
    if tile == 42:
        return 28
    if tile == 43:
        return 29
    if tile == 44:
        return 30
    if tile == 45:
        return 33
    if tile == 46:
        return 31
    if tile == 47:
        return 32
    raise ValueError(f"未知的牌 id: {tile}")


def tile_34_to_id(t34: int) -> int:
    """mahjong 34 编码 -> 内部普通 id（不包含赤 5 信息）"""
    if 0 <= t34 <= 8:
        return 11 + t34
    if 9 <= t34 <= 17:
        return 21 + (t34 - 9)
    if 18 <= t34 <= 26:
        return 31 + (t34 - 18)
    if t34 == 27:
        return 41
    if t34 == 28:
        return 42
    if t34 == 29:
        return 43
    if t34 == 30:
        return 44
    if t34 == 31:
        return 46
    if t34 == 32:
        return 47
    if t34 == 33:
        return 45
    raise ValueError(f"未知的 34 编码: {t34}")


def count_aka(tiles: List[int]) -> int:
    """统计赤宝牌数量"""
    return sum(1 for t in tiles if t in (105, 205, 305))


def tiles_to_136(tiles: List[int], has_aka: bool = True) -> Tuple[List[int], List[int]]:
    """
    将内部 id 列表转换为 mahjong 库的 136 张编码列表；返回 (tiles_136, aka_136)
    mahjong 库的 136 编码：34*4 + 偏移。库里每种花色的第 16 索引 (4m / 5m / 5p / 5s 对应的 4 张中某张) 作为赤宝牌。
    我们直接使用库的 TilesConverter.string_to_136_array 风格来保持兼容。
    """
    man = ""
    pin = ""
    sou = ""
    honors = ""
    aka_flags = {"m": False, "p": False, "s": False}
    for t in tiles:
        if t == 105:
            man += "0"
            aka_flags["m"] = True
            continue
        if t == 205:
            pin += "0"
            aka_flags["p"] = True
            continue
        if t == 305:
            sou += "0"
            aka_flags["s"] = True
            continue
        if 11 <= t <= 19:
            man += str(t - 10)
        elif 21 <= t <= 29:
            pin += str(t - 20)
        elif 31 <= t <= 39:
            sou += str(t - 30)
        elif t == 41:
            honors += "1"
        elif t == 42:
            honors += "2"
        elif t == 43:
            honors += "3"
        elif t == 44:
            honors += "4"
        elif t == 45:
            honors += "7"
        elif t == 46:
            honors += "5"
        elif t == 47:
            honors += "6"
    tiles136 = TilesConverter.string_to_136_array(
        man=man, pin=pin, sou=sou, honors=honors, has_aka_dora=True
    )
    return tiles136


def tile_id_to_136_single(tile: int) -> int:
    """单张内部 id -> 136 索引（用于作为 win_tile 传入 HandCalculator）"""
    if tile == 105:
        return TilesConverter.string_to_136_array(man="0", has_aka_dora=True)[0]
    if tile == 205:
        return TilesConverter.string_to_136_array(pin="0", has_aka_dora=True)[0]
    if tile == 305:
        return TilesConverter.string_to_136_array(sou="0", has_aka_dora=True)[0]
    normal = _normalize(tile)
    if 11 <= normal <= 19:
        return TilesConverter.string_to_136_array(man=str(normal - 10))[0]
    if 21 <= normal <= 29:
        return TilesConverter.string_to_136_array(pin=str(normal - 20))[0]
    if 31 <= normal <= 39:
        return TilesConverter.string_to_136_array(sou=str(normal - 30))[0]
    honor_map = {41: "1", 42: "2", 43: "3", 44: "4", 45: "7", 46: "5", 47: "6"}
    return TilesConverter.string_to_136_array(honors=honor_map[normal])[0]


def convert_combination(combination: str) -> Tuple[str, List[int]]:
    """
    将组合字符串（如 "k12"、"s23"、"g41"、"G41"）转为 (meld_type, tile_ids)
    meld_type ∈ {"chi", "pon", "kan_open", "kan_added", "kan_closed"}
    - 小写 s = 吃（顺子） 小写 k = 碰（刻） 小写 g = 明杠 大写 G = 暗杠
    - k<tile> 若后续升为 g<tile>（加杠），在 combination_tiles 中会体现为 g<tile>；
      如果服务端以 Riichi 语义细分，可扩展再此处。
    """
    sign = combination[0]
    tile = int(combination[1:])
    if sign == "s":
        return "chi", [tile - 1, tile, tile + 1]
    if sign == "k":
        return "pon", [tile, tile, tile]
    if sign == "g":
        return "kan_open", [tile, tile, tile, tile]
    if sign == "G":
        return "kan_closed", [tile, tile, tile, tile]
    raise ValueError(f"未知的组合字符串: {combination}")


def wind_to_tile_index(wind: int) -> int:
    """门风/场风索引 (0=东 1=南 2=西 3=北) -> mahjong 34 编码"""
    return 27 + wind  # EAST=27 SOUTH=28 WEST=29 NORTH=30
