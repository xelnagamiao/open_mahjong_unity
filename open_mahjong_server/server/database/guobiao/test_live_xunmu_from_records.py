"""用新牌谱 ticks 回放服务端 player_index_go_to，对照分析器周巡目。"""
from types import SimpleNamespace
from typing import Any, Dict

from server.database.guobiao.record_analyzer import (
    CLAIM_CODES,
    _parse_hu_tick,
    _tick_int,
    reconstruct_round_win_turns,
    round_start_player,
)
from server.gamestate.public.logic_common import player_index_go_to, player_index_next


def _live_state(dealer: int = 0):
    return SimpleNamespace(
        current_player_index=dealer,
        xunmu=1,
        action_history=[],
        player_list=[SimpleNamespace(discard_tiles=[]) for _ in range(4)],
    )


def replay_round_server_xunmu(rd: Dict[str, Any]) -> Dict[int, int]:
    """按对局中的 player_index_go_to / 切牌入河 / 鸣牌 pop 河牌，重建每位 seat 的和巡。"""
    ticks = rd.get("action_ticks") or []
    if not isinstance(ticks, list):
        return {}
    dealer = round_start_player(rd)
    state = _live_state(dealer)
    win_turn_by_seat: Dict[int, int] = {}

    for tick in ticks:
        if not isinstance(tick, list) or not tick:
            continue
        code = tick[0]
        if code == "end":
            break
        if code == "reset":
            seat = _tick_int(tick, 1, state.current_player_index)
            if seat is not None:
                player_index_go_to(state, seat % 4)
            continue
        if code in ("bh", "bd"):
            seat = _tick_int(tick, 2, state.current_player_index)
            if seat is not None:
                player_index_go_to(state, seat % 4)
            continue
        if code in ("d", "mo"):
            explicit = _tick_int(tick, 2)
            if explicit is not None and 0 <= explicit <= 3:
                player_index_go_to(state, explicit)
            else:
                player_index_next(state)
            continue
        if code == "gd":
            explicit = _tick_int(tick, 2)
            if explicit is not None and 0 <= explicit <= 3:
                player_index_go_to(state, explicit)
            continue
        if code == "c":
            state.player_list[state.current_player_index].discard_tiles.append(tick[1] if len(tick) > 1 else 0)
            continue
        if code in CLAIM_CODES:
            river = state.player_list[state.current_player_index].discard_tiles
            if river:
                river.pop(-1)
            seat = _tick_int(tick, 2)
            if seat is not None:
                player_index_go_to(state, seat % 4)
            continue
        hu = _parse_hu_tick(tick)
        if hu:
            yaku = hu["yaku"]
            is_cuohe = isinstance(yaku, list) and any("错和" in str(f) for f in yaku)
            if not is_cuohe:
                win_seat = hu["winner_seat"]
                win_turn_by_seat[win_seat] = win_turn_by_seat.get(win_seat, 0) + state.xunmu
    return win_turn_by_seat


def replay_round_server_xunmu_sticky(rd: Dict[str, Any]) -> Dict[int, int]:
    """同 player_index_go_to，但庄河用「曾经切过」而不是 pop 后可能变空。"""
    ticks = rd.get("action_ticks") or []
    if not isinstance(ticks, list):
        return {}
    dealer = round_start_player(rd)
    state = _live_state(dealer)
    win_turn_by_seat: Dict[int, int] = {}

    for tick in ticks:
        if not isinstance(tick, list) or not tick:
            continue
        code = tick[0]
        if code == "end":
            break
        if code == "reset":
            seat = _tick_int(tick, 1, state.current_player_index)
            if seat is not None:
                player_index_go_to(state, seat % 4)
            continue
        if code in ("bh", "bd"):
            seat = _tick_int(tick, 2, state.current_player_index)
            if seat is not None:
                player_index_go_to(state, seat % 4)
            continue
        if code in ("d", "mo"):
            explicit = _tick_int(tick, 2)
            if explicit is not None and 0 <= explicit <= 3:
                player_index_go_to(state, explicit)
            else:
                player_index_next(state)
            continue
        if code == "gd":
            explicit = _tick_int(tick, 2)
            if explicit is not None and 0 <= explicit <= 3:
                player_index_go_to(state, explicit)
            continue
        if code == "c":
            state.player_list[state.current_player_index].discard_tiles.append(tick[1] if len(tick) > 1 else 0)
            continue
        if code in CLAIM_CODES:
            seat = _tick_int(tick, 2)
            if seat is not None:
                player_index_go_to(state, seat % 4)
            continue
        hu = _parse_hu_tick(tick)
        if hu:
            yaku = hu["yaku"]
            is_cuohe = isinstance(yaku, list) and any("错和" in str(f) for f in yaku)
            if not is_cuohe:
                win_seat = hu["winner_seat"]
                win_turn_by_seat[win_seat] = win_turn_by_seat.get(win_seat, 0) + state.xunmu
    return win_turn_by_seat


def _new_format_sample_round() -> Dict[str, Any]:
    """新格式：reset + bh/bd 带座位 + d/c 无行动者。"""
    return {
        "current_round": 1,
        "seats": [0, 1, 2, 3],
        "start_player_index": 0,
        "dealer_index": 0,
        "action_ticks": [
            ["bh", 51, 0, "F"],
            ["bd", 12, 0],
            ["bh", 52, 2, "F"],
            ["bd", 13, 2],
            ["reset", 0],
            ["c", 11, "F"],
            ["d", 21],
            ["c", 21, "T"],
            ["d", 22],
            ["c", 22, "T"],
            ["d", 23],
            ["p", 23, 1],
            ["c", 24, "F"],
            ["d", 25],
            ["c", 25, "T"],
            ["d", 26],
            ["c", 26, "T"],
            ["d", 27],
            ["hu_self", 0, 8, ["平胡"], [8, -8, 0, 0]],
            ["end"],
        ],
    }


def test_new_format_analyzer_matches_sticky_server_go_to():
    rd = _new_format_sample_round()
    assert reconstruct_round_win_turns(rd) == replay_round_server_xunmu_sticky(rd)


def test_claimed_dealer_discard_pop_can_lag_analyzer():
    """庄第一张被碰后 live 河空，分析器 sticky 仍加巡。"""
    rd = {
        "start_player_index": 0,
        "action_ticks": [
            ["reset", 0],
            ["c", 11, "F"],
            ["p", 11, 2],
            ["c", 12, "F"],
            ["d", 13],
            ["c", 13, "T"],
            ["d", 14],
            ["c", 14, "T"],
            ["d", 14],
            ["hu_self", 0, 8, ["平胡"], [8, 0, 0, 0]],
        ],
    }
    sticky = replay_round_server_xunmu_sticky(rd)
    popped = replay_round_server_xunmu(rd)
    analyzer = reconstruct_round_win_turns(rd)
    assert analyzer == sticky
    assert analyzer[0] == 2
    assert popped[0] == 1


def _real_new_format_round_bwtc57fs2k() -> Dict[str, Any]:
    """2026-08-12 对局 BwTc57fS2K 第 1 局 ticks（新格式：seats + reset + bd 带座位）。"""
    return {
        "current_round": 1,
        "seats": [0, 1, 2, 3],
        "start_player_index": 0,
        "dealer_index": 0,
        "action_ticks": [
            ["bh", 54, 1, "F"],
            ["bd", 26, 1],
            ["bh", 57, 2, "F"],
            ["bd", 17, 2],
            ["bh", 51, 2, "F"],
            ["bd", 14, 2],
            ["bh", 58, 3, "F"],
            ["bd", 11, 3],
            ["reset", 0],
            ["c", 47, "F"],
            ["d", 56],
            ["bh", 56, 1, "T"],
            ["bd", 24, 1],
            ["c", 46, "F"],
            ["p", 46, 3, 46, 46],
            ["c", 26, "F"],
            ["d", 16],
            ["c", 44, "F"],
            ["d", 32],
            ["c", 44, "F"],
            ["d", 25],
            ["c", 33, "F"],
            ["d", 44],
            ["c", 44, "T"],
            ["d", 18],
            ["c", 38, "F"],
            ["d", 35],
            ["hu_self", 1, 11, ["平胡"], [-19, 57, -19, -19], 35],
            ["end"],
        ],
    }


def test_real_new_format_round_matches_server_go_to():
    rd = _real_new_format_round_bwtc57fs2k()
    sticky = replay_round_server_xunmu_sticky(rd)
    popped = replay_round_server_xunmu(rd)
    analyzer = reconstruct_round_win_turns(rd)
    assert analyzer == sticky == popped
    assert analyzer[1] == 3


def test_real_round_without_reset_undercounts():
    """对照：故意剥掉 reset 后，分析器会把补花后指针留在北家，少计 1 巡。
    生产库已在 2026-08-31 用 guobiao_record_reset_xunmu_v2 回填，现存国标小局都有 reset。
    """
    rd = _real_new_format_round_bwtc57fs2k()
    stripped = {
        **rd,
        "action_ticks": [t for t in rd["action_ticks"] if t[0] != "reset"],
    }
    assert reconstruct_round_win_turns(stripped)[1] == 2
    assert replay_round_server_xunmu_sticky(stripped)[1] == 2


def test_full_cycle_without_claim_all_agree():
    rd = {
        "start_player_index": 0,
        "seats": [0, 1, 2, 3],
        "action_ticks": [
            ["reset", 0],
            ["c", 11, "F"],
            ["d", 12],
            ["c", 12, "T"],
            ["d", 13],
            ["c", 13, "T"],
            ["d", 14],
            ["c", 14, "T"],
            ["d", 15],
            ["hu_self", 0, 8, ["平胡"], [8, 0, 0, 0]],
        ],
    }
    assert reconstruct_round_win_turns(rd) == replay_round_server_xunmu(rd)
    assert reconstruct_round_win_turns(rd)[0] == 2
