"""Qingque13 计算兼容层（纯 Python 实现，不再依赖 pythonnet / C# DLL）。

保留 `call_hepai_check`、`call_tingpai_check`、`call_get_base_point` 三个接口，
直接委托给 `server.game_calculation.qingque` 纯 Python 包。
"""

from __future__ import annotations

try:
    from .qingque import (
        qingque_get_base_point,
        qingque_hepai_check,
        qingque_tingpai_check,
    )
except ImportError:
    from qingque import (  # type: ignore
        qingque_get_base_point,
        qingque_hepai_check,
        qingque_tingpai_check,
    )


def call_hepai_check(
    hand_list: list[int],
    tiles_combination: list[str],
    way_to_hepai: list[str],
    get_tile: int,
    debug: bool = False,
) -> tuple[float, list[str]]:
    """检查青雀13张和牌并返回 (番数, 中文番名列表)。

    注意：`hand_list` 不包含 `get_tile`（与原 C# `Qingque13Hepai.HepaiCheck` 一致）。
    """
    return qingque_hepai_check(
        hand_list, tiles_combination, way_to_hepai, get_tile, debug=debug
    )


def call_tingpai_check(
    hand_tile_list: list[int],
    combination_list: list[str],
    debug: bool = False,
) -> set[int]:
    """检查青雀13张听牌并返回等待牌集合。"""
    return qingque_tingpai_check(hand_tile_list, combination_list, debug=debug)


def call_get_base_point(fan: float) -> int:
    """根据番数计算基础点数（banker's rounding: round(fan * fan)）。"""
    return qingque_get_base_point(fan)
