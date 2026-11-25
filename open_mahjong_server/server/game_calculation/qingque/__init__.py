"""Pure-Python Qingque 13-tile mahjong calculator package.

Stateless and safe for multi-threaded and Python 3.14 InterpreterPoolExecutor
execution without locks.
"""

from .hepai import qingque_hepai_check
from .scoring import qingque_get_base_point
from .tingpai import qingque_tingpai_check

__all__ = [
    "qingque_hepai_check",
    "qingque_tingpai_check",
    "qingque_get_base_point",
]
