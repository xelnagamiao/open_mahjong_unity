"""Unit tests for dynamic 1-to-64+ core CPU allocation (Python 3.14+)."""
from __future__ import annotations

import pytest

from server.runtime.cpu_allocation import (
    compute_pool_allocation,
    detect_available_cpus,
)


@pytest.mark.parametrize(
    ("budget", "exp_calc", "exp_backend", "exp_bot"),
    [
        (1, 0, "inline", 1),
        (2, 1, "interpreter", 1),
        (4, 1, "interpreter", 2),
        (8, 3, "interpreter", 4),
        (16, 6, "interpreter", 9),
        (32, 12, "interpreter", 16),
        (64, 12, "interpreter", 16),
        (128, 12, "interpreter", 16),
    ],
)
def test_compute_pool_allocation_matrix(budget, exp_calc, exp_backend, exp_bot):
    alloc = compute_pool_allocation(budget, environ={})
    assert alloc.cpu_budget == budget
    assert alloc.calc_workers == exp_calc
    assert alloc.calc_backend == exp_backend
    assert alloc.bot_workers == exp_bot


def test_env_overrides_and_inline_mode():
    alloc = compute_pool_allocation(
        64,
        environ={"CALC_CPU_WORKERS": "20", "BOT_CPU_WORKERS": "24"},
    )
    assert alloc.calc_workers == 20
    assert alloc.bot_workers == 24
    assert alloc.calc_backend == "interpreter"

    inline = compute_pool_allocation(
        8,
        environ={"CALC_EXECUTOR_BACKEND": "inline"},
    )
    assert inline.calc_backend == "inline"
    assert inline.calc_workers == 0
    assert inline.bot_workers == 4


def test_detect_available_cpus_priority():
    assert detect_available_cpus(config_budget=6, environ={"MAHJONG_CPU_BUDGET": "16"}) == 6
    assert detect_available_cpus(config_budget=None, environ={"MAHJONG_CPU_BUDGET": "16"}) == 16
    assert detect_available_cpus(config_budget=None, environ={}) >= 1
