"""摸牌张按视角脱敏：仅摸牌玩家本人可见真实牌 id。"""
from typing import Optional


def sanitize_deal_tile_for_viewer(
    deal_tile: Optional[int],
    action_player: int,
    viewer_index: int,
) -> Optional[int]:
    if deal_tile is None:
        return None
    if action_player == viewer_index:
        return deal_tile
    return None
