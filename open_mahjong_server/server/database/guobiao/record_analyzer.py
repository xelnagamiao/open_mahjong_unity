"""
从国标牌谱 JSON 推理玩家本场指标（和牌/放铳/错和/副露/和巡等）。
供 backfill_history_stats 与 backfill_game_player_metrics 共用。
"""
from typing import Any, Dict, Optional

from .round_score_utils import _parse_score_changes, resolve_round_seats
from .store_guobiao import FAN_NAME_TO_FIELD, STACKABLE_FANS

HU_ACTIONS = frozenset({"hu_self", "hu_first", "hu_second", "hu_third"})
RON_ACTIONS = frozenset({"hu_first", "hu_second", "hu_third"})
VISIBLE_FULU_CODES = frozenset({"cl", "cm", "cr", "p", "g", "jg"})
CLAIM_CODES = frozenset({"cl", "cm", "cr", "p", "g"})
OPENING_FLOWER_CODES = frozenset({"bh", "bd"})


def _parse_hu_tick(tick: list) -> Optional[dict]:
    """统一解析和牌 tick（国标 hu_* 与日麻 hu_riichi）。"""
    if not isinstance(tick, list) or not tick:
        return None
    code = tick[0]
    if code == "hu_riichi" and len(tick) >= 7:
        hu_class = tick[2]
        if not isinstance(hu_class, str) or hu_class not in HU_ACTIONS:
            return None
        seat = _tick_int(tick, 1)
        if seat is None:
            return None
        return {
            "hu_class": hu_class,
            "winner_seat": seat % 4,
            "fan_score": int(tick[3]) if isinstance(tick[3], (int, float)) else 0,
            "yaku": tick[5] if len(tick) > 5 else [],
            "score_changes": _parse_score_changes(tick[6]),
        }
    if code in HU_ACTIONS and len(tick) >= 5:
        seat = _tick_int(tick, 1)
        if seat is None:
            return None
        return {
            "hu_class": code,
            "winner_seat": seat % 4,
            "fan_score": int(tick[2]) if isinstance(tick[2], (int, float)) else 0,
            "yaku": tick[3] if len(tick) > 3 else [],
            "score_changes": _parse_score_changes(tick[4]),
        }
    return None


def _tick_int(tick: list, index: int, default: Optional[int] = None) -> Optional[int]:
    if index >= len(tick):
        return default
    value = tick[index]
    if isinstance(value, bool):
        return default
    if isinstance(value, int):
        return value
    if isinstance(value, str) and value.lstrip("-").isdigit():
        return int(value)
    return default


def round_start_player(rd: Dict[str, Any]) -> int:
    start = rd.get("start_player_index")
    if not isinstance(start, int):
        start = rd.get("dealer_index")
    if not isinstance(start, int):
        start = 0
    return start % 4


def insert_opening_reset(ticks: list, start_player_index: int) -> bool:
    """在开局 bh/bd 前缀后插入 ['reset', start]。已有则跳过。返回是否插入。"""
    if not isinstance(ticks, list):
        return False
    start = start_player_index % 4
    index = 0
    while index < len(ticks):
        tick = ticks[index]
        if not isinstance(tick, list) or not tick:
            index += 1
            continue
        if tick[0] in OPENING_FLOWER_CODES:
            index += 1
            continue
        break
    if index < len(ticks) and isinstance(ticks[index], list) and ticks[index] and ticks[index][0] == "reset":
        return False
    ticks.insert(index, ["reset", start])
    return True


def patch_guobiao_record_resets(record: Dict[str, Any]) -> int:
    """给国标牌谱每局补上开局 reset。返回插入条数。"""
    game_round = record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return 0
    inserted = 0
    for rd in game_round.values():
        if not isinstance(rd, dict):
            continue
        ticks = rd.get("action_ticks")
        if not isinstance(ticks, list):
            ticks = []
            rd["action_ticks"] = ticks
        if insert_opening_reset(ticks, round_start_player(rd)):
            inserted += 1
    return inserted


def reconstruct_round_win_turns(rd: Dict[str, Any]) -> Dict[int, int]:
    """从一局 action_ticks 按 player_index_go_to 语义推理每位 seat 的和巡总和。"""
    ticks = rd.get("action_ticks") or []
    if not isinstance(ticks, list):
        return {}
    dealer = round_start_player(rd)
    current_seat = dealer
    history: list = []
    xunmu = 1
    dealer_discarded = False
    win_turn_by_seat: Dict[int, int] = {}

    def go_to(seat: int) -> None:
        nonlocal current_seat, xunmu
        seat = seat % 4
        if history and seat != history[-1] and seat < history[-1] and dealer_discarded:
            xunmu += 1
        history.append(seat)
        current_seat = seat

    for tick in ticks:
        if not isinstance(tick, list) or not tick:
            continue
        code = tick[0]
        if code == "end":
            break
        if code == "reset":
            seat = _tick_int(tick, 1, current_seat)
            if seat is not None:
                go_to(seat)
            continue
        if code in ("bh", "bd"):
            seat = _tick_int(tick, 2, current_seat)
            if seat is not None:
                go_to(seat)
            continue
        if code in ("d", "mo"):
            explicit = _tick_int(tick, 2)
            if explicit is not None and 0 <= explicit <= 3:
                go_to(explicit)
            else:
                go_to(0 if current_seat == 3 else current_seat + 1)
            continue
        if code == "gd":
            explicit = _tick_int(tick, 2)
            if explicit is not None and 0 <= explicit <= 3:
                go_to(explicit)
            continue
        if code == "c":
            if current_seat == dealer:
                dealer_discarded = True
            continue
        if code in CLAIM_CODES:
            seat = _tick_int(tick, 2)
            if seat is not None:
                go_to(seat)
            continue
        hu = _parse_hu_tick(tick)
        if hu:
            yaku = hu["yaku"]
            is_cuohe = isinstance(yaku, list) and any("错和" in str(f) for f in yaku)
            if not is_cuohe:
                win_seat = hu["winner_seat"]
                win_turn_by_seat[win_seat] = win_turn_by_seat.get(win_seat, 0) + xunmu
    return win_turn_by_seat


def seat_to_original_map(seats) -> Dict[int, int]:
    """seats[original] = seat → {seat: original}。缺省视为 seat==original。"""
    if not isinstance(seats, list) or len(seats) != 4:
        return {0: 0, 1: 1, 2: 2, 3: 3}
    m: Dict[int, int] = {}
    for orig, seat in enumerate(seats):
        try:
            m[int(seat)] = int(orig)
        except (TypeError, ValueError):
            pass
    if len(m) == 4:
        return m
    return {0: 0, 1: 1, 2: 2, 3: 3}


def analyze_record_for_player(record: Dict[str, Any], original_player_index: int) -> Optional[dict]:
    """从牌谱重建该玩家本场计数（zimo/dianhe/fangchong/fangchong_score/cuohe/fulu_rounds/win_score）。"""
    game_round = record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return None
    zimo = dianhe = fangchong = cuohe = fulu_rounds = 0
    win_score = fangchong_score = win_turn = 0
    for rd in game_round.values():
        if not isinstance(rd, dict):
            continue
        seat2orig = seat_to_original_map(resolve_round_seats(rd))
        my_seat = None
        for s, o in seat2orig.items():
            if o == original_player_index:
                my_seat = s
                break
        if my_seat is None:
            continue
        ticks = rd.get("action_ticks") or []
        had_fulu = False
        for tick in ticks:
            if not isinstance(tick, list) or not tick:
                continue
            code = tick[0]
            if code in VISIBLE_FULU_CODES and _tick_int(tick, 2) == my_seat:
                had_fulu = True
            hu = _parse_hu_tick(tick)
            if hu is None:
                continue
            sc = hu["score_changes"]
            if sc is None or my_seat < 0 or my_seat >= len(sc):
                continue
            yaku = hu["yaku"]
            is_cuohe = isinstance(yaku, list) and any("错和" in str(f) for f in yaku)
            hu_score = hu["fan_score"]
            my_delta = sc[my_seat]
            hu_class = hu["hu_class"]
            if is_cuohe:
                if my_delta < 0:
                    cuohe += 1
                continue
            if my_delta > 0:
                if hu_class == "hu_self":
                    zimo += 1
                else:
                    dianhe += 1
                win_score += int(hu_score)
            elif hu_class in RON_ACTIONS and my_delta < 0:
                neg = [x for x in sc if isinstance(x, (int, float)) and x < 0]
                if neg and my_delta == min(neg):
                    fangchong += 1
                    fangchong_score += int(hu_score)
        if had_fulu:
            fulu_rounds += 1
        win_turn += reconstruct_round_win_turns(rd).get(my_seat, 0)
    return {
        "zimo": zimo, "dianhe": dianhe, "fangchong": fangchong,
        "fangchong_score": fangchong_score, "cuohe": cuohe,
        "fulu_rounds": fulu_rounds, "win_score": win_score, "win_turn": win_turn,
    }


def parse_fan_increment(hu_fan: Any) -> Dict[str, int]:
    """从一次和牌的 hu_fan 列表解析番种字段增量。"""
    inc: Dict[str, int] = {}
    if not isinstance(hu_fan, list):
        return inc
    for fan_name in hu_fan:
        if not isinstance(fan_name, str):
            continue
        if "*" in fan_name:
            base_name, _, count_str = fan_name.partition("*")
            base_name = base_name.strip()
            if base_name in STACKABLE_FANS and base_name in FAN_NAME_TO_FIELD:
                try:
                    cnt = int(count_str.strip())
                except ValueError:
                    continue
                field = FAN_NAME_TO_FIELD[base_name]
                inc[field] = inc.get(field, 0) + cnt
        else:
            field = FAN_NAME_TO_FIELD.get(fan_name)
            if field:
                inc[field] = inc.get(field, 0) + 1
    return inc


def collect_fans_for_player(record: Dict[str, Any], original_player_index: int) -> Dict[str, int]:
    """从牌谱 hu tick 重建该玩家本场番种增量（跳过错和）。"""
    game_round = record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return {}
    total: Dict[str, int] = {}
    for rd in game_round.values():
        if not isinstance(rd, dict):
            continue
        seat2orig = seat_to_original_map(resolve_round_seats(rd))
        my_seat = None
        for s, o in seat2orig.items():
            if o == original_player_index:
                my_seat = s
                break
        if my_seat is None:
            continue
        for tick in rd.get("action_ticks") or []:
            if not isinstance(tick, list) or len(tick) < 4:
                continue
            code = tick[0]
            if code not in HU_ACTIONS:
                continue
            if _tick_int(tick, 1) != my_seat:
                continue
            hu_fan = tick[3] if len(tick) > 3 else []
            if isinstance(hu_fan, list) and any("错和" in str(f) for f in hu_fan):
                continue
            for field, cnt in parse_fan_increment(hu_fan).items():
                total[field] = total.get(field, 0) + cnt
    return total
