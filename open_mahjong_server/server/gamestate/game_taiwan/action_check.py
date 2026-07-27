"""台湾麻将动作合法性检查。"""

from collections import Counter
from typing import Dict, List, Optional, Set, Tuple

from ...game_calculation.taiwan.rules import FLOWER_TILES
HU_ACTIONS = ("hu_self", "hu_first", "hu_second", "hu_third")


def is_forced_ready_win(game_state, player_index: int) -> bool:
    """按公开报听与天地听馆规判断玩家能否拒绝合法胡牌。"""

    player = game_state.player_list[player_index]
    declared_ready_forces_win = (
        bool(getattr(player, "declared_ready", False))
        and getattr(game_state.rules, "declared_ready_win_policy", "allow_pass")
        == "force_win"
    )
    qualified_ready_forces_win = (
        getattr(
            game_state.rules,
            "qualified_ready_win_policy",
            "follow_declared_ready_policy",
        )
        == "force_win"
        and bool(getattr(player, "qualification_alive", False))
        and bool(
            getattr(player, "heavenly_ready", False)
            or getattr(player, "earthly_ready", False)
        )
    )
    return declared_ready_forces_win or qualified_ready_forces_win


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


def _current_kong_count(game_state) -> int:
    """返回已经成立的杠数量。"""

    counter = getattr(game_state, "_kong_count", None)
    if callable(counter):
        try:
            return max(0, int(counter()))
        except (AttributeError, TypeError, ValueError):
            pass
    return sum(
        1
        for player in getattr(game_state, "player_list", ())
        for code in getattr(player, "combination_tiles", ())
        if code and code[0] in ("g", "G")
    )


def _expected_playable_wall_after_kong(game_state) -> int:
    """估算完成一次杠后剩余的可摸牌墙数量。"""

    estimator = getattr(game_state, "expected_playable_wall_after_kong", None)
    if callable(estimator):
        try:
            return max(0, int(estimator()))
        except (AttributeError, TypeError, ValueError):
            pass

    playable = max(0, int(game_state.playable_wall_count()))
    rules = getattr(game_state, "rules", None)
    if (
        bool(getattr(rules, "four_kongs_abort", False))
        and _current_kong_count(game_state) >= 3
    ):
        return playable

    mode = getattr(rules, "dead_wall_mode", "fixed_tail_16")
    if mode == "kong_expands_tail":
        return max(0, playable - min(2, playable))

    can_supplement = getattr(game_state, "can_take_supplement_tile", None)
    if callable(can_supplement):
        return max(0, playable - (1 if can_supplement() else 0))
    if mode == "fixed_tail_16":
        can_draw_normal = getattr(game_state, "can_take_wall_tile", None)
        if callable(can_draw_normal) and not can_draw_normal():
            return playable
    return max(0, playable - (1 if playable else 0))


def _claim_keeps_wall(game_state, *, kong: bool = False) -> bool:
    reserve = game_state.rules.required_claim_wall_reserve
    if reserve <= 0:
        return True
    if kong:
        return _expected_playable_wall_after_kong(game_state) >= reserve
    return game_state.playable_wall_count() >= reserve


def _same_round_claim_forbidden(
    game_state,
    player,
    action_type: str,
    claimed_tile: int,
) -> bool:
    if not bool(getattr(game_state.rules, "same_turn_claim_forbidden", False)):
        return False
    previous_discard = getattr(player, "last_discarded_tile", None)
    if previous_discard is None:
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
        water_blocks_self = player.water and game_state.rules.missed_win_blocks_self_draw
        if detail is not None and not water_blocks_self and game_state.supplement_win_allowed:
            actions[player_index].append("hu_self")
            game_state.result_dict["hu_self"] = detail
            if (
                _is_legal_win_detail(detail)
                and is_forced_ready_win(game_state, player_index)
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
        and _claim_keeps_wall(game_state, kong=True)
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
                and game_state.rules.missed_win_blocks_claims
            )
        )
        if (
            not claims_blocked
            and _claim_keeps_wall(game_state)
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
                    game_state.rules.chow_discard_restriction_mode,
                )
                and not _same_round_claim_forbidden(
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
                    game_state.rules.chow_discard_restriction_mode,
                )
                and not _same_round_claim_forbidden(
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
                    game_state.rules.chow_discard_restriction_mode,
                )
                and not _same_round_claim_forbidden(
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
            if player.water and game_state.rules.missed_win_blocks_claims:
                continue
            count = player.hand_tiles.count(cut_tile)
            if (
                count >= 2
                and _claim_keeps_wall(game_state)
                and not _same_round_claim_forbidden(
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
                and _claim_keeps_wall(game_state, kong=True)
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
            and is_forced_ready_win(game_state, player_index)
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
        detail = _score_action_candidate(game_state, player_index, "robbing_kong", tile)
        if detail is None:
            continue
        action = hu_action_for_player(declarer, player_index)
        actions[player_index] = [action]
        if not (
            _is_legal_win_detail(detail)
            and is_forced_ready_win(game_state, player_index)
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
