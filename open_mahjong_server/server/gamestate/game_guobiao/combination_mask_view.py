"""国标副露掩码按视角脱敏：暗杠对他人隐藏真实牌 id。"""
from typing import List, Optional, Tuple


def _is_full_concealed_kong_mask(mask: List[int]) -> bool:
    if not mask or len(mask) != 8:
        return False
    for i in range(0, 8, 2):
        if mask[i] != 2:
            return False
    return True


def sanitize_angang_mask(mask: Optional[List[int]], owner_index: int, viewer_index: int) -> Optional[List[int]]:
    if mask is None:
        return None
    if owner_index == viewer_index:
        return mask
    if not _is_full_concealed_kong_mask(mask):
        return mask
    out = list(mask)
    for i in range(1, len(out), 2):
        out[i] = 0
    return out


def sanitize_combination_target_for_viewer(
    target: Optional[str], owner_index: int, viewer_index: int
) -> Optional[str]:
    if target is None:
        return None
    if owner_index == viewer_index:
        return target
    if target.startswith("G"):
        return "G0"
    return target


def sanitize_combination_tiles_for_viewer(
    combination_tiles: List[str], owner_index: int, viewer_index: int
) -> List[str]:
    if owner_index == viewer_index:
        return combination_tiles
    return ["G0" if t.startswith("G") else t for t in combination_tiles]


def sanitize_combination_masks_for_viewer(
    combination_masks: List[List[int]],
    combination_tiles: List[str],
    owner_index: int,
    viewer_index: int,
) -> List[List[int]]:
    if owner_index == viewer_index:
        return combination_masks
    result = []
    for i, mask in enumerate(combination_masks):
        combo = combination_tiles[i] if i < len(combination_tiles) else ""
        if combo.startswith("G") or _is_full_concealed_kong_mask(mask):
            result.append(sanitize_angang_mask(mask, owner_index, viewer_index))
        else:
            result.append(mask)
    return result


def build_revealed_angang_masks(player_list) -> Optional[dict]:
    """局终亮杠：player_index -> 暗杠 mask 列表（不含对局中脱敏）。"""
    result = {}
    for p in player_list:
        kongs = [list(m) for m in p.combination_mask if _is_full_concealed_kong_mask(m)]
        if kongs:
            result[p.player_index] = kongs
    return result or None


def get_combination_fields_for_viewer(player, viewer_player_index: int) -> Tuple[List[str], List[List[int]]]:
    owner = player.player_index
    tiles = sanitize_combination_tiles_for_viewer(player.combination_tiles, owner, viewer_player_index)
    masks = sanitize_combination_masks_for_viewer(
        player.combination_mask, player.combination_tiles, owner, viewer_player_index
    )
    return tiles, masks
