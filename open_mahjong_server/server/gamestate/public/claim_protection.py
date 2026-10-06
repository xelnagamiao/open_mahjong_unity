"""鸣牌保护（支持的规则中，纯真人房可选启用；含机器人时停用）。

目的：可被鸣牌的出牌，对「不能鸣牌的家」延迟广播出牌，隐藏「是否有人能鸣牌」这一信息。
玩家最多只能推断出「有人可以鸣牌」，无法得知是谁（因为只有能鸣牌者自己会收到询问）。

规则（claimable_only 触发 + B 方案 + 1.3s 上限）：
- 仅当打出的牌能被任意他家吃/碰/杠时才启用本区间；
  若任意家可荣和/抢杠和，则本区间不启用（避免和牌面板与出牌揭示贴脸）。
- 出牌者、能鸣牌者：立即收到出牌；能鸣牌者同时收到 ask_other 可立即决策。
- 受保护观众（既不能鸣牌、又不是出牌者）：出牌(cut) 进入暂存，延迟发送。
  触发把暂存 cut 发给受保护观众的时机，取最早：
    1) 有人实际鸣牌，或战术鸣牌申请（is_claim）被广播 -> 立即 flush cut（若尚未揭示），
       再按「cut 揭示时刻 + MELD_FOLLOWUP_GAP」计算剩余延迟；
    2) 能鸣牌者全部 pass / 超时无人鸣牌 -> 立即 flush cut（区间结束，无鸣牌）；
    3) MELD_PROTECT_DELAY 超时 -> flush cut（此后「暴露有人可鸣牌」是允许的，但仍不知是谁）。
- 节奏：flush cut 后，鸣牌/申请对受保护观众经 per-viewer outbound_pipe 延迟发送
  （主循环不 await gap，避免点吃碰后整桌卡住；同一观众后续消息仍 FIFO，不会乱序）。
- 第二追赶：实际鸣牌入队后，该受保护观众下一条经 pipe 的消息再额外延迟 MELD_POST_GAP，
  避免「鸣牌帧与下一手出牌」贴脸。
- 与战术鸣牌的配合：is_claim 一律先 flush cut 再入队（受保护观众也能听到申请发声），
  之后的实际鸣牌尊重 silent（战术申请后静默执行），避免「申请 + 执行」双响。
"""
from __future__ import annotations

from .player_count import game_player_count
import asyncio
from dataclasses import dataclass, field
import logging
import time
from typing import Dict, Optional

logger = logging.getLogger(__name__)

# 受保护观众看到出牌的最大延迟（秒）——默认值，可被 game_state.claim_protect_delay 覆盖
MELD_PROTECT_DELAY = 1.3
# 出牌与紧随其后的鸣牌之间，对受保护观众的间隔（秒）——默认值，可被 game_state.claim_meld_followup_gap 覆盖
MELD_FOLLOWUP_GAP = 0.7
# 鸣牌与紧随其后的下一手出牌等消息之间的间隔（秒）——默认值，可被 game_state.claim_meld_post_gap 覆盖
MELD_POST_GAP = 0.5

# 真实鸣牌行为（不含 is_claim 申请、不含和牌/抢杠的终结结算）
REAL_MELD_ACTIONS = frozenset({"chi_left", "chi_mid", "chi_right", "peng", "gang"})
# 可和牌动作：存在任一此类动作时本区间不启用鸣牌保护
HU_CLAIM_ACTIONS = frozenset({"hu", "hu_first", "hu_second", "hu_third"})


# Only these engines implement the discard visibility protocol.
CLAIM_PROTECTION_RULES = frozenset({"guobiao", "qingque", "changsha", "sichuan", "zhongyong", "nanque", "jiandan", "riichi"})


def supports_claim_protection(rule) -> bool:
    return rule in CLAIM_PROTECTION_RULES


def has_bot_players(players) -> bool:
    """Reserved bot IDs are 0..9; empty seats (-1) and offline humans are not bots."""
    return any(
        isinstance(uid, int) and 0 <= uid < 10
        for player in (players or ())
        for uid in (player if isinstance(player, int) else getattr(player, "user_id", None),)
    )


def room_claim_protection_enabled(room, rule=None) -> bool:
    """Derive effective settings without overwriting the lobby's selected switch."""
    rule = rule or room.get("room_rule")
    return (supports_claim_protection(rule)
            and bool(room.get("claim_protection", rule != "riichi"))
            and not has_bot_players(room.get("player_list"))
            and not has_bot_players(room.get("seat_list")))


def claim_protection_enabled(game_state) -> bool:
    return (bool(getattr(game_state, "claim_protection", False))
            and not has_bot_players(getattr(game_state, "player_list", ())))


def get_protect_delay(game_state) -> float:
    """受保护观众看到出牌的最大延迟（秒），优先取房间可配置项。"""
    return float(getattr(game_state, "claim_protect_delay", MELD_PROTECT_DELAY))


def get_meld_followup_gap(game_state) -> float:
    """出牌与紧随其后的鸣牌/和牌之间的间隔（秒），优先取房间可配置项。"""
    return float(getattr(game_state, "claim_meld_followup_gap", MELD_FOLLOWUP_GAP))


def get_meld_post_gap(game_state) -> float:
    """鸣牌与紧随其后的下一手出牌等消息之间的间隔（秒），优先取房间可配置项。"""
    return float(getattr(game_state, "claim_meld_post_gap", MELD_POST_GAP))


def compute_protected_meld_delay(game_state) -> float:
    """受保护观众鸣牌/和牌相对当下的剩余追赶延迟（秒）。

    保证 cut 揭示与紧随鸣牌之间至少相隔 claim_meld_followup_gap：
    - cut 刚 flush：返回 gap（如 0.7s）；
    - cut 在 1.3s 超时揭示、鸣牌在 1.5s：返回 0.5s（对齐 2.0s）；
    - 鸣牌在 cut 揭示 + gap 之后：返回 0（立即）。
    """
    flush_time = getattr(game_state, "_cp_cut_flush_time", None)
    if flush_time is None:
        return get_meld_followup_gap(game_state)
    gap = get_meld_followup_gap(game_state)
    return max(0.0, flush_time + gap - time.monotonic())


async def prepare_protected_meld_for_viewers(game_state, send_fn) -> float:
    """实际吃碰杠 / 战术申请（is_claim）广播前：补发暂存 cut（若尚未揭示），
    并返回受保护观众的剩余追赶延迟（秒）。调用方应将鸣牌/申请经 outbound_pipe
    以该 delay 入队（勿在主循环 await sleep）。"""
    await flush_protected_cut(game_state, send_fn)
    return compute_protected_meld_delay(game_state)


def init_claim_protection_state(game_state) -> None:
    from .outbound_pipe import init_outbound_pipes

    game_state._cp_active = False
    game_state._cp_protected = [False] * game_player_count(game_state)
    game_state._cp_pending_cut: Dict[int, dict] = {}
    game_state._cp_cut_flushed = False
    game_state._cp_cut_flush_time = None
    game_state._cp_timer_task: Optional[asyncio.Task] = None
    game_state._cp_need_post_gap = [False] * game_player_count(game_state)
    game_state._cp_discard_delivery = None
    init_outbound_pipes(game_state)


def _cancel_timer(game_state) -> None:
    """取消超时 flush 定时器。

    注意：flush 可能由定时器任务自身调用。若对 current_task 执行 cancel，
    会在随后的 await send 处抛出 CancelledError，导致：
    - `_cp_cut_flushed` 已置 True、pending 已清空
    - 出牌实际未发出
    - 之后 prepare/finalize 的 flush 直接跳过
    受保护观众就会「永远看不见这张出牌」。
    """
    task = getattr(game_state, "_cp_timer_task", None)
    game_state._cp_timer_task = None
    if task is None or task.done():
        return
    # 定时器回调里触发的 flush：只清空引用，不要 cancel 自己
    if task is asyncio.current_task():
        return
    task.cancel()


def is_protected_viewer(game_state, viewer_index: int) -> bool:
    """该座位在本区间是否为受保护观众（不能鸣牌且非出牌者）。flush 后仍保持，便于鸣牌阶段判断。"""
    protected = getattr(game_state, "_cp_protected", None)
    return bool(protected) and 0 <= viewer_index < game_player_count(game_state) and bool(protected[viewer_index])


def action_dict_has_hu_claim(action_dict) -> bool:
    """询问快照中是否存在可和牌动作（荣和/抢杠和等）。"""
    for actions in action_dict.values():
        for action in (actions or []):
            if action in HU_CLAIM_ACTIONS:
                return True
    return False


def mark_post_meld_gap(game_state, viewer_index: int) -> None:
    """实际鸣牌已入队/发出后调用：该观众下一条 pipe 消息需再等第二追赶。"""
    need = getattr(game_state, "_cp_need_post_gap", None)
    if need is None:
        game_state._cp_need_post_gap = [False] * game_player_count(game_state)
        need = game_state._cp_need_post_gap
    if 0 <= viewer_index < game_player_count(game_state):
        need[viewer_index] = True


def take_post_meld_gap_delay(game_state, viewer_index: int) -> float:
    """若该观众需要第二追赶，返回间隔并清除标记；否则 0。"""
    need = getattr(game_state, "_cp_need_post_gap", None)
    if not need or not (0 <= viewer_index < game_player_count(game_state)) or not need[viewer_index]:
        return 0.0
    need[viewer_index] = False
    return get_meld_post_gap(game_state)


def begin_claim_protection_interval(game_state, action_dict, action_player: int) -> None:
    """出牌后、广播 cut 前调用：根据鸣牌询问快照确定本轮受保护观众。"""
    from .game_record_manager import flush_all_unexecuted_claim_applications
    flush_all_unexecuted_claim_applications(game_state)
    if not hasattr(game_state, "_cp_pending_cut"):
        init_claim_protection_state(game_state)
    _cancel_timer(game_state)
    game_state._cp_pending_cut = {}
    game_state._cp_cut_flushed = False
    game_state._cp_cut_flush_time = None
    game_state._cp_protected = [False] * game_player_count(game_state)
    game_state._cp_active = False
    # 注意：不清除 _cp_need_post_gap——上一手鸣牌的第二追赶要作用到本手 cut
    if not claim_protection_enabled(game_state):
        return
    if action_dict_has_hu_claim(action_dict):
        # 可和牌：关闭本区间保护，避免出牌揭示与和牌面板贴脸
        logger.info("鸣牌保护跳过：本张可和牌 cutter=%s", action_player)
        return
    can_claim = {
        pid for pid in range(game_player_count(game_state))
        if any(a != "pass" for a in (action_dict.get(pid) or []))
    }
    if not can_claim:
        # 没人能鸣牌：不延迟（claimable_only）
        return
    game_state._cp_protected = [
        (pid != action_player and pid not in can_claim) for pid in range(game_player_count(game_state))
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
    """广播 cut 后启动 claim_protect_delay 超时定时器：到点把暂存 cut 发给受保护观众。"""
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


async def flush_protected_cut(game_state, send_fn) -> bool:
    """把暂存 cut 发给受保护观众。出牌追赶揭示始终有声（与战术鸣牌申请无关）。"""
    if getattr(game_state, "_cp_cut_flushed", True):
        return False
    pending = getattr(game_state, "_cp_pending_cut", {}) or {}
    if not pending:
        game_state._cp_cut_flushed = True
        game_state._cp_cut_flush_time = time.monotonic()
        _cancel_timer(game_state)
        return False

    # 取出 pending 并标记 flushed，避免并发重复发送；失败则回滚未发出座位。
    game_state._cp_pending_cut = {}
    game_state._cp_cut_flushed = True
    game_state._cp_cut_flush_time = time.monotonic()
    _cancel_timer(game_state)

    sent: set[int] = set()
    duplicate_flush_done = None
    if getattr(game_state, "duplicate_key", None):
        # A duplicate terminal broadcast must wait for all viewers of this
        # flush, including viewers whose send has not entered their pipe yet.
        duplicate_flush_done = asyncio.get_running_loop().create_future()
        game_state._duplicate_claim_flush = (asyncio.current_task(), duplicate_flush_done)
    try:
        for viewer_index, payload in pending.items():
            out = dict(payload)
            out["silent"] = None
            try:
                await send_fn(game_state, viewer_index, out)
                sent.add(viewer_index)
                logger.info(
                    "鸣牌保护 flush 出牌 viewer=%s action_tick=%s",
                    viewer_index,
                    out.get("action_tick"),
                )
            except asyncio.CancelledError:
                logger.warning("鸣牌保护 flush 被取消 viewer=%s", viewer_index)
                raise
            except Exception:
                logger.exception("鸣牌保护 flush 发送失败 viewer=%s", viewer_index)
    except asyncio.CancelledError:
        unsent = {idx: dict(pl) for idx, pl in pending.items() if idx not in sent}
        if unsent:
            game_state._cp_pending_cut.update(unsent)
            game_state._cp_cut_flushed = False
            game_state._cp_cut_flush_time = None
        raise
    finally:
        if duplicate_flush_done is not None and not duplicate_flush_done.done():
            duplicate_flush_done.set_result(None)

    unsent = {idx: dict(pl) for idx, pl in pending.items() if idx not in sent}
    if unsent:
        game_state._cp_pending_cut.update(unsent)
        game_state._cp_cut_flushed = False
        game_state._cp_cut_flush_time = None
        return bool(sent)
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


@dataclass
class DiscardDelivery:
    """Current human discard's FIFO slots and actual delivery times."""
    tick: int
    deliveries: dict = field(default_factory=dict)
    pending: dict = field(default_factory=dict)


def begin_discard(state, action_player):
    state._cp_discard_delivery = (
        DiscardDelivery(state.server_action_tick) if claim_protection_enabled(state) else None)


def stage_protected_cut(state, viewer, payload, deliver):
    """Reserve the hidden cut's FIFO slot before dora, tags, or other metadata."""
    from .outbound_pipe import DeliveryMark, schedule_viewer_send
    record = getattr(state, "_cp_discard_delivery", None)
    if record is None:
        return
    released = record.pending[viewer] = asyncio.Event()
    mark = record.deliveries[viewer] = DeliveryMark()
    async def gated():
        await released.wait()
        await deliver()
    schedule_viewer_send(state, viewer, gated,
                         delay_before=take_post_meld_gap_delay(state, viewer), mark=mark)


async def send_cut(state, viewer, payload, deliver):
    from .outbound_pipe import DeliveryMark, send_to_viewer
    record = getattr(state, "_cp_discard_delivery", None)
    if record is None or record.tick != payload.get("action_tick"):
        return False
    if viewer in record.pending:
        record.pending[viewer].set()
    else:
        mark = record.deliveries[viewer] = DeliveryMark()
        await send_to_viewer(state, viewer, deliver,
                             delay_before=take_post_meld_gap_delay(state, viewer), mark=mark)
    return True


def schedule_meld(state, viewer, deliver, *, protected):
    from .outbound_pipe import schedule_viewer_send
    record = getattr(state, "_cp_discard_delivery", None)
    if not protected or record is None or viewer not in record.deliveries:
        return False
    schedule_viewer_send(state, viewer, deliver, after=record.deliveries[viewer],
                         min_gap=get_meld_followup_gap(state),
                         delay_before=take_post_meld_gap_delay(state, viewer))
    return True
