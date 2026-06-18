"""
立直麻将和牌检查（基于 mahjong 库）

外部接口：
    Riichi_Hepai_Check().hepai_check(hand_list, tiles_combination, way_to_hepai, get_tile, context) -> dict

输入：
- hand_list: List[int] 玩家闭手（含得牌），使用内部 id（含赤 5 的 105/205/305）
- tiles_combination: List[str] 副露组合，如 "s13"/"k12"/"g41"/"G41"
- way_to_hepai: List[str] 和牌方式标签（兼容项目其他脚本）
- get_tile: int 和牌的牌 id
- context: dict 提供立直/场况相关上下文，字段：
    - is_tsumo: bool
    - is_riichi: bool
    - is_daburu_riichi: bool
    - is_ippatsu: bool
    - is_rinshan: bool
    - is_chankan: bool
    - is_haitei: bool
    - is_houtei: bool
    - is_tenhou: bool
    - is_chiihou: bool
    - is_dealer: bool
    - player_wind: int  0=东 1=南 2=西 3=北（门风）
    - round_wind: int  0=东 1=南（圈风）
    - dora_indicators: List[int]  宝牌指示牌（含杠宝牌）
    - ura_dora_indicators: List[int]  里宝牌指示牌
    - has_open_tanyao: bool  是否开启食断
    - aka_count: int  赤宝牌数量（由外部按需求传入；若为 None 则从手牌推断）

返回值：
{
    "is_valid": bool,
    "han": int,
    "fu": int,
    "score": int,        # 和牌者从敌方总收分（不含本场/供托）
    "yaku": [str, ...],  # 役名（中文）
    "error": str or None,
}
"""
from typing import List, Optional
import copy

from mahjong.hand_calculating.hand import HandCalculator
from mahjong.hand_calculating.hand_config import HandConfig, OptionalRules
from mahjong.meld import Meld

from mahjong.hand_calculating.scores import ScoresCalculator

from .riichi_tile_converter import (
    tile_id_to_34,
    convert_combination,
    wind_to_tile_index,
    count_aka_in_tiles,
    count_dora_in_tiles,
    tiles_from_mask,
    alloc_136_for_tile,
)


# 役名（库的 English 名称）-> 中文名
YAKU_NAME_MAP = {
    "Riichi": "立直",
    "Double Riichi": "双立直",
    "Daburu Riichi": "双立直",
    "Ippatsu": "一发",
    "Menzen Tsumo": "门前清自摸和",
    "Pinfu": "平和",
    "Tanyao": "断幺九",
    "Iipeiko": "一杯口",
    "Haitei Raoyue": "海底捞月",
    "Houtei Raoyui": "河底捞鱼",
    "Rinshan Kaihou": "岭上开花",
    "Chankan": "枪杠",
    "Yakuhai (haku)": "役牌·白",
    "Yakuhai (hatsu)": "役牌·发",
    "Yakuhai (chun)": "役牌·中",
    "Yakuhai (seat wind east)": "自风·东",
    "Yakuhai (seat wind south)": "自风·南",
    "Yakuhai (seat wind west)": "自风·西",
    "Yakuhai (seat wind north)": "自风·北",
    "Yakuhai (round wind east)": "场风·东",
    "Yakuhai (round wind south)": "场风·南",
    "Yakuhai (round wind west)": "场风·西",
    "Yakuhai (round wind north)": "场风·北",
    "East": "东",
    "South": "南",
    "West": "西",
    "North": "北",
    "Haku": "白",
    "Hatsu": "发",
    "Chun": "中",
    "Sanshoku Doujun": "三色同顺",
    "Sanshoku Doukou": "三色同刻",
    "Ittsu": "一气通贯",
    "Chantai": "混全带幺九",
    "Chanta": "混全带幺九",
    "Junchan": "纯全带幺九",
    "Toitoi": "对对和",
    "San Ankou": "三暗刻",
    "Sanankou": "三暗刻",
    "San Kantsu": "三杠子",
    "Sankantsu": "三杠子",
    "Shou Sangen": "小三元",
    "Shousangen": "小三元",
    "Honroutou": "混老头",
    "Ryanpeikou": "二杯口",
    "Honitsu": "混一色",
    "Chinitsu": "清一色",
    "Dora": "宝牌",
    "Aka Dora": "赤宝牌",
    "Ura Dora": "里宝牌",
    "Uradora": "里宝牌",
    "Kokushi Musou": "国士无双",
    "Daisangen": "大三元",
    "Shousuushii": "小四喜",
    "Dai Suushii": "大四喜",
    "Daisuushii": "大四喜",
    "Tsuu Iisou": "字一色",
    "Tsuuiisou": "字一色",
    "Ryuuiisou": "绿一色",
    "Chinroutou": "清老头",
    "Chuuren Poutou": "九莲宝灯",
    "Suu Ankou": "四暗刻",
    "Suuankou": "四暗刻",
    "Suu Kantsu": "四杠子",
    "Suukantsu": "四杠子",
    "Tenhou": "天和",
    "Chiihou": "地和",
    "Renhou": "人和",
    "Renhou (yakuman)": "人和",
    "Kokushi Musou Juusanmen Matchi": "国士无双十三面",
    "Daburu Kokushi Musou": "国士无双十三面",
    "Suu Ankou Tanki": "四暗刻单骑",
    "Suuankou Tanki": "四暗刻单骑",
    "Daburu Chuuren Poutou": "纯正九莲宝灯",
    "Chiitoitsu": "七对子",
    "Nagashi Mangan": "流局满贯",
    "Open Riichi": "开立直",
    "Double Open Riichi": "双倍开立直",
    "Daichisei": "大七星",
    "Daisharin": "大车轮",
    "Paarenchan": "八连庄",
    "Sashikomi": "包牌",
}


WIND_TEXT = ["东", "南", "西", "北"]


def _localize_yaku(name: str, context: Optional[dict] = None) -> str:
    context = context or {}
    if name == "Yakuhai (of place wind)":
        return f"自风·{WIND_TEXT[int(context.get('player_wind', 0))]}"
    if name == "Yakuhai (of round wind)":
        return f"场风·{WIND_TEXT[int(context.get('round_wind', 0))]}"
    return YAKU_NAME_MAP.get(name, name)


def _hand_is_open(melds) -> bool:
    return bool(melds) and any(m.opened for m in melds)


# 门清与食下番数不同的役，下发带后缀的役名供客户端字典直接匹配
_YAKU_WITH_OPEN_CLOSED_FORM = frozenset({
    "三色同顺", "一气通贯", "混全带幺九", "纯全带幺九", "混一色", "清一色",
})


def _localize_yaku_display(name: str, context: Optional[dict], is_open: bool) -> str:
    localized = _localize_yaku(name, context)
    if localized in _YAKU_WITH_OPEN_CLOSED_FORM:
        return f"{localized}{'（食下）' if is_open else '（门清）'}"
    return localized


class Riichi_Hepai_Check:
    def __init__(self):
        self._calc = HandCalculator()

    def _build_melds(
        self,
        tiles_combination: List[str],
        combination_masks: Optional[List[List[int]]],
        used_136: set,
    ):
        melds = []
        for i, combo in enumerate(tiles_combination):
            meld_type, tile_ids = convert_combination(combo)
            if combination_masks and i < len(combination_masks):
                mask_tiles = tiles_from_mask(combination_masks[i])
                if mask_tiles:
                    tile_ids = mask_tiles
            # combination_mask 中的牌序取决于鸣牌来源方位（上家在首位/对家居中/下家在末位），
            # 吃副露经常是乱序（如 [37,36,35]）；mahjong 库按升序匹配顺子，
            # 乱序的 Meld 会导致整手被判 hand_not_winning（自摸/荣和全部失败），必须排序。
            tile_ids = sorted(tile_ids, key=tile_id_to_34)
            tiles_136 = []
            for tid in tile_ids:
                tiles_136.append(alloc_136_for_tile(tid, used_136))
            if meld_type == "chi":
                mtype = Meld.CHI
                opened = True
            elif meld_type == "pon":
                mtype = Meld.PON
                opened = True
            elif meld_type == "kan_open":
                mtype = Meld.KAN
                opened = True
            elif meld_type == "kan_closed":
                mtype = Meld.KAN
                opened = False
            else:
                continue
            melds.append(Meld(meld_type=mtype, tiles=tiles_136, opened=opened))
        return melds

    def _collect_all_tile_ids(
        self,
        hand_list: List[int],
        tiles_combination: List[str],
        combination_masks: Optional[List[List[int]]],
    ) -> List[int]:
        all_tile_ids = list(hand_list)
        if combination_masks:
            for mask in combination_masks:
                all_tile_ids.extend(tiles_from_mask(mask))
        else:
            for combo in tiles_combination:
                _, combo_tiles = convert_combination(combo)
                all_tile_ids.extend(combo_tiles)
        return all_tile_ids

    def _lib_bonus_han(self, result_yaku, is_open: bool) -> tuple:
        lib_dora = lib_aka = lib_ura = 0
        for y in result_yaku:
            han = y.han_open if is_open else y.han_closed
            if y.name == "Dora":
                lib_dora = han
            elif y.name == "Aka Dora":
                lib_aka = han
            elif y.name in ("Ura Dora", "Uradora"):
                lib_ura = han
        return lib_dora, lib_aka, lib_ura

    def hepai_check(
        self,
        hand_list: List[int],
        tiles_combination: List[str],
        way_to_hepai: List[str],
        get_tile: int,
        context: Optional[dict] = None,
        combination_masks: Optional[List[List[int]]] = None,
    ) -> dict:
        context = context or {}

        used_136: set = set()
        melds = self._build_melds(tiles_combination, combination_masks, used_136)

        hand_without_win = copy.copy(hand_list)
        try:
            hand_without_win.remove(get_tile)
        except ValueError:
            return {
                "is_valid": False,
                "han": 0,
                "fu": 0,
                "score": 0,
                "yaku": [],
                "error": "get_tile 不在手牌中",
            }

        # 组装 136 编码：按真实牌 ID 分配，普通 5 优先非赤索引
        tiles_136_total = []
        for m in melds:
            tiles_136_total.extend(m.tiles)
        for tid in hand_without_win:
            tiles_136_total.append(alloc_136_for_tile(tid, used_136))
        win_136 = alloc_136_for_tile(get_tile, used_136)
        tiles_136_total.append(win_136)

        config_rules = OptionalRules(
            has_open_tanyao=bool(context.get("has_open_tanyao", True)),
            has_aka_dora=True,
            has_double_yakuman=False,
            kazoe_limit=HandConfig.KAZOE_LIMITED,
            fu_for_open_pinfu=True,
            fu_for_pinfu_tsumo=False,
        )

        player_wind = wind_to_tile_index(int(context.get("player_wind", 0)))
        round_wind = wind_to_tile_index(int(context.get("round_wind", 0)))

        config = HandConfig(
            is_tsumo=bool(context.get("is_tsumo", False)),
            is_riichi=bool(context.get("is_riichi", False)),
            is_daburu_riichi=bool(context.get("is_daburu_riichi", False)),
            is_ippatsu=bool(context.get("is_ippatsu", False)),
            is_rinshan=bool(context.get("is_rinshan", False)),
            is_chankan=bool(context.get("is_chankan", False)),
            is_haitei=bool(context.get("is_haitei", False)),
            is_houtei=bool(context.get("is_houtei", False)),
            is_tenhou=bool(context.get("is_tenhou", False)),
            is_chiihou=bool(context.get("is_chiihou", False)),
            player_wind=player_wind,
            round_wind=round_wind,
            kyoutaku_number=int(context.get("kyoutaku_number", 0)),
            tsumi_number=int(context.get("tsumi_number", 0)),
            options=config_rules,
        )

        dora_indicators = [tile_id_to_34(t) * 4 for t in context.get("dora_indicators", [])]
        ura_dora_indicators = None
        if context.get("is_riichi") and context.get("ura_dora_indicators"):
            ura_dora_indicators = [tile_id_to_34(t) * 4 for t in context.get("ura_dora_indicators", [])]

        result = self._calc.estimate_hand_value(
            tiles=tiles_136_total,
            win_tile=win_136,
            melds=melds if melds else None,
            dora_indicators=dora_indicators if dora_indicators else None,
            ura_dora_indicators=ura_dora_indicators,
            config=config,
        )

        # 牌型成立但无役（如吃了牌却没有任何役、单纯宝牌无役）：日麻规则下不能正常和牌。
        # 不再直接判为非法，而是返回 no_yaku=True 并附宝牌/赤宝/里宝信息，交由上层在开启错和时按"错和"处理。
        # 注意：宝牌/赤宝/里宝不是役，故 han 记 0；错和的展示番数由结算层固定为"错和1番"。
        if result.error == HandCalculator.ERR_NO_YAKU:
            all_tile_ids = self._collect_all_tile_ids(hand_list, tiles_combination, combination_masks)
            aka_count = count_aka_in_tiles(all_tile_ids)
            dora_count = count_dora_in_tiles(all_tile_ids, context.get("dora_indicators", []))
            ura_count = count_dora_in_tiles(all_tile_ids, context.get("ura_dora_indicators", [])) if context.get("is_riichi") else 0
            no_yaku_yaku: List[str] = []
            if dora_count > 0:
                no_yaku_yaku.append(f"宝牌*{dora_count}")
            if ura_count > 0:
                no_yaku_yaku.append(f"里宝牌*{ura_count}")
            if aka_count > 0:
                no_yaku_yaku.append(f"赤宝牌*{aka_count}")
            return {
                "is_valid": True,
                "no_yaku": True,
                "han": 0,
                "fu": 0,
                "score": 0,
                "yaku": no_yaku_yaku,
                "cost": {},
                "aka_count": int(aka_count),
                "error": None,
            }

        if result.error is not None or result.han is None:
            return {
                "is_valid": False,
                "han": 0,
                "fu": 0,
                "score": 0,
                "yaku": [],
                "error": str(result.error) if result.error else "无役",
            }

        yaku_names = []
        is_open = _hand_is_open(melds)
        lib_dora_han, lib_aka_han, lib_ura_han = self._lib_bonus_han(result.yaku, is_open)
        for y in result.yaku:
            if y.name in ("Dora", "Aka Dora", "Ura Dora", "Uradora"):
                continue
            localized = _localize_yaku(y.name, context)
            if localized in ("宝牌", "里宝牌", "赤宝牌"):
                continue
            yaku_names.append(_localize_yaku_display(y.name, context, is_open))

        all_tile_ids = self._collect_all_tile_ids(hand_list, tiles_combination, combination_masks)
        aka_count = count_aka_in_tiles(all_tile_ids)
        dora_count = count_dora_in_tiles(all_tile_ids, context.get("dora_indicators", []))
        ura_count = count_dora_in_tiles(all_tile_ids, context.get("ura_dora_indicators", [])) if context.get("is_riichi") else 0
        if dora_count > 0:
            yaku_names.append(f"宝牌*{dora_count}")
        if ura_count > 0:
            yaku_names.append(f"里宝牌*{ura_count}")
        if aka_count > 0:
            yaku_names.append(f"赤宝牌*{aka_count}")

        han = int(result.han) - lib_dora_han - lib_aka_han - lib_ura_han
        han += dora_count + aka_count + ura_count
        fu = int(result.fu)

        is_yakuman = han >= 13 or any(
            (y.han_open if is_open else y.han_closed) >= 13 for y in result.yaku
            if y.name not in ("Dora", "Aka Dora", "Ura Dora", "Uradora")
        )
        # 库对国士等例外牌型返回 fu=0；展示与七对子一致，役满统一记 25 符（不影响役满固定点数）
        if is_yakuman:
            fu = 25
        score_info = ScoresCalculator.calculate_scores(han, fu, config, is_yakuman)
        if not context.get("is_tsumo", False):
            score = int(score_info.get("main", 0))
        else:
            score = int(score_info.get("main", 0)) + int(score_info.get("additional", 0)) * 2

        return {
            "is_valid": True,
            "han": int(han),
            "fu": int(fu),
            "score": score,
            "yaku": yaku_names,
            "cost": score_info,
            "aka_count": int(aka_count),
            "error": None,
        }


if __name__ == "__main__":
    checker = Riichi_Hepai_Check()
    # 立直+断幺 测试
    hand = [12, 13, 14, 22, 23, 24, 33, 34, 35, 36, 37, 38, 28, 28]
    result = checker.hepai_check(
        hand_list=hand,
        tiles_combination=[],
        way_to_hepai=[],
        get_tile=28,
        context={
            "is_tsumo": True,
            "is_riichi": True,
            "is_dealer": False,
            "player_wind": 1,
            "round_wind": 0,
            "has_open_tanyao": True,
            "dora_indicators": [11],
        },
    )
    print(result)
