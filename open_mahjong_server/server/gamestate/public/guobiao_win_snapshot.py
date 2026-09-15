"""国标最高番快照：结算时采集；旧牌谱按动作追溯，不重新计算历史番数。"""
from collections import Counter
from copy import deepcopy
import json

from ...game_calculation.guobiao_hepai_check import Chinese_Hepai_Check


SNAPSHOT_VERSION = 1
WIN_ACTIONS = frozenset(("hu_self", "hu_first", "hu_second", "hu_third"))
TILES = frozenset(range(11, 20)) | frozenset(range(21, 30)) | frozenset(range(31, 40)) | frozenset(range(41, 48))
FAN_VALUES = {
    name: Chinese_Hepai_Check.count_model_dict.get(key, 0)
    for key, name in Chinese_Hepai_Check.eng_to_chinese_dict.items()
}


def fan_list(value):
    if isinstance(value, str):
        value = json.loads(value) if value.startswith("[") else [value]
    return [str(item) for item in (value or [])]


def is_valid_win(tick):
    return (len(tick) >= 4 and tick[0] in WIN_ACTIONS
            and int(tick[2]) > 0 and "错和" not in fan_list(tick[3]))


def win_order(win):
    return (win["total_fan"], int(win["round_index"]), int(win["action_index"]))


def make_snapshot(hand, melds, flowers, *, total_fan, fans, win_type,
                  winning_tile, round_index, action_index, combination_mask=None):
    hand = [int(tile) for tile in hand]
    melds = [[int(tile) for tile in group] for group in melds]
    all_tiles = hand + [tile for group in melds for tile in group]
    if len(hand) + 3 * len(melds) != 14 or any(len(group) not in (3, 4) for group in melds):
        raise ValueError("和牌手牌与副露数量不完整")
    if any(tile not in TILES for tile in all_tiles) or any(count > 4 for count in Counter(all_tiles).values()):
        raise ValueError("和牌牌面包含非法牌号或超过四张同牌")
    if winning_tile not in hand:
        raise ValueError("和牌张未包含在手牌中")
    fans = fan_list(fans)
    names = [name.split("*", 1)[0] for name in fans]
    main_fan = max(names, key=lambda name: FAN_VALUES.get(name, 0), default="和牌")
    return {
        "total_fan": int(total_fan), "fan_name": main_fan, "fans": fans,
        "win_type": win_type, "winning_tile": int(winning_tile),
        "concealed_tiles": sorted(hand), "melds": melds,
        "flower_tiles": list(flowers), "combination_mask": deepcopy(combination_mask or []),
        "round_index": int(round_index), "action_index": int(action_index),
    }


def capture_guobiao_win(gs, hu_class, hu_score, hu_fan, player_index, winning_tile):
    """只在真实结算牌谱 tick 写入时调用；错和既不加和牌张，也不覆盖最高番。"""
    record = gs.game_record
    if record.get("game_title", {}).get("rule") != "guobiao":
        return
    if not is_valid_win([hu_class, player_index, hu_score, hu_fan]):
        return
    player = gs.player_list[player_index]
    masks = player.combination_mask
    snapshot = make_snapshot(
        player.hand_tiles, [mask[1::2] for mask in masks], player.huapai_list,
        total_fan=hu_score, fans=hu_fan, win_type=hu_class,
        winning_tile=winning_tile, round_index=gs.round_index,
        action_index=gs.player_action_tick, combination_mask=masks,
    )
    best = record.setdefault("player_best_wins", {})
    key = str(player.user_id)
    if key not in best or win_order(snapshot) > win_order(best[key]):
        best[key] = snapshot


def _remove(hand, tiles):
    for tile in tiles:
        # 历史数据缺牌时必须报错，不能虚构手牌再当作成功回填。
        hand.remove(tile)


def _round_wins(record, round_data, round_index):
    title = record["game_title"]
    seats = [int(seat) for seat in round_data.get("seats", [0, 1, 2, 3])]
    if sorted(seats) != [0, 1, 2, 3]:
        raise ValueError("无效的换位映射")
    hands = [list(round_data[f"p{seat}_tiles"]) for seat in range(4)]
    melds = [[] for _ in range(4)]
    flowers = [[] for _ in range(4)]
    drawn = [False] * 4
    current = int(round_data.get("start_player_index", 0))
    last_winnable = None
    ticks = round_data.get("action_ticks") or []
    last_win = max(i for i, tick in enumerate(ticks) if is_valid_win(tick))
    for index, tick in enumerate(ticks[:last_win + 1], 1):
        if not tick:
            continue
        action = tick[0]
        if action in ("ask_hand", "ask_other", "ca", "liuju", "end"):
            continue
        if action == "reset":
            current = int(tick[1])
            continue
        if action in WIN_ACTIONS:
            if not is_valid_win(tick):
                continue
            winner = int(tick[1])
            hand = list(hands[winner])
            winning_tile = int(tick[5]) if len(tick) > 5 and tick[5] is not None else None
            if action == "hu_self":
                winning_tile = winning_tile or (hand[-1] if hand else None)
            else:
                winning_tile = winning_tile or last_winnable
                if drawn[winner] and hand:
                    hand.pop()
                if len(hand) % 3 == 1 and winning_tile:
                    hand.append(winning_tile)
            original = seats.index(winner)
            user_id = int(title[f"p{original}_uid"])
            yield user_id, make_snapshot(
                hand, melds[winner], flowers[winner], total_fan=tick[2], fans=tick[3],
                win_type=action, winning_tile=winning_tile,
                round_index=round_index, action_index=index,
            )
            continue
        actor = int(tick[2]) if action in ("bh", "bd", "cl", "cm", "cr", "p", "g") and len(tick) > 2 else current
        hand = hands[actor]
        tile = int(tick[1])
        if action in ("d", "gd", "bd"):
            hand.append(tile)
            drawn[actor] = True
        elif action == "c":
            _remove(hand, [tile])
            drawn[actor] = False
            last_winnable = tile
            current = (actor + 1) % 4
            continue
        elif action == "bh":
            _remove(hand, [tile])
            flowers[actor].append(tile)
            drawn[actor] = False
        elif action in ("cl", "cm", "cr", "p", "g"):
            defaults = {"cl": [tile - 2, tile - 1], "cm": [tile - 1, tile + 1],
                        "cr": [tile + 1, tile + 2], "p": [tile, tile], "g": [tile] * 3}
            count = 3 if action == "g" else 2
            removed = [int(value) for value in tick[3:3 + count]] if len(tick) >= 3 + count else defaults[action]
            _remove(hand, removed)
            melds[actor].append(sorted([tile] + removed))
            drawn[actor] = False
            last_winnable = None
        elif action == "ag":
            _remove(hand, [tile] * 4)
            melds[actor].append([tile] * 4)
            drawn[actor] = False
        elif action == "jg":
            _remove(hand, [tile])
            group = next((group for group in melds[actor] if group == [tile] * 3), None)
            if group is None:
                raise ValueError("加杠缺少对应碰牌")
            group.append(tile)
            last_winnable = tile
            drawn[actor] = False
        else:
            raise ValueError(f"不支持的国标牌谱动作: {action}")
        current = actor


def restore_guobiao_best_wins(record):
    """返回按 user_id 区分的本谱最高番及追溯错误；扫描全部局而非最近窗口。"""
    if record.get("player_best_wins_version") == SNAPSHOT_VERSION:
        return deepcopy(record.get("player_best_wins") or {}), []
    best, errors = {}, []
    for key, data in (record.get("game_round") or {}).items():
        try:
            ticks = data.get("action_ticks") or []
            if not any(is_valid_win(tick) for tick in ticks):
                continue
            round_index = int(data.get("round_index") or key.rsplit("_", 1)[-1])
            for user_id, snapshot in _round_wins(record, data, round_index):
                uid = str(user_id)
                if uid not in best or win_order(snapshot) > win_order(best[uid]):
                    best[uid] = snapshot
        except (ValueError, TypeError, KeyError, IndexError, AttributeError) as error:
            errors.append(f"{key}: {error}")
    return best, errors
