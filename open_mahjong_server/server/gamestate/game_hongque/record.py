"""虹雀牌谱：牌码转 Unity 整数 ID，并写入与 GameRecordManager 相同的 tick。

虹雀牌山普通摸牌从列表尾 ``pop()``，Unity 回放 ``d`` 从 ``tiles_list`` 头取。
因此局头 ``tiles_list`` 存剩余牌山的反转；补牌从原列表头取，对应 Unity ``bd`` 从尾取。
"""
from __future__ import annotations

from typing import Optional, Sequence

from ..public.game_record_manager import (
    end_game_record,
    flush_all_unexecuted_claim_applications,
    flush_unexecuted_claim_applications,
    init_game_record,
    init_game_round,
    player_action_record_chipenggang,
    player_action_record_cut,
    player_action_record_deal,
    player_action_record_hu,
    player_action_record_jiagang,
    player_action_record_liuju,
    player_action_record_round_end,
    remember_local_record_detail,
    track_claim_application,
)
from .action_priority import claim_action_type
from .tile import HongqueTile

COLOUR_CODES = (
    "AX", "AY", "BX", "BY", "CX", "CY", "DX",
    "DY", "EX", "EY", "FX", "FY", "GX", "GY",
)
BASE_ID = 1000


def code_to_id(code: str) -> int:
    text = str(code or "").strip().upper()
    if len(text) != 3:
        return 0
    try:
        colour = COLOUR_CODES.index(text[:2])
    except ValueError:
        return 0
    number = ord(text[2]) - ord("0")
    if number < 1 or number > 9:
        return 0
    return BASE_ID + colour * 10 + number


def codes_to_ids(codes: Sequence[str]) -> list[int]:
    return [code_to_id(code) for code in codes]


def wall_tiles_list_for_record(wall: Sequence[str]) -> list[int]:
    return [code_to_id(code) for code in reversed(wall)]


def fan_labels(fans) -> list[str]:
    labels: list[str] = []
    for item in fans or ():
        if isinstance(item, str):
            labels.append(item)
            continue
        name = str(item.get("name") or "")
        count = int(item.get("count") or 1)
        if not name:
            continue
        labels.append(f"{name}*{count}" if count > 1 else name)
    return labels


def chi_action_type(claimed: str, tiles: Sequence[str]) -> str:
    ranked = sorted(tiles, key=lambda tile: (HongqueTile.parse(tile).number, tile))
    try:
        index = ranked.index(claimed)
    except ValueError:
        return "chi_mid"
    if index <= 0:
        return "chi_left"
    if index >= len(ranked) - 1:
        return "chi_right"
    return "chi_mid"


def _ensure_round(game_state) -> bool:
    record = getattr(game_state, "game_record", None)
    if not record or "game_title" not in record:
        return False
    round_key = f"round_index_{game_state.round_index}"
    return round_key in record.get("game_round", {})


def snapshot_round_header(game_state) -> None:
    if not getattr(game_state, "game_record", None) or not game_state.game_record.get("game_title"):
        init_game_record(game_state)
    game_state.tiles_list = wall_tiles_list_for_record(game_state.wall)
    init_game_round(game_state)


def record_cut(game_state, player, code: str, is_moqie: bool) -> None:
    if not _ensure_round(game_state):
        return
    cut_id = code_to_id(code)
    flush_all_unexecuted_claim_applications(game_state)
    player_action_record_cut(game_state, cut_tile=cut_id, is_moqie=is_moqie)


def record_deal(game_state, tile: str, deal_type: str = "d", action_player: Optional[int] = None) -> None:
    if not _ensure_round(game_state):
        return
    player_action_record_deal(
        game_state,
        deal_tile=code_to_id(tile),
        deal_type=deal_type,
        action_player=action_player,
    )


def record_kong(game_state, player, candidate: dict) -> None:
    if not _ensure_round(game_state):
        return
    hand_tiles = list(candidate.get("hand_tiles") or ())
    if not hand_tiles:
        return
    tile_id = code_to_id(hand_tiles[0])
    is_mo = player.drawn_tile in hand_tiles
    player_action_record_jiagang(game_state, jiagang_tile=tile_id, is_mo_gang=is_mo)


def record_claim_apply(game_state, player_index: int, candidate: dict) -> None:
    if not _ensure_round(game_state) or not game_state.last_discard:
        return
    cut_id = code_to_id(game_state.last_discard["tile"])
    action_type = candidate.get("action_type") or chi_action_type(
        game_state.last_discard["tile"], candidate.get("tiles") or ()
    )
    track_claim_application(game_state, player_index, action_type, cut_id)


def record_claim_execute(game_state, player, candidate: dict) -> None:
    if not _ensure_round(game_state) or not game_state.last_discard:
        return
    claimed = game_state.last_discard["tile"]
    claimed_id = code_to_id(claimed)
    kind = candidate.get("kind")
    hand_tiles = list(candidate.get("hand_tiles") or ())
    tiles = list(candidate.get("tiles") or ())
    if kind == "triplet":
        action_type = "peng"
    else:
        action_type = chi_action_type(claimed, tiles)
    hand_ids = codes_to_ids(hand_tiles)
    mask = [1, claimed_id]
    for tile_id in hand_ids:
        mask.extend([0, tile_id])
    flush_unexecuted_claim_applications(
        game_state,
        claimed_id,
        executed_player=player.index,
        executed_action_type=action_type,
    )
    player_action_record_chipenggang(
        game_state,
        action_type=action_type,
        mingpai_tile=claimed_id,
        action_player=player.index,
        combination_mask=mask,
    )


def record_unclaimed_discard(game_state) -> None:
    if not _ensure_round(game_state) or not game_state.last_discard:
        return
    flush_unexecuted_claim_applications(game_state, code_to_id(game_state.last_discard["tile"]))


def _score_changes(game_state, winners: list[tuple]) -> list[int]:
    changes = [0, 0, 0, 0]
    for winner, result in winners:
        changes[winner.index] = int(result.get("points") or 0)
    return changes


def record_round_result(game_state, winners: list[tuple], reason: str) -> None:
    if not _ensure_round(game_state):
        return
    if not winners:
        player_action_record_liuju(game_state)
        player_action_record_round_end(game_state)
        return
    discarder = None
    if reason == "ron" and game_state.last_discard:
        discarder = game_state.last_discard["player"]
    for winner, result in winners:
        if reason == "self_draw":
            hu_class = "hu_self"
        else:
            hu_class = claim_action_type("win", winner.index, discarder)
        winning_hand = list(result.get("winning_hand") or winner.hand)
        hepai_tile = code_to_id(winning_hand[-1]) if winning_hand else None
        player_action_record_hu(
            game_state,
            hu_class=hu_class,
            hu_score=int(result.get("points") or 0),
            hu_fan=fan_labels(result.get("fans")),
            hepai_player_index=winner.index,
            score_changes=_score_changes(game_state, [(winner, result)]),
            hepai_tile=hepai_tile,
            multi_ron=len(winners) > 1,
            ron_discarder_index=discarder if reason == "ron" else None,
        )
    player_action_record_round_end(game_state)


def assign_hongque_ranks(players) -> None:
    ranked = sorted(players, key=lambda player: (-int(player.score), player.original_player_index))
    for index, player in enumerate(ranked):
        player.record_counter.rank_result = index + 1


def persist_and_remember(game_state) -> Optional[str]:
    record = getattr(game_state, "game_record", None)
    if not record or "game_title" not in record:
        return None
    end_game_record(game_state)
    assign_hongque_ranks(game_state.players)
    match_type = f"{game_state.max_round}/4"
    game_id = None
    db_manager = getattr(game_state, "db_manager", None)
    if db_manager is not None and hasattr(db_manager, "store_hongque_game_record"):
        try:
            game_id = db_manager.store_hongque_game_record(
                game_state.game_record,
                game_state.player_list,
                game_state.room_type,
                match_type,
            )
        except Exception:
            game_id = None
    remember_local_record_detail(game_state, game_id, match_type)
    return game_id
