"""四川麻将·顺和：跳过和牌后，若打出听牌，则至下次摸牌前不可点和≤跳过番的牌（仅可点和更高番，自摸不受限）。"""
from typing import Iterable, Optional

SHUNHE_TAG_PREFIX = "shunhe_"


def shunhe_tag_for_fan(fan: int) -> str:
    return f"{SHUNHE_TAG_PREFIX}{fan}"


def is_shunhe_tag(tag: str) -> bool:
    return bool(tag) and tag.startswith(SHUNHE_TAG_PREFIX)


def tag_list_for_viewer(tags, subject_player_index: int, viewer_player_index: int) -> list:
    """顺和 tag 仅同步给本人视角（与日麻 furiten 一致）。"""
    out = list(tags) if tags is not None else []
    if subject_player_index != viewer_player_index:
        return [t for t in out if not is_shunhe_tag(t)]
    return out


def sync_shunhe_tag(player) -> bool:
    """根据 shunhe_passed_max_fan 刷新 tag_list 中的 shunhe_N。返回是否改动。"""
    cap = getattr(player, "shunhe_passed_max_fan", None)
    old_tags = [t for t in player.tag_list if is_shunhe_tag(t)]
    new_tag = shunhe_tag_for_fan(cap) if cap is not None else None
    if old_tags == ([new_tag] if new_tag else []):
        return False
    player.tag_list = [t for t in player.tag_list if not is_shunhe_tag(t)]
    if new_tag:
        player.tag_list.append(new_tag)
    return True


def clear_shunhe(player) -> bool:
    """自家摸牌时解除生效中的顺和限制。"""
    changed = getattr(player, "shunhe_passed_max_fan", None) is not None
    player.shunhe_passed_max_fan = None
    tag_changed = sync_shunhe_tag(player)
    return changed or tag_changed


def record_skipped_win_fan(player, passed_fan: int) -> bool:
    """记录最近一次跳过和牌的番数（自摸/点炮/抢杠/碰杠放弃）。返回 tag 是否变化。"""
    active_cap = getattr(player, "shunhe_passed_max_fan", None)
    if active_cap is not None:
        player.shunhe_passed_max_fan = passed_fan
        return sync_shunhe_tag(player)
    player.shunhe_skipped_fan = passed_fan
    return False


def activate_shunhe_if_tenpai_discard(player, was_tenpai: bool) -> bool:
    """听牌状态下出牌后，将待生效的跳过番数转为顺和限制。"""
    if not was_tenpai:
        return False
    skipped = getattr(player, "shunhe_skipped_fan", None)
    if skipped is None:
        return False
    player.shunhe_passed_max_fan = skipped
    player.shunhe_skipped_fan = None
    return sync_shunhe_tag(player)


def is_blocked_by_shunhe(player, win_fan: int) -> bool:
    skipped_fan = getattr(player, "shunhe_passed_max_fan", None)
    return skipped_fan is not None and win_fan <= skipped_fan


def apply_passed_win_shunhe(game_state, hu_eligible_indexes: Iterable[int]) -> bool:
    """有和牌机会但未和牌：记录跳过番数；若顺和已生效则刷新 tag。"""
    changed = False
    for idx in hu_eligible_indexes:
        info = game_state.sichuan_hu_results.get(idx)
        if not info:
            continue
        fan = info.get("fan", 0)
        if record_skipped_win_fan(game_state.player_list[idx], fan):
            changed = True
    return changed


def record_passed_self_draw_shunhe(game_state, player_index: int) -> bool:
    """摸牌后放弃自摸：记录跳过番数。"""
    if "hu_self" not in game_state.action_dict.get(player_index, []):
        return False
    info = game_state.sichuan_hu_results.get(player_index)
    if not info:
        return False
    return record_skipped_win_fan(game_state.player_list[player_index], info.get("fan", 0))

