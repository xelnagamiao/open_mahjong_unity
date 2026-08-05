"""同种子全庄墙钟：四席高性能 vs 四席牌效各 1 场。

口径对齐 smoke / drawrate / tournament：
  guobiao/standard、hepai_limit=8、tactical_call=true、game_round=4、
  fast_sleep + _BOT_DELAY=0.02。

默认种子 72001（与 drawrate 一致）。从 open_mahjong_server：

    python -m server.gamestate.public.ai.guobiao_heuristic_vs_paixiao_wallclock
    python -m server.gamestate.public.ai.guobiao_heuristic_vs_paixiao_wallclock --seed 72001
"""
from __future__ import annotations

import argparse
import asyncio
import os
import sys

_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

from server.gamestate.public.ai import guobiao_heuristic_tournament_vs_paixiao as tour  # noqa: E402
from server.gamestate.public.ai import test_guobiao_heuristic_smoke as smoke  # noqa: E402

DEFAULT_SEED = 72001
DEFAULT_GAME_ROUND = 4
DEFAULT_MATCH_TIMEOUT = 1800.0  # 放宽；实测全庄通常远低于此


def _fmt_row(label: str, st) -> str:
    err = getattr(st, "error", None) or "-"
    scores = getattr(st, "final_scores", None) or []
    ok = "OK" if not getattr(st, "error", None) else "FAIL"
    return (
        f"| {label} | {getattr(st, 'elapsed_sec', 0):.1f} | "
        f"{getattr(st, 'hands', 0)} | {getattr(st, 'hu', 0)} | "
        f"{getattr(st, 'liuju', 0)} | {scores} | {ok} | {err} |"
    )


async def _run(*, seed: int, game_round: int, match_timeout: float) -> None:
    print(
        f"=== 同种子全庄墙钟对照 ===\n"
        f"seed={seed} game_round={game_round} match_timeout={match_timeout}\n"
        f"测速: fast_sleep + _BOT_DELAY=0.02\n"
        f"高性能 user_id=3 / 牌效罗伯特 user_id=2\n",
        flush=True,
    )

    print(f"--- 1/2 四席高性能罗伯特 seed={seed} ---", flush=True)
    heur = await smoke.run_one_match(
        seed=seed,
        tactical_call=True,
        game_round=game_round,
        match_timeout=match_timeout,
    )
    print(
        f"高性能: {heur.elapsed_sec:.1f}s hands={heur.hands} "
        f"hu={heur.hu} liuju={heur.liuju} scores={heur.final_scores} "
        f"err={heur.error or '-'}",
        flush=True,
    )

    # tournament runner 支持任意 player_list；全牌效用 [2,2,2,2]
    tour.MATCH_TIMEOUT = match_timeout
    tour.GAME_ROUND = game_round
    print(f"--- 2/2 四席牌效罗伯特 seed={seed} ---", flush=True)
    paixiao = await tour.run_one_match(
        seed=seed,
        player_list=[tour.PAIXIAO_USER_ID] * 4,
        mode="4px",
    )
    print(
        f"牌效: {paixiao.elapsed_sec:.1f}s hands={paixiao.hands} "
        f"hu={paixiao.hu} liuju={paixiao.liuju} scores={paixiao.final_scores} "
        f"err={paixiao.error or '-'}",
        flush=True,
    )

    print(
        "\n| 策略 | 墙钟(s) | hands | hu | liuju | final scores | status | error |\n"
        "|---|---:|---:|---:|---:|---|---|---|\n"
        + _fmt_row("高性能×4", heur)
        + "\n"
        + _fmt_row("牌效×4", paixiao)
        + "\n",
        flush=True,
    )


def main() -> None:
    p = argparse.ArgumentParser(description="同种子全庄：高性能 vs 牌效墙钟对照")
    p.add_argument("--seed", type=int, default=DEFAULT_SEED)
    p.add_argument("--game-round", type=int, default=DEFAULT_GAME_ROUND)
    p.add_argument("--match-timeout", type=float, default=DEFAULT_MATCH_TIMEOUT)
    args = p.parse_args()
    asyncio.run(
        _run(
            seed=int(args.seed),
            game_round=max(1, int(args.game_round)),
            match_timeout=float(args.match_timeout),
        )
    )


if __name__ == "__main__":
    main()
