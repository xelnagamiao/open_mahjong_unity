"""按建房勾选组成牌墙并洗牌，不发到手。"""
from __future__ import annotations

import random
from typing import Any

from ..public.random_seed_manager import derive_round_seed

WALL_WAN = list(range(11, 20))
WALL_TONG = list(range(21, 30))
WALL_SUO = list(range(31, 40))
WALL_WINDS = [41, 42, 43, 44]
WALL_DRAGONS = [45, 46, 47]
WALL_FLOWERS = list(range(51, 59))

WALL_FLAG_KEYS = (
    "wall_wan",
    "wall_tong",
    "wall_suo",
    "wall_winds",
    "wall_dragons",
    "wall_flowers",
)


def _flag(room_data: dict, key: str, default: bool = True) -> bool:
    value = room_data.get(key, default)
    if isinstance(value, str):
        return value.strip().lower() not in ("0", "false", "off", "")
    return bool(value)


def build_wall_tiles(room_data: dict) -> list[int]:
    tiles: list[int] = []
    if _flag(room_data, "wall_wan"):
        for tile in WALL_WAN:
            tiles.extend([tile] * 4)
    if _flag(room_data, "wall_tong"):
        for tile in WALL_TONG:
            tiles.extend([tile] * 4)
    if _flag(room_data, "wall_suo"):
        for tile in WALL_SUO:
            tiles.extend([tile] * 4)
    if _flag(room_data, "wall_winds"):
        for tile in WALL_WINDS:
            tiles.extend([tile] * 4)
    if _flag(room_data, "wall_dragons"):
        for tile in WALL_DRAGONS:
            tiles.extend([tile] * 4)
    if _flag(room_data, "wall_flowers"):
        tiles.extend(WALL_FLOWERS)
    return tiles


def shuffle_wall(game_state: Any) -> None:
    game_state.round_random_seed = derive_round_seed(game_state.master_seed, game_state.current_round)
    tiles = list(game_state.wall_template)
    rng = random.Random(game_state.round_random_seed)
    rng.shuffle(tiles)
    game_state.tiles_list = tiles
