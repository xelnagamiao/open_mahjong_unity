"""
提供给 Web 前端的国标计算、听牌等 HTTP 接口（无副作用）。
由 server 在创建 GameServer 后调用 register_calc_routes(app, game_server) 挂载。
"""
import logging
from typing import List, Literal

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field, StrictBool


class GBCalcRequest(BaseModel):
    hand_tiles: List[int] = Field(..., description="手牌（包含和牌张）")
    tiles_combination: List[str] = Field(default_factory=list, description="副露列表")
    way_to_hepai: List[str] = Field(default_factory=list, description="和牌方式标记")
    get_tile: int = Field(..., description="和牌张")
    flower_tiles: List[int] = Field(default_factory=list, description="花牌")


class GBTingpaiRequest(BaseModel):
    hand_tiles: List[int] = Field(..., description="手牌")
    tiles_combination: List[str] = Field(default_factory=list, description="副露列表")


class FourJokersWaitRequest(BaseModel):
    hand_tiles: List[int] = Field(..., max_items=17)
    tiles_combination: List[str] = Field(default_factory=list, max_items=5)
    sub_rule: Literal["guobiao/four_jokers", "guobiao/huahun", "guobiao/sixteen"] = "guobiao/four_jokers"


class FourJokersHintRequest(FourJokersWaitRequest):
    ron_way: List[str] = Field(default_factory=list, max_items=20)
    self_way: List[str] = Field(default_factory=list, max_items=20)
    get_tile: int
    hepai_limit: int = Field(8, ge=1, le=64)
    joker_multi_wait: StrictBool = False
    allow_single_wait: StrictBool = True
    no_joker_bonus: Literal["add8", "double", "none"] = "add8"
    black_flower_progressive: StrictBool = True


def _augment_way_with_flowers(way_to_hepai: List[str], flower_tiles: List[int]) -> List[str]:
    augmented = list(way_to_hepai)
    for tile in flower_tiles:
        if 50 < tile < 60:
            augmented.append("花牌")
    return augmented



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

    @app.post('/calc/gb/sixteen/waits')
    @app.post('/calc/gb/four-jokers/waits')
    async def four_jokers_waits(req: FourJokersWaitRequest):
        from ..game_calculation.guobiao_four_jokers import hint_snapshot
        if req.sub_rule == "guobiao/huahun":
            from ..game_calculation.guobiao_huahun import hint_snapshot
        if req.sub_rule == "guobiao/sixteen":
            from ..game_calculation.guobiao_sixteen import hint_snapshot
        from ..gamestate.public.ai.bot_executor import run_room_bot_cpu
        result = await run_room_bot_cpu(game_server, hint_snapshot, req.hand_tiles, req.tiles_combination)
        result["sub_rule"] = req.sub_rule
        return result

    @app.post('/calc/gb/sixteen/hint')
    @app.post('/calc/gb/four-jokers/hint')
    async def four_jokers_hint(req: FourJokersHintRequest):
        from ..game_calculation.guobiao_four_jokers import calculate_hint
        from ..gamestate.public.ai.bot_executor import run_room_bot_cpu
        if req.sub_rule == "guobiao/sixteen":
            from ..game_calculation.guobiao_sixteen import calculate_hint
            return await run_room_bot_cpu(game_server, calculate_hint, req.hand_tiles, req.tiles_combination,
                req.ron_way, req.self_way, req.get_tile, req.hepai_limit, req.allow_single_wait)
        if req.sub_rule == "guobiao/huahun":
            from ..game_calculation.guobiao_huahun import calculate_hint
            return await run_room_bot_cpu(game_server, calculate_hint, req.hand_tiles, req.tiles_combination,
                req.ron_way, req.self_way, req.get_tile, req.hepai_limit, req.joker_multi_wait,
                req.allow_single_wait, req.no_joker_bonus, req.black_flower_progressive)
        return await run_room_bot_cpu(game_server, calculate_hint, req.hand_tiles, req.tiles_combination,
                                     req.ron_way, req.self_way, req.get_tile, req.hepai_limit,
                                     req.joker_multi_wait, req.allow_single_wait)

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
