"""Fan cache loading and scoring for Qingque 13-tile mahjong.

Pure Python, stateless port of QingqueScoring.cs.
Loads fan_cache.json lazily into an immutable read-only dict per interpreter.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

from .fans import OCCASIONAL_FAN_VALUES, QingqueFan

# Default path: server/game_calculation/Qingque13Calc/fan_cache.json
_DEFAULT_FAN_CACHE_PATH = (
    Path(__file__).resolve().parent.parent / "Qingque13Calc" / "fan_cache.json"
)

# Lazily populated read-only mapping: 96-char bitset code -> fan float value.
# Safe across threads (atomic reference assignment) and subinterpreters (each
# interpreter has its own module namespace).
_FAN_CACHE: Optional[Dict[str, float]] = None


def load_fan_cache(file_path: Optional[Path | str] = None) -> Dict[str, float]:
    """Lazily load fan_cache.json and return the read-only lookup dict."""
    global _FAN_CACHE
    if _FAN_CACHE is not None and file_path is None:
        return _FAN_CACHE

    path = Path(file_path) if file_path is not None else _DEFAULT_FAN_CACHE_PATH
    if not path.is_file():
        cache: Dict[str, float] = {}
    else:
        raw = json.loads(path.read_text(encoding="utf-8"))
        entries = raw.get("fan_cache", [])
        cache = {entry["code"]: float(entry["value"]) for entry in entries}

    if file_path is None:
        _FAN_CACHE = cache
    return cache


def generate_fan_code(fans: Iterable[QingqueFan]) -> str:
    """Generate the 96-character bitset key matching C# GenerateFanCode."""
    bits = ["0"] * 96
    for fan in fans:
        idx = int(fan)
        if 0 <= idx < 96:
            bits[95 - idx] = "1"
    return "".join(bits)


def get_fan(
    fans: Iterable[QingqueFan],
    cache: Optional[Dict[str, float]] = None,
) -> float:
    """Look up fan score for a fan set in fan_cache, with fallback."""
    lookup = cache if cache is not None else load_fan_cache()
    code = generate_fan_code(fans)
    val = lookup.get(code)
    if val is not None:
        return val

    total = 0.0
    for fan in fans:
        occ = OCCASIONAL_FAN_VALUES.get(fan)
        if occ is not None:
            total += occ
    return total


def get_max_fan(
    all_fans: Sequence[Sequence[QingqueFan]],
    cache: Optional[Dict[str, float]] = None,
) -> Tuple[float, List[QingqueFan]]:
    """Compute maximum fan score across all decompositions.

    Separates occasional fans before cache lookup, adds their values back,
    and preserves C# HashSet iteration order (regular fans first, then
    occasional fans).
    """
    lookup = cache if cache is not None else load_fan_cache()
    max_fan = 0.0
    best_fans: List[QingqueFan] = []

    for fans in all_fans:
        regular: List[QingqueFan] = []
        occasional: List[QingqueFan] = []
        occasional_value = 0.0

        for f in fans:
            occ = OCCASIONAL_FAN_VALUES.get(f)
            if occ is not None:
                occasional.append(f)
                occasional_value += occ
            else:
                regular.append(f)

        current_fan = get_fan(regular, lookup) + occasional_value
        if current_fan > max_fan:
            max_fan = current_fan
            best_fans = regular + occasional

    return max_fan, best_fans


def qingque_get_base_point(fan: float) -> int:
    """Calculate base point matching C# (int)Math.Round(fan * fan).

    Both C# Math.Round(double) and Python round(float) use round-half-to-even
    (banker's rounding).
    """
    return int(round(float(fan) * float(fan)))
