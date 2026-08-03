"""10 全庄四席高性能：和率/流局 + 半庄墙钟（转发多进程脚本）。

从 open_mahjong_server 目录：

    python -m server.gamestate.public.ai.guobiao_heuristic_drawrate10
    python -m server.gamestate.public.ai.guobiao_heuristic_drawrate10 --workers 4
"""
from __future__ import annotations

import os
import sys

_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

from server.gamestate.public.ai.guobiao_heuristic_drawrate import main  # noqa: E402

if __name__ == "__main__":
    # 默认 10 全庄；其余 CLI（--workers / --seed / --skip-half）原样透传
    argv = list(sys.argv[1:])
    if "--matches" not in argv:
        out = os.path.join(_SERVER_ROOT, "guobiao_heuristic_drawrate10_result.json")
        argv = ["--matches", "10", "--out", out, *argv]
    sys.exit(main(argv))
