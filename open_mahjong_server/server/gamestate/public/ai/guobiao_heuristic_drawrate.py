"""全庄四席高性能：和率/流局 + 主番分布 + 半庄/全庄墙钟（多进程分片）。

口径：guobiao/standard、hepai_limit=8、tactical_call=true、game_round=4、
fast_sleep + _BOT_DELAY=0.02。默认种子 72001 起连续 N 全庄。

对齐 OMC `packages/headless/scripts/guobiao-drawrate.ts`：按全庄种子分片到
多个进程（非 asyncio.gather / 线程池；GIL 下 CPU 密集无效）。

与 test_guobiao_heuristic_smoke.py 同级；从 open_mahjong_server 目录：

    python -m server.gamestate.public.ai.guobiao_heuristic_drawrate
    python -m server.gamestate.public.ai.guobiao_heuristic_drawrate --matches 4 --workers 2 --skip-half
    python -m server.gamestate.public.ai.guobiao_heuristic_drawrate --matches 63 --workers 14
    python -m server.gamestate.public.ai.guobiao_heuristic_drawrate10

结果 JSON 默认写到 open_mahjong_server 根（该层被 gitignore；勿强行入库大 JSON）。
"""
from __future__ import annotations

import argparse
import asyncio
import json
import logging
import os
import sys
import time
import uuid
from collections import Counter
from concurrent.futures import ProcessPoolExecutor, as_completed
from types import SimpleNamespace
from typing import Any, Dict, List, Optional, Sequence, Tuple
from unittest.mock import AsyncMock, MagicMock

_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

# 主进程与 worker 都会 import；重模块延迟到 shard / match 内亦可，
# 但 FAN 表与房间构造在单测冒烟前就要可用，故顶层 import。
from server.game_calculation.game_calculation_service import GameCalculationService  # noqa: E402
from server.game_calculation.guobiao_hepai_check import Chinese_Hepai_Check  # noqa: E402
from server.gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState  # noqa: E402
import server.gamestate.public.ai.guobiao_heuristic_ai as heuristic_ai  # noqa: E402

HEURISTIC_USER_ID = 3
DEFAULT_BASE_SEED = 72001
DEFAULT_MATCHES = 63
DEFAULT_GAME_ROUND = 4
MATCH_TIMEOUT = 1800.0
MAX_TICKS = 200_000
BOT_DELAY_FAST = 0.02
DEFAULT_OUT_JSON = os.path.join(_SERVER_ROOT, "guobiao_heuristic_drawrate63_result.json")

_FAN_VALUE: Dict[str, int] = {
    Chinese_Hepai_Check.eng_to_chinese_dict[k]: v
    for k, v in Chinese_Hepai_Check.count_model_dict.items()
}


def _default_workers(matches: int) -> int:
    cpu = os.cpu_count() or 4
    # 留 1~2 核给系统；不超过 matches
    return max(1, min(matches, max(1, cpu - 2)))


def _build_room_data(*, seed: int, room_id: str, game_round: int) -> dict:
    return {
        "room_id": room_id,
        "player_list": [HEURISTIC_USER_ID] * 4,
        "player_settings": {},
        "tips": False,
        "game_round": game_round,
        "step_timer": 10,
        "round_timer": 60,
        "room_rule": "guobiao",
        "room_type": "match",
        "sub_rule": "guobiao/standard",
        "random_seed": seed,
        "open_cuohe": False,
        "cuohe_type": 0,
        "show_moqie_hint": False,
        "hepai_limit": 8,
        "tactical_call": True,
        "claim_protection": False,
        "tactical_pre_grace_delay": 0.0,
        "tactical_grace_seconds": 0.05,
        "claim_protect_delay": 0.0,
        "claim_meld_followup_gap": 0.0,
        "claim_meld_post_gap": 0.0,
        "allow_spectator": False,
        "match_queue_type": None,
        "match_tier": None,
        "event_id": None,
        "is_game_running": True,
    }


def _build_db_manager() -> MagicMock:
    db = MagicMock()
    db.get_rank_data.return_value = None
    db.store_guobiao_game_record.return_value = None
    db.store_guobiao_game_stats.return_value = None
    db.store_guobiao_fan_stats.return_value = None
    db.update_rank_data.return_value = None
    return db


def _build_game_server(db_manager: MagicMock) -> SimpleNamespace:
    gsm = SimpleNamespace(
        room_id_to_GuobiaoGameState={},
        room_id_to_QingqueGameState={},
        room_id_to_ChangshaGameState={},
        room_id_to_ClassicalGameState={},
        room_id_to_RiichiGameState={},
        room_id_to_SichuanGameState={},
        room_id_to_JiandanGameState={},
        room_id_to_TaiwanGameState={},
        gamestate_id_to_game_state={},
        user_id_to_game_state={},
        game_server=None,
    )

    async def cleanup_game_state_complete(gamestate_id: str = None, room_id: str = None):
        game_state = None
        if gamestate_id:
            game_state = gsm.gamestate_id_to_game_state.get(gamestate_id)
        if game_state is None:
            return
        for player in game_state.player_list:
            gsm.user_id_to_game_state.pop(player.user_id, None)
        gsm.room_id_to_GuobiaoGameState.pop(game_state.room_id, None)
        gsm.gamestate_id_to_game_state.pop(game_state.gamestate_id, None)
        await game_state.cleanup_game_state()

    gsm.cleanup_game_state_complete = cleanup_game_state_complete
    gsm.get_game_state_by_gamestate_id = lambda gid: gsm.gamestate_id_to_game_state.get(gid)

    room_manager = SimpleNamespace(
        rooms={},
        finish_custom_game_room=AsyncMock(),
    )

    server = SimpleNamespace(
        user_id_to_connection={},
        players={},
        db_manager=db_manager,
        calculation_service=GameCalculationService(),
        gamestate_manager=gsm,
        room_manager=room_manager,
        match_manager=None,
        friend_manager=None,
    )
    gsm.game_server = server
    return server


async def _install_fast_async():
    real_sleep = asyncio.sleep

    async def fast_sleep(delay=0, result=None):
        try:
            d = float(delay or 0)
        except (TypeError, ValueError):
            d = 0.0
        if d >= 0.15:
            await real_sleep(0)
        elif d > 0:
            await real_sleep(min(d, 0.01))
        else:
            await real_sleep(0)
        return result

    asyncio.sleep = fast_sleep  # type: ignore[assignment]
    return real_sleep


def _parse_fan_entry(entry: str) -> Tuple[str, int]:
    if "*" in entry:
        name, cnt = entry.split("*", 1)
        try:
            return name, int(cnt.strip())
        except ValueError:
            return name, 1
    return entry, 1


def _flower_count(fan_list: Sequence[str]) -> int:
    total = 0
    for entry in fan_list or []:
        name, cnt = _parse_fan_entry(entry)
        if name.startswith("花牌"):
            total += cnt
    return total


def _main_fans_ge4(fan_list: Sequence[str]) -> List[str]:
    out: List[str] = []
    for entry in fan_list or []:
        name, cnt = _parse_fan_entry(entry)
        if name.startswith("花牌"):
            continue
        val = _FAN_VALUE.get(name, 0)
        if val >= 4:
            out.extend([name] * cnt)
    return out


def _stats_to_dict(stats: "MatchStats") -> Dict[str, Any]:
    return {
        "hands": stats.hands,
        "hu": stats.hu,
        "liuju": stats.liuju,
        "zimo": stats.zimo,
        "dianhe": stats.dianhe,
        "fulu": stats.fulu,
        "hand_slots": stats.hand_slots,
        "win_fan_non_flower": list(stats.win_fan_non_flower),
        "main_fans": list(stats.main_fans),
        "ticks": stats.ticks,
        "final_scores": list(stats.final_scores),
        "elapsed_sec": stats.elapsed_sec,
        "seed": stats.seed,
        "error": stats.error,
        "ok": stats.ok,
    }


def _stats_from_dict(d: Dict[str, Any]) -> "MatchStats":
    st = MatchStats()
    st.hands = int(d.get("hands", 0) or 0)
    st.hu = int(d.get("hu", 0) or 0)
    st.liuju = int(d.get("liuju", 0) or 0)
    st.zimo = int(d.get("zimo", 0) or 0)
    st.dianhe = int(d.get("dianhe", 0) or 0)
    st.fulu = int(d.get("fulu", 0) or 0)
    st.hand_slots = int(d.get("hand_slots", 0) or 0)
    st.win_fan_non_flower = list(d.get("win_fan_non_flower") or [])
    st.main_fans = list(d.get("main_fans") or [])
    st.ticks = int(d.get("ticks", 0) or 0)
    st.final_scores = list(d.get("final_scores") or [])
    st.elapsed_sec = float(d.get("elapsed_sec", 0) or 0)
    st.seed = int(d.get("seed", 0) or 0)
    st.error = d.get("error")
    st.ok = bool(d.get("ok"))
    return st


class MatchStats:
    def __init__(self):
        self.hands = 0
        self.hu = 0
        self.liuju = 0
        self.zimo = 0
        self.dianhe = 0
        self.fulu = 0
        self.hand_slots = 0
        self.win_fan_non_flower: List[int] = []
        self.main_fans: List[str] = []
        self.ticks = 0
        self.final_scores: List[int] = []
        self.elapsed_sec = 0.0
        self.seed = 0
        self.error: Optional[str] = None
        self.ok = False


async def run_one_match(*, seed: int, game_round: int = DEFAULT_GAME_ROUND) -> MatchStats:
    """单进程内跑一全庄。子进程各自 import/初始化引擎，勿跨进程共享 game state。"""
    expected_hands = game_round * 4
    stats = MatchStats()
    stats.seed = seed
    t0 = time.perf_counter()

    real_sleep = await _install_fast_async()
    old_heur = heuristic_ai._BOT_DELAY
    heuristic_ai._BOT_DELAY = BOT_DELAY_FAST

    db = _build_db_manager()
    server = _build_game_server(db)
    room_id = f"d63-{seed}-{uuid.uuid4().hex[:8]}"
    room = _build_room_data(seed=seed, room_id=room_id, game_round=game_round)
    gamestate_id = str(uuid.uuid4())

    game: Optional[GuobiaoGameState] = None
    watchdog_task: Optional[asyncio.Task] = None
    import builtins

    real_print = builtins.print

    def quiet_print(*_a, **_k):
        return None

    logging.disable(logging.WARNING)
    builtins.print = quiet_print

    try:
        game = GuobiaoGameState(
            server,
            room,
            server.calculation_service,
            db,
            gamestate_id,
        )
        assert all(p.user_id == HEURISTIC_USER_ID for p in game.player_list)
        assert game.tactical_call is True
        assert game.hepai_limit == 8
        assert game.sub_rule == "guobiao/standard"
        assert game.max_round == game_round

        server.gamestate_manager.gamestate_id_to_game_state[gamestate_id] = game
        server.gamestate_manager.room_id_to_GuobiaoGameState[room_id] = game

        async def watchdog():
            last_tick = -1
            stagnant = 0
            while True:
                await real_sleep(0.5)
                if game is None:
                    return
                tick = getattr(game, "server_action_tick", 0)
                if tick >= MAX_TICKS:
                    raise RuntimeError(
                        f"超过 MAX_TICKS={MAX_TICKS}（当前 tick={tick}），疑似死循环"
                    )
                if tick == last_tick:
                    stagnant += 1
                    if stagnant >= 120:
                        raise RuntimeError(
                            f"对局卡死：server_action_tick 停滞于 {tick}，"
                            f"status={getattr(game, 'game_status', None)} "
                            f"round={getattr(game, 'current_round', None)}"
                        )
                else:
                    stagnant = 0
                    last_tick = tick

        watchdog_task = asyncio.create_task(watchdog())

        try:
            await asyncio.wait_for(game.game_loop_chinese(), timeout=MATCH_TIMEOUT)
        except asyncio.TimeoutError as e:
            raise RuntimeError(
                f"单场超时（>{MATCH_TIMEOUT}s），"
                f"round={getattr(game, 'current_round', None)} "
                f"tick={getattr(game, 'server_action_tick', None)} "
                f"status={getattr(game, 'game_status', None)}"
            ) from e

        if watchdog_task.done() and not watchdog_task.cancelled():
            exc = watchdog_task.exception()
            if exc:
                raise exc

        rounds = game.game_record.get("game_round", {}) or {}
        stats.hands = len(rounds)
        stats.ticks = int(getattr(game, "server_action_tick", 0) or 0)
        stats.final_scores = [int(p.score) for p in game.player_list]

        for p in game.player_list:
            rc = p.record_counter
            z = int(getattr(rc, "zimo_times", 0) or 0)
            d = int(getattr(rc, "dianhe_times", 0) or 0)
            stats.zimo += z
            stats.dianhe += d
            stats.fulu += int(getattr(rc, "fulu_times", 0) or 0)
            for fan_list in getattr(rc, "recorded_fans", []) or []:
                if not isinstance(fan_list, (list, tuple)):
                    continue
                flower = _flower_count(fan_list)
                est = 0
                for entry in fan_list:
                    name, cnt = _parse_fan_entry(entry)
                    est += _FAN_VALUE.get(name, 0) * cnt
                stats.win_fan_non_flower.append(max(0, est - flower))
                stats.main_fans.extend(_main_fans_ge4(fan_list))

        stats.hu = stats.zimo + stats.dianhe
        stats.liuju = max(0, stats.hands - stats.hu)
        stats.hand_slots = stats.hands * 4

        if stats.hands < expected_hands:
            raise RuntimeError(
                f"未打满 {expected_hands} 手，仅 {stats.hands} 手，"
                f"scores={stats.final_scores}"
            )
        stats.ok = True
    except Exception as e:
        stats.error = f"{type(e).__name__}: {e}"
        real_print(f"FAIL seed={seed}: {stats.error}", flush=True)
    finally:
        builtins.print = real_print
        logging.disable(logging.NOTSET)
        if watchdog_task is not None and not watchdog_task.done():
            watchdog_task.cancel()
            try:
                await watchdog_task
            except (asyncio.CancelledError, Exception):
                pass
        heuristic_ai._BOT_DELAY = old_heur
        asyncio.sleep = real_sleep  # type: ignore[assignment]
        stats.elapsed_sec = time.perf_counter() - t0

    return stats


def _run_shard(payload: Tuple[int, int, int]) -> Dict[str, Any]:
    """ProcessPool worker：独立事件循环跑一段连续种子。

    payload = (seed_start, n_matches, game_round)
    """
    seed_start, n_matches, game_round = payload
    logging.basicConfig(level=logging.WARNING)
    results: List[Dict[str, Any]] = []
    t0 = time.perf_counter()
    for i in range(n_matches):
        seed = seed_start + i
        st = asyncio.run(run_one_match(seed=seed, game_round=game_round))
        results.append(_stats_to_dict(st))
        status = "OK" if st.ok else f"FAIL {st.error}"
        print(
            f"  [pid {os.getpid()}] seed={seed} {st.elapsed_sec:.1f}s "
            f"hands={st.hands} hu={st.hu} liuju={st.liuju} fulu={st.fulu} {status}",
            flush=True,
        )
        if not st.ok:
            break
    return {
        "seed_start": seed_start,
        "n_matches": n_matches,
        "elapsed_sec": time.perf_counter() - t0,
        "results": results,
    }


def _fmt_top_fans(counter: Counter, hu_n: int, top_n: int = 15) -> str:
    if hu_n <= 0:
        return "(无和牌)"
    parts = []
    for name, cnt in counter.most_common(top_n):
        pct = 100.0 * cnt / hu_n
        parts.append(f"{name} {pct:.1f}%({cnt})")
    return "；".join(parts)


def _parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    p = argparse.ArgumentParser(description="国标高性能罗伯特全庄流局/番种多进程采样")
    p.add_argument("--matches", type=int, default=DEFAULT_MATCHES, help="全庄场数（默认 63）")
    p.add_argument("--seed", type=int, default=DEFAULT_BASE_SEED, help="起始全庄种子")
    p.add_argument(
        "--workers",
        type=int,
        default=0,
        help="进程数（默认 min(matches, cpu-2)；传 1 即串行）",
    )
    p.add_argument("--game-round", type=int, default=DEFAULT_GAME_ROUND, help="每场圈数（4=全庄）")
    p.add_argument("--out", type=str, default=DEFAULT_OUT_JSON, help="汇总 JSON 路径")
    p.add_argument("--skip-half", action="store_true", help="跳过半庄测速")
    return p.parse_args(argv)


async def _run_half_bench(seed: int) -> Tuple[float, Any]:
    from server.gamestate.public.ai.test_guobiao_heuristic_smoke import (  # noqa: WPS433
        run_one_match as smoke_run_one_match,
    )

    t0 = time.perf_counter()
    half = await smoke_run_one_match(
        seed=seed, tactical_call=True, game_round=2, match_timeout=MATCH_TIMEOUT
    )
    return time.perf_counter() - t0, half


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = _parse_args(argv)
    matches = max(1, int(args.matches))
    base_seed = int(args.seed)
    game_round = max(1, int(args.game_round))
    workers = int(args.workers) if int(args.workers) > 0 else _default_workers(matches)
    workers = max(1, min(workers, matches))
    out_json = args.out

    print(
        f"=== {matches} 全庄 × 四席高性能罗伯特（多进程分片）===\n"
        f"规则: guobiao/standard hepai_limit=8 tactical_call=true\n"
        f"种子: {base_seed}..{base_seed + matches - 1}\n"
        f"workers={workers}（cpu={os.cpu_count()}） game_round={game_round}\n"
        f"测速: fast_sleep + _BOT_DELAY={BOT_DELAY_FAST}\n"
        f"单场 timeout={MATCH_TIMEOUT}s\n",
        flush=True,
    )

    half_sec = None
    half_err = None
    if not args.skip_half:
        print(f"--- 半庄测速 seed={base_seed} game_round=2 ---", flush=True)
        half_sec, half = asyncio.run(_run_half_bench(base_seed))
        half_err = getattr(half, "error", None)
        print(
            f"HALF seed={base_seed} wall={half_sec:.1f}s hands={half.hands} "
            f"hu={half.hu} liuju={half.liuju} err={half_err}",
            flush=True,
        )

    # 分片 payload 使用实际 game_round
    shard_count = workers
    base = matches // shard_count
    extra = matches % shard_count
    cursor = base_seed
    shards: List[Tuple[int, int, int]] = []
    for i in range(shard_count):
        count = base + (1 if i < extra else 0)
        if count <= 0:
            continue
        shards.append((cursor, count, game_round))
        cursor += count

    print(
        f"\n--- 分片: {len(shards)} 段 "
        f"{[(s, n) for s, n, _ in shards]} ---\n",
        flush=True,
    )

    t_all = time.perf_counter()
    shard_outputs: List[Dict[str, Any]] = []

    if len(shards) == 1:
        shard_outputs.append(_run_shard(shards[0]))
    else:
        # Windows spawn：worker 重新 import 本模块；勿在 __main__ 外提交
        with ProcessPoolExecutor(max_workers=len(shards)) as pool:
            futs = {pool.submit(_run_shard, sh): sh for sh in shards}
            for fut in as_completed(futs):
                sh = futs[fut]
                try:
                    out = fut.result()
                    shard_outputs.append(out)
                    print(
                        f"  分片 seed={sh[0]}+{sh[1]} 完成 "
                        f"({out['elapsed_sec']:.1f}s 墙钟)",
                        flush=True,
                    )
                except Exception as e:
                    print(f"  分片 FAIL seed={sh[0]}: {type(e).__name__}: {e}", flush=True)
                    shard_outputs.append(
                        {
                            "seed_start": sh[0],
                            "n_matches": sh[1],
                            "elapsed_sec": 0.0,
                            "results": [
                                {
                                    "seed": sh[0],
                                    "ok": False,
                                    "error": f"{type(e).__name__}: {e}",
                                    "hands": 0,
                                    "hu": 0,
                                    "liuju": 0,
                                    "zimo": 0,
                                    "dianhe": 0,
                                    "fulu": 0,
                                    "hand_slots": 0,
                                    "win_fan_non_flower": [],
                                    "main_fans": [],
                                    "ticks": 0,
                                    "final_scores": [],
                                    "elapsed_sec": 0.0,
                                }
                            ],
                        }
                    )

    wall = time.perf_counter() - t_all

    # 按种子排序汇总（与串行口径一致）
    by_seed: Dict[int, MatchStats] = {}
    for out in shard_outputs:
        for d in out.get("results") or []:
            st = _stats_from_dict(d)
            by_seed[st.seed] = st
    results = [by_seed[s] for s in sorted(by_seed.keys())]

    ok = [r for r in results if r.ok]
    fail = [r for r in results if not r.ok]

    total_hands = sum(r.hands for r in ok)
    total_hu = sum(r.hu for r in ok)
    total_liuju = sum(r.liuju for r in ok)
    total_fulu = sum(r.fulu for r in ok)
    total_slots = sum(r.hand_slots for r in ok)
    all_fans: List[int] = []
    main_counter: Counter = Counter()
    for r in ok:
        all_fans.extend(r.win_fan_non_flower)
        main_counter.update(r.main_fans)

    hu_rate = (total_hu / total_hands * 100) if total_hands else 0.0
    liuju_rate = (total_liuju / total_hands * 100) if total_hands else 0.0
    fulu_rate = (total_fulu / total_slots * 100) if total_slots else 0.0
    avg_fan = (sum(all_fans) / len(all_fans)) if all_fans else None
    avg_qz = (wall / len(ok)) if ok else None

    top_fans = [
        {"name": n, "count": c, "pct_of_hu": (100.0 * c / total_hu) if total_hu else 0.0}
        for n, c in main_counter.most_common(20)
    ]

    payload: Dict[str, Any] = {
        "half_seed": base_seed,
        "half_wall_sec": half_sec,
        "half_error": half_err,
        "workers": workers,
        "n_matches_requested": matches,
        "n_matches_ok": len(ok),
        "n_matches_fail": len(fail),
        "seeds": f"{base_seed}..{base_seed + matches - 1}",
        "total_hands": total_hands,
        "total_hu": total_hu,
        "total_liuju": total_liuju,
        "hu_rate_pct": hu_rate,
        "liuju_rate_pct": liuju_rate,
        "fulu_rate_pct": fulu_rate,
        "avg_fan_non_flower": avg_fan,
        "zimo": sum(r.zimo for r in ok),
        "dianhe": sum(r.dianhe for r in ok),
        "wall_sec_total": wall,
        "wall_sec_63": wall,  # 兼容旧字段名
        "avg_quanzhuang_sec": avg_qz,
        "top_main_fans_ge4": top_fans,
        "per_match": [
            {
                "seed": r.seed,
                "sec": r.elapsed_sec,
                "hands": r.hands,
                "hu": r.hu,
                "liuju": r.liuju,
                "fulu": r.fulu,
                "ok": r.ok,
                "error": r.error,
            }
            for r in results
        ],
    }
    with open(out_json, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)

    print("\n" + "=" * 72)
    print("| seed  | 秒    | 手 | 和 | 流 | 副露 | 状态 |")
    print("|-------|-------|----|----|----|------|------|")
    for r in results:
        status = "OK" if r.ok else "FAIL"
        print(
            f"| {r.seed} | {r.elapsed_sec:5.1f} | {r.hands:2d} | {r.hu:2d} | "
            f"{r.liuju:2d} | {r.fulu:4d} | {status} |"
        )
    print("=" * 72)

    avg_fan_s = f"{avg_fan:.2f}（n={len(all_fans)}）" if avg_fan is not None else "-"
    avg_qz_s = f"{avg_qz:.1f}s" if avg_qz is not None else "-"
    half_s = f"{half_sec:.1f}s" if half_sec is not None else "(skipped)"

    print(
        f"\n=== 汇总 ===\n"
        f"半庄墙钟 ({base_seed}): {half_s}（基线加速前 324.5s / 近期 ~11.9s）\n"
        f"全庄: ok={len(ok)}/{matches} fail={len(fail)} workers={workers}\n"
        f"总局数: {total_hands}（期望 {matches * game_round * 4}）\n"
        f"和牌局数: {total_hu}\n"
        f"流局数: {total_liuju}\n"
        f"和率: {hu_rate:.2f}%\n"
        f"流局率: {liuju_rate:.2f}%\n"
        f"副露率: {fulu_rate:.2f}%\n"
        f"非花均番: {avg_fan_s}\n"
        f"自摸/点和: {sum(r.zimo for r in ok)}/{sum(r.dianhe for r in ok)}\n"
        f"总墙钟: {wall:.1f}s\n"
        f"单全庄均值(墙钟/ok): {avg_qz_s}\n"
        f"\n主番（≥4，出现次数/和牌次数）：\n"
        f"  {_fmt_top_fans(main_counter, total_hu)}\n"
        f"\n结果已写: {out_json}\n"
        f"\n对照基线:\n"
        f"  OMC 长样本(4030局): 流局 2.53% / 和 97.47% / 副露 69.34% / 均番 12.31；"
        f"三色三步高 34.4%\n"
        f"  Unity 63全庄(修隔步后·连步+隔步): 流局 2.18% / 副露 59.7% / 三色三步高 31.4%\n"
        f"  Unity 63全庄(修隔步前·仅连步): 流局 2.38% / 副露 58.2% / 三色三步高 23.9%\n"
        f"  OMC restore-gebu 63z: 流局 2.48% / 副露 59.3% / 三色三步高 31.1%\n"
        f"  加速前半庄: 324.5s\n",
        flush=True,
    )

    if fail:
        print("异常:")
        for r in fail:
            print(f"  - seed={r.seed}: {r.error}")
        return 1
    return 0


if __name__ == "__main__":
    logging.basicConfig(level=logging.WARNING)
    # Windows spawn 需要此守卫；ProcessPoolExecutor 在 main() 内创建
    sys.exit(main())
