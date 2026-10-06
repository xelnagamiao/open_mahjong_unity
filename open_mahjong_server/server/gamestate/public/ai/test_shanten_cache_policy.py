from __future__ import annotations

from dataclasses import asdict
import os
import time

import pytest

from . import bot_executor, guobiao_shanten as gb
from .shanten_cache_config import (
    DEFAULT_BUDGETS,
    MIB,
    ShantenCacheBudgets,
    cache_budgets_from_total_mib,
    configured_shanten_cache_budgets,
)


@pytest.fixture
def fresh_caches():
    previous = ShantenCacheBudgets(**{
        name: row["budget_bytes"] for name, row in gb.shanten_cache_stats().items()
    })
    gb.configure_shanten_cache_budgets(DEFAULT_BUDGETS)
    for cache in gb._CACHES:
        cache.clear(reset_stats=True)
    yield
    gb.configure_shanten_cache_budgets(previous)
    for cache in gb._CACHES:
        cache.clear(reset_stats=True)


def test_default_budget_is_44_mib_guobiao_plus_48_mib_general():
    values = asdict(configured_shanten_cache_budgets({}))
    assert values.pop("yiban_general") == 48 * MIB
    assert sum(values.values()) == 44 * MIB


@pytest.mark.parametrize("invalid", ["", "invalid", "nan", "inf", "-inf", "1e308"])
def test_invalid_environment_values_use_defaults(invalid):
    assert configured_shanten_cache_budgets({
        "GUOBIAO_AI_CACHE_MB": invalid,
        "GENERAL_AI_CACHE_YIBAN_MB": invalid,
    }) == DEFAULT_BUDGETS


def test_legacy_total_and_independent_overrides():
    legacy = asdict(cache_budgets_from_total_mib(200))
    assert 160 * MIB - 5 <= sum(legacy.values()) <= 160 * MIB
    assert legacy["effective"] == 2 * legacy["yiban"]
    budgets = configured_shanten_cache_budgets({
        "GUOBIAO_AI_CACHE_MB": "200",
        "GUOBIAO_AI_CACHE_EFFECTIVE_MB": "16",
        "GENERAL_AI_CACHE_YIBAN_MB": "48",
        "GUOBIAO_AI_CACHE_YIBAN_MB": "-1",
    })
    assert budgets.effective == 16 * MIB
    assert budgets.yiban_general == 48 * MIB
    assert budgets.yiban == 0
    assert budgets.shanten == legacy["shanten"]


def test_general_cache_survives_guobiao_evictions(fresh_caches):
    gb.configure_shanten_cache_budgets(ShantenCacheBudgets(
        suit=8 * MIB, shanten=0, yiban=272, effective=0, yiban_general=272 * 4,
    ))
    hand = gb.counts_from_tiles([11, 12, 13, 14, 15, 16, 21, 22, 23, 37, 38, 39, 45, 45])
    assert gb.xiangting_yiban(hand) == -1
    for tile in (41, 42, 43, 44, 46, 47):
        counts = dict(hand)
        counts.pop(45)
        counts[tile] = 2
        assert gb.guobiao_shanten(counts, specials=False) == -1
    assert gb._YIBAN_CACHE.snapshot().evictions > 0
    before = gb.shanten_cache_stats()["yiban_general"]
    assert before["lookups"] == 1
    assert gb.xiangting_yiban(hand) == -1
    after = gb.shanten_cache_stats()["yiban_general"]
    assert after["hits"] == 1
    assert after["misses"] == 1
    assert gb.shanten_cache_stats()["yiban"]["entries"] == 1


def test_combination_dragon_uses_guobiao_cache(fresh_caches):
    counts = gb.counts_from_tiles(list(gb.ZUHELONG_PATTERNS[0]) + [41, 41, 41, 45, 45])
    assert gb.shanten_zuhelong(counts) == -1
    assert gb.shanten_cache_stats()["yiban"]["lookups"] > 0
    assert gb.shanten_cache_stats()["yiban_general"]["lookups"] == 0


def test_guobiao_qidui_protection_does_not_fill_general_cache(fresh_caches):
    from .guobiao_heuristic_logic import should_open_qidui_protect

    hand = [11, 11, 19, 19, 21, 21, 29, 29, 31, 31, 39, 39, 41]
    assert should_open_qidui_protect(hand, 0)
    assert gb.shanten_cache_stats()["yiban"]["lookups"] == 1
    assert gb.shanten_cache_stats()["yiban_general"]["lookups"] == 0


def test_resize_and_clear_cover_both_usages(fresh_caches):
    counts = gb.counts_from_tiles([11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 45, 45])
    expected = gb.xiangting_yiban(counts)
    gb.guobiao_shanten(counts, specials=False)
    assert gb.shanten_cache_stats()["yiban_general"]["entries"] == 1
    assert gb.shanten_cache_stats()["yiban"]["entries"] == 1
    gb.clear_shanten_cache()
    assert all(row["entries"] == 0 for row in gb.shanten_cache_stats().values())
    gb.configure_shanten_cache_budget(0)
    assert gb.xiangting_yiban(counts) == expected
    assert gb.guobiao_shanten(counts, specials=False) == expected
    assert all(row["entries"] == row["budget_bytes"] == 0
               for row in gb.shanten_cache_stats().values())


def _worker_cache_report():
    # Give the executor enough time to dispatch work to every started worker.
    time.sleep(0.04)
    return os.getpid(), gb.shanten_cache_stats()


@pytest.mark.parametrize("workers", [1, 2])
def test_real_process_pool_shares_every_part_budget(monkeypatch, workers):
    for variable in (
        "GUOBIAO_AI_CACHE_MB", "GUOBIAO_AI_CACHE_SUIT_MB",
        "GUOBIAO_AI_CACHE_SHANTEN_MB", "GUOBIAO_AI_CACHE_YIBAN_MB",
        "GUOBIAO_AI_CACHE_EFFECTIVE_MB", "GENERAL_AI_CACHE_YIBAN_MB",
    ):
        monkeypatch.delenv(variable, raising=False)
    monkeypatch.setenv("BOT_CPU_WORKERS", str(workers))
    monkeypatch.setenv("GENERAL_AI_CACHE_YIBAN_MB", "30")
    pool_budgets = configured_shanten_cache_budgets()
    parent_before = gb.shanten_cache_stats()
    with bot_executor._process_executor() as executor:
        futures = [executor.submit(_worker_cache_report) for _ in range(workers * 8)]
        reports = dict(future.result(timeout=30) for future in futures)
    assert len(reports) == workers
    shares = asdict(pool_budgets.divided(workers))
    for stats in reports.values():
        assert {name: row["budget_bytes"] for name, row in stats.items()} == shares
    for name, allowance in asdict(pool_budgets).items():
        assert sum(stats[name]["budget_bytes"] for stats in reports.values()) <= allowance
    assert gb.shanten_cache_stats() == parent_before


def test_hongque_strategies_reuse_one_structural_lru():
    from ...game_hongque import efficiency_bot, heuristic_bot

    cache = efficiency_bot._structural_value
    assert heuristic_bot._structural_value is cache
    assert cache.cache_parameters()["maxsize"] == 32768
    cache.cache_clear()
    value = efficiency_bot._structural_value(0)
    assert heuristic_bot._structural_value(0) == value
    assert cache.cache_info().currsize == 1
    assert cache.cache_info().hits == 1
