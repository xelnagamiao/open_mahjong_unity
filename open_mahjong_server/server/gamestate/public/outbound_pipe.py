"""每观众串行出站管道：保证同一观众 FIFO，可选单条 delay，不阻塞主循环。

鸣牌保护曾用「广播函数内 await sleep(gap)」保证受保护观众 wire 顺序，
但会卡住整桌（点吃碰后要等 gap 才进入下一询问）。改为：
- 非受保护观众 / delay=0：入队后可 await 本条完成；
- 受保护观众的鸣牌/申请：入队 delay=gap1，主循环不 await；
- 实际鸣牌后再标记第二追赶：该观众下一条消息 delay=gap2；
- ask_hand：当前行动者 await 发送；其余座位 schedule 入队（可带 post_gap），
  避免串行 await 旁观者管道拖住吃碰后的出牌权；计时按送达起算（ask_timing）。
- ask_other 与同一观众后续消息（含下一手 cut / show_result）也走管道，自然排在 delayed 帧之后。
"""
from __future__ import annotations

import asyncio
import logging
from typing import Awaitable, Callable, Dict, Optional

logger = logging.getLogger(__name__)

SendFn = Callable[[], Awaitable[None]]


def init_outbound_pipes(game_state) -> None:
    if not hasattr(game_state, "_outbound_tails"):
        game_state._outbound_tails: Dict[int, asyncio.Task] = {}
    game_state._outbound_closed = False


def close_outbound_pipes(game_state) -> None:
    """对局结束时取消未完成的出站任务。"""
    game_state._outbound_closed = True
    tails = getattr(game_state, "_outbound_tails", None) or {}
    for task in list(tails.values()):
        if task is not None and not task.done():
            task.cancel()
    game_state._outbound_tails = {}


def schedule_viewer_send(
    game_state,
    viewer_index: int,
    send_fn: SendFn,
    *,
    delay_before: float = 0.0,
) -> asyncio.Task:
    """将发送追加到该观众管道末尾，立即返回 Task（不阻塞调用方）。"""
    if not hasattr(game_state, "_outbound_tails"):
        init_outbound_pipes(game_state)

    prev: Optional[asyncio.Task] = game_state._outbound_tails.get(viewer_index)

    async def _run():
        try:
            if prev is not None:
                try:
                    await prev
                except asyncio.CancelledError:
                    pass
                except Exception:
                    pass
            if getattr(game_state, "_outbound_closed", False):
                return
            if delay_before > 0:
                await asyncio.sleep(delay_before)
            if getattr(game_state, "_outbound_closed", False):
                return
            await send_fn()
        except asyncio.CancelledError:
            raise
        except Exception:
            logger.exception("outbound send failed viewer=%s", viewer_index)

    task = asyncio.create_task(_run())
    game_state._outbound_tails[viewer_index] = task
    return task


async def send_to_viewer(
    game_state,
    viewer_index: int,
    send_fn: SendFn,
    *,
    delay_before: float = 0.0,
) -> None:
    """入队并等待本条发送完成（仍排在更早的队列项之后）。"""
    task = schedule_viewer_send(
        game_state, viewer_index, send_fn, delay_before=delay_before
    )
    try:
        await task
    except asyncio.CancelledError:
        raise
    except Exception:
        # 已在 _run 内打日志；避免拖垮广播循环
        pass
