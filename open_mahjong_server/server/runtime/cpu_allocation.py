"""Dynamic CPU core budget detection and pool worker allocation (Python 3.14+, 1 to 64+ cores)."""
from __future__ import annotations

from dataclasses import dataclass
import os
from typing import Mapping, Optional

# Soft caps for a single asyncio main process so very large hosts (32/64+ cores)
# do not waste memory or dilute per-worker caches beyond what one event loop can feed.
DEFAULT_MAX_CALC_WORKERS = 12
DEFAULT_MAX_BOT_WORKERS = 16
DEFAULT_CALC_SHARE = 0.4


@dataclass(frozen=True)
class CpuAllocation:
    """Resolved CPU core allocation for the Python game server."""

    cpu_budget: int
    calc_workers: int
    calc_backend: str  # "inline" | "interpreter"
    bot_workers: int

    def summary(self) -> str:
        calc_desc = (
            "inline"
            if self.calc_backend == "inline"
            else f"{self.calc_workers} (interpreter)"
        )
        return (
            f"CPU allocation: budget={self.cpu_budget} -> "
            f"main=1, calc={calc_desc}, bot={self.bot_workers} (process)"
        )


def _parse_positive_int(raw: object) -> Optional[int]:
    if raw is None or isinstance(raw, bool):
        return None
    try:
        value = int(raw)
    except (TypeError, ValueError):
        return None
    return value if value > 0 else None


def detect_available_cpus(
    config_budget: Optional[int] = None,
    environ: Optional[Mapping[str, str]] = None,
) -> int:
    """Detect the CPU core budget available to this Python 3.14 process."""
    env = os.environ if environ is None else environ

    configured = _parse_positive_int(config_budget)
    if configured is not None:
        return configured

    env_budget = _parse_positive_int(env.get("MAHJONG_CPU_BUDGET"))
    if env_budget is not None:
        return env_budget

    detected = _parse_positive_int(os.process_cpu_count())
    return detected if detected is not None else 1


def compute_pool_allocation(
    cpu_budget: int,
    *,
    environ: Optional[Mapping[str, str]] = None,
) -> CpuAllocation:
    """Compute main/calc/bot worker counts from a CPU budget (1 to 64+ cores)."""
    env = os.environ if environ is None else environ
    budget = max(1, int(cpu_budget))

    if budget == 1:
        default_calc = 0
        default_bot = 1
    elif budget == 2:
        default_calc = 1
        default_bot = 1
    else:
        available_for_pools = budget - 1  # reserve 1 core for main asyncio loop
        default_calc = min(
            DEFAULT_MAX_CALC_WORKERS,
            max(1, round(available_for_pools * DEFAULT_CALC_SHARE)),
        )
        default_bot = min(
            DEFAULT_MAX_BOT_WORKERS,
            max(1, available_for_pools - default_calc),
        )

    calc_override = _parse_positive_int(env.get("CALC_CPU_WORKERS"))
    bot_override = _parse_positive_int(env.get("BOT_CPU_WORKERS"))

    calc_workers = calc_override if calc_override is not None else default_calc
    bot_workers = bot_override if bot_override is not None else default_bot

    requested_backend = env.get("CALC_EXECUTOR_BACKEND", "auto").strip().lower()
    if requested_backend == "inline" or calc_workers <= 0:
        calc_backend = "inline"
        calc_workers = 0
    else:
        calc_backend = "interpreter"

    return CpuAllocation(
        cpu_budget=budget,
        calc_workers=calc_workers,
        calc_backend=calc_backend,
        bot_workers=bot_workers,
    )


def resolve_server_cpu_allocation(
    config_budget: Optional[int] = None,
    *,
    environ: Optional[Mapping[str, str]] = None,
) -> CpuAllocation:
    """Detect available CPUs (respecting Config.cpu_budget) and compute pool sizes."""
    if config_budget is None:
        config_budget = _read_config_cpu_budget()
    budget = detect_available_cpus(config_budget=config_budget, environ=environ)
    return compute_pool_allocation(budget, environ=environ)


def _read_config_cpu_budget() -> Optional[int]:
    try:
        from ..local_config import Config
    except ImportError:
        try:
            from ..test_config import Config
        except ImportError:
            return None
    return _parse_positive_int(getattr(Config, "cpu_budget", None))
