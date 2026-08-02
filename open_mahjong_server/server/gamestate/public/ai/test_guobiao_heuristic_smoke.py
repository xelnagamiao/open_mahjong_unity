"""
高性能罗伯特四席同策 smoke（服务端自对弈）。

口径：guobiao/standard、hepai_limit=8、tactical_call=true、fast_sleep。
已跑通规模：两个全庄（四席高性能罗伯特）。63 全庄挂起。

从 open_mahjong_server 目录：

    python -m pytest server/gamestate/public/ai/test_guobiao_heuristic_smoke.py -v -k two_quanzhuang

    # 63 全庄（slow，默认勿跑）
    python -m pytest server/gamestate/public/ai/test_guobiao_heuristic_smoke.py -v -m slow

环境变量（可选）：
    SMOKE_MATCHES=63          # 全庄数，默认 63（仅 slow）
    SMOKE_BASE_SEED=72001     # 基种子；第 i 场用 base+i
    SMOKE_TACTICAL_CALL=1     # 1/true 开战术鸣牌（默认开）
    SMOKE_MATCH_TIMEOUT=600   # 单场超时秒数
    SMOKE_MAX_TICKS=200000    # server_action_tick 上限（防死循环）
"""
from __future__ import annotations

import asyncio
import logging
import os
import sys
import time
import uuid
from types import SimpleNamespace
from typing import Any, Dict, List, Optional
from unittest.mock import AsyncMock, MagicMock

import pytest

_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

from server.game_calculation.game_calculation_service import GameCalculationService  # noqa: E402
from server.gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState  # noqa: E402
import server.gamestate.public.ai.guobiao_heuristic_ai as heuristic_ai  # noqa: E402

logger = logging.getLogger(__name__)

HEURISTIC_USER_ID = 3
DEFAULT_MATCHES = 63
DEFAULT_BASE_SEED = 72001
DEFAULT_MATCH_TIMEOUT = 1200.0  # 假想番 Python 检番较慢，单全庄可能数分钟
DEFAULT_MAX_TICKS = 200_000


def _env_bool(name: str, default: bool) -> bool:
    raw = os.environ.get(name)
    if raw is None:
        return default
    return raw.strip().lower() in ("1", "true", "yes", "on")


def _env_int(name: str, default: int) -> int:
    raw = os.environ.get(name)
    if raw is None or not raw.strip():
        return default
    return int(raw)


def _env_float(name: str, default: float) -> float:
    raw = os.environ.get(name)
    if raw is None or not raw.strip():
        return default
    return float(raw)


def _build_room_data(
    *,
    seed: int,
    tactical_call: bool,
    room_id: str,
    game_round: int = 4,
) -> dict:
    # 四席均为高性能罗伯特；列表可重复 user_id，座位按顺序建玩家对象。
    # game_round: 1=东风(4手) 2=半庄(8手) 4=全庄(16手)
    return {
        "room_id": room_id,
        "player_list": [HEURISTIC_USER_ID] * 4,
        "player_settings": {},
        "tips": False,
        "game_round": game_round,
        "step_timer": 10,
        "round_timer": 60,  # 超时兜底；正常应由 bot 出牌
        "room_rule": "guobiao",
        "room_type": "match",  # 跳过 custom 房间销毁
        "sub_rule": "guobiao/standard",
        "random_seed": seed,
        "open_cuohe": False,
        "cuohe_type": 0,
        "show_moqie_hint": False,
        "hepai_limit": 8,
        "tactical_call": tactical_call,
        "claim_protection": False,
        # 加速：战术窗口压到极短，仍走战术鸣牌状态机
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


class MatchStats:
    def __init__(self):
        self.hands = 0
        self.hu = 0
        self.liuju = 0
        self.ticks = 0
        self.final_scores: List[int] = []
        self.elapsed_sec = 0.0
        self.seed = 0
        self.error: Optional[str] = None


async def _install_fast_async():
    """压缩 UI 级 sleep，保留短轮询以免 bot 抢在 wait_action 前耗尽重试。

    `_wait_until_actionable` 用 ~0.01s × 200 次等 waiting_players_list；
    若一律 sleep(0)，重试会在入队前瞬间耗尽，对局卡死。
    """
    real_sleep = asyncio.sleep

    async def fast_sleep(delay=0, result=None):
        try:
            d = float(delay or 0)
        except (TypeError, ValueError):
            d = 0.0
        if d >= 0.15:
            # 换位 4s、局终演出、步时轮询 1s、bot 0.5s 等 → 让步即可
            await real_sleep(0)
        elif d > 0:
            await real_sleep(min(d, 0.01))
        else:
            await real_sleep(0)
        return result

    asyncio.sleep = fast_sleep  # type: ignore[assignment]
    return real_sleep


def _restore_async_sleep(real_sleep):
    asyncio.sleep = real_sleep  # type: ignore[assignment]


async def run_one_match(
    *,
    seed: int,
    tactical_call: bool = True,
    game_round: int = 4,
    match_timeout: float = DEFAULT_MATCH_TIMEOUT,
    max_ticks: int = DEFAULT_MAX_TICKS,
) -> MatchStats:
    """跑完一场（东风/半庄/全庄由 game_round 决定）；异常/超时/卡死写入 stats.error。"""
    expected_hands = game_round * 4
    stats = MatchStats()
    stats.seed = seed
    t0 = time.perf_counter()

    real_sleep = await _install_fast_async()
    old_delay = heuristic_ai._BOT_DELAY
    # 略大于 0：让 wait_action 先入 waiting_players_list，再配合 fast_sleep 短轮询
    heuristic_ai._BOT_DELAY = 0.02

    db = _build_db_manager()
    server = _build_game_server(db)
    room_id = f"smoke-{seed}-{uuid.uuid4().hex[:8]}"
    room = _build_room_data(
        seed=seed,
        tactical_call=tactical_call,
        room_id=room_id,
        game_round=game_round,
    )
    gamestate_id = str(uuid.uuid4())

    game: Optional[GuobiaoGameState] = None
    watchdog_task: Optional[asyncio.Task] = None
    import builtins

    real_print = builtins.print

    def quiet_print(*_a, **_k):
        return None

    # 压制和牌/听牌检测里的无条件 print；进度用 real_print / 恢复后的 print
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
        assert game.tactical_call is tactical_call
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
                if tick >= max_ticks:
                    raise RuntimeError(
                        f"超过 MAX_TICKS={max_ticks}（当前 tick={tick}），疑似死循环"
                    )
                if tick == last_tick:
                    stagnant += 1
                    if stagnant >= 120:  # ~60s 无推进
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
            await asyncio.wait_for(game.game_loop_chinese(), timeout=match_timeout)
        except asyncio.TimeoutError as e:
            raise RuntimeError(
                f"单场超时（>{match_timeout}s），"
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

        zimo = sum(getattr(p.record_counter, "zimo_times", 0) for p in game.player_list)
        dianhe = sum(getattr(p.record_counter, "dianhe_times", 0) for p in game.player_list)
        stats.hu = zimo + dianhe
        stats.liuju = max(0, stats.hands - stats.hu)
        stats.ticks = int(getattr(game, "server_action_tick", 0) or 0)
        stats.final_scores = [int(p.score) for p in game.player_list]

        if stats.hands < expected_hands:
            raise RuntimeError(
                f"未打满 {expected_hands} 手（game_round={game_round}），"
                f"仅 {stats.hands} 手，scores={stats.final_scores}"
            )
    except Exception as e:
        stats.error = f"{type(e).__name__}: {e}"
        real_print(f"smoke 失败 seed={seed}: {stats.error}", flush=True)
    finally:
        builtins.print = real_print
        logging.disable(logging.NOTSET)
        if watchdog_task is not None and not watchdog_task.done():
            watchdog_task.cancel()
            try:
                await watchdog_task
            except (asyncio.CancelledError, Exception):
                pass
        heuristic_ai._BOT_DELAY = old_delay
        _restore_async_sleep(real_sleep)
        stats.elapsed_sec = time.perf_counter() - t0

    return stats


# 兼容旧名
run_one_quanzhuang = run_one_match


def _print_progress(i: int, total: int, stats: MatchStats) -> None:
    status = "OK" if not stats.error else f"FAIL {stats.error}"
    print(
        f"[smoke {i}/{total}] seed={stats.seed} hands={stats.hands} "
        f"hu={stats.hu} liuju≈{stats.liuju} ticks={stats.ticks} "
        f"scores={stats.final_scores} {stats.elapsed_sec:.1f}s {status}",
        flush=True,
    )


def _run_n_matches(n: int, *, game_round: int = 4) -> List[MatchStats]:
    base = _env_int("SMOKE_BASE_SEED", DEFAULT_BASE_SEED)
    match_timeout = _env_float("SMOKE_MATCH_TIMEOUT", DEFAULT_MATCH_TIMEOUT)
    max_ticks = _env_int("SMOKE_MAX_TICKS", DEFAULT_MAX_TICKS)
    results: List[MatchStats] = []
    for i in range(n):
        stats = asyncio.run(
            run_one_match(
                seed=base + i,
                tactical_call=True,
                game_round=game_round,
                match_timeout=match_timeout,
                max_ticks=max_ticks,
            )
        )
        results.append(stats)
        _print_progress(i + 1, n, stats)
        if stats.error:
            break
    return results


def test_east_wind_tactical_true():
    """默认短 smoke：东风 4 手，战术鸣牌开，standard / 8 番。"""
    results = _run_n_matches(1, game_round=1)
    assert len(results) == 1
    stats = results[0]
    assert stats.error is None, stats.error
    assert stats.hands == 4


@pytest.mark.slow
def test_one_quanzhuang_tactical_true():
    """单全庄（slow；单场约十余分钟）。"""
    results = _run_n_matches(1, game_round=4)
    assert len(results) == 1
    stats = results[0]
    assert stats.error is None, stats.error
    assert stats.hands == 16


@pytest.mark.slow
def test_two_quanzhuang_tactical_true():
    """两全庄（slow）。"""
    results = _run_n_matches(2, game_round=4)
    assert len(results) == 2
    for stats in results:
        assert stats.error is None, stats.error
        assert stats.hands == 16


@pytest.mark.slow
def test_63_quanzhuang_heuristic_self_play():
    """63 全庄（slow；挂起，默认勿跑）。"""
    matches = _env_int("SMOKE_MATCHES", DEFAULT_MATCHES)
    base = _env_int("SMOKE_BASE_SEED", DEFAULT_BASE_SEED)
    tactical = _env_bool("SMOKE_TACTICAL_CALL", True)
    match_timeout = _env_float("SMOKE_MATCH_TIMEOUT", DEFAULT_MATCH_TIMEOUT)
    max_ticks = _env_int("SMOKE_MAX_TICKS", DEFAULT_MAX_TICKS)

    t0 = time.perf_counter()
    results: List[MatchStats] = []
    for i in range(matches):
        seed = base + i
        stats = asyncio.run(
            run_one_match(
                seed=seed,
                tactical_call=tactical,
                game_round=4,
                match_timeout=match_timeout,
                max_ticks=max_ticks,
            )
        )
        results.append(stats)
        _print_progress(i + 1, matches, stats)
        if stats.error:
            break

    elapsed = time.perf_counter() - t0
    ok = [r for r in results if not r.error]
    fail = [r for r in results if r.error]
    total_hands = sum(r.hands for r in ok)
    total_hu = sum(r.hu for r in ok)
    total_liuju = sum(r.liuju for r in ok)

    print(
        f"\n=== smoke 汇总 matches={len(results)}/{matches} ok={len(ok)} fail={len(fail)} "
        f"hands={total_hands} hu={total_hu} liuju≈{total_liuju} "
        f"tactical_call={tactical} base_seed={base} elapsed={elapsed:.1f}s ===",
        flush=True,
    )
    if fail:
        pytest.fail(
            f"{len(fail)} 场失败，首例 seed={fail[0].seed}: {fail[0].error}"
        )
    assert len(ok) == matches
    assert total_hands == matches * 16


if __name__ == "__main__":
    logging.basicConfig(level=logging.WARNING)

    async def _main():
        mode = os.environ.get("SMOKE_MODE", "east")
        if mode == "63" or mode == "full":
            matches = _env_int("SMOKE_MATCHES", DEFAULT_MATCHES)
            base = _env_int("SMOKE_BASE_SEED", DEFAULT_BASE_SEED)
            tactical = _env_bool("SMOKE_TACTICAL_CALL", True)
            t0 = time.perf_counter()
            fails = []
            for i in range(matches):
                st = await run_one_match(seed=base + i, tactical_call=tactical, game_round=4)
                _print_progress(i + 1, matches, st)
                if st.error:
                    fails.append(st)
                    break
            print(f"done in {time.perf_counter() - t0:.1f}s fails={len(fails)}")
            sys.exit(1 if fails else 0)
        elif mode == "one":
            st = await run_one_match(seed=DEFAULT_BASE_SEED, tactical_call=True, game_round=4)
            _print_progress(1, 1, st)
            sys.exit(1 if st.error else 0)
        else:
            st = await run_one_match(seed=DEFAULT_BASE_SEED, tactical_call=True, game_round=1)
            _print_progress(1, 1, st)
            sys.exit(1 if st.error else 0)

    asyncio.run(_main())
