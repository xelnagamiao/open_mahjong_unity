"""
副露局数统计：对局内实时累计。
明副露 = 吃/碰/明杠/加杠（combination 编码以 k/g/s 开头；不含暗杠 G、暗刻 K）。
"""
from typing import Iterable

VISIBLE_MELD_COMBO_PREFIXES = ("k", "g", "s")


def has_visible_meld(combination_tiles: Iterable[str]) -> bool:
    """玩家当前是否有明副露（不含暗杠/暗刻）。"""
    for combo in combination_tiles or []:
        if combo and combo[0] in VISIBLE_MELD_COMBO_PREFIXES:
            return True
    return False


def record_fulu_rounds_for_players(player_list) -> None:
    """本局结束时，为有明副露的玩家累计副露局数。"""
    for player in player_list:
        combos = getattr(player, "combination_tiles", None) or []
        if has_visible_meld(combos):
            player.record_counter.fulu_times += 1
