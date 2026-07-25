"""台湾麻将动作合法性检查。"""

from collections import Counter
from typing import Dict, List, Optional, Set, Tuple

from ...game_calculation.taiwan.rules import FLOWER_TILES
HU_ACTIONS = ("hu_self", "hu_first", "hu_second", "hu_third")


def is_forced_declared_ready_win(game_state, player_index: int) -> bool:
    """公开听牌玩家在禁止拒胡馆规下不能放弃合法胡牌。"""

    player = game_state.player_list[player_index]
    platform_qualification_forces_win = (
        getattr(game_state.rules, "scoring_preset", "") == "star31"
        and bool(getattr(player, "qualification_alive", False))
        and bool(
            getattr(player, "heavenly_ready", False)
            or getattr(player, "earthly_ready", False)
        )
    )
    return bool(getattr(player, "declared_ready", False)) and (
        getattr(game_state.rules, "declared_ready_win_policy", "allow_pass") == "force_win"
        or platform_qualification_forces_win
    )


def hu_action_for_player(discarder: int, player_index: int) -> str:
    distance = (player_index - discarder) % 4
    if distance == 1:
        return "hu_first"
    if distance == 2:
        return "hu_second"
    if distance == 3:
        return "hu_third"
    return "hu_self"


def _is_number(tile: int) -> bool:
    return 11 <= tile <= 39 and tile // 10 in (1, 2, 3) and 1 <= tile % 10 <= 9


def _valid_sequence_members(*tiles: int) -> bool:
    return all(_is_number(tile) for tile in tiles) and len({tile // 10 for tile in tiles}) == 1


def _can_take_normal(game_state) -> bool:
    checker = getattr(game_state, "can_take_normal_tile", None) or game_state.can_take_wall_tile
    return checker()


def _can_establish_kong(game_state) -> bool:
    checker = (
        getattr(game_state, "can_establish_kong", None)
        or getattr(game_state, "can_take_supplement_tile", None)
        or game_state.can_take_wall_tile
    )
    return checker()


def _cml_claim_keeps_wall(game_state, *, kong: bool = False) -> bool:
    if getattr(game_state.rules, "scoring_preset", "") != "cml":
        return True
    minimum = 5 if kong else 4
    return game_state.playable_wall_count() >= minimum


def _cml_same_round_claim_forbidden(
    game_state,
    player,
    action_type: str,
    claimed_tile: int,
) -> bool:
    if getattr(game_state.rules, "scoring_preset", "") != "cml":
        return False
    discards = getattr(player, "discard_tiles", [])
    if not discards:
        return False
    previous_discard = discards[-1]
    if action_type == "peng":
        return previous_discard == claimed_tile
    return previous_discard in strict_kuikae_forbidden(
        action_type,
        claimed_tile,
        "strict",
    )


def is_kuikae_forbidden_cut(player, tile: int) -> bool:
    normal = tile
    return normal in {
        forbidden
        for forbidden in getattr(player, "kuikae_forbidden_tiles", set())
    }


def _chi_leaves_legal_discard(
    hand_tiles: List[int],
    required: Tuple[int, int],
    action_type: str,
    claimed_tile: int,
    mode: str,
) -> bool:
    remaining = list(hand_tiles)
    try:
        for tile in required:
            remaining.remove(tile)
    except ValueError:
        return False
    forbidden = strict_kuikae_forbidden(action_type, claimed_tile, mode)
    return any(tile not in forbidden for tile in remaining)


def refresh_waiting_tiles(game_state, player_index: int) -> Set[int]:
    player = game_state.player_list[player_index]
    waits = game_state.calculation_service.Taiwan_tingpai_check(
        player.hand_tiles,
        player.combination_tiles,
        game_state.rules_dict,
    )
    player.waiting_tiles = waits
    return waits


def _score_action_candidate(game_state, player_index: int, source: str, tile: Optional[int] = None):
    checker = getattr(game_state, "score_action_candidate", None)
    if checker is None:
        checker = game_state.score_candidate
    return checker(player_index, source, tile)


def _is_legal_win_detail(detail: Optional[dict]) -> bool:
    return bool(detail and detail.get("is_win", True))


def check_action_hand_action(game_state, player_index: int) -> Dict[int, list]:
    actions: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    player = game_state.player_list[player_index]
    is_peida = "peida" in player.tag_list

    # 台湾麻将摸到花牌必须补花。
    if any(tile in FLOWER_TILES for tile in player.hand_tiles):
        actions[player_index].append("buhua")
        return actions

    if not is_peida:
        detail = _score_action_candidate(game_state, player_index, "self_draw")
        water_blocks_self = player.water and game_state.rules.water_blocks_self_draw
        if detail is not None and not water_blocks_self and game_state.supplement_win_allowed:
            actions[player_index].append("hu_self")
            game_state.result_dict["hu_self"] = detail
            if (
                _is_legal_win_detail(detail)
                and is_forced_declared_ready_win(game_state, player_index)
            ):
                player.riichi_candidate_cuts = {}
                return actions

    can_supplement = _can_establish_kong(game_state)
    terminal_kong = (
        game_state.last_draw_was_last
        and not can_supplement
    )
    if (
        not getattr(player, "ready_locked", False)
        and (can_supplement or terminal_kong)
        and (terminal_kong or _cml_claim_keeps_wall(game_state, kong=True))
    ):
        counts = Counter(player.hand_tiles)
        if any(count == 4 for tile, count in counts.items() if tile < 50):
            actions[player_index].append("angang")
        if any(
            combination.startswith("k")
            and player.hand_tiles.count(int(combination[1:])) > 0
            for combination in player.combination_tiles
        ):
            actions[player_index].append("jiagang")

    candidate_builder = getattr(game_state, "ready_candidate_cuts", None)
    candidates = (
        candidate_builder(player_index)
        if not is_peida and callable(candidate_builder)
        else {}
    )
    player.riichi_candidate_cuts = candidates
    if candidates:
        actions[player_index].append("riichi_cut")

    actions[player_index].append("cut")
    return actions


def check_action_after_cut(game_state, cut_tile: int) -> Dict[int, list]:
    actions: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    discarder = game_state.current_player_index
    last_discard = not _can_take_normal(game_state)

    # 胡牌先独立判断；最低台、过水与碰杠补牌禁胡都在 score_candidate 前后裁决。
    for distance in (1, 2, 3):
        player_index = (discarder + distance) % 4
        player = game_state.player_list[player_index]
        if player.water or "peida" in player.tag_list:
            continue
        detail = _score_action_candidate(game_state, player_index, "discard", cut_tile)
        if detail is not None:
            action = hu_action_for_player(discarder, player_index)
            actions[player_index].append(action)
            game_state.result_dict[action] = detail

    if not last_discard:
        next_player = (discarder + 1) % 4
        next_hand = game_state.player_list[next_player].hand_tiles
        claims_blocked = (
            getattr(game_state.player_list[next_player], "ready_locked", False)
            or (
                game_state.player_list[next_player].water
                and game_state.rules.water_blocks_claims
            )
        )
        if (
            not claims_blocked
            and _cml_claim_keeps_wall(game_state)
            and _is_number(cut_tile)
        ):
            if (
                _valid_sequence_members(cut_tile - 2, cut_tile - 1, cut_tile)
                and cut_tile - 2 in next_hand
                and cut_tile - 1 in next_hand
                and _chi_leaves_legal_discard(
                    next_hand,
                    (cut_tile - 2, cut_tile - 1),
                    "chi_left",
                    cut_tile,
                    game_state.rules.strict_kuikae,
                )
                and not _cml_same_round_claim_forbidden(
                    game_state,
                    game_state.player_list[next_player],
                    "chi_left",
                    cut_tile,
                )
            ):
                actions[next_player].append("chi_left")
            if (
                _valid_sequence_members(cut_tile - 1, cut_tile, cut_tile + 1)
                and cut_tile - 1 in next_hand
                and cut_tile + 1 in next_hand
                and _chi_leaves_legal_discard(
                    next_hand,
                    (cut_tile - 1, cut_tile + 1),
                    "chi_mid",
                    cut_tile,
                    game_state.rules.strict_kuikae,
                )
                and not _cml_same_round_claim_forbidden(
                    game_state,
                    game_state.player_list[next_player],
                    "chi_mid",
                    cut_tile,
                )
            ):
                actions[next_player].append("chi_mid")
            if (
                _valid_sequence_members(cut_tile, cut_tile + 1, cut_tile + 2)
                and cut_tile + 1 in next_hand
                and cut_tile + 2 in next_hand
                and _chi_leaves_legal_discard(
                    next_hand,
                    (cut_tile + 1, cut_tile + 2),
                    "chi_right",
                    cut_tile,
                    game_state.rules.strict_kuikae,
                )
                and not _cml_same_round_claim_forbidden(
                    game_state,
                    game_state.player_list[next_player],
                    "chi_right",
                    cut_tile,
                )
            ):
                actions[next_player].append("chi_right")

        for distance in (1, 2, 3):
            player_index = (discarder + distance) % 4
            player = game_state.player_list[player_index]
            if "peida" in player.tag_list or getattr(player, "ready_locked", False):
                continue
            if player.water and game_state.rules.water_blocks_claims:
                continue
            count = player.hand_tiles.count(cut_tile)
            if (
                count >= 2
                and _cml_claim_keeps_wall(game_state)
                and not _cml_same_round_claim_forbidden(
                    game_state,
                    player,
                    "peng",
                    cut_tile,
                )
            ):
                actions[player_index].append("peng")
            # 推荐标准：上家弃牌（即玩家的直接上家）只能碰，不能直接碰杠。
            from_upper = distance == 1
            if (
                count == 3
                and (game_state.rules.allow_kong_from_upper_discard or not from_upper)
                and _can_establish_kong(game_state)
                and _cml_claim_keeps_wall(game_state, kong=True)
            ):
                actions[player_index].append("gang")

    for player_index, player_actions in actions.items():
        has_hu = any(action in HU_ACTIONS for action in player_actions)
        has_forced_legal_hu = has_hu and any(
            _is_legal_win_detail(game_state.result_dict.get(action))
            for action in player_actions
            if action in HU_ACTIONS
        )
        if player_actions and not (
            has_forced_legal_hu
            and is_forced_declared_ready_win(game_state, player_index)
        ):
            player_actions.append("pass")
    actions[discarder] = []
    return actions


def check_action_jiagang(game_state, tile: int) -> Dict[int, list]:
    actions: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    if not game_state.rules.allow_rob_added_kong:
        return actions
    declarer = game_state.current_player_index
    for distance in (1, 2, 3):
        player_index = (declarer + distance) % 4
        player = game_state.player_list[player_index]
        if player.water or "peida" in player.tag_list:
            continue
        detail = _score_action_candidate(game_state, player_index, "rob_kong", tile)
        if detail is None:
            continue
        action = hu_action_for_player(declarer, player_index)
        actions[player_index] = [action]
        if not (
            _is_legal_win_detail(detail)
            and is_forced_declared_ready_win(game_state, player_index)
        ):
            actions[player_index].append("pass")
        game_state.result_dict[action] = detail
    return actions


def strict_kuikae_forbidden(action_type: str, claimed_tile: int, mode: str) -> Set[int]:
    if mode == "none":
        return set()
    forbidden = {claimed_tile}
    if mode != "strict":
        return forbidden
    if action_type == "chi_left":
        extension = claimed_tile - 3
        if _is_number(extension) and extension // 10 == claimed_tile // 10:
            forbidden.add(extension)
    elif action_type == "chi_right":
        extension = claimed_tile + 3
        if _is_number(extension) and extension // 10 == claimed_tile // 10:
            forbidden.add(extension)
    return forbidden
