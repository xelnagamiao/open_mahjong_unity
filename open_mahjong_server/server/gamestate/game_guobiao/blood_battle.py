"""国标血战专用流程。标准国标不调用这些入口；计番和正常摸打仍使用国标实现。"""
import asyncio
from copy import deepcopy

from ..public.game_record_manager import (
    append_action_tick, player_action_record_hu, player_action_record_round_end,
)
from ..public.hand_slot_utils import clear_draw_slot
from ..public.round_end_timing import (
    hu_result_ready_pre_panel_seconds, sichuan_settle_hu_panel_wait_seconds,
    liuju_ready_wait_seconds,
)
from ..public.ready_phase import run_hu_result_ready_phase
from ...database.fulu_utils import record_fulu_rounds_for_players

SUB_RULE = "guobiao/blood_battle"
RON_ACTIONS = frozenset(("hu_first", "hu_second", "hu_third"))
HU_TAGS = ("first_hu", "second_hu", "third_hu")


def enabled(state):
    return getattr(state, "sub_rule", None) == SUB_RULE


def reset_round(state):
    state.blood_win_events = []
    state.blood_pending_claims = {}
    state.blood_public_win_tiles = []
    state.blood_last_result = None
    state.blood_finished = False
    state.result_dict = {}
    state.hu_class = None
    state.jiagang_tile = None
    for player in state.player_list:
        player.is_hu = False
        player.hu_order = 0
        player.blood_win_tile = None
        player.blood_win_is_zimo = False
        player.blood_win_multi = False
        player.tag_list = [tag for tag in player.tag_list if tag not in HU_TAGS]


def next_active_index(state, after):
    for distance in range(1, 5):
        index = (after + distance) % 4
        if not getattr(state.player_list[index], "is_hu", False):
            return index
    raise ValueError("血战没有可行动玩家")


def advance_active(state):
    previous = state.current_player_index
    index = next_active_index(state, previous)
    # 庄家首打即和也仍按跨越原四人座位边界计巡。
    if index <= previous:
        state.xunmu += 1
    state.action_history.append(index)
    state.current_player_index = index
    append_action_tick(state, ["reset", index])


def score_event(active, winners, fan_by_winner, discarder=None):
    """同一事件的赢家互不付款；返回按逻辑座位索引的逐赢家分变。"""
    active, winners = set(active), list(winners)
    if not winners or len(set(winners)) != len(winners) or not set(winners) <= active:
        raise ValueError("非法血战赢家集合")
    if discarder is None and len(winners) != 1:
        raise ValueError("自摸只能有一位赢家")
    payers = active - set(winners)
    if discarder is not None and discarder not in payers:
        raise ValueError("放铳者必须是本事件在场的非赢家")
    result = {}
    for winner in winners:
        changes = dict.fromkeys(range(4), 0)
        for payer in payers:
            pay = 8 + (fan_by_winner[winner] if discarder is None or payer == discarder else 0)
            changes[payer] -= pay
            changes[winner] += pay
        result[winner] = changes
    return result


def _rollback_added_kong(player, tile):
    for index, combo in enumerate(player.combination_tiles):
        if combo == f"g{tile}" and 3 in player.combination_mask[index][::2]:
            player.combination_tiles[index] = f"k{tile}"
            mask = player.combination_mask[index]
            offset = next(i for i in range(0, len(mask), 2) if mask[i] == 3)
            del mask[offset:offset + 2]
            return
    raise ValueError("抢杠未找到对应的加杠副露")


def player_snapshot(player, viewer_index):
    """退场手牌保留在服务器，视图移去最后一张并在补花区末尾放独立标记。"""
    retired = getattr(player, "is_hu", False)
    own = player.player_index == viewer_index
    return {
        "is_hu": retired,
        "hu_order": getattr(player, "hu_order", 0),
        "hand_tiles_count": len(player.hand_tiles) - (1 if retired else 0),
        "hand_tiles": (player.hand_tiles[:-1] if retired else player.hand_tiles) if own else None,
        "blood_hu_tile": (player.blood_win_tile if own or not player.blood_win_is_zimo else 0) if retired else None,
        "blood_hu_zimo": player.blood_win_is_zimo if retired else None,
        "blood_hu_multi": player.blood_win_multi if retired else None,
    }


def result_for_viewer(payload, viewer_index):
    result = deepcopy(payload)
    if result.get("defer_score_settlement") and result.get("is_zimo") and result.get("hepai_player_index") != viewer_index:
        result["hepai_tile"] = 0
    return result


async def _broadcast(state, **payload):
    state.blood_last_result = deepcopy(payload)
    await state.broadcast_result(**payload)


async def settle_win(state):
    """处理本次确认的和牌申请，冻结计分但不入账。"""
    source = state.current_player_index
    zimo = state.hu_class == "hu_self"
    qianggang = not zimo and state.jiagang_tile is not None
    if zimo:
        claims = {source: "hu_self"}
        tile = state.player_list[source].hand_tiles[-1]
    else:
        claims = dict(state.blood_pending_claims)
        tile = state.jiagang_tile if qianggang else state.player_list[source].discard_tiles[-1]
    active = [p.player_index for p in state.player_list if not p.is_hu]
    winners = sorted(claims, key=lambda i: (i - source) % 4)
    confirmed = {}
    for winner in winners:
        if winner not in active or (not zimo and winner == source):
            continue
        result = state.result_dict.get(claims[winner])
        if result is None:
            continue
        fan, fan_list = result
        flowers = len(state.player_list[winner].huapai_list)
        if fan - flowers >= state.hepai_limit:
            confirmed[winner] = (fan, list(fan_list))
    winners = [winner for winner in winners if winner in confirmed]
    state.blood_pending_claims = {}
    if not winners:
        raise ValueError("血战和牌申请没有合法赢家")
    changes = score_event(active, winners, {w: confirmed[w][0] for w in winners}, None if zimo else source)
    if not zimo:
        if qianggang:
            _rollback_added_kong(state.player_list[source], tile)
            state.jiagang_tile = None
        else:
            state.player_list[source].discard_tiles.pop()
            state.player_list[source].discard_origin_tiles.append(tile)
        # 一炮多响也只有一张公开实体牌。
        state.blood_public_win_tiles.append(tile)

    event_id = len(state.blood_win_events) + 1
    for order, winner in enumerate(winners):
        player = state.player_list[winner]
        if not zimo:
            player.hand_tiles.append(tile)
        fan, fan_list = confirmed[winner]
        player.is_hu = True
        player.hu_order = sum(p.is_hu for p in state.player_list)
        player.tag_list.append(HU_TAGS[player.hu_order - 1])
        player.blood_win_tile = tile
        player.blood_win_is_zimo = zimo
        player.blood_win_multi = len(winners) > 1
        clear_draw_slot(player)
        player.waiting_tiles = set()
        record = {
            "event_id": event_id, "winner": winner, "hu_class": claims[winner],
            "fan": fan, "fan_list": fan_list, "changes": changes[winner],
            "hand": list(player.hand_tiles), "flowers": list(player.huapai_list),
            "melds": deepcopy(player.combination_mask), "tile": tile,
            "is_zimo": zimo, "discarder": None if zimo else source,
            "applied": False,
        }
        state.blood_win_events.append(record)
        counter = player.record_counter
        counter.zimo_times += int(zimo)
        counter.dianhe_times += int(not zimo)
        counter.recorded_fans.append(fan_list)
        counter.win_score += fan
        counter.win_turn += state.xunmu
        if not zimo:
            state.player_list[source].record_counter.fangchong_times += 1
            state.player_list[source].record_counter.fangchong_score += fan
        player_action_record_hu(
            state, claims[winner], fan, fan_list, winner, [0] * 4,
            hepai_tile=tile, multi_ron=len(winners) > 1,
            ron_discarder_index=source if not zimo else None,
            recycle_discard=(order == len(winners) - 1) if not zimo else None,
        )
        await state.broadcast_refresh_player_tag_list()
        await _broadcast(
            state, hu_class=claims[winner], hepai_player_index=winner,
            hepai_tile=tile, is_zimo=zimo, is_qianggang=qianggang,
            multi_ron=len(winners) > 1, ron_discarder_index=None if zimo else source,
            recycle_discard=not zimo and order == len(winners) - 1,
            suppress_hand_reveal=True, defer_score_settlement=True,
            blood_battle_step="mid_win", blood_event_id=event_id,
            round_continues=True, next_status="round_continue",
        )
        await asyncio.sleep(hu_result_ready_pre_panel_seconds())
    state.result_dict = {}
    state.hu_class = None
    state.current_player_index = winners[-1]
    state.game_status = "END" if sum(p.is_hu for p in state.player_list) >= 3 else "deal_card"


async def finish_round(state, scores_before):
    """终局才结清冻结账单；不调用川麻查叫/退税，也不触发国标单家局终分支。"""
    if state.blood_finished:
        return
    state.blood_finished = True
    events = state.blood_win_events
    reason = "three_winners" if sum(p.is_hu for p in state.player_list) == 3 else "wall_exhausted"
    next_status = "match_end" if state.current_round >= state.max_round * 4 else "round_end_by_ready"
    state.next_status = next_status
    from .combination_mask_view import build_revealed_angang_masks
    hands = {p.player_index: list(p.hand_tiles) for p in state.player_list}
    revealed_kongs = build_revealed_angang_masks(state.player_list)
    if events:
        append_action_tick(state, ["blood", "reveal_hu", hands])
        await _broadcast(
            state, hu_class="blood_reveal", blood_battle_step="reveal_hu",
            liuju_hu_hands=hands, revealed_angang_masks=revealed_kongs,
            blood_end_reason=reason, next_status="round_continue",
        )
        await asyncio.sleep(hu_result_ready_pre_panel_seconds())
    for index, record in enumerate(events):
        final = index == len(events) - 1
        if not record["applied"]:
            for player in state.player_list:
                player.score += record["changes"][player.player_index]
            record["applied"] = True
        totals = {p.player_index: p.score - scores_before[p.original_player_index] for p in state.player_list}
        append_action_tick(state, [
            "blood", "settle_hu", record["hu_class"], record["winner"], record["fan"],
            record["fan_list"], [record["changes"][i] for i in range(4)], int(final), record["flowers"],
        ])
        await _broadcast(
            state, hu_class=record["hu_class"], hepai_player_index=record["winner"],
            hu_score=record["fan"], hu_fan=record["fan_list"],
            hepai_player_hand=record["hand"], hepai_player_huapai=record["flowers"],
            hepai_player_combination_mask=record["melds"],
            player_to_score={p.player_index: p.score for p in state.player_list},
            score_changes=record["changes"], blood_round_changes=totals,
            blood_battle_step="settle_hu", blood_event_id=record["event_id"],
            blood_end_reason=reason, liuju_status_final=final, liuju_hu_hands=hands,
            revealed_angang_masks=revealed_kongs, skip_hand_reveal=True, silent=True,
            next_status=next_status if final else "round_continue",
        )
        if not final:
            await asyncio.sleep(sichuan_settle_hu_panel_wait_seconds(len(record["fan_list"])))
    if not events:
        append_action_tick(state, ["liuju"])
        await _broadcast(
            state, hu_class="liuju", blood_battle_step="final", blood_end_reason=reason,
            player_to_score={p.player_index: p.score for p in state.player_list},
            score_changes=dict.fromkeys(range(4), 0), blood_round_changes=dict.fromkeys(range(4), 0),
            revealed_angang_masks=revealed_kongs, next_status=next_status,
        )
    record_fulu_rounds_for_players(state.player_list)
    for player in state.player_list:
        delta = player.score - scores_before[player.original_player_index]
        player.record_counter.round_score_total += delta
        player.score_history.append(f"{delta:+03d}" if delta else "0")
        player.round_number_history.append(state.current_round)
    player_action_record_round_end(state)
    if not events:
        await asyncio.sleep(liuju_ready_wait_seconds())
    elif next_status == "match_end":
        await asyncio.sleep(sichuan_settle_hu_panel_wait_seconds(len(events[-1]["fan_list"]), is_final=True))
    else:
        from .boardcast import broadcast_ready_status
        await run_hu_result_ready_phase(state, len(events[-1]["fan_list"]), broadcast_ready_status, pre_panel_delay_sec=0)
