"""高性能(user_id=3) vs 牌效(user_id=2) 混席锦标赛（战绩向）。

口径：guobiao/standard、hepai_limit=8、tactical_call=true、game_round=4（16 手）、
fast_sleep + _BOT_DELAY=0.02。master_seed 贯穿全庄；跳过入场座位 shuffle，固定座位。

座位（入场即终局风位，不经 master_seed 打乱）：
  2v2 对角：东0/西2=高性能(3)，南1/北3=牌效(2)  → [3,2,3,2]
  1v3：     东0=高性能(3)，南1/西2/北3=牌效(2)   → [3,2,2,2]
  3v1：     东0/南1/西2=高性能(3)，北3=牌效(2)   → [3,3,3,2]

与 test_guobiao_heuristic_smoke.py 同级；从 open_mahjong_server 目录：

    python -m server.gamestate.public.ai.guobiao_heuristic_tournament_vs_paixiao
    python -m server.gamestate.public.ai.guobiao_heuristic_tournament_vs_paixiao --modes 2v2,1v3
    python -m server.gamestate.public.ai.guobiao_heuristic_tournament_vs_paixiao --modes 2v2,1v3,3v1 --base-seed 83001 --matches 10
"""
from __future__ import annotations

import argparse
import asyncio
import logging
import os
import random
import sys
import time
import uuid
from collections import Counter, defaultdict
from types import SimpleNamespace
from typing import Any, Dict, List, Optional, Sequence, Tuple
from unittest.mock import AsyncMock, MagicMock

_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

from server.game_calculation.game_calculation_service import GameCalculationService  # noqa: E402
from server.game_calculation.guobiao_hepai_check import Chinese_Hepai_Check  # noqa: E402
from server.gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState  # noqa: E402
import server.gamestate.public.ai.guobiao_heuristic_ai as heuristic_ai  # noqa: E402
import server.gamestate.public.ai.smart_bot_ai as smart_bot_ai  # noqa: E402

PAIXIAO_USER_ID = 2
HEURISTIC_USER_ID = 3
DEFAULT_BASE_SEED = 83001
DEFAULT_MATCHES = 10
GAME_ROUND = 4
MATCH_TIMEOUT = 300.0
MAX_TICKS = 200_000
BOT_DELAY_FAST = 0.02

# 中文番名 → 番值（与 Chinese_Hepai_Check 对齐）
_FAN_VALUE: Dict[str, int] = {
    Chinese_Hepai_Check.eng_to_chinese_dict[k]: v
    for k, v in Chinese_Hepai_Check.count_model_dict.items()
}

SEAT_LAYOUTS: Dict[str, Tuple[str, List[int]]] = {
    "2v2": ("对角 东/西=高性能 南/北=牌效", [3, 2, 3, 2]),
    "1v3": ("东=高性能 南/西/北=牌效", [3, 2, 2, 2]),
    "3v1": ("东/南/西=高性能 北=牌效", [3, 3, 3, 2]),
}

WIND = ("东", "南", "西", "北")


def _build_room_data(*, seed: int, player_list: Sequence[int], room_id: str) -> dict:
    return {
        "room_id": room_id,
        "player_list": list(player_list),
        "player_settings": {},
        "tips": False,
        "game_round": GAME_ROUND,
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


def _install_no_seat_shuffle():
    """保留 master_seed 洗牌墙，但固定入场座位（不打乱 player_list）。"""
    real_shuffle = random.Random.shuffle

    def selective_shuffle(self, x, random=None):  # noqa: A002 — 对齐 Random.shuffle 签名
        if (
            isinstance(x, list)
            and len(x) == 4
            and all(hasattr(p, "user_id") and hasattr(p, "hand_tiles") for p in x)
        ):
            return None
        if random is not None:
            return real_shuffle(self, x, random)
        return real_shuffle(self, x)

    random.Random.shuffle = selective_shuffle  # type: ignore[assignment]
    return real_shuffle


def _parse_fan_entry(entry: str) -> Tuple[str, int]:
    """'花牌*2' / '平和' → (名, 复计次数)。"""
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
    """≥4 番番种（按出现计入，花牌不计）。"""
    out: List[str] = []
    for entry in fan_list or []:
        name, cnt = _parse_fan_entry(entry)
        if name.startswith("花牌"):
            continue
        val = _FAN_VALUE.get(name, 0)
        if val >= 4:
            out.extend([name] * cnt)
    return out


class SeatAgg:
    __slots__ = (
        "strategy",
        "user_id",
        "seat",
        "rank",
        "score",
        "zimo",
        "dianhe",
        "fulu",
        "fangchong",
        "win_fan_non_flower",
        "main_fans",
    )

    def __init__(self):
        self.strategy = ""
        self.user_id = 0
        self.seat = 0
        self.rank = 0
        self.score = 0
        self.zimo = 0
        self.dianhe = 0
        self.fulu = 0
        self.fangchong = 0
        self.win_fan_non_flower: List[int] = []
        self.main_fans: List[str] = []


class MatchStats:
    def __init__(self):
        self.mode = ""
        self.seed = 0
        self.hands = 0
        self.hu = 0
        self.liuju = 0
        self.ticks = 0
        self.elapsed_sec = 0.0
        self.seats: List[SeatAgg] = []
        self.final_scores: List[int] = []
        self.seat_user_ids: List[int] = []
        self.error: Optional[str] = None
        self.ok = False


def _strategy_name(uid: int) -> str:
    if uid == HEURISTIC_USER_ID:
        return "高性能"
    if uid == PAIXIAO_USER_ID:
        return "牌效"
    return f"uid{uid}"


async def run_one_match(
    *,
    seed: int,
    player_list: Sequence[int],
    mode: str,
) -> MatchStats:
    expected_hands = GAME_ROUND * 4
    stats = MatchStats()
    stats.mode = mode
    stats.seed = seed
    t0 = time.perf_counter()

    real_sleep = await _install_fast_async()
    real_shuffle = _install_no_seat_shuffle()
    old_smart = smart_bot_ai._BOT_DELAY
    old_heur = heuristic_ai._BOT_DELAY
    smart_bot_ai._BOT_DELAY = BOT_DELAY_FAST
    heuristic_ai._BOT_DELAY = BOT_DELAY_FAST

    db = _build_db_manager()
    server = _build_game_server(db)
    room_id = f"tour-{mode}-{seed}-{uuid.uuid4().hex[:8]}"
    room = _build_room_data(seed=seed, player_list=player_list, room_id=room_id)
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
        assert [p.user_id for p in game.player_list] == list(player_list)
        assert game.tactical_call is True
        assert game.hepai_limit == 8
        assert game.sub_rule == "guobiao/standard"
        assert game.max_round == GAME_ROUND

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

        # 入场座位（跳过 shuffle）；局间会换位，终局 player_list 顺序≠入场序
        by_orig = sorted(
            game.player_list,
            key=lambda p: int(getattr(p, "original_player_index", p.player_index)),
        )
        got_entry = [int(p.user_id) for p in by_orig]
        if got_entry != list(player_list):
            raise RuntimeError(
                f"入场座位被打乱: got={got_entry} want={list(player_list)}"
            )

        rounds = game.game_record.get("game_round", {}) or {}
        stats.hands = len(rounds)
        stats.ticks = int(getattr(game, "server_action_tick", 0) or 0)
        stats.final_scores = [int(p.score) for p in by_orig]
        stats.seat_user_ids = got_entry

        table_hu = 0
        for p in by_orig:
            rc = p.record_counter
            seat = SeatAgg()
            seat.user_id = int(p.user_id)
            seat.strategy = _strategy_name(seat.user_id)
            seat.seat = int(getattr(p, "original_player_index", p.player_index))
            seat.rank = int(getattr(rc, "rank_result", 0) or 0)
            seat.score = int(p.score)
            seat.zimo = int(getattr(rc, "zimo_times", 0) or 0)
            seat.dianhe = int(getattr(rc, "dianhe_times", 0) or 0)
            seat.fulu = int(getattr(rc, "fulu_times", 0) or 0)
            seat.fangchong = int(getattr(rc, "fangchong_times", 0) or 0)
            for fan_list in getattr(rc, "recorded_fans", []) or []:
                if not isinstance(fan_list, (list, tuple)):
                    continue
                flower = _flower_count(fan_list)
                est = 0
                for entry in fan_list:
                    name, cnt = _parse_fan_entry(entry)
                    est += _FAN_VALUE.get(name, 0) * cnt
                non_flower = max(0, est - flower)
                seat.win_fan_non_flower.append(non_flower)
                seat.main_fans.extend(_main_fans_ge4(fan_list))
            table_hu += seat.zimo + seat.dianhe
            stats.seats.append(seat)

        stats.hu = table_hu
        stats.liuju = max(0, stats.hands - stats.hu)

        if stats.hands < expected_hands:
            raise RuntimeError(
                f"未打满 {expected_hands} 手，仅 {stats.hands} 手，"
                f"scores={stats.final_scores}"
            )
        stats.ok = True
    except Exception as e:
        stats.error = f"{type(e).__name__}: {e}"
        real_print(f"FAIL {mode} seed={seed}: {stats.error}", flush=True)
    finally:
        builtins.print = real_print
        logging.disable(logging.NOTSET)
        if watchdog_task is not None and not watchdog_task.done():
            watchdog_task.cancel()
            try:
                await watchdog_task
            except (asyncio.CancelledError, Exception):
                pass
        smart_bot_ai._BOT_DELAY = old_smart
        heuristic_ai._BOT_DELAY = old_heur
        random.Random.shuffle = real_shuffle  # type: ignore[assignment]
        asyncio.sleep = real_sleep  # type: ignore[assignment]
        stats.elapsed_sec = time.perf_counter() - t0

    return stats


def _agg_mode(results: List[MatchStats]) -> Dict[str, Any]:
    ok = [r for r in results if r.ok]
    by_strat: Dict[str, Dict[str, Any]] = {}
    for name in ("高性能", "牌效"):
        by_strat[name] = {
            "n_seat_results": 0,
            "ranks": [],
            "rank_hist": Counter(),
            "scores": [],
            "zimo": 0,
            "dianhe": 0,
            "fulu": 0,
            "fangchong": 0,
            "hand_slots": 0,  # 席位数 × 每场手数（副露分母）
            "fans": [],
            "main_fans": Counter(),
        }

    for r in ok:
        for s in r.seats:
            bucket = by_strat.get(s.strategy)
            if bucket is None:
                continue
            bucket["n_seat_results"] += 1
            bucket["ranks"].append(s.rank)
            if s.rank:
                bucket["rank_hist"][s.rank] += 1
            bucket["scores"].append(s.score)
            bucket["zimo"] += s.zimo
            bucket["dianhe"] += s.dianhe
            bucket["fulu"] += s.fulu
            bucket["fangchong"] += s.fangchong
            bucket["hand_slots"] += r.hands
            bucket["fans"].extend(s.win_fan_non_flower)
            bucket["main_fans"].update(s.main_fans)

    table_hu = sum(r.hu for r in ok)
    table_liuju = sum(r.liuju for r in ok)
    table_hands = sum(r.hands for r in ok)

    summary = {
        "ok": len(ok),
        "fail": len(results) - len(ok),
        "table_hands": table_hands,
        "table_hu": table_hu,
        "table_liuju": table_liuju,
        "hu_rate": (table_hu / table_hands) if table_hands else 0.0,
        "liuju_rate": (table_liuju / table_hands) if table_hands else 0.0,
        "elapsed": sum(r.elapsed_sec for r in results),
        "by_strat": {},
    }

    for name, b in by_strat.items():
        n = b["n_seat_results"]
        hu = b["zimo"] + b["dianhe"]
        fans = b["fans"]
        avg_rank = (sum(b["ranks"]) / len(b["ranks"])) if b["ranks"] else None
        avg_score = (sum(b["scores"]) / len(b["scores"])) if b["scores"] else None
        fulu_rate = (b["fulu"] / b["hand_slots"]) if b["hand_slots"] else 0.0
        avg_fan = (sum(fans) / len(fans)) if fans else None
        top_fans = b["main_fans"].most_common(8)
        summary["by_strat"][name] = {
            "n": n,
            "avg_rank": avg_rank,
            "rank_hist": dict(sorted(b["rank_hist"].items())),
            "avg_score": avg_score,
            "hu": hu,
            "zimo": b["zimo"],
            "dianhe": b["dianhe"],
            "fangchong": b["fangchong"],
            "fulu_rate": fulu_rate,
            "avg_fan_non_flower": avg_fan,
            "top_main_fans": top_fans,
            "hu_for_fan_pct": hu,
        }
    return summary


def _fmt_rank_hist(hist: Dict[int, int]) -> str:
    return "/".join(str(hist.get(i, 0)) for i in (1, 2, 3, 4))


def _fmt_top_fans(top: List[Tuple[str, int]], hu: int) -> str:
    if not top or hu <= 0:
        return "-"
    parts = []
    for name, cnt in top[:5]:
        parts.append(f"{name} {cnt}/{hu}({100.0 * cnt / hu:.0f}%)")
    return "; ".join(parts)


def _print_mode_table(mode: str, layout_desc: str, seats: List[int], summary: Dict[str, Any]):
    print("\n" + "=" * 72)
    print(f"### {mode}  —  {layout_desc}")
    print(f"座位: " + " ".join(f"{WIND[i]}={_strategy_name(uid)}" for i, uid in enumerate(seats)))
    print(
        f"完成: {summary['ok']} 场 ok / fail {summary['fail']}  "
        f"桌级 和={summary['table_hu']} 流={summary['table_liuju']} "
        f"(和率 {summary['hu_rate']*100:.1f}% / 流局率 {summary['liuju_rate']*100:.1f}%)  "
        f"墙钟 {summary['elapsed']:.0f}s"
    )
    print()
    print(
        "| 策略 | 席次样本 | 均顺 | 1/2/3/4位 | 均分 | 和(自/点) | 放铳 | 副露率 | 非花均番 |"
    )
    print(
        "|------|----------|------|-----------|------|-----------|------|--------|----------|"
    )
    for name in ("高性能", "牌效"):
        s = summary["by_strat"][name]
        if s["n"] == 0:
            continue
        ar = f"{s['avg_rank']:.2f}" if s["avg_rank"] is not None else "-"
        asc = f"{s['avg_score']:.1f}" if s["avg_score"] is not None else "-"
        af = (
            f"{s['avg_fan_non_flower']:.2f}"
            if s["avg_fan_non_flower"] is not None
            else "-"
        )
        print(
            f"| {name} | {s['n']} | {ar} | {_fmt_rank_hist(s['rank_hist'])} | {asc} | "
            f"{s['hu']}({s['zimo']}/{s['dianhe']}) | {s['fangchong']} | "
            f"{s['fulu_rate']*100:.1f}% | {af} |"
        )
    print()
    print("主番（≥4，占该策略和牌次数）：")
    for name in ("高性能", "牌效"):
        s = summary["by_strat"][name]
        if s["n"] == 0:
            continue
        print(f"  {name}: {_fmt_top_fans(s['top_main_fans'], s['hu_for_fan_pct'])}")


def _verdict(summaries: Dict[str, Dict[str, Any]]) -> str:
    lines = []
    stronger = 0
    compared = 0
    for mode, summary in summaries.items():
        h = summary["by_strat"].get("高性能")
        p = summary["by_strat"].get("牌效")
        if not h or not p or not h["n"] or not p["n"]:
            continue
        compared += 1
        hr, pr = h["avg_rank"], p["avg_rank"]
        hs, ps = h["avg_score"], p["avg_score"]
        if hr is None or pr is None:
            continue
        # 均顺更低更好；均分更高更好
        better = (hr < pr - 0.05) or (hr <= pr and (hs or 0) > (ps or 0) + 5)
        clearly = (hr < pr - 0.15) and ((hs or 0) > (ps or 0) + 20)
        if clearly:
            stronger += 1
            lines.append(
                f"{mode}: 高性能明显更强（均顺 {hr:.2f} vs {pr:.2f}，均分 {hs:.0f} vs {ps:.0f}）"
            )
        elif better:
            stronger += 1
            lines.append(
                f"{mode}: 高性能略强（均顺 {hr:.2f} vs {pr:.2f}，均分 {hs:.0f} vs {ps:.0f}）"
            )
        elif abs(hr - pr) <= 0.05 and abs((hs or 0) - (ps or 0)) <= 15:
            lines.append(
                f"{mode}: 接近（均顺 {hr:.2f} vs {pr:.2f}，均分 {hs:.0f} vs {ps:.0f}）"
            )
        else:
            lines.append(
                f"{mode}: 牌效不弱/更强（均顺 高{hr:.2f} vs 效{pr:.2f}，均分 {hs:.0f} vs {ps:.0f}）"
            )

    if compared == 0:
        return "无有效对局，无法下结论。"
    if stronger == compared:
        head = "结论：高性能罗伯特在本样本中明显强于牌效罗伯特。"
    elif stronger > 0:
        head = "结论：高性能罗伯特整体占优，但优势幅度因配置而异。"
    else:
        head = "结论：本样本未能显示高性能明显强于牌效。"
    return head + " " + "；".join(lines)


async def main_async(args: argparse.Namespace) -> int:
    modes = [m.strip() for m in args.modes.split(",") if m.strip()]
    for m in modes:
        if m not in SEAT_LAYOUTS:
            print(f"未知 mode={m}，可选: {', '.join(SEAT_LAYOUTS)}")
            return 2

    print(
        "=== 高性能 vs 牌效 混席锦标赛 ===\n"
        f"规则: guobiao/standard hepai_limit=8 tactical_call=true game_round={GAME_ROUND}\n"
        f"种子: {args.base_seed}..{args.base_seed + args.matches - 1}（master_seed 贯穿全庄）\n"
        f"测速: fast_sleep + _BOT_DELAY={BOT_DELAY_FAST}；固定入场座位（跳过 shuffle）\n"
        f"模式: {modes}  各 {args.matches} 全庄  单场 timeout={MATCH_TIMEOUT}s\n",
        flush=True,
    )

    all_summaries: Dict[str, Dict[str, Any]] = {}
    any_fail = False
    t_all = time.perf_counter()

    for mode in modes:
        layout_desc, seats = SEAT_LAYOUTS[mode]
        print(f"\n>>> 开始 {mode}: {layout_desc}  seats={seats}", flush=True)
        rows: List[MatchStats] = []
        for i in range(args.matches):
            seed = args.base_seed + i
            st = await run_one_match(seed=seed, player_list=seats, mode=mode)
            rows.append(st)
            status = "OK" if st.ok else f"FAIL {st.error}"
            rank_s = ",".join(
                f"{WIND[s.seat]}{_strategy_name(s.user_id)[0]}#{s.rank}" for s in st.seats
            )
            print(
                f"  [{mode} {i+1}/{args.matches}] seed={seed} "
                f"{st.elapsed_sec:.1f}s hands={st.hands} hu={st.hu} liuju≈{st.liuju} "
                f"scores={st.final_scores} ranks=[{rank_s}] {status}",
                flush=True,
            )
            if st.error:
                any_fail = True

        summary = _agg_mode(rows)
        all_summaries[mode] = summary
        _print_mode_table(mode, layout_desc, seats, summary)

    wall = time.perf_counter() - t_all
    print("\n" + "=" * 72)
    print(_verdict(all_summaries))
    print(
        f"\n复现命令（在 open_mahjong_server）:\n"
        f"  python guobiao_heuristic_tournament_vs_paixiao.py "
        f"--modes {args.modes} --base-seed {args.base_seed} --matches {args.matches}\n"
        f"总墙钟: {wall:.1f}s",
        flush=True,
    )
    return 1 if any_fail else 0


def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    p = argparse.ArgumentParser(description="高性能 vs 牌效混席锦标赛")
    p.add_argument(
        "--modes",
        default="2v2,1v3",
        help="逗号分隔：2v2,1v3,3v1（默认 2v2,1v3）",
    )
    p.add_argument("--base-seed", type=int, default=DEFAULT_BASE_SEED)
    p.add_argument("--matches", type=int, default=DEFAULT_MATCHES)
    return p.parse_args(argv)


if __name__ == "__main__":
    logging.basicConfig(level=logging.WARNING)
    sys.exit(asyncio.run(main_async(parse_args())))
