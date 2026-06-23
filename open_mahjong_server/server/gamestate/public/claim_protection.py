"""鸣牌保护（国标 / 青雀 / 四川等规则可选启用）。

目的：可被鸣牌的出牌，对「不能鸣牌的家」延迟广播出牌，隐藏「是否有人能鸣牌」这一信息。
玩家最多只能推断出「有人可以鸣牌」，无法得知是谁（因为只有能鸣牌者自己会收到询问）。

规则（claimable_only 触发 + B 方案 + 1.5s 上限）：
- 仅当打出的牌能被任意他家吃/碰/杠/荣和时才启用本区间。
- 出牌者、能鸣牌者：立即收到出牌；能鸣牌者同时收到 ask_other 可立即决策。
- 受保护观众（既不能鸣牌、又不是出牌者）：出牌(cut) 进入暂存，延迟发送。
  触发把暂存 cut 发给受保护观众的时机，取最早：
    1) 有人实际鸣牌/荣和 -> 立即 flush cut，再延迟 MELD_FOLLOWUP_GAP 把鸣牌发给受保护观众；
    2) 能鸣牌者全部 pass / 超时无人鸣牌 -> 立即 flush cut（区间结束，无鸣牌）；
    3) MELD_PROTECT_DELAY 超时 -> flush cut（此后「暴露有人可鸣牌」是允许的，但仍不知是谁）。
- 与战术鸣牌正交：
  - 受保护观众尚未看到出牌：战术 is_claim 不发送；实际鸣牌有声（追赶路径唯一音效）。
  - 受保护观众已看到出牌（1.5s 超时 flush 或鸣牌前 flush）：可收到战术 is_claim；
    实际鸣牌尊重 silent（战术申请后静默执行），避免「申请 + 执行」双响。
"""
from __future__ import annotations

import asyncio
import logging
from typing import Dict, Optional

logger = logging.getLogger(__name__)

# 受保护观众看到出牌的最大延迟（秒）——默认值，可被 game_state.claim_protect_delay 覆盖
MELD_PROTECT_DELAY = 1.5
# 出牌与紧随其后的鸣牌之间，对受保护观众的间隔（秒）——默认值，可被 game_state.claim_meld_followup_gap 覆盖
MELD_FOLLOWUP_GAP = 0.3

# 真实鸣牌行为（不含 is_claim 申请、不含和牌/抢杠的终结结算）
REAL_MELD_ACTIONS = frozenset({"chi_left", "chi_mid", "chi_right", "peng", "gang"})


def claim_protection_enabled(game_state) -> bool:
    return bool(getattr(game_state, "claim_protection", False))


def get_protect_delay(game_state) -> float:
    """受保护观众看到出牌的最大延迟（秒），优先取房间可配置项。"""
    return float(getattr(game_state, "claim_protect_delay", MELD_PROTECT_DELAY))


def get_meld_followup_gap(game_state) -> float:
    """出牌与紧随其后的鸣牌/和牌之间的间隔（秒），优先取房间可配置项。"""
    return float(getattr(game_state, "claim_meld_followup_gap", MELD_FOLLOWUP_GAP))


def init_claim_protection_state(game_state) -> None:
    game_state._cp_active = False
    game_state._cp_protected = [False, False, False, False]
    game_state._cp_pending_cut: Dict[int, dict] = {}
    game_state._cp_cut_flushed = False
    game_state._cp_timer_task: Optional[asyncio.Task] = None


def _cancel_timer(game_state) -> None:
    task = getattr(game_state, "_cp_timer_task", None)
    if task is not None and not task.done():
        task.cancel()
    game_state._cp_timer_task = None


def is_protected_viewer(game_state, viewer_index: int) -> bool:
    """该座位在本区间是否为受保护观众（不能鸣牌且非出牌者）。flush 后仍保持，便于鸣牌阶段判断。"""
    protected = getattr(game_state, "_cp_protected", None)
    return bool(protected) and 0 <= viewer_index < 4 and bool(protected[viewer_index])


def begin_claim_protection_interval(game_state, action_dict, action_player: int) -> None:
    """出牌后、广播 cut 前调用：根据鸣牌询问快照确定本轮受保护观众。"""
    from .game_record_manager import flush_all_unexecuted_claim_applications
    flush_all_unexecuted_claim_applications(game_state)
    if not hasattr(game_state, "_cp_pending_cut"):
        init_claim_protection_state(game_state)
    _cancel_timer(game_state)
    game_state._cp_pending_cut = {}
    game_state._cp_cut_flushed = False
    game_state._cp_protected = [False, False, False, False]
    game_state._cp_active = False
    if not claim_protection_enabled(game_state):
        return
    can_claim = {
        pid for pid in range(4)
        if any(a != "pass" for a in (action_dict.get(pid) or []))
    }
    if not can_claim:
        # 没人能鸣牌：不延迟（claimable_only）
        return
    game_state._cp_protected = [
        (pid != action_player and pid not in can_claim) for pid in range(4)
    ]
    game_state._cp_active = any(game_state._cp_protected)
    if game_state._cp_active:
        logger.info(
            "鸣牌保护开始 protected=%s can_claim=%s cutter=%s",
            game_state._cp_protected, sorted(can_claim), action_player,
        )


def stash_protected_cut_payload(game_state, viewer_index: int, payload: dict) -> None:
    """暂存受保护观众的 cut payload，等待 flush。"""
    game_state._cp_pending_cut[viewer_index] = payload


def arm_claim_protection_timer(game_state, send_fn) -> None:
    """广播 cut 后启动 1.5s 超时定时器：到点把暂存 cut 发给受保护观众。"""
    if not getattr(game_state, "_cp_active", False):
        return
    if not getattr(game_state, "_cp_pending_cut", None):
        return

    async def _run():
        try:
            await asyncio.sleep(get_protect_delay(game_state))
            await flush_protected_cut(game_state, send_fn)
        except asyncio.CancelledError:
            pass
        except Exception:
            logger.exception("鸣牌保护超时 flush 失败")

    _cancel_timer(game_state)
    game_state._cp_timer_task = asyncio.create_task(_run())


async def flush_protected_cut(game_state, send_fn, silent_cut: bool = False) -> bool:
    """把暂存 cut 发给受保护观众。返回 True 表示本次确有发送（用于判断鸣牌是否需间隔）。

    silent_cut=True 时，这张追赶补发的 cut 静默落桌（不发切牌声），用于「紧接着就要鸣牌」的
    追赶场景，避免受保护观众在 0.3s 内连续听到「切牌声 + 鸣牌声」的快速双响；
    其它揭示时机（1.5s 超时、全员 pass/超时）保持 silent_cut=False，正常播放切牌声。
    """
    if getattr(game_state, "_cp_cut_flushed", True):
        return False
    game_state._cp_cut_flushed = True
    _cancel_timer(game_state)
    pending = getattr(game_state, "_cp_pending_cut", {}) or {}
    game_state._cp_pending_cut = {}
    if not pending:
        return False
    for viewer_index, payload in pending.items():
        if silent_cut:
            payload = dict(payload)
            payload["silent"] = True
        try:
            await send_fn(game_state, viewer_index, payload)
        except Exception:
            logger.exception("鸣牌保护 flush 发送失败 viewer=%s", viewer_index)
    return True


async def finalize_claim_protection(game_state, send_fn) -> None:
    """能鸣牌者全部 pass / 超时无人鸣牌：立即把出牌发给受保护观众并结束区间。"""
    if not getattr(game_state, "_cp_active", False):
        return
    await flush_protected_cut(game_state, send_fn)
    game_state._cp_active = False


def end_claim_protection_interval(game_state) -> None:
    """鸣牌发生后结束区间（鸣牌者将切牌进入新区间）。"""
    _cancel_timer(game_state)
    game_state._cp_active = False
    game_state._cp_pending_cut = {}


def schedule_protected_meld_send(game_state, viewer_index: int, payload: dict, delay: float, send_fn) -> None:
    """延迟 delay 秒后把鸣牌动作发给某受保护观众（用于 cut 之后的 0.4s 间隔）。"""
    async def _run():
        try:
            if delay > 0:
                await asyncio.sleep(delay)
            await send_fn(game_state, viewer_index, payload)
        except asyncio.CancelledError:
            pass
        except Exception:
            logger.exception("鸣牌保护延迟鸣牌发送失败 viewer=%s", viewer_index)

    asyncio.create_task(_run())
