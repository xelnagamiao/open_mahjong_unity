"""
提供给 Web 前端的国标计算、听牌、牌理等 HTTP 接口（无副作用）。
由 server 在创建 GameServer 后调用 register_calc_routes(app, game_server) 挂载。
"""
import logging
from typing import List, Dict, Any

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field


class GBCalcRequest(BaseModel):
    hand_tiles: List[int] = Field(..., description="手牌（包含和牌张）")
    tiles_combination: List[str] = Field(default_factory=list, description="副露列表")
    way_to_hepai: List[str] = Field(default_factory=list, description="和牌方式标记")
    get_tile: int = Field(..., description="和牌张")
    flower_tiles: List[int] = Field(default_factory=list, description="花牌")


class GBTingpaiRequest(BaseModel):
    hand_tiles: List[int] = Field(..., description="手牌")
    tiles_combination: List[str] = Field(default_factory=list, description="副露列表")


class PailiRequest(BaseModel):
    hand_tiles: List[int] = Field(..., description="完整手牌")
    tiles_combination: List[str] = Field(default_factory=list, description="副露列表")


_ALL_RIICHI_TILE_IDS = (
    list(range(11, 20))
    + list(range(21, 30))
    + list(range(31, 40))
    + [41, 42, 43, 44, 45, 46, 47]
)


def _augment_way_with_flowers(way_to_hepai: List[str], flower_tiles: List[int]) -> List[str]:
    augmented = list(way_to_hepai)
    for tile in flower_tiles:
        if 50 < tile < 60:
            augmented.append("花牌")
    return augmented


def _build_paili_response(hand_tiles: List[int], tiles_combination: List[str]) -> Dict[str, Any]:
    from ..gamestate.public.ai.smart_bot_logic import (
        calc_shanten,
        count_melds,
        hand_to_34array,
        normalize_tile,
        tile_to_34,
        combination_to_34array,
    )

    meld_count = count_melds(tiles_combination)
    hand_len = len(hand_tiles)
    if meld_count * 3 + hand_len not in (13, 14):
        raise HTTPException(status_code=400, detail="手牌总数应为 13 或 14（含副露占位）")

    used_34 = hand_to_34array(hand_tiles)
    meld_used_34 = combination_to_34array(tiles_combination)
    visible_34 = [used_34[i] + meld_used_34[i] for i in range(34)]

    if hand_len + meld_count * 3 == 13:
        base_shanten = calc_shanten(hand_tiles, meld_count)
        accept = []
        for tid in _ALL_RIICHI_TILE_IDS:
            idx = tile_to_34(tid)
            remaining = 4 - visible_34[idx]
            if remaining <= 0:
                continue
            test = hand_tiles + [tid]
            new_s = calc_shanten(test, meld_count)
            if new_s < base_shanten:
                accept.append({"tile": tid, "remaining": remaining})
        accept.sort(key=lambda x: x["tile"])
        return {
            "success": True,
            "mode": "shanten",
            "shanten": base_shanten,
            "is_tingpai": base_shanten == 0,
            "accept": accept,
            "total_accept": sum(a["remaining"] for a in accept),
        }

    discards = []
    seen = set()
    for i, tid in enumerate(hand_tiles):
        norm = normalize_tile(tid)
        if norm in seen:
            continue
        seen.add(norm)
        remaining_hand = hand_tiles[:i] + hand_tiles[i + 1:]
        new_shanten = calc_shanten(remaining_hand, meld_count)
        rem_used_34 = hand_to_34array(remaining_hand)
        rem_visible_34 = [rem_used_34[k] + meld_used_34[k] for k in range(34)]
        accept = []
        for cand in _ALL_RIICHI_TILE_IDS:
            idx = tile_to_34(cand)
            remaining = 4 - rem_visible_34[idx]
            if remaining <= 0:
                continue
            test_hand = remaining_hand + [cand]
            test_shanten = calc_shanten(test_hand, meld_count)
            if test_shanten < new_shanten:
                accept.append({"tile": cand, "remaining": remaining})
        accept.sort(key=lambda x: x["tile"])
        discards.append({
            "discard": tid,
            "shanten": new_shanten,
            "accept": accept,
            "total_accept": sum(a["remaining"] for a in accept),
        })

    discards.sort(key=lambda x: (x["shanten"], -x["total_accept"], x["discard"]))
    best_shanten = discards[0]["shanten"] if discards else 0
    return {
        "success": True,
        "mode": "discard",
        "best_shanten": best_shanten,
        "discards": discards,
    }


def _augment_way_with_he_dan_zhang(
    way_to_hepai: List[str],
    hand_tiles: List[int],
    get_tile: int,
    tiles_combination: List[str],
    calc_service,
) -> List[str]:
    """前 13 张听牌检测：待牌唯一时自动追加和单张（与对局逻辑一致）。"""
    augmented = list(way_to_hepai)
    if "和单张" in augmented:
        return augmented
    hand_13 = list(hand_tiles)
    if get_tile in hand_13:
        hand_13.remove(get_tile)
    elif len(hand_13) > 13:
        hand_13 = hand_13[:13]
    try:
        waiting = calc_service.GB_tingpai_check(hand_13, list(tiles_combination))
        if len(waiting) == 1:
            augmented.append("和单张")
    except Exception:
        logging.debug("和单张自动判定跳过", exc_info=True)
    return augmented


def register_calc_routes(app: FastAPI, game_server) -> None:
    """将 /calc/* 路由挂到已创建的 FastAPI app 上。"""
    calc = game_server.calculation_service

    @app.post("/calc/gb/score")
    async def calc_gb_score(req: GBCalcRequest):
        try:
            way = _augment_way_with_flowers(req.way_to_hepai, req.flower_tiles)
            way = _augment_way_with_he_dan_zhang(
                way, list(req.hand_tiles), req.get_tile, req.tiles_combination, calc
            )
            score, fan_list = calc.GB_hepai_check(
                list(req.hand_tiles),
                list(req.tiles_combination),
                way,
                req.get_tile,
            )
            return {
                "success": True,
                "score": score,
                "fan_list": fan_list,
                "is_hepai": score > 0,
            }
        except IndexError:
            return {
                "success": True,
                "score": 0,
                "fan_list": [],
                "is_hepai": False,
                "message": "该牌型不能和牌",
            }
        except Exception as exc:
            logging.exception("国标算分接口异常")
            raise HTTPException(status_code=400, detail=f"计算失败: {exc}")

    @app.post("/calc/gb/decompose")
    async def calc_gb_decompose(req: GBCalcRequest):
        try:
            way = _augment_way_with_flowers(req.way_to_hepai, req.flower_tiles)
            way = _augment_way_with_he_dan_zhang(
                way, list(req.hand_tiles), req.get_tile, req.tiles_combination, calc
            )
            decompositions = calc.GB_hepai_decompose(
                list(req.hand_tiles),
                list(req.tiles_combination),
                way,
                req.get_tile,
            )
            return {
                "success": True,
                "is_hepai": len(decompositions) > 0,
                "decompositions": decompositions,
            }
        except Exception as exc:
            logging.exception("国标拆解接口异常")
            raise HTTPException(status_code=400, detail=f"计算失败: {exc}")

    @app.post("/calc/gb/tingpai")
    async def calc_gb_tingpai(req: GBTingpaiRequest):
        try:
            waiting = calc.GB_tingpai_check(
                list(req.hand_tiles),
                list(req.tiles_combination),
            )
            waiting_sorted = sorted(int(t) for t in waiting)
            return {
                "success": True,
                "is_tingpai": len(waiting_sorted) > 0,
                "waiting_tiles": waiting_sorted,
            }
        except Exception as exc:
            logging.exception("国标听牌接口异常")
            raise HTTPException(status_code=400, detail=f"计算失败: {exc}")

    @app.post("/calc/paili")
    async def calc_paili(req: PailiRequest):
        try:
            return _build_paili_response(list(req.hand_tiles), list(req.tiles_combination))
        except HTTPException:
            raise
        except Exception as exc:
            logging.exception("牌理接口异常")
            raise HTTPException(status_code=400, detail=f"计算失败: {exc}")
