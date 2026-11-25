"""Unified, stateless pure-Python calculation dispatch registry.

Every rule family's tingpai / hepai / action-evaluation entry point is
dispatched through this module using immutable primitive / tuple arguments so
the exact same functions execute cleanly in:
  - Python 3.14 InterpreterPoolExecutor (per-interpreter GIL, zero-pipe)
  - ProcessPoolExecutor (spawn)
  - Inline mode (1-core servers)

Each interpreter / worker process lazily initializes its own isolated checker
instances, eliminating the global threading.RLock bottleneck.
"""
from __future__ import annotations

import importlib
import json
from typing import Any, Callable, Dict, Optional, Tuple

# Per-interpreter / per-process lazily cached checker instances (never shared across GILs).
_CHECKERS: Dict[str, Any] = {}


def _get_checker(key: str, factory: Callable[[], Any]) -> Any:
    inst = _CHECKERS.get(key)
    if inst is None:
        inst = factory()
        _CHECKERS[key] = inst
    return inst


def _loads_opt(raw_json: str) -> Any:
    return json.loads(raw_json) if raw_json else None


def _dumps_opt(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False) if obj is not None else ""


# ── Core & Regional Tingpai Dispatch ──────────────────────────────────────────

def tingpai_dispatch(
    rule_key: str,
    hand: Tuple[int, ...],
    combos: Tuple[str, ...],
    extra_json: str = "",
) -> Tuple[int, ...]:
    """Pure, stateless tingpai check safe for InterpreterPoolExecutor.

    Returns a sorted tuple of waiting tile IDs.
    """
    hand_list = list(hand)
    combo_list = list(combos)
    extra = _loads_opt(extra_json)

    if rule_key == "guobiao":
        from .gb_tingpai_check import Chinese_Tingpai_Check
        from .riichi.washizu_tiles import face

        checker = _get_checker("gb_tingpai", Chinese_Tingpai_Check)
        waits = checker.tingpai_check([face(t) for t in hand_list], combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "guobiao/lanshi":
        from .guobiao_lanshi_hepai_check import Lanshi_Hepai_Check

        checker = _get_checker("gb_lanshi", Lanshi_Hepai_Check)
        waits = checker.tingpai_check(hand_list, combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "guobiao/sixteen":
        from .guobiao_sixteen import waiting_tiles

        waits = waiting_tiles(hand_list, combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key in ("guobiao/four_jokers", "guobiao/huahun"):
        if rule_key == "guobiao/huahun":
            from .guobiao_huahun import waiting_tiles
        else:
            from .guobiao_four_jokers import waiting_tiles

        waits = waiting_tiles(hand_list, combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "qingque":
        from .qingque import qingque_tingpai_check

        waits = qingque_tingpai_check(hand_list, combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "riichi":
        from .riichi.riichi_tingpai_check import Riichi_Tingpai_Check

        checker = _get_checker("riichi_tingpai", Riichi_Tingpai_Check)
        waits = checker.tingpai_check(hand_list, combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "classical":
        from .classical.classical_tingpai_check import Classical_Tingpai_Check

        checker = _get_checker("classical_tingpai", Classical_Tingpai_Check)
        waits = checker.tingpai_check(hand_list, combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "sichuan":
        from .sichuan.sichuan_tingpai_check import Sichuan_Tingpai_Check

        checker = _get_checker("sichuan_tingpai", Sichuan_Tingpai_Check)
        waits = checker.tingpai_check(hand_list, combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "sichuan/xueliu":
        from .sichuan.xueliu_hepai_check import Xueliu_Tingpai_Check

        checker = _get_checker("xueliu_tingpai", Xueliu_Tingpai_Check)
        meld_count = int((extra or {}).get("meld_count", 3))
        waits = checker.tingpai_check(hand_list, combo_list, meld_count)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "changsha":
        from .changsha.changsha_tingpai_check import Changsha_Tingpai_Check

        checker = _get_checker("changsha_tingpai", Changsha_Tingpai_Check)
        waits = checker.tingpai_check(hand_list, combo_list)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "taiwan":
        from .taiwan.taiwan_tingpai_check import Taiwan_Tingpai_Check

        checker = _get_checker("taiwan_tingpai", Taiwan_Tingpai_Check)
        waits = checker.tingpai_check(hand_list, combo_list, extra)
        return tuple(sorted(int(t) for t in waits))

    if rule_key == "nanque":
        from .nanque import tingpai_check

        waits = tingpai_check(hand_list, combo_list, extra)
        return tuple(sorted(int(t) for t in waits))

    # Generic dotted module callable: "module.path:func_name"
    if ":" in rule_key:
        mod_path, func_name = rule_key.split(":", 1)
        fn = getattr(importlib.import_module(mod_path), func_name)
        kwargs = extra if isinstance(extra, dict) else {}
        waits = fn(hand_list, combo_list, **kwargs)
        return tuple(sorted(int(t) for t in waits))

    raise ValueError(f"Unknown tingpai rule_key: {rule_key!r}")


# ── Core & Regional Hepai Dispatch ────────────────────────────────────────────

def hepai_dispatch(
    rule_key: str,
    hand: Tuple[int, ...],
    combos: Tuple[str, ...],
    way: Tuple[str, ...],
    get_tile: int,
    extra_json: str = "",
) -> str:
    """Pure, stateless hepai/scoring check safe for InterpreterPoolExecutor.

    Returns JSON-serialized result so arbitrary dicts/lists/tuples cross
    subinterpreter boundaries with zero NotShareableError risk.
    """
    hand_list = list(hand)
    combo_list = list(combos)
    way_list = list(way)
    extra = _loads_opt(extra_json)

    if rule_key == "guobiao":
        from .guobiao_hepai_check import Chinese_Hepai_Check
        from .riichi.washizu_tiles import face

        checker = _get_checker("gb_hepai", Chinese_Hepai_Check)
        score, fan_list = checker.hepai_check(
            [face(t) for t in hand_list], combo_list, way_list, face(get_tile)
        )
        score, fan_list = checker.filter_zero_value_fans(score, fan_list)
        return json.dumps([score, fan_list], ensure_ascii=False)

    if rule_key == "guobiao/decompose":
        from .guobiao_hepai_check import Chinese_Hepai_Check
        from .riichi.washizu_tiles import face

        checker = _get_checker("gb_hepai", Chinese_Hepai_Check)
        res = checker.hepai_decompose(
            [face(t) for t in hand_list], combo_list, way_list, face(get_tile)
        )
        return json.dumps(res, ensure_ascii=False)

    if rule_key == "guobiao/xiaolin":
        from .guobiao_xiaolin_hepai_check import Xiaolin_Hepai_Check

        checker = _get_checker("gb_xiaolin", Xiaolin_Hepai_Check)
        score, fan_list = checker.hepai_check(hand_list, combo_list, way_list, get_tile)
        return json.dumps([score, fan_list], ensure_ascii=False)

    if rule_key == "guobiao/kshen":
        from .guobiao_kshen_hepai_check import Kshen_Hepai_Check

        checker = _get_checker("gb_kshen", Kshen_Hepai_Check)
        score, fan_list = checker.hepai_check(hand_list, combo_list, way_list, get_tile)
        return json.dumps([score, fan_list], ensure_ascii=False)

    if rule_key == "guobiao/lanshi":
        from .guobiao_lanshi_hepai_check import Lanshi_Hepai_Check

        checker = _get_checker("gb_lanshi", Lanshi_Hepai_Check)
        score, fan_list = checker.hepai_check(hand_list, combo_list, way_list, get_tile)
        return json.dumps([min(score, 100), fan_list], ensure_ascii=False)

    if rule_key == "qingque":
        from .qingque import qingque_hepai_check

        hand_without = list(hand_list)
        if get_tile in hand_without:
            hand_without.remove(get_tile)
        score, fan_list = qingque_hepai_check(hand_without, combo_list, way_list, get_tile)
        return json.dumps([score, fan_list], ensure_ascii=False)

    if rule_key == "riichi":
        from .riichi.riichi_hepai_check import Riichi_Hepai_Check

        checker = _get_checker("riichi_hepai", Riichi_Hepai_Check)
        ctx = (extra or {}).get("context") or {}
        masks = (extra or {}).get("combination_masks")
        res = checker.hepai_check(
            hand_list, combo_list, way_list, get_tile, ctx, combination_masks=masks
        )
        return json.dumps(res, ensure_ascii=False)

    if rule_key == "classical":
        from .classical.classical_hepai_check import Classical_Hepai_Check

        checker = _get_checker("classical_hepai", Classical_Hepai_Check)
        res = checker.hepai_check(hand_list, combo_list, way_list, get_tile)
        return json.dumps(list(res), ensure_ascii=False)

    if rule_key == "classical/fushu":
        from .classical.classical_fushu_check import Classical_Fushu_Check

        checker = _get_checker("classical_fushu", Classical_Fushu_Check)
        res = checker.fushucheck(hand_list, combo_list, way_list, get_tile)
        return json.dumps(res, ensure_ascii=False)

    if rule_key == "sichuan":
        from .sichuan.sichuan_hepai_check import Sichuan_Hepai_Check

        checker = _get_checker("sichuan_hepai", Sichuan_Hepai_Check)
        dingque_suit = int((extra or {}).get("dingque_suit", 0))
        score, fan_list = checker.hepai_check(
            hand_list, combo_list, way_list, get_tile, dingque_suit
        )
        return json.dumps([score, fan_list], ensure_ascii=False)

    if rule_key == "sichuan/xueliu":
        from .sichuan.xueliu_hepai_check import Xueliu_Hepai_Check

        checker = _get_checker("xueliu_hepai", Xueliu_Hepai_Check)
        dingque_suit = int((extra or {}).get("dingque_suit", 0))
        meld_count = int((extra or {}).get("meld_count", 3))
        score, fan_list = checker.hepai_check(
            hand_list, combo_list, way_list, get_tile, dingque_suit, meld_count
        )
        return json.dumps([score, fan_list], ensure_ascii=False)

    if rule_key == "changsha":
        from .changsha.changsha_hepai_check import Changsha_Hepai_Check

        checker = _get_checker("changsha_hepai", Changsha_Hepai_Check)
        score, fan_list = checker.hepai_check(hand_list, combo_list, way_list, get_tile)
        return json.dumps([score, fan_list], ensure_ascii=False)

    if rule_key == "taiwan":
        from .taiwan.taiwan_hepai_check import Taiwan_Hepai_Check

        checker = _get_checker("taiwan_hepai", Taiwan_Hepai_Check)
        score, fan_list = checker.hepai_check(hand_list, combo_list, way_list, get_tile, extra)
        return json.dumps([score, fan_list], ensure_ascii=False)

    if rule_key == "taiwan/detail":
        from .taiwan.taiwan_hepai_check import Taiwan_Hepai_Check

        checker = _get_checker("taiwan_hepai", Taiwan_Hepai_Check)
        detail = checker.hepai_detail(hand_list, combo_list, way_list, get_tile, extra)
        if isinstance(detail, dict) and isinstance(detail.get("waiting_tiles"), set):
            detail = dict(detail)
            detail["waiting_tiles"] = sorted(detail["waiting_tiles"])
        return json.dumps(detail, ensure_ascii=False)

    if rule_key == "nanque":
        from .nanque import HandContext as NanqueHandContext, score_hand as nanque_score_hand

        ctx = extra or {}
        if "抢杠" in way_list:
            default_src = "rob_kong"
        elif "点和" in way_list or "河底捞鱼" in way_list:
            default_src = "discard"
        else:
            default_src = "self_draw"
        nctx = NanqueHandContext(
            hand_tiles=hand_list,
            meld_codes=combo_list,
            winning_tile=get_tile,
            win_source=ctx.get("win_source") or default_src,
            pre_win_tiles=ctx.get("pre_win_tiles"),
            heavenly_win=bool(ctx.get("heavenly_win") or "天和" in way_list),
            earthly_win=bool(ctx.get("earthly_win") or "地和" in way_list),
            haitei=bool(ctx.get("haitei") or "海底捞月" in way_list),
            houtei=bool(ctx.get("houtei") or "河底捞鱼" in way_list),
            rinshan=bool(ctx.get("rinshan") or "杠上开花" in way_list),
            chankan=bool(ctx.get("chankan") or "抢杠" in way_list),
        )
        res = nanque_score_hand(nctx)
        return json.dumps(
            {
                "is_win": res.is_win,
                "points": res.points,
                "raw_points": res.raw_points,
                "fan_ids": list(res.fan_ids),
                "fan_names": list(res.fan_names),
            },
            ensure_ascii=False,
        )

    if ":" in rule_key:
        mod_path, func_name = rule_key.split(":", 1)
        fn = getattr(importlib.import_module(mod_path), func_name)
        kwargs = extra if isinstance(extra, dict) else {}
        res = fn(hand_list, combo_list, way_list, get_tile, **kwargs)
        return json.dumps(res, ensure_ascii=False)

    raise ValueError(f"Unknown hepai rule_key: {rule_key!r}")


def invoke_callable_json(mod_path: str, func_name: str, payload_json: str) -> str:
    """Invoke any stateless module-level calculation function via JSON over GIL pool.

    Used by regional rules whose tingpai/hepai/hint functions take custom
    JSON-serializable arguments, keeping 100% compatibility with Python 3.14
    InterpreterPoolExecutor (only str arguments cross the interpreter boundary).
    """
    fn = getattr(importlib.import_module(mod_path), func_name)
    payload = json.loads(payload_json)
    args = payload.get("args", [])
    kwargs = payload.get("kwargs", {})
    result = fn(*args, **kwargs)
    return json.dumps(result, ensure_ascii=False)


def warm_calc_worker() -> int:
    """Warm up a calc worker interpreter/process by importing common pure-Python modules."""
    from .gb_tingpai_check import Chinese_Tingpai_Check
    from .guobiao_hepai_check import Chinese_Hepai_Check

    _get_checker("gb_tingpai", Chinese_Tingpai_Check)
    _get_checker("gb_hepai", Chinese_Hepai_Check)
    return 1
