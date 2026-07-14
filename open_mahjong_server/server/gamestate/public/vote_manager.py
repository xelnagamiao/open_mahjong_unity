# 房间对局投票暂停/结束管理器
# 仅用于自定义房间（room_type == "custom"）对局；机器人不参与投票。
# 设计要点：
# - 投票发起后 20 秒倒计时；未全员同意则作废，发起者 5 分钟内不可再次发起。
# - 全员同意「暂停」→ 进入 pause_pending，由主状态机迭代顶部的检查点 checkpoint() 在
#   方便服务器还原的节点（每一步开始前）真正挂起；战鸣/抢杠等复杂节点不会被中断，
#   自然推迟到下一次主状态机迭代。
# - 暂停最长 1000 秒，超时自动解除。
# - 「解除暂停」同样需要全员真人同意：点击后进入 resume_voting（20 秒），
#   全员同意 → 5 秒倒计时 → 恢复；任一拒绝或超时 → 回到 paused 继续暂停。
# - 全员同意「结束」→ 5 秒倒计时后清理对局，客户端回主菜单。
import asyncio
import time
import logging

from ...response import Response

logger = logging.getLogger(__name__)

VOTE_TIMEOUT = 20.0          # 投票倒计时（秒）
PAUSE_MAX = 1000.0           # 暂停最长时长（秒）
RESUME_COUNTDOWN = 5.0       # 解除暂停通过后倒计时（秒）
END_COUNTDOWN = 5.0          # 结束对局倒计时（秒）
REJECT_COUNTDOWN = 5.0       # 有人拒绝后保留红色方块展示的倒计时（秒）
INITIATE_THROTTLE = 300.0    # 同一发起者最短间隔（5 分钟）


class VoteManager:
    """单个对局进程的投票状态机。由 gamestate_router 在收到投票消息时惰性挂载到 game_state.vote_manager。"""

    def __init__(self, game_state):
        self.gs = game_state
        # idle / voting / pause_pending / paused / resume_voting / resume_countdown / end_countdown
        self.phase = "idle"
        self.vote_type = None        # "pause" / "end" / "resume"
        self.votes = {}              # user_id -> "agree" / "refuse"
        self.initiator_user_id = None
        self._last_initiate_ts = {}  # (user_id, vote_type) -> monotonic；按类型分别节流
        self._pause_event = asyncio.Event()
        self._timer_task = None      # 投票超时 / 结束倒计时任务
        self._pause_deadline_task = None  # 暂停 1000s 自动解除任务
        self._pause_start_ts = 0.0   # 进入 paused 时刻，用于计算剩余时长

    # ---------- 辅助 ----------

    @property
    def human_players(self):
        return [p for p in self.gs.player_list if not getattr(p, "is_bot", False)]

    def _player_of(self, user_id):
        for p in self.gs.player_list:
            if p.user_id == user_id:
                return p
        return None

    def is_busy(self):
        return self.phase != "idle"

    @staticmethod
    def _format_remaining(seconds):
        seconds = max(0, int(seconds))
        m, s = divmod(seconds, 60)
        return f"{m}分{s}秒" if m > 0 else f"{s}秒"

    def _throttle_remaining(self, user_id, vote_type):
        """返回该发起者该类型投票的剩余冷却秒数；None 表示可发起。"""
        last = self._last_initiate_ts.get((user_id, vote_type))
        if last is None:
            return None
        elapsed = time.monotonic() - last
        if elapsed >= INITIATE_THROTTLE:
            return None
        return INITIATE_THROTTLE - elapsed

    def _clear_throttle(self, vote_type):
        """投票通过后清除该类型的全部节流记录，允许再次发起。"""
        stale = [k for k in self._last_initiate_ts if k[1] == vote_type]
        for k in stale:
            del self._last_initiate_ts[k]

    def _cancel_vote_timer(self):
        """仅取消投票/结束倒计时任务，保留暂停 1000s 自动解除任务。"""
        if self._timer_task and not self._timer_task.done():
            self._timer_task.cancel()
        self._timer_task = None

    def _cancel_all_timers(self):
        self._cancel_vote_timer()
        if self._pause_deadline_task and not self._pause_deadline_task.done():
            self._pause_deadline_task.cancel()
        self._pause_deadline_task = None

    # ---------- 来自客户端的请求 ----------

    async def initiate_vote(self, user_id, vote_type):
        if self.is_busy():
            return False, "已有进行中的投票"
        if vote_type not in ("pause", "end"):
            return False, "非法的投票类型"
        player = self._player_of(user_id)
        if player is None or getattr(player, "is_bot", False):
            return False, "只有真人玩家可以发起投票"
        remain = self._throttle_remaining(user_id, vote_type)
        if remain is not None:
            label = "暂停" if vote_type == "pause" else "结束"
            return False, f"发起{label}投票过于频繁，请在 {self._format_remaining(remain)} 后重试"
        self._last_initiate_ts[(user_id, vote_type)] = time.monotonic()

        self.vote_type = vote_type
        self.initiator_user_id = user_id
        self.votes = {}  # 发起者不自动同意，由其本人手动点同意/拒绝
        self.phase = "voting"
        self._cancel_vote_timer()
        self._timer_task = asyncio.create_task(self._vote_timeout_task())
        await self._broadcast()
        logger.info(f"投票发起 gamestate_id={self.gs.gamestate_id} user_id={user_id} type={vote_type}")
        return True, "投票已发起"

    async def cast_vote(self, user_id, vote):
        if self.phase not in ("voting", "resume_voting"):
            return False, "当前没有进行中的投票"
        if vote not in ("agree", "refuse"):
            return False, "非法的投票选项"
        if user_id not in [p.user_id for p in self.human_players]:
            return False, "非对局真人玩家"
        self.votes[user_id] = vote
        await self._broadcast()
        await self._evaluate()
        return True, "投票已记录"

    async def request_resume(self, user_id):
        """点击「解除暂停」→ 发起解除暂停投票（需全员真人同意）。"""
        if self.phase != "paused":
            return False, "当前未处于暂停状态"
        player = self._player_of(user_id)
        if player is None or getattr(player, "is_bot", False):
            return False, "只有真人玩家可以发起解除暂停"
        remain = self._throttle_remaining(user_id, "resume")
        if remain is not None:
            return False, f"发起解除暂停投票过于频繁，请在 {self._format_remaining(remain)} 后重试"
        self._last_initiate_ts[(user_id, "resume")] = time.monotonic()
        self.vote_type = "resume"
        self.initiator_user_id = user_id
        self.votes = {}  # 发起者不自动同意，由其本人手动点同意/拒绝
        self.phase = "resume_voting"
        self._cancel_vote_timer()
        self._timer_task = asyncio.create_task(self._resume_vote_timeout_task())
        await self._broadcast()
        logger.info(f"解除暂停投票发起 gamestate_id={self.gs.gamestate_id} user_id={user_id}")
        return True, "已发起解除暂停投票"

    # ---------- Web 后台管理端强制控制（绕过投票，限管理端调用）----------

    async def admin_force_pause(self):
        """管理端强制暂停：置 pause_pending，由主循环 checkpoint 在安全节点挂起。"""
        if self.phase in ("paused", "pause_pending"):
            return True, "对局已处于暂停/待暂停状态"
        self._cancel_all_timers()
        self.vote_type = "pause"
        self.initiator_user_id = None
        self.votes = {}
        self.phase = "pause_pending"
        await self._broadcast(reason="管理员请求暂停")
        logger.info(f"管理端强制暂停 gamestate_id={self.gs.gamestate_id}")
        return True, "已请求暂停，将在下一安全节点生效"

    async def admin_force_resume(self):
        """管理端强制解除暂停。"""
        if self.phase == "pause_pending":
            self._cancel_all_timers()
            self.phase = "idle"
            self.vote_type = None
            self.votes = {}
            await self._broadcast(reason="管理员取消待决暂停")
            return True, "已取消待决暂停"
        if self.phase in ("paused", "resume_voting", "resume_countdown"):
            self._cancel_all_timers()
            self._pause_event.set()
            logger.info(f"管理端强制解除暂停 gamestate_id={self.gs.gamestate_id}")
            return True, "已请求解除暂停"
        return False, "当前未处于暂停状态"

    async def admin_force_end(self):
        """管理端强制结束对局：广播 vote_end 后立即清理，不走 5 秒倒计时。"""
        self._cancel_all_timers()
        self.phase = "end_countdown"
        self.vote_type = "end"
        await self._broadcast_end()
        try:
            await self.gs.game_server.gamestate_manager.cleanup_game_state_complete(
                gamestate_id=self.gs.gamestate_id
            )
            if getattr(self.gs, "room_type", None) != "match":
                await self.gs.game_server.room_manager.finish_custom_game_room(self.gs.room_id)
        except Exception as e:
            logger.error(f"管理端结束对局清理失败: {e}", exc_info=True)
        logger.info(f"管理端强制结束对局 gamestate_id={self.gs.gamestate_id}")
        return True, "已结束对局"

    # ---------- 内部状态推进 ----------

    async def _evaluate(self):
        humans = self.human_players
        agree = sum(1 for p in humans if self.votes.get(p.user_id) == "agree")
        refuse = sum(1 for p in humans if self.votes.get(p.user_id) == "refuse")
        total = len(humans)

        if refuse > 0:
            # 有人拒绝：进入 rejected 阶段，保留 votes（红色方块可见）并 5 秒倒计时，
            # 倒计时结束后再回到 idle（普通投票）或 paused（解除暂停投票）。
            self._cancel_vote_timer()
            self.phase = "rejected"
            await self._broadcast(reason="有玩家拒绝")
            self._timer_task = asyncio.create_task(self._reject_resolve_task())
            return

        if total > 0 and agree >= total:
            if self.phase == "resume_voting":
                # 全员同意解除暂停 → 交由 checkpoint 完成 5 秒倒计时与恢复
                self._cancel_all_timers()
                self._clear_throttle("resume")
                self._pause_event.set()
                return
            self._cancel_all_timers()
            if self.vote_type == "pause":
                self._clear_throttle("pause")
                self.phase = "pause_pending"  # 等待主循环 checkpoint 真正挂起
                await self._broadcast()
            else:  # end
                self._clear_throttle("end")
                self.phase = "end_countdown"
                await self._broadcast()
                self._timer_task = asyncio.create_task(self._end_resolve_task())

    async def _vote_timeout_task(self):
        try:
            await asyncio.sleep(VOTE_TIMEOUT)
            if self.phase == "voting":
                await self._cancel("投票超时")
        except asyncio.CancelledError:
            pass

    async def _resume_vote_timeout_task(self):
        try:
            await asyncio.sleep(VOTE_TIMEOUT)
            if self.phase == "resume_voting":
                await self._back_to_paused("解除暂停投票超时")
        except asyncio.CancelledError:
            pass

    async def _reject_resolve_task(self):
        try:
            await asyncio.sleep(REJECT_COUNTDOWN)
            if self.phase != "rejected":
                return
            if self.vote_type == "resume":
                await self._back_to_paused("有玩家拒绝解除暂停")
            else:
                await self._cancel("有玩家拒绝")
        except asyncio.CancelledError:
            pass

    async def _cancel(self, reason):
        self._cancel_all_timers()
        self.phase = "idle"
        self.vote_type = None
        self.votes = {}
        self.initiator_user_id = None
        await self._broadcast(reason=reason)

    async def _back_to_paused(self, reason):
        """解除暂停投票未通过：回到 paused 继续暂停（保留 1000s 自动解除任务）。"""
        self._cancel_vote_timer()
        self.vote_type = None
        self.votes = {}
        self.initiator_user_id = None
        self.phase = "paused"
        await self._broadcast(reason=reason)

    async def _end_resolve_task(self):
        try:
            await asyncio.sleep(END_COUNTDOWN)
            if self.phase != "end_countdown":
                return
            # 若对局已在这 5 秒内自然结束，则不再强制结束
            if self.gs.gamestate_id not in self.gs.game_server.gamestate_manager.gamestate_id_to_game_state:
                self.phase = "idle"
                return
            await self._broadcast_end()
            try:
                await self.gs.game_server.gamestate_manager.cleanup_game_state_complete(
                    gamestate_id=self.gs.gamestate_id
                )
                # 自定义/赛事房间需恢复等待态（重置 is_game_running、清空 ready_list、广播房间信息），
                # 否则房主再次开局会被「游戏已在进行中」拦截。match 房走销毁，此处不处理。
                if getattr(self.gs, "room_type", None) != "match":
                    await self.gs.game_server.room_manager.finish_custom_game_room(self.gs.room_id)
            except Exception as e:
                logger.error(f"投票结束清理对局失败: {e}", exc_info=True)
        except asyncio.CancelledError:
            pass

    # ---------- 主循环检查点 ----------

    async def checkpoint(self):
        """在主状态机每次迭代顶部调用；非 pause_pending 时为空操作。"""
        if self.phase != "pause_pending":
            return
        self.phase = "paused"
        self._pause_event.clear()
        self._pause_start_ts = time.monotonic()
        self._pause_deadline_task = asyncio.create_task(self._pause_deadline_task_fn())
        await self._broadcast()
        try:
            await self._pause_event.wait()
        finally:
            self._cancel_all_timers()
        # 解除暂停 5 秒倒计时（仍处于挂起状态，主循环不会推进）
        self.phase = "resume_countdown"
        await self._broadcast()
        await asyncio.sleep(RESUME_COUNTDOWN)
        # 恢复
        self.phase = "idle"
        self.vote_type = None
        self.votes = {}
        self.initiator_user_id = None
        await self._broadcast()

    async def _pause_deadline_task_fn(self):
        try:
            await asyncio.sleep(PAUSE_MAX)
            if self.phase in ("paused", "resume_voting"):
                self._pause_event.set()  # 1000 秒到自动解除
        except asyncio.CancelledError:
            pass

    # ---------- 广播 ----------

    def _build_payload(self, reason=""):
        humans = self.human_players
        vote_map = {}
        for p in self.gs.player_list:
            if getattr(p, "is_bot", False):
                vote_map[str(p.player_index)] = "bot"
            else:
                vote_map[str(p.player_index)] = self.votes.get(p.user_id, "none")
        agree = sum(1 for p in humans if self.votes.get(p.user_id) == "agree")
        refuse = sum(1 for p in humans if self.votes.get(p.user_id) == "refuse")
        total = len(humans)
        countdown = 0
        if self.phase in ("voting", "resume_voting"):
            countdown = int(VOTE_TIMEOUT)
        elif self.phase == "paused":
            elapsed = time.monotonic() - self._pause_start_ts
            countdown = max(0, int(PAUSE_MAX - elapsed))
        elif self.phase == "resume_countdown":
            countdown = int(RESUME_COUNTDOWN)
        elif self.phase == "end_countdown":
            countdown = int(END_COUNTDOWN)
        elif self.phase == "rejected":
            countdown = int(REJECT_COUNTDOWN)
        info = {
            "phase": self.phase,
            "vote_type": self.vote_type,
            "agree": agree,
            "refuse": refuse,
            "total": total,
            "countdown": countdown,
            "votes": vote_map,
            "reason": reason,
        }
        response = Response(
            type="gamestate/vote_update",
            success=True,
            message=reason,
            vote_info=info,
        )
        return response.dict(exclude_none=True)

    async def _broadcast(self, reason=""):
        payload = self._build_payload(reason=reason)
        game_server = self.gs.game_server
        for player in self.gs.player_list:
            try:
                if player.user_id < 10:
                    continue
                if "offline" in getattr(player, "tag_list", []):
                    continue
                conn = game_server.user_id_to_connection.get(player.user_id)
                if conn is None:
                    continue
                await conn.websocket.send_json(payload)
            except Exception as e:
                logger.error(f"广播投票状态给 user_id={player.user_id} 失败: {e}", exc_info=True)

    async def _broadcast_end(self):
        payload = Response(
            type="gamestate/vote_end",
            success=True,
            message="投票结束对局通过",
        ).dict(exclude_none=True)
        game_server = self.gs.game_server
        for player in self.gs.player_list:
            try:
                if player.user_id < 10:
                    continue
                conn = game_server.user_id_to_connection.get(player.user_id)
                if conn is None:
                    continue
                await conn.websocket.send_json(payload)
            except Exception:
                pass


async def vote_checkpoint(game_state):
    """主循环安全检查点：未挂载 VoteManager 或非暂停待决时为空操作。"""
    vm = getattr(game_state, "vote_manager", None)
    if vm is None:
        return
    await vm.checkpoint()


def get_or_create_vote_manager(game_state):
    vm = getattr(game_state, "vote_manager", None)
    if vm is None:
        vm = VoteManager(game_state)
        game_state.vote_manager = vm
    return vm
