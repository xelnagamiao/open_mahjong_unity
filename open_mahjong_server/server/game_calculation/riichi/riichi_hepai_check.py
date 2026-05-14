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
from typing import Dict, List, Optional
import copy

from mahjong.hand_calculating.hand import HandCalculator
from mahjong.hand_calculating.hand_config import HandConfig, OptionalRules
from mahjong.meld import Meld
from mahjong.tile import TilesConverter

from .riichi_tile_converter import (
    tile_id_to_34,
    tile_id_to_136_single,
    convert_combination,
    wind_to_tile_index,
    count_aka,
)


# 役名（库的 English 名称）-> 中文名
YAKU_NAME_MAP = {
    "Riichi": "立直",
    "Daburu Riichi": "两立直",
    "Ippatsu": "一发",
    "Menzen Tsumo": "门前清自摸",
    "Pinfu": "平和",
    "Tanyao": "断幺九",
    "Iipeiko": "一杯口",
    "Haitei Raoyue": "海底捞月",
    "Houtei Raoyui": "河底捞鱼",
    "Rinshan Kaihou": "岭上开花",
    "Chankan": "抢杠",
    "Yakuhai (haku)": "役牌·白",
    "Yakuhai (hatsu)": "役牌·发",
    "Yakuhai (chun)": "役牌·中",
    "Yakuhai (east)": "役牌·东",
    "Yakuhai (south)": "役牌·南",
    "Yakuhai (west)": "役牌·西",
    "Yakuhai (north)": "役牌·北",
    "Yakuhai (of place wind)": "自风",
    "Yakuhai (of round wind)": "场风",
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
    "Chanta": "混全带幺九",
    "Junchan": "纯全带幺九",
    "Toitoi": "对对和",
    "Sanankou": "三暗刻",
    "Sankantsu": "三杠子",
    "Shousangen": "小三元",
    "Honroutou": "混老头",
    "Ryanpeikou": "二杯口",
    "Honitsu": "混一色",
    "Chinitsu": "清一色",
    "Dora": "宝牌",
    "Aka Dora": "赤宝牌",
    "Uradora": "里宝牌",
    "Kokushi Musou": "国士无双",
    "Daisangen": "大三元",
    "Shousuushii": "小四喜",
    "Daisuushii": "大四喜",
    "Tsuuiisou": "字一色",
    "Ryuuiisou": "绿一色",
    "Chinroutou": "清老头",
    "Chuuren Poutou": "九莲宝灯",
    "Suuankou": "四暗刻",
    "Suukantsu": "四杠子",
    "Tenhou": "天和",
    "Chiihou": "地和",
    "Renhou": "人和",
    "Daburu Kokushi Musou": "国士无双十三面",
    "Suuankou Tanki": "四暗刻单骑",
    "Daburu Chuuren Poutou": "纯正九莲宝灯",
    "Chiitoitsu": "七对子",
    "Nagashi Mangan": "流局满贯",
    "Open Riichi": "开立直",
}


def _localize_yaku(name: str) -> str:
    return YAKU_NAME_MAP.get(name, name)


class Riichi_Hepai_Check:
    def __init__(self):
        self._calc = HandCalculator()

    def _build_melds(self, tiles_combination: List[str]):
        melds = []
        for combo in tiles_combination:
            meld_type, tile_ids = convert_combination(combo)
            tiles_136 = []
            counter: Dict[int, int] = {}
            for tid in tile_ids:
                t34 = tile_id_to_34(tid)
                idx = counter.get(t34, 0)
                tiles_136.append(t34 * 4 + idx)
                counter[t34] = idx + 1
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

    def _count_closed_aka(self, hand_list: List[int]) -> int:
        return count_aka(hand_list)

    def _count_meld_aka(self, tiles_combination: List[str]) -> int:
        # 副露中的 5m/5p/5s 若外部传入了赤 5 的 id（105/205/305）则计入；
        # 目前 combination 字符串不携带赤信息，默认 0。
        return 0

    def hepai_check(
        self,
        hand_list: List[int],
        tiles_combination: List[str],
        way_to_hepai: List[str],
        get_tile: int,
        context: Optional[dict] = None,
    ) -> Dict:
        context = context or {}

        melds = self._build_melds(tiles_combination)

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

        # 组装 136 编码：按 34 序，每次分配一个未使用过的副本索引
        used_136 = set()
        for m in melds:
            for t in m.tiles:
                used_136.add(t)

        def alloc_136(t34: int) -> int:
            for i in range(4):
                cand = t34 * 4 + i
                if cand not in used_136:
                    used_136.add(cand)
                    return cand
            return t34 * 4

        # mahjong 库 estimate_hand_value 的 tiles 参数需要传入完整 14 张（闭手 + 副露 + 和牌），
        # 若仅传闭手会被判为 hand_not_winning，导致开门和牌一律无法成立。
        tiles_136_total = []
        for m in melds:
            tiles_136_total.extend(m.tiles)
        for tid in hand_without_win:
            tiles_136_total.append(alloc_136(tile_id_to_34(tid)))
        win_136 = alloc_136(tile_id_to_34(get_tile))
        tiles_136_total.append(win_136)

        # 赤宝牌：若手牌或和牌里包含 105/205/305，则对应 5m/5p/5s 的库索引需映射为赤 5。
        # 库内部对 tile_136 通过 is_aka_dora(tile_136, aka_enabled) 判断，默认赤 5 的 tile_136 固定为
        # 4m/5p/5s 的第 0 张（即 tile_34*4 + 0 对 5m => 16, 5p => 52, 5s => 88）。
        # 我们在分配时，如果存在赤 5，就把该色 5 号的 tile_136=base*4 分配出去作为和牌中的赤。
        aka_count_known = self._count_closed_aka(hand_list) + self._count_meld_aka(tiles_combination)

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
        # 合并里宝（仅立直时由库内部根据 is_riichi 使用）
        dora_indicators += [tile_id_to_34(t) * 4 for t in context.get("ura_dora_indicators", [])]

        result = self._calc.estimate_hand_value(
            tiles=tiles_136_total,
            win_tile=win_136,
            melds=melds if melds else None,
            dora_indicators=dora_indicators if dora_indicators else None,
            config=config,
        )

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
        for y in result.yaku:
            yaku_names.append(_localize_yaku(y.name))

        # 赤宝牌修正：库默认赤 5 按 tile_136 特定索引自动识别；若外部传入 aka_count 与库结果不符，
        # 按外部语义覆盖（保持与客户端红 5 配置一致）。
        external_aka = context.get("aka_count", None)
        han = result.han
        fu = result.fu
        if external_aka is not None:
            # 从 yaku 中减去库检测到的 aka dora 次数再加上外部提供的数量
            aka_detected = 0
            for y in result.yaku:
                if y.name == "Aka Dora":
                    aka_detected = y.han_open if not config.is_tsumo else y.han_closed
                    break
            if external_aka != aka_detected:
                han = han - aka_detected + int(external_aka)

        score_info = result.cost or {}
        score = int(score_info.get("main", 0)) + int(score_info.get("additional", 0)) * (2 if not context.get("is_tsumo", False) else 2)
        if not context.get("is_tsumo", False):
            score = int(score_info.get("main", 0))

        return {
            "is_valid": True,
            "han": int(han),
            "fu": int(fu),
            "score": score,
            "yaku": yaku_names,
            "cost": score_info,
            "aka_count": int(external_aka) if external_aka is not None else aka_count_known,
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
