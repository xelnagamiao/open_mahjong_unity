import time
from types import SimpleNamespace

from .ask_timing import reconnect_clock, reconnect_remaining_time


def _clock(remaining, step, elapsed, status="waiting_hand_action"):
    now = time.time()
    player = SimpleNamespace(player_index=0, remaining_time=remaining)
    game = SimpleNamespace(
        step_time=step,
        game_status=status,
        _ask_delivered_at={0: now - elapsed},
        _ask_broadcast_time=now - elapsed,
    )
    return reconnect_clock(game, player)


def test_reconnect_clock_zero_bank_keeps_leftover_step():
    # 局时 0、步时 5、已过 4 秒：真实剩余 1 秒，必须显示 0+1 而不是 0+5
    assert _clock(0, 5, 4.0) == (0, 1)


def test_reconnect_clock_still_in_step_does_not_tax_bank():
    assert _clock(20, 5, 3.0) == (20, 2)


def test_reconnect_clock_overtime_taxes_bank_then_clears_step():
    assert _clock(20, 5, 8.0) == (17, 0)


def test_reconnect_clock_small_bank_after_step():
    # 局时 3、步时 5、已过 7 秒：剩余 1 秒局时，不再叠满步时
    assert _clock(3, 5, 7.0) == (1, 0)


def test_reconnect_clock_fresh_ask_zero_bank():
    assert _clock(0, 5, 0.0) == (0, 5)


def test_reconnect_clock_waiting_ready_has_no_step():
    assert _clock(20, 5, 3.0, status="waiting_ready") == (17, 0)


def test_reconnect_remaining_time_matches_bank_half():
    now = time.time()
    player = SimpleNamespace(player_index=1, remaining_time=0)
    game = SimpleNamespace(
        step_time=5,
        game_status="waiting_hand_action",
        _ask_delivered_at={1: now - 4},
    )
    assert reconnect_remaining_time(game, player) == 0
    assert reconnect_clock(game, player)[1] == 1


def test_reconnect_clock_falls_back_to_broadcast_time():
    now = time.time()
    player = SimpleNamespace(player_index=2, remaining_time=0)
    game = SimpleNamespace(
        step_time=5,
        game_status="waiting_hand_action",
        _ask_delivered_at={},
        _ask_broadcast_time=now - 3,
    )
    assert reconnect_clock(game, player) == (0, 2)
