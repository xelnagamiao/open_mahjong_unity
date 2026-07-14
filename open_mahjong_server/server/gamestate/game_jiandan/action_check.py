from __future__ import annotations

from typing import Dict

def empty_action_dict() -> Dict[int, list[str]]:
    return {0: [], 1: [], 2: [], 3: []}


def check_action_hand_action(game_state, player_index: int, *, is_get_gang_tile: bool = False) -> Dict[int, list[str]]:
    """Check actions after a player's own draw or supplement draw.

    The authoritative calculation service refreshes waiting_tiles whenever the
    hand changes.
    """
    actions = empty_action_dict()
    player = game_state.player_list[player_index]
    if player.is_hu:
        return actions

    player_actions = actions[player_index]
    if player.hand_tiles and player.hand_tiles[-1] in player.waiting_tiles:
        player_actions.append("hu_self")

    if game_state.tiles_list:
        if _has_concealed_kong(player.hand_tiles):
            player_actions.append("angang")
        if _has_added_kong(player.hand_tiles, player.combination_tiles):
            player_actions.append("jiagang")

    player_actions.append("cut")
    return actions


def check_action_after_cut(game_state, cut_tile: int) -> Dict[int, list[str]]:
    """Check other-player responses after a discard.

    If the wall is empty, only discard wins are offered. Chi/peng/kong are
    intentionally suppressed on the final discard.
    """
    actions = empty_action_dict()
    current = game_state.current_player_index
    wall_has_tiles = bool(game_state.tiles_list)

    if wall_has_tiles:
        _add_chi_actions(game_state, actions, cut_tile)
        _add_peng_gang_actions(game_state, actions, cut_tile)

    _add_discard_win_actions(game_state, actions, cut_tile)
    actions[current] = []
    _add_pass(actions)
    return actions


def check_action_jiagang(game_state, jiagang_tile: int) -> Dict[int, list[str]]:
    """Check robbing-kong responses for an attempted added kong."""
    actions = empty_action_dict()
    current = game_state.current_player_index
    for player in game_state.player_list:
        if player.player_index == current or player.is_hu:
            continue
        if jiagang_tile in player.waiting_tiles and game_state.can_win_by_discard(player.player_index, jiagang_tile):
            actions[player.player_index].append("hu")
    _add_pass(actions)
    return actions


def check_only_cut(game_state, player_index: int) -> Dict[int, list[str]]:
    actions = empty_action_dict()
    if not game_state.player_list[player_index].is_hu:
        actions[player_index].append("cut")
    return actions


def refresh_waiting_tiles(game_state, player_index: int, *, exclude_last_tile: bool = False) -> set[int]:
    """Refresh a player's waiting-tile cache from the Jiandan calculator."""
    player = game_state.player_list[player_index]
    hand_tiles = player.hand_tiles[:-1] if exclude_last_tile else player.hand_tiles
    player.waiting_tiles = game_state.calculation_service.Jiandan_tingpai_check(
        hand_tiles,
        player.combination_tiles,
    )
    return player.waiting_tiles


def _add_chi_actions(game_state, actions: Dict[int, list[str]], cut_tile: int) -> None:
    if not _is_suited(cut_tile):
        return
    next_player_index = (game_state.current_player_index + 1) % 4
    player = game_state.player_list[next_player_index]
    if player.is_hu:
        return
    hand = player.hand_tiles
    tile_num = cut_tile % 10
    if tile_num >= 3 and cut_tile - 2 in hand and cut_tile - 1 in hand:
        actions[next_player_index].append("chi_left")
    if 2 <= tile_num <= 8 and cut_tile - 1 in hand and cut_tile + 1 in hand:
        actions[next_player_index].append("chi_mid")
    if tile_num <= 7 and cut_tile + 1 in hand and cut_tile + 2 in hand:
        actions[next_player_index].append("chi_right")


def _add_peng_gang_actions(game_state, actions: Dict[int, list[str]], cut_tile: int) -> None:
    for player in game_state.player_list:
        if player.player_index == game_state.current_player_index or player.is_hu:
            continue
        count = player.hand_tiles.count(cut_tile)
        if count >= 2:
            actions[player.player_index].append("peng")
        if count >= 3:
            actions[player.player_index].append("gang")


def _add_discard_win_actions(game_state, actions: Dict[int, list[str]], cut_tile: int) -> None:
    for player in game_state.player_list:
        if player.player_index == game_state.current_player_index or player.is_hu:
            continue
        if cut_tile in player.waiting_tiles and game_state.can_win_by_discard(player.player_index, cut_tile):
            actions[player.player_index].append("hu")


def _add_pass(actions: Dict[int, list[str]]) -> None:
    for player_index, player_actions in actions.items():
        if player_actions:
            player_actions.append("pass")


def _has_concealed_kong(hand_tiles: list[int]) -> bool:
    return any(hand_tiles.count(tile) == 4 for tile in set(hand_tiles))


def _has_added_kong(hand_tiles: list[int], combination_tiles: list[str]) -> bool:
    hand_set = set(hand_tiles)
    for meld in combination_tiles:
        if not meld or meld[0] != "k":
            continue
        if int(meld[1:]) in hand_set:
            return True
    return False


def _is_suited(tile: int) -> bool:
    return 11 <= tile <= 39 and tile % 10 != 0


class JiandanActionPolicy:
    """Replaceable legal-action module used by the Jiandan composition."""

    check_hand_action = staticmethod(check_action_hand_action)
    check_after_cut = staticmethod(check_action_after_cut)
    check_jiagang = staticmethod(check_action_jiagang)
    check_only_cut = staticmethod(check_only_cut)
    refresh_waiting_tiles = staticmethod(refresh_waiting_tiles)
