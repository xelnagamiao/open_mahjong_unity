"""进程内 IP 注册次数限制（每日 04:00 由 server 侧定时清空）。"""
import threading
from typing import Dict


class IpRegistrationLimiter:
    DAILY_LIMIT = 3

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._counts: Dict[str, int] = {}

    def can_register(self, ip: str) -> bool:
        if not ip or ip == "unknown":
            return True
        with self._lock:
            return self._counts.get(ip, 0) < self.DAILY_LIMIT

    def record_registration(self, ip: str) -> None:
        if not ip or ip == "unknown":
            return
        with self._lock:
            self._counts[ip] = self._counts.get(ip, 0) + 1

    def reset(self) -> None:
        with self._lock:
            self._counts.clear()
