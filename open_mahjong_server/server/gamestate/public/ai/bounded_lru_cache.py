"""Thread-safe, memory-budgeted LRU caches for bot decision hot paths."""
from __future__ import annotations

from collections import OrderedDict
from collections.abc import Iterator, MutableMapping
from dataclasses import dataclass
from threading import RLock
from typing import Generic, Optional, TypeVar


K = TypeVar("K")
V = TypeVar("V")
_MISSING = object()


@dataclass(frozen=True)
class CacheSnapshot:
    name: str
    entries: int
    estimated_bytes: int
    budget_bytes: int
    lookups: int
    hits: int
    misses: int
    evictions: int

    @property
    def hit_rate(self) -> float:
        return self.hits / self.lookups if self.lookups else 0.0


class MemoryBoundedLRUCache(MutableMapping[K, V], Generic[K, V]):
    """LRU mapping with a conservative fixed byte charge per entry.

    The cached key/value shapes are stable and were measured in the performance
    suite. A fixed padded charge keeps insertions cheap and, unlike recursive
    ``getsizeof`` calls, remains deterministic under load.
    """

    def __init__(self, name: str, budget_bytes: int, entry_charge_bytes: int):
        self.name = name
        self.budget_bytes = max(0, int(budget_bytes))
        self.entry_charge_bytes = max(1, int(entry_charge_bytes))
        self._data: OrderedDict[K, V] = OrderedDict()
        self._lock = RLock()
        self._estimated_bytes = 0
        self._lookups = 0
        self._hits = 0
        self._misses = 0
        self._evictions = 0

    def __getitem__(self, key: K) -> V:
        with self._lock:
            self._lookups += 1
            try:
                value = self._data[key]
            except KeyError:
                self._misses += 1
                raise
            self._hits += 1
            self._data.move_to_end(key)
            return value

    def get(self, key: K, default: Optional[V] = None):
        with self._lock:
            self._lookups += 1
            value = self._data.get(key, _MISSING)
            if value is _MISSING:
                self._misses += 1
                return default
            self._hits += 1
            self._data.move_to_end(key)
            return value

    def __setitem__(self, key: K, value: V) -> None:
        with self._lock:
            if self.budget_bytes <= 0:
                return
            if key in self._data:
                self._data[key] = value
                self._data.move_to_end(key)
                return
            self._data[key] = value
            self._estimated_bytes += self.entry_charge_bytes
            while self._estimated_bytes > self.budget_bytes and self._data:
                self._data.popitem(last=False)
                self._estimated_bytes -= self.entry_charge_bytes
                self._evictions += 1

    def __delitem__(self, key: K) -> None:
        with self._lock:
            del self._data[key]
            self._estimated_bytes -= self.entry_charge_bytes

    def __iter__(self) -> Iterator[K]:
        with self._lock:
            return iter(tuple(self._data.keys()))

    def __len__(self) -> int:
        with self._lock:
            return len(self._data)

    def clear(self, *, reset_stats: bool = False) -> None:
        with self._lock:
            self._data.clear()
            self._estimated_bytes = 0
            if reset_stats:
                self._lookups = 0
                self._hits = 0
                self._misses = 0
                self._evictions = 0

    def resize(self, budget_bytes: int) -> None:
        with self._lock:
            self.budget_bytes = max(0, int(budget_bytes))
            while self._estimated_bytes > self.budget_bytes and self._data:
                self._data.popitem(last=False)
                self._estimated_bytes -= self.entry_charge_bytes
                self._evictions += 1

    def snapshot(self) -> CacheSnapshot:
        with self._lock:
            return CacheSnapshot(
                name=self.name,
                entries=len(self._data),
                estimated_bytes=self._estimated_bytes,
                budget_bytes=self.budget_bytes,
                lookups=self._lookups,
                hits=self._hits,
                misses=self._misses,
                evictions=self._evictions,
            )
