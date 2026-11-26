"""Full-branch unit tests for dynamic 1-to-64+ core CPU allocation (Python 3.14+)."""
from __future__ import annotations

import builtins
import os
import sys
from types import SimpleNamespace

import pytest

from server.runtime import cpu_allocation
from server.runtime.cpu_allocation import (
    CpuAllocation,
    _parse_positive_int,
    _read_config_cpu_budget,
    compute_pool_allocation,
    detect_available_cpus,
    resolve_server_cpu_allocation,
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
    summary = alloc.summary()
    if exp_backend == "inline":
        assert "calc=inline" in summary
    else:
        assert f"calc={exp_calc} (interpreter)" in summary


def test_parse_positive_int_all_branches():
    assert _parse_positive_int(None) is None
    assert _parse_positive_int(True) is None
    assert _parse_positive_int(False) is None
    assert _parse_positive_int("not-an-int") is None
    assert _parse_positive_int(object()) is None
    assert _parse_positive_int(0) is None
    assert _parse_positive_int(-5) is None
    assert _parse_positive_int("12") == 12
    assert _parse_positive_int(3) == 3


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

    # Default environ=None path
    default_alloc = compute_pool_allocation(2)
    assert default_alloc.cpu_budget == 2


def test_detect_available_cpus_priority_and_fallback(monkeypatch):
    assert detect_available_cpus(config_budget=6, environ={"MAHJONG_CPU_BUDGET": "16"}) == 6
    assert detect_available_cpus(config_budget=None, environ={"MAHJONG_CPU_BUDGET": "16"}) == 16
    assert detect_available_cpus(config_budget=None, environ={}) >= 1
    assert detect_available_cpus(config_budget=4) == 4

    # Fallback to 1 when process_cpu_count returns None
    monkeypatch.setattr(os, "process_cpu_count", lambda: None)
    assert detect_available_cpus(config_budget=None, environ={}) == 1


def test_resolve_server_cpu_allocation_explicit_and_config(monkeypatch):
    explicit = resolve_server_cpu_allocation(config_budget=8, environ={})
    assert explicit == CpuAllocation(cpu_budget=8, calc_workers=3, calc_backend="interpreter", bot_workers=4)

    monkeypatch.setattr(cpu_allocation, "_read_config_cpu_budget", lambda: 4)
    from_cfg = resolve_server_cpu_allocation(config_budget=None, environ={})
    assert from_cfg == CpuAllocation(cpu_budget=4, calc_workers=1, calc_backend="interpreter", bot_workers=2)


def test_read_config_cpu_budget_import_branches(monkeypatch):
    # 1) local_config present
    monkeypatch.setitem(sys.modules, "server.local_config", SimpleNamespace(Config=SimpleNamespace(cpu_budget=10)))
    assert _read_config_cpu_budget() == 10

    # 2) local_config missing -> falls back to test_config
    real_import = builtins.__import__

    def fake_import_no_local(name, globals=None, locals=None, fromlist=(), level=0):
        if name == "local_config" or name.endswith(".local_config"):
            raise ImportError("no local_config")
        return real_import(name, globals, locals, fromlist, level)

    monkeypatch.setitem(sys.modules, "server.test_config", SimpleNamespace(Config=SimpleNamespace(cpu_budget=6)))
    monkeypatch.setattr(builtins, "__import__", fake_import_no_local)
    assert _read_config_cpu_budget() == 6

    # 3) both local_config and test_config missing -> returns None
    def fake_import_neither(name, globals=None, locals=None, fromlist=(), level=0):
        if "local_config" in name or "test_config" in name:
            raise ImportError("missing config")
        return real_import(name, globals, locals, fromlist, level)

    monkeypatch.setattr(builtins, "__import__", fake_import_neither)
    assert _read_config_cpu_budget() is None
