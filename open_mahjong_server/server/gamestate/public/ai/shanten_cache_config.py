"""Persistent shanten cache charges, shared by the parent and bot pool setup."""
from __future__ import annotations

from dataclasses import asdict, dataclass
import math
import os
from typing import Mapping


MIB = 1024 * 1024


@dataclass(frozen=True)
class ShantenCacheBudgets:
    """Byte allowances, allocated lazily; these are not process memory limits."""

    suit: int = 8 * MIB
    shanten: int = 12 * MIB
    yiban: int = 8 * MIB
    effective: int = 16 * MIB
    yiban_general: int = 48 * MIB

    def __post_init__(self) -> None:
        if any(type(value) is not int or value < 0 for value in asdict(self).values()):
            raise ValueError("Cache budgets must be non-negative integer bytes")

    def divided(self, workers: int) -> ShantenCacheBudgets:
        """Keep the sum of worker shares within the pool's allowances."""
        if workers < 1:
            raise ValueError("workers must be positive")
        return ShantenCacheBudgets(**{
            name: value // workers for name, value in asdict(self).items()
        })


DEFAULT_BUDGETS = ShantenCacheBudgets()
_PART_ENV = {
    "suit": "GUOBIAO_AI_CACHE_SUIT_MB",
    "shanten": "GUOBIAO_AI_CACHE_SHANTEN_MB",
    "yiban": "GUOBIAO_AI_CACHE_YIBAN_MB",
    "effective": "GUOBIAO_AI_CACHE_EFFECTIVE_MB",
    "yiban_general": "GENERAL_AI_CACHE_YIBAN_MB",
}


def _nonnegative_mib(value: object, default: float) -> float:
    try:
        result = float(value)
    except (TypeError, ValueError, OverflowError):
        return default
    return max(0.0, result) if math.isfinite(result * MIB) else default


def cache_budgets_from_total_mib(total_mib: float) -> ShantenCacheBudgets:
    """Legacy total: charge at most 80%, in the new 8:12:8:16:48 ratio.

    The remaining 20% is only a planning allowance for temporary work; it does
    not constrain decision-local memo, other rules, or the Python allocator.
    """
    defaults = asdict(DEFAULT_BUDGETS)
    default_total_mib = sum(defaults.values()) / MIB / 0.8
    total = _nonnegative_mib(total_mib, default_total_mib) * MIB * 0.8
    scale = total / sum(defaults.values())
    return ShantenCacheBudgets(**{
        name: int(value * scale) for name, value in defaults.items()
    })


def configured_shanten_cache_budgets(
    environ: Mapping[str, str] | None = None,
) -> ShantenCacheBudgets:
    """Read defaults, optional legacy total, then independent part overrides."""
    environ = os.environ if environ is None else environ
    budgets = DEFAULT_BUDGETS
    if "GUOBIAO_AI_CACHE_MB" in environ:
        budgets = cache_budgets_from_total_mib(environ["GUOBIAO_AI_CACHE_MB"])
    values = asdict(budgets)
    for name, variable in _PART_ENV.items():
        if variable in environ:
            mib = _nonnegative_mib(environ[variable], values[name] / MIB)
            values[name] = int(mib * MIB)
    return ShantenCacheBudgets(**values)
