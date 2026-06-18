"""四川麻将（血战到底）对局状态机。

要点：
- 108 张（万/饼/条），庄家 14 张其余 13 张，不允许吃牌。
- 定缺阶段：四家同时选缺门花色。
- 血战到底（可选 blood_battle）：开=和牌后退场续打至三家和或流局；关=一家和牌即结束本盘。
- 一炮多响：同一弃牌所有可和家均和。
- 刮风下雨：点杠/摸杠加杠/暗杠即时收分，流局没叫者退税、杠上炮/抢杠退税。
- 流局查大叫：没叫者按有叫者理论最大番支付基本分；花猪均按没叫查叫，分被动（原始出牌全为定缺）/主动（曾打非定缺且手含定缺→没叫 + 额外扣 24 分）。
- 定庄：上副最先和者为庄；并列最先（一炮多响）则点炮者为庄；流局连庄。
"""
import asyncio
import json
import logging
import math
import random
import time
from typing import Any, Dict, List, Optional, Tuple

from .action_check import check_action_hand_action, refresh_waiting_tiles
from .wait_action import wait_action
from .shunhe import clear_shunhe
from .boardcast import (
    broadcast_game_start, broadcast_ask_hand_action, broadcast_ask_other_action,
    broadcast_do_action, broadcast_result, broadcast_game_end,
    broadcast_refresh_player_tag_list, broadcast_ready_status,
    broadcast_dingque_ask, broadcast_dingque_done, reconnected_send_pending_ask,
)
from ..public.logic_common import next_current_num, assign_strict_final_ranks
from .init_tiles import init_sichuan_tiles
from ..public.spectator_rules import too_many_ai_for_spectator
from ..public.game_record_manager import (
    init_game_record, init_game_round, player_action_record_deal,
    player_action_record_round_end, end_game_record, capture_player_entry_order,
    player_action_record_hu, player_action_record_liuju,
    player_action_record_sichuan_liuju_step, player_action_record_gang_refund,
)
from ..public.round_end_timing import (
    hu_result_ready_wait_seconds,
    shuhewei_ready_wait_seconds,
    sichuan_chajiao_panel_wait_seconds,
    sichuan_settle_hu_panel_wait_seconds,
    ROUND_END_HAND_REVEAL_SEC,
)
from ..public.ready_phase import run_sichuan_liuju_final_ready_phase
from ...game_calculation.game_calculation_service import GameCalculationService
from ...database.db_manager import DatabaseManager
from ..public.random_seed_manager import setup_random_seed_system

logger = logging.getLogger(__name__)

# 主动花猪：曾打出非定缺牌但流局时手牌仍含定缺牌，查叫阶段额外自扣（不转付他人）
SICHUAN_ACTIVE_HUAZHU_PENALTY = 24
HUA_ZHU_DISPLAY_STATUS = frozenset({"hua_zhu", "hua_zhu_passive", "hua_zhu_active"})

HU_ORDER_TAGS = ("first_hu", "second_hu", "third_hu")
SICHUAN_MID_HU_ANIM_SECONDS = 1.5


class RecordCounter:
    def __init__(self):
        self.fulu_times = 0
        self.recorded_fans = []
        self.rank_result = 0
        self.zimo_times = 0
        self.dianhe_times = 0
        self.fangchong_times = 0
        self.fangchong_score = 0
        self.win_turn = 0
        self.win_score = 0


class SichuanPlayer:
    def __init__(self, user_id: int, username: str, tiles: list, remaining_time: int):
        self.user_id = user_id
        self.username = username
        self.is_bot = user_id <= 10
        self.hand_tiles = tiles
        self.huapai_list = []
        self.discard_tiles = []
        self.discard_origin_tiles = []
        self.combination_tiles = []
        self.combination_mask = []
        self.score = 0
        self.remaining_time = remaining_time
        self.player_index = 0
        self.original_player_index = 0
        self.tag_list = []
        self.waiting_tiles = set()
        self.record_counter = RecordCounter()
        self.score_history = []
        self.round_number_history = []
        self.title_used = 0
        self.profile_used = 0
        self.character_used = 0
        self.voice_used = 0
        self.has_draw_slot = False
        # 四川专用
        self.dingque_suit = 0   # 1万 2饼 3条 0未定缺
        self.is_hu = False      # 血战：本盘已和退场
        self.hu_order = 0       # 和牌顺序（1=最先）
        self.gang_score_records = []  # 刮风下雨记录，用于退税
        self.shunhe_skipped_fan = None     # 顺和：最近一次跳过和牌的番数（待听牌出牌后生效）
        self.shunhe_passed_max_fan = None  # 顺和：已生效跳过番（下次摸牌前不可点和≤该番，自摸不受限）

    def get_tile(self, tiles_list, *, mark_draw_slot: bool = True):
        element = tiles_list.pop(0)
        self.hand_tiles.append(element)
        if mark_draw_slot:
            self.has_draw_slot = True

    def get_gang_tile(self, tiles_list, gamestate):
        # 四川无死墙，补牌从牌墙尾摸取
        element = tiles_list.pop(-1)
        self.hand_tiles.append(element)
        self.has_draw_slot = True


class SichuanGameState:
    def __init__(self, game_server, room_data: dict, calculation_service: GameCalculationService,
                 db_manager: DatabaseManager, gamestate_id: str):
        self.game_server = game_server
        self.calculation_service = calculation_service
        self.db_manager = db_manager
        self.gamestate_id = gamestate_id
        self.game_record = {}
        self.game_task: Optional[asyncio.Task] = None
        self.player_list: List[SichuanPlayer] = []
        player_settings = room_data.get("player_settings", {})
        for user_id in room_data["player_list"]:
            ps = player_settings.get(user_id, {})
            if user_id == 0:
                username = "麻雀罗伯特"
            elif user_id == 2:
                username = "牌效罗伯特"
            else:
                username = ps.get("username", f"用户{user_id}")
            player = SichuanPlayer(user_id, username, [], room_data["round_timer"])
            player.title_used = ps.get("title_id", 1)
            player.profile_used = ps.get("profile_image_id", 1)
            player.character_used = ps.get("character_id", 1)
            player.voice_used = ps.get("voice_id", 1)
            self.player_list.append(player)

        self.room_id = room_data["room_id"]
        self.tips = room_data["tips"]
        self.max_round = room_data["game_round"]
        self.step_time = room_data["step_timer"]
        self.round_time = room_data["round_timer"]
        self.room_rule = room_data["room_rule"]
        self.room_type = room_data["room_type"]
        self.sub_rule = room_data.get("sub_rule", "sichuan/standard")

        self.room_random_seed = room_data.get("random_seed", 0)
        self.open_cuohe = False
        self.show_moqie_hint = room_data.get("show_moqie_hint", False)
        self.tactical_call = room_data.get("tactical_call", False)
        self.blood_battle = room_data.get("blood_battle", True)
        self.hepai_limit = 1
        self.tourist_limit = room_data.get("tourist_limit", False)
        self.allow_spectator_config = room_data.get("allow_spectator", True)
        self.isPlayerSetRandomSeed = False

        self.tiles_list = []
        self.current_player_index = 0
        self.dealer_index = 0
        self.xunmu = 1
        self.master_seed = 0
        self.commitment = 0
        self.salt = ""
        self.round_random_seed = 0
        self.game_status = "waiting"
        self.server_action_tick = 0
        self.current_round = 1
        self.round_index = 1
        self.jiagang_tile = None
        self.last_action_was_gang = False
        self.paofen_watch = None  # (gainer_index, gang_record) 用于杠上炮退税
        self.pending_win = None
        self.sichuan_hu_results: Dict[int, dict] = {}
        self.first_win_event = None  # {"winners":[idx...], "discarder": idx_or_None}
        self.hu_order_counter = 0
        self.deferred_hu_settlements: List[dict] = []  # 血战：终局再按和牌顺序结算
        self.ended_by = None  # "win" / "liuju"

        self.action_events: Dict[int, asyncio.Event] = {i: asyncio.Event() for i in range(4)}
        self.action_queues: Dict[int, asyncio.Queue] = {i: asyncio.Queue() for i in range(4)}
        self.waiting_players_list = []
        self.action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
        self.action_priority: Dict[str, int] = {
            "hu_self": 6, "hu": 5,
            "peng": 2, "gang": 2,
            "dingque": 6, "ready": 0,
            "pass": 0, "cut": 0, "angang": 0, "jiagang": 0,
            "deal_tile": 0, "deal_gang_tile": 0,
        }
        self.dead_wall_count = 0  # 四川无死墙
        self.Debug = False

        self.spectator_enabled = self.allow_spectator_config and not too_many_ai_for_spectator(self.player_list)
        from ..game_classical.spectator_manager import SpectatorManager
        self.spectator_manager = SpectatorManager(self, delay=180.0, enabled=self.spectator_enabled)
        self.realtime_spectators = []

    # ============ 网络/重连/掉线（与古典一致） ============

    async def send_to_realtime_spectators(self, player_index: int, response):
        from ..public.spectator_rules import deliver_realtime_spectator_message
        await deliver_realtime_spectator_message(self, player_index, response)

    async def player_disconnect(self, user_id: int):
        for p in self.player_list:
            if p.user_id == user_id and "offline" not in p.tag_list:
                p.tag_list.append("offline")
                await broadcast_refresh_player_tag_list(self)
                break
        non_ai = [p for p in self.player_list if p.user_id >= 10]
        if non_ai and all("offline" in p.tag_list for p in non_ai):
            await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)

    async def player_reconnect(self, user_id: int):
        for p in self.player_list:
            if p.user_id != user_id:
                continue
            if "offline" in p.tag_list:
                p.tag_list.remove("offline")
                await broadcast_refresh_player_tag_list(self)
            if user_id in self.game_server.user_id_to_connection:
                from ...response import Response, GameInfo
                from .boardcast import _base_game_info, _build_players_info
                player_conn = self.game_server.user_id_to_connection[user_id]
                game_info = GameInfo(
                    **{**_base_game_info(self), 'players_info': _build_players_info(self, user_id)},
                    self_hand_tiles=None,
                )
                response = Response(type="gamestate/sichuan/game_start", success=True,
                                    message="重连成功，游戏继续", game_info=game_info)
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await reconnected_send_pending_ask(self, user_id)
            break

    async def cleanup_game_state(self):
        await self.spectator_manager.cleanup()
        if self.game_task and not self.game_task.done():
            self.game_task.cancel()
            try:
                await self.game_task
            except asyncio.CancelledError:
                logger.info(f"四川游戏循环已取消 room_id={self.room_id}")
            except Exception as e:
                logger.error(f"取消四川游戏循环出错 room_id={self.room_id}: {e}")

    async def add_spectator(self, user_id: int, connection: Any):
        await self.spectator_manager.add_spectator(user_id, connection)

    async def remove_spectator(self, user_id: int):
        await self.spectator_manager.remove_spectator(user_id)

    # ============ 主循环 ============

    async def run_game_loop(self):
        try:
            await self.game_loop_sichuan()
        except asyncio.CancelledError:
            logger.info(f"四川游戏循环被取消 room_id={self.room_id}")
            raise
        except Exception as e:
            logger.error(f"四川游戏循环未捕获异常 room_id={self.room_id}: {e}", exc_info=True)
            try:
                await self.cleanup_game_state()
            except Exception as ce:
                logger.error(f"清理四川游戏状态出错: {ce}", exc_info=True)

    def _next_active_index(self, from_index: int) -> Optional[int]:
        idx = from_index
        for _ in range(4):
            idx = next_current_num(idx)
            if not self.player_list[idx].is_hu:
                return idx
        return None

    def _distance_from(self, start: int, target: int) -> int:
        d = 0
        idx = start
        while idx != target and d < 4:
            idx = next_current_num(idx)
            d += 1
        return d

    async def game_loop_sichuan(self):
        if not self.Debug:
            user_seed = self.room_random_seed if self.room_random_seed else None
            self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(user_seed)
            capture_player_entry_order(self)
            rng = random.Random(self.master_seed)
            rng.shuffle(self.player_list)
        else:
            self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system()
            capture_player_entry_order(self)
        for index, player in enumerate(self.player_list):
            player.player_index = index
            player.original_player_index = index

        init_game_record(self)
        self.game_record["game_title"]["sub_rule"] = self.sub_rule
        self.game_record["game_title"]["hepai_limit"] = self.hepai_limit
        self.dealer_index = 0

        while self.current_round <= self.max_round * 4:
            self._reset_round_state()
            init_sichuan_tiles(self)
            await self.broadcast_game_start()
            init_game_round(self)

            # 定缺阶段
            await self._dingque_phase()
            await broadcast_dingque_done(self)

            # 庄家摸第 14 张
            self.current_player_index = self.dealer_index
            self.game_status = "waiting_hand_action"
            refresh_waiting_tiles(self, self.current_player_index)
            self.player_list[self.current_player_index].get_tile(self.tiles_list)
            player_action_record_deal(self, deal_tile=self.player_list[self.current_player_index].hand_tiles[-1], deal_type="d")
            await self.broadcast_do_action(action_list=["deal_tile"], action_player=self.current_player_index,
                                           deal_tile=self.player_list[self.current_player_index].hand_tiles[-1])
            self.action_dict = check_action_hand_action(self, self.current_player_index, is_first_action=True)
            await self.broadcast_ask_hand_action()
            await self.wait_action()

            while self.game_status != "END":
                match self.game_status:
                    case "deal_card":
                        nxt = self._next_active_index(self.current_player_index)
                        if nxt is None or len(self.tiles_list) <= self.dead_wall_count:
                            self.ended_by = "liuju"
                            self.game_status = "END"
                            break
                        self.current_player_index = nxt
                        refresh_waiting_tiles(self, self.current_player_index)
                        draw_player = self.player_list[self.current_player_index]
                        draw_player.get_tile(self.tiles_list)
                        if clear_shunhe(draw_player):
                            await broadcast_refresh_player_tag_list(self)
                        self.last_action_was_gang = False
                        player_action_record_deal(self, deal_tile=self.player_list[self.current_player_index].hand_tiles[-1], deal_type="d")
                        await self.broadcast_do_action(action_list=["deal_tile"], action_player=self.current_player_index,
                                                       deal_tile=self.player_list[self.current_player_index].hand_tiles[-1])
                        self.action_dict = check_action_hand_action(self, self.current_player_index)
                        self.game_status = "waiting_hand_action"

                    case "deal_card_after_gang":
                        if len(self.tiles_list) <= self.dead_wall_count:
                            self.ended_by = "liuju"
                            self.game_status = "END"
                            break
                        refresh_waiting_tiles(self, self.current_player_index)
                        gang_draw_player = self.player_list[self.current_player_index]
                        gang_draw_player.get_gang_tile(self.tiles_list, self)
                        if clear_shunhe(gang_draw_player):
                            await broadcast_refresh_player_tag_list(self)
                        player_action_record_deal(self, deal_tile=self.player_list[self.current_player_index].hand_tiles[-1], deal_type="gd")
                        await self.broadcast_do_action(action_list=["deal_gang_tile"], action_player=self.current_player_index,
                                                       deal_tile=self.player_list[self.current_player_index].hand_tiles[-1])
                        self.action_dict = check_action_hand_action(self, self.current_player_index, is_get_gang_tile=True)
                        self.game_status = "waiting_hand_action"

                    case "waiting_hand_action":
                        await self.broadcast_ask_hand_action()
                        await self.wait_action()

                    case "waiting_action_after_cut" | "waiting_action_qianggang":
                        await self.broadcast_ask_other_action()
                        await self.wait_action()

                    case "onlycut_after_action":
                        self.action_dict = {0: [], 1: [], 2: [], 3: []}
                        self.action_dict[self.current_player_index].append("cut")
                        self.game_status = "waiting_hand_action"

                    case "settle_win":
                        await self._settle_win()

                    case _:
                        logger.error(f"四川未匹配的 game_status: {self.game_status}")
                        self.game_status = "END"

            # 流局终局：与血战三家和共用 _settle_liuju（见 ABCD 顺序注释，禁止跳过查叫）
            if self.ended_by == "liuju":
                await self._settle_liuju()
                player_action_record_round_end(self)
                if hasattr(self, 'spectator_manager'):
                    self.spectator_manager.record_tick(["end"])
                await self._ready_phase(liuju=True)
            else:
                player_action_record_round_end(self)
                if hasattr(self, 'spectator_manager'):
                    self.spectator_manager.record_tick(["end"])
                await self._ready_phase(liuju=False)

            # 定庄
            self.dealer_index = self._decide_next_dealer()
            self.current_round += 1
            self.round_index += 1

        end_game_record(self)
        assign_strict_final_ranks(self.player_list)
        await self.broadcast_game_end()
        if hasattr(self, 'spectator_manager'):
            await self.spectator_manager.send_final_record_and_close()

        # 持久化（四川暂未接入专用统计表，自定义房不强制落库）
        try:
            store = getattr(self.db_manager, "store_sichuan_game_record", None)
            if store:
                store(self.game_record, self.player_list, self.room_type, f"{self.max_round}/4")
        except Exception as e:
            logger.warning(f"四川对局落库跳过/失败: {e}")

        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
        if self.room_type == "match":
            await self.game_server.room_manager.destroy_room(self.room_id)
        else:
            await self.game_server.room_manager.finish_custom_game_room(self.room_id)
        logger.info(f"四川对局结束清理完成 room_id={self.room_id}")

    def _reset_round_state(self):
        for p in self.player_list:
            p.hand_tiles = []
            p.huapai_list = []
            p.discard_tiles = []
            p.discard_origin_tiles = []
            p.combination_tiles = []
            p.combination_mask = []
            p.waiting_tiles = set()
            p.has_draw_slot = False
            p.dingque_suit = 0
            p.is_hu = False
            p.hu_order = 0
            p.gang_score_records = []
            p.shunhe_skipped_fan = None
            p.shunhe_passed_max_fan = None
        self.xunmu = 1
        self.jiagang_tile = None
        self.last_action_was_gang = False
        self.paofen_watch = None
        self.pending_win = None
        self.sichuan_hu_results = {}
        self.first_win_event = None
        self.hu_order_counter = 0
        self.deferred_hu_settlements = []
        self.ended_by = None
        self._liuju_endgame_started = False
        for p in self.player_list:
            p.tag_list = [t for t in p.tag_list if t not in HU_ORDER_TAGS and not t.startswith("shunhe_")]

    # ============ 定缺阶段 ============

    async def _dingque_phase(self):
        self.game_status = "waiting_dingque"
        self.action_dict = {i: ["dingque"] for i in range(4)}
        self.waiting_players_list = [0, 1, 2, 3]
        for p in self.player_list:
            p.remaining_time = self.round_time
        for i in range(4):
            while not self.action_queues[i].empty():
                try:
                    self.action_queues[i].get_nowait()
                except Exception:
                    break
            self.action_events[i].clear()
        await broadcast_dingque_ask(self)

        deadline = time.time() + max(self.step_time, 10)
        pending = set(range(4))
        while pending and time.time() < deadline:
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in pending]
            await asyncio.wait(tasks, timeout=1, return_when=asyncio.FIRST_COMPLETED)
            for t in tasks:
                if not t.done():
                    t.cancel()
            for i in list(pending):
                if self.action_events[i].is_set():
                    try:
                        data = self.action_queues[i].get_nowait()
                    except Exception:
                        data = {}
                    suit = data.get("target_tile", 0)
                    if suit in (1, 2, 3):
                        self.player_list[i].dingque_suit = suit
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if i in self.waiting_players_list:
                        self.waiting_players_list.remove(i)
                    pending.discard(i)
        for i in pending:
            counts = {1: 0, 2: 0, 3: 0}
            for t in self.player_list[i].hand_tiles:
                counts[t // 10] = counts.get(t // 10, 0) + 1
            self.player_list[i].dingque_suit = min(counts, key=lambda s: counts[s])
            self.action_dict[i] = []
            if i in self.waiting_players_list:
                self.waiting_players_list.remove(i)
        logger.info(f"四川定缺完成: {[(p.player_index, p.dingque_suit) for p in self.player_list]}")

    # ============ 刮风下雨记分/退税 ============

    def _record_gang_score(self, gainer: int, tile: int, gtype: str, payer_index: Optional[int] = None) -> Dict[int, int]:
        # 已和玩家刮风下雨不影响其他玩家分数
        if getattr(self.player_list[gainer], "is_hu", False):
            return {p.player_index: 0 for p in self.player_list}
        if gtype == "guafeng":
            payers = {payer_index: 2} if payer_index is not None else {}
        elif gtype == "xiayu1":
            payers = {p.player_index: 1 for p in self.player_list if p.player_index != gainer and not p.is_hu}
        elif gtype == "xiayu2":
            payers = {p.player_index: 2 for p in self.player_list if p.player_index != gainer and not p.is_hu}
        else:
            payers = {}
        changes: Dict[int, int] = {}
        total = 0
        for pidx, amt in payers.items():
            self.player_list[pidx].score -= amt
            changes[pidx] = changes.get(pidx, 0) - amt
            total += amt
        if total:
            self.player_list[gainer].score += total
            changes[gainer] = changes.get(gainer, 0) + total
        record = {"type": gtype, "tile": tile, "gainer": gainer, "payers": dict(payers), "total": total}
        self.player_list[gainer].gang_score_records.append(record)
        if total > 0:
            self.paofen_watch = (gainer, record)
        return changes

    def _refund_gang_record(self, record: dict) -> Dict[int, int]:
        changes: Dict[int, int] = {}
        gainer = record["gainer"]
        for pidx, amt in record["payers"].items():
            self.player_list[pidx].score += amt
            changes[pidx] = changes.get(pidx, 0) + amt
        self.player_list[gainer].score -= record["total"]
        changes[gainer] = changes.get(gainer, 0) - record["total"]
        if record in self.player_list[gainer].gang_score_records:
            self.player_list[gainer].gang_score_records.remove(record)
        return changes

    def _refund_last_gang(self, jiagang_player: int, tile: int) -> Dict[int, int]:
        for record in reversed(self.player_list[jiagang_player].gang_score_records):
            if record["tile"] == tile and record["type"] == "xiayu1":
                return self._refund_gang_record(record)
        return {}

    def _clear_paofen_pending(self, player_index: int):
        if self.paofen_watch and self.paofen_watch[0] == player_index:
            self.paofen_watch = None

    # ============ 和牌结算（血战续打） ============

    def _apply_hu_score_changes(
        self, winner: int, base: int, is_zimo: bool, discarder: Optional[int] = None,
    ) -> Dict[int, int]:
        """和牌收支，返回四家分数变更（未参与者显式为 0，便于客户端展示）。"""
        changes: Dict[int, int] = {p.player_index: 0 for p in self.player_list}
        if is_zimo:
            pay = base + 1  # 自摸加底
            total = 0
            winner_order = self.player_list[winner].hu_order or 0
            for p in self.player_list:
                if p.player_index == winner:
                    continue
                # 血战：先和者不向后和者的自摸付分；后和者仍须向先和者的自摸付分
                if p.is_hu and (p.hu_order or 0) < winner_order:
                    continue
                p.score -= pay
                changes[p.player_index] = -pay
                total += pay
            self.player_list[winner].score += total
            changes[winner] = total
        elif discarder is not None:
            self.player_list[discarder].score -= base
            changes[discarder] = -base
            self.player_list[winner].score += base
            changes[winner] = base
        return changes

    async def _settle_win(self):
        pw = self.pending_win
        self.pending_win = None
        gang_refund_changes: Dict[int, int] = {}
        # 杠上炮退税：点炮者恰为上一开杠者（其杠后弃牌被和）
        if pw["type"] == "ron" and self.paofen_watch and self.paofen_watch[0] == pw["discarder"]:
            gang_refund_changes = self._refund_gang_record(self.paofen_watch[1])
            self.paofen_watch = None
        elif pw.get("gang_refund_changes"):
            gang_refund_changes = pw["gang_refund_changes"]

        if pw["type"] == "zimo":
            winners = [self.current_player_index]
        else:
            discarder = pw["discarder"]
            winners = sorted(self.sichuan_hu_results.keys(), key=lambda x: self._distance_from(discarder, x))

        # 血战最多三家和：一炮多响时只取距点炮者最近且未超出剩余名额的和牌者
        if self.blood_battle:
            slots = max(0, 3 - sum(1 for p in self.player_list if p.is_hu))
            winners = [w for w in winners if not self.player_list[w].is_hu][:slots]
            if not winners:
                logger.warning("四川血战：无有效和牌者（已满三家或结果为空），续打")
                self.sichuan_hu_results = {}
                self.game_status = "deal_card"
                return

        if self.first_win_event is None:
            self.first_win_event = {
                "winners": list(winners),
                "discarder": pw.get("discarder"),
            }

        has_gang_refund = gang_refund_changes and any(v != 0 for v in gang_refund_changes.values())
        if has_gang_refund:
            player_action_record_gang_refund(self, gang_refund_changes)

        ron_idx = 0
        for ron_i, w in enumerate(winners):
            info = self.sichuan_hu_results.get(w)
            if not info:
                continue
            fan = info["fan"]
            fan_list = info["fan_list"]
            base = self.calculation_service.Sichuan_base_from_fan(fan, fan_list)
            is_zimo = (pw["type"] == "zimo")

            if not is_zimo:
                self.player_list[w].hand_tiles.append(pw["hepai_tile"])

            discarder = pw.get("discarder") if not is_zimo else None
            defer_score = self.blood_battle
            if defer_score:
                changes = {p.player_index: 0 for p in self.player_list}
            else:
                changes = self._apply_hu_score_changes(w, base, is_zimo, discarder)
            if is_zimo:
                self.player_list[w].record_counter.zimo_times += 1
            else:
                self.player_list[w].record_counter.dianhe_times += 1
                self.player_list[discarder].record_counter.fangchong_times += 1
                self.player_list[discarder].record_counter.fangchong_score += base

            self.hu_order_counter += 1
            self.player_list[w].hu_order = self.hu_order_counter
            self.player_list[w].is_hu = True
            self.player_list[w].record_counter.recorded_fans.append(fan_list)
            self.player_list[w].record_counter.win_score += base

            if defer_score and self.hu_order_counter <= len(HU_ORDER_TAGS):
                hu_tag = HU_ORDER_TAGS[self.hu_order_counter - 1]
                if hu_tag not in self.player_list[w].tag_list:
                    self.player_list[w].tag_list.append(hu_tag)

            hued = sum(1 for p in self.player_list if p.is_hu)
            round_over = (not self.blood_battle) or hued >= 3
            round_continues = self.blood_battle and not round_over
            hepai_tile = info.get("hepai_tile", 0)
            is_qianggang = pw["type"] == "qianggang"
            multi_ron = (not is_zimo) and len(winners) > 1
            recycle_discard = (not is_zimo) and ((not multi_ron) or (ron_i == len(winners) - 1))
            suppress_hand = self.blood_battle
            hand_payload = None if suppress_hand else self.player_list[w].hand_tiles
            mask_payload = None if suppress_hand else self.player_list[w].combination_mask
            # 复用既有和牌文案/音效：自摸=hu_self，点炮按一炮多响次序=hu_first/second/third
            if is_zimo:
                hu_class = "hu_self"
            else:
                hu_class = ["hu_first", "hu_second", "hu_third"][min(ron_idx, 2)]
                ron_idx += 1

            if defer_score:
                self.deferred_hu_settlements.append({
                    "winner": w,
                    "base": base,
                    "fan": fan,
                    "fan_list": list(fan_list),
                    "is_zimo": is_zimo,
                    "discarder": discarder,
                    "hepai_tile": hepai_tile,
                    "multi_ron": multi_ron,
                    "hu_class": hu_class,
                    "hu_order": self.hu_order_counter,
                })
                await broadcast_refresh_player_tag_list(self)

            # 牌谱：逐家记录和牌 tick（score_changes 按 player_index 排列）
            hu_changes_list = [changes.get(i, 0) for i in range(4)]
            player_action_record_hu(
                self, hu_class=hu_class, hu_score=base, hu_fan=fan_list,
                hepai_player_index=w, score_changes=hu_changes_list,
                hepai_tile=hepai_tile,
                multi_ron=multi_ron if not is_zimo else None,
                ron_discarder_index=discarder if not is_zimo else None,
                recycle_discard=recycle_discard if not is_zimo else None,
            )
            # 观战延迟缓冲：与持久牌谱保持一致（一炮多响逐家各记一帧）
            if hasattr(self, 'spectator_manager'):
                self.spectator_manager.record_tick([hu_class, w, base, fan_list, hu_changes_list])
            player_to_score = {p.player_index: p.score for p in self.player_list}
            await broadcast_result(
                self, hu_class=hu_class,
                hepai_player_index=w,
                win_player_index=w, is_zimo=is_zimo,
                hu_score=base if not defer_score else None,
                hu_fan=fan_list,
                hepai_player_hand=hand_payload,
                hepai_player_combination_mask=mask_payload,
                hepai_tile=hepai_tile,
                multi_ron=multi_ron,
                is_qianggang=is_qianggang if not is_zimo else None,
                ron_discarder_index=discarder if not is_zimo else None,
                recycle_discard=recycle_discard if not is_zimo else None,
                suppress_hand_reveal=suppress_hand,
                defer_score_settlement=defer_score,
                player_to_score=player_to_score,
                score_changes=None if defer_score else changes,
                gang_refund_changes=gang_refund_changes if has_gang_refund and ron_i == 0 else None,
                round_continues=round_continues,
            )
            if defer_score:
                await asyncio.sleep(SICHUAN_MID_HU_ANIM_SECONDS)
            else:
                await asyncio.sleep(hu_result_ready_wait_seconds(len(fan_list)))

        self.sichuan_hu_results = {}
        hued = sum(1 for p in self.player_list if p.is_hu)
        if (not self.blood_battle) or hued >= 3:
            # 血战终局 / 流局：统一走 _settle_liuju（见该函数顶部 ABCD 顺序注释，禁止跳过查叫）
            if self.blood_battle and hued >= 3:
                await self._settle_liuju()
            self.ended_by = "win"
            self.game_status = "END"
        else:
            # 续打：从最后一个和家的下家开始摸牌
            self.current_player_index = winners[-1]
            self.game_status = "deal_card"

    # ============ 终局结算（流局 / 血战三家和 共用，见 _settle_liuju 顶部 ABCD 顺序注释） ============

    def _player_has_dingque_tile(self, hand_tiles: List[int], combination_tiles: List[str], dingque_suit: int) -> bool:
        if dingque_suit not in (1, 2, 3):
            return False
        if any((t // 10) == dingque_suit for t in hand_tiles):
            return True
        for c in combination_tiles:
            if len(c) >= 3 and c[1:].isdigit() and int(c[1:]) // 10 == dingque_suit:
                return True
        return False

    def _original_discards(self, player) -> List[int]:
        """玩家本局实际打出的牌（河牌现存 + 被鸣牌取走），不含副露。"""
        return list(player.discard_tiles) + list(player.discard_origin_tiles)

    def _classify_hua_zhu(
        self,
        hand_tiles: List[int],
        combination_tiles: List[str],
        dingque_suit: int,
        original_discards: List[int],
    ) -> Optional[str]:
        """None=非花猪；passive=被动花猪；active=主动花猪。"""
        if dingque_suit not in (1, 2, 3):
            return None
        if not self._player_has_dingque_tile(hand_tiles, combination_tiles, dingque_suit):
            return None
        if not original_discards:
            return None
        if all((t // 10) == dingque_suit for t in original_discards):
            return "passive"
        if any((t // 10) != dingque_suit for t in original_discards):
            return "active"
        return None

    def _hand_without_dingque_suit(self, hand_tiles: List[int], dingque_suit: int) -> List[int]:
        if dingque_suit not in (1, 2, 3):
            return list(hand_tiles)
        return [t for t in hand_tiles if (t // 10) != dingque_suit]

    def _evaluate_liuju_ting(
        self,
        hand_tiles: List[int],
        combination_tiles: List[str],
        dingque_suit: int,
        original_discards: List[int],
    ) -> Tuple[bool, set, int, Optional[str]]:
        """流局听牌判定。返回 (是否听牌, 听牌张集合, 理论最大番, 花猪类型)。

        花猪（passive/active）均按没叫，不再判听；active 查叫阶段另扣 24 分。
        手牌为 14/11/8…（刚摸牌未打出）时，需尝试打掉一张后再判听，避免误判为没叫。
        """
        hua_type = self._classify_hua_zhu(
            hand_tiles, combination_tiles, dingque_suit, original_discards,
        )
        if hua_type in ("passive", "active"):
            return False, set(), 0, hua_type

        eval_hand = list(hand_tiles)
        if self._player_has_dingque_tile(hand_tiles, combination_tiles, dingque_suit):
            return False, set(), 0, None

        hand = list(eval_hand)
        best_waits: set = set()
        best_max_fan = 0

        def _eval_one(test_hand: List[int]):
            nonlocal best_waits, best_max_fan
            waits = self.calculation_service.Sichuan_tingpai_check(test_hand, combination_tiles)
            waits = {w for w in waits if (w // 10) != dingque_suit}
            if not waits:
                return
            mf, mf_names = self.calculation_service.Sichuan_max_fan_for_chajiao(
                test_hand, combination_tiles, dingque_suit,
            )
            best_waits |= waits
            if mf > best_max_fan:
                best_max_fan = mf

        if len(hand) % 3 == 2:
            for tile in set(hand):
                test = list(hand)
                test.remove(tile)
                _eval_one(test)
        else:
            _eval_one(hand)

        return bool(best_waits), best_waits, best_max_fan, hua_type

    def _max_fan_for_hu_player_hand(self, player: SichuanPlayer) -> Tuple[int, List[str]]:
        """已和玩家手牌：遍历所有可能和牌张，取理论最大番（查牌付分用）。"""
        hand = list(player.hand_tiles)
        best_fan, best_names = 0, []
        for w in set(hand):
            fan, names = self.calculation_service.Sichuan_hepai_check(
                hand, player.combination_tiles, [], w, player.dingque_suit,
            )
            if fan > best_fan:
                best_fan, best_names = fan, list(names) if names else []
        return best_fan, best_names

    def _max_fan_for_no_ting_payer_hand(
        self, payer: SichuanPlayer, *, strip_dingque: bool = False,
    ) -> Tuple[int, List[str]]:
        """没叫/花猪向已和玩家付分：按付分者手牌遍历所有和牌可能取理论最大番。"""
        hand = list(payer.hand_tiles)
        if strip_dingque:
            hand = self._hand_without_dingque_suit(hand, payer.dingque_suit)
        combo = payer.combination_tiles
        dingque = payer.dingque_suit
        best_fan, best_names = 0, []

        def _try_hepai(test_hand: List[int], win_tile: int):
            nonlocal best_fan, best_names
            fan, names = self.calculation_service.Sichuan_hepai_check(
                test_hand, combo, [], win_tile, dingque,
            )
            if fan > best_fan:
                best_fan, best_names = fan, list(names) if names else []

        if len(hand) % 3 == 2:
            for win_tile in set(hand):
                _try_hepai(hand, win_tile)
            for discard in set(hand):
                test = list(hand)
                test.remove(discard)
                mf, mf_names = self.calculation_service.Sichuan_max_fan_for_chajiao(
                    test, combo, dingque,
                )
                if mf > best_fan:
                    best_fan, best_names = mf, list(mf_names) if mf_names else []
        else:
            mf, mf_names = self.calculation_service.Sichuan_max_fan_for_chajiao(hand, combo, dingque)
            if mf > best_fan:
                best_fan, best_names = mf, list(mf_names) if mf_names else []
        return best_fan, best_names

    def _reveal_hand_payload(self, player) -> List[int]:
        """终局亮牌：和牌者将和牌张置于末位，便于客户端倒牌展示。"""
        tiles = list(player.hand_tiles)
        if not getattr(player, "is_hu", False):
            return tiles
        hepai_tile = None
        for rec in self.deferred_hu_settlements:
            if rec["winner"] == player.player_index:
                hepai_tile = rec.get("hepai_tile")
                break
        if hepai_tile and hepai_tile in tiles:
            tiles.remove(hepai_tile)
            tiles.sort()
            tiles.append(hepai_tile)
        elif hepai_tile and hepai_tile not in tiles:
            tiles.append(hepai_tile)
        return tiles

    # ┌─────────────────────────────────────────────────────────────────────────┐
    # │ 终局查牌演出顺序（永不跳过查叫；勿再添加 skip/early-return 短路查叫）    │
    # │                                                                         │
    # │ 核心规则：只要本盘结束，就必须按下述顺序看完所有人的手牌。              │
    # │ 检查顺序一律按本局 player_index 从 0 递增；先看“和牌玩家”，再看“流局     │
    # │ （未和）玩家”。每家手牌只展示一次面板。                                  │
    # │                                                                         │
    # │ 以四家 A/B/C/D 为例（player_index: A=0,B=1,C=2,D=3；A/B/C 和，D 没叫）： │
    # │   ① reveal_hu    — 四家同时亮完整手牌（3D，无计分面板）                  │
    # │   ② settle_hu×N  — 先看和牌玩家：按 player_index 升序逐笔入账并播面板：   │
    # │        · A(0) 的和牌结算 → B(1) → C(2)                                   │
    # │      中途和牌阶段 defer 不计分，仅在此处逐笔结算；非末步 3s，末步见下。   │
    # │      （注：付分对象由各自 hu_order 决定，与展示顺序无关，分数不受影响）   │
    # │   ③ chajiao×M  — 再看流局玩家：按 player_index 升序逐家展示手牌+状态：    │
    # │        · 每家仅 1 次面板，合并该家全部查叫收支（禁止同一家连播多次）       │
    # │        · D(3) 没叫 → 向 A/B/C 各付理论最大番（合并显示在一个面板里）       │
    # │        · 有叫/没叫/花猪 均须展示，即使分数变动为 0 也不可省略             │
    # │        · 没叫/花猪开杠者本副“刮风下雨”退税并入本家面板：标“退税”、多 0.5s │
    # │      非末步 3s；仅最后一家显示 8s 可点确定。                              │
    # │   ④ waiting_ready — 末步 8s 确认后进入下一局                              │
    # │                                                                         │
    # │ liuju_step 协议：reveal_hu → settle_hu → chajiao（退税并入此步，无独立步）│
    # │ 客户端 RoundEndPresentation.SichuanQueue 与服务端 round_end_timing 对齐。 │
    # └─────────────────────────────────────────────────────────────────────────┘

    async def _settle_liuju(self):
        non_hu = [p for p in self.player_list if not p.is_hu]
        # 血战终局：即使四家均已和（历史一炮多响边界），仍须执行 reveal/settle_hu
        needs_blood_endgame = self.blood_battle and (
            self.deferred_hu_settlements or any(p.is_hu for p in self.player_list)
        )
        if not non_hu and not needs_blood_endgame:
            return
        self._liuju_final_panel_shown = False
        self._liuju_endgame_started = False
        status: Dict[int, str] = {}
        display_status: Dict[int, str] = {}
        tenpai_max_fan: Dict[int, int] = {}
        tenpai_max_fan_names: Dict[int, List[str]] = {}
        tenpai_tiles_map: Dict[int, List[int]] = {}
        for p in non_hu:
            original_discards = self._original_discards(p)
            is_ting, waits, max_fan, hua_type = self._evaluate_liuju_ting(
                p.hand_tiles, p.combination_tiles, p.dingque_suit, original_discards,
            )
            if hua_type == "passive":
                status[p.player_index] = "no_ting"
                display_status[p.player_index] = "hua_zhu_passive"
                continue
            if hua_type == "active":
                status[p.player_index] = "no_ting"
                display_status[p.player_index] = "hua_zhu_active"
                continue
            if is_ting:
                status[p.player_index] = "ting"
                display_status[p.player_index] = "ting"
                tenpai_max_fan[p.player_index] = max_fan
                tenpai_tiles_map[p.player_index] = sorted(waits)
                _, mf_names = self.calculation_service.Sichuan_max_fan_for_chajiao(
                    self._hand_for_chajiao_eval(p.hand_tiles, p.combination_tiles, p.dingque_suit),
                    p.combination_tiles,
                    p.dingque_suit,
                )
                tenpai_max_fan_names[p.player_index] = mf_names or []
            else:
                status[p.player_index] = "no_ting"
                display_status[p.player_index] = "no_ting"

        ting_players = [idx for idx, s in status.items() if s == "ting"]
        noting_players = [idx for idx, s in status.items() if s == "no_ting"]

        if not self.blood_battle:
            player_action_record_liuju(self)

        hu_players = [p for p in self.player_list if p.is_hu]
        all_hands = {p.player_index: self._reveal_hand_payload(p) for p in self.player_list}
        player_scores = {p.player_index: p.score for p in self.player_list}

        # 1) 查牌：四家一起亮手牌（不计分）
        if self.blood_battle:
            self._liuju_endgame_started = True
            player_action_record_sichuan_liuju_step(
                self, "reveal_hu", json.dumps({str(k): v for k, v in all_hands.items()}),
            )
        await broadcast_result(
            self, hu_class="liuju",
            liuju_step="reveal_hu",
            liuju_hu_hands=all_hands,
            player_to_score=player_scores,
            round_continues=False,
        )
        await asyncio.sleep(ROUND_END_HAND_REVEAL_SEC)

        # 2) 先看和牌玩家：按 player_index 升序逐笔结算并播面板（血战：终局才入账）
        #    展示顺序按座位号；付分对象由各自 hu_order 决定（见 _apply_hu_score_changes），与顺序无关。
        hu_players_sorted = sorted(hu_players, key=lambda p: p.player_index)
        if self.blood_battle and self.deferred_hu_settlements:
            deferred_sorted = sorted(self.deferred_hu_settlements, key=lambda x: x["winner"])
            for settle_idx, rec in enumerate(deferred_sorted):
                w = rec["winner"]
                p = self.player_list[w]
                changes = self._apply_hu_score_changes(
                    w, rec["base"], rec["is_zimo"], rec.get("discarder"),
                )
                player_scores = {p.player_index: p.score for p in self.player_list}
                is_last_settle = settle_idx == len(deferred_sorted) - 1
                # settle_hu 仅在「无未和家」时为末步；否则查叫才是 8s 末步（退税已并入查叫）
                is_final_panel = is_last_settle and not non_hu
                if is_final_panel:
                    self._liuju_final_panel_shown = True
                if self.blood_battle:
                    player_action_record_sichuan_liuju_step(
                        self, "settle_hu", rec["hu_class"], w, rec["base"], rec["fan_list"],
                        [changes.get(i, 0) for i in range(4)],
                    )
                await broadcast_result(
                    self,
                    hu_class=rec["hu_class"],
                    liuju_step="settle_hu",
                    hepai_player_index=w,
                    win_player_index=w,
                    is_zimo=rec["is_zimo"],
                    hu_score=rec["base"],
                    hu_fan=rec["fan_list"],
                    hepai_tile=rec.get("hepai_tile"),
                    multi_ron=rec.get("multi_ron"),
                    hepai_player_hand=self._reveal_hand_payload(p),
                    hepai_player_combination_mask=p.combination_mask,
                    suppress_hand_reveal=True,
                    score_changes=changes,
                    player_to_score=player_scores,
                    liuju_status_final=is_final_panel,
                    round_continues=False,
                )
                if hasattr(self, "spectator_manager"):
                    self.spectator_manager.record_tick(
                        [rec["hu_class"], w, rec["base"], rec["fan_list"], [changes.get(i, 0) for i in range(4)]]
                    )
                await asyncio.sleep(
                    sichuan_settle_hu_panel_wait_seconds(len(rec["fan_list"]), is_final=is_final_panel)
                )

        # 3) 再看流局玩家：按 player_index 升序逐家展示，每家仅 1 次面板（合并该家全部查叫收支）
        # 禁止 skip/early-return：即使全员同状态或分数变动为 0，也必须播完所有未和家面板。
        # 没叫/花猪开杠者本副“刮风下雨”退税并入本家面板（不再单独 cha_refund 步）。
        non_hu_ordered = sorted(non_hu, key=lambda p: p.player_index)

        for chajiao_idx, n_p in enumerate(non_hu_ordered):
            panel_changes: Dict[int, int] = {i: 0 for i in range(4)}
            p_idx = n_p.player_index
            p_display = display_status.get(p_idx, "no_ting")

            if status.get(p_idx) == "no_ting" and hu_players_sorted:
                strip_dingque = p_display in ("hua_zhu_passive", "hua_zhu_active")
                mf, mf_names = self._max_fan_for_no_ting_payer_hand(
                    n_p, strip_dingque=strip_dingque,
                )
                base = self.calculation_service.Sichuan_base_from_fan(mf, mf_names)
                if base > 0:
                    for h_p in hu_players_sorted:
                        self.player_list[p_idx].score -= base
                        panel_changes[p_idx] -= base
                        self.player_list[h_p.player_index].score += base
                        panel_changes[h_p.player_index] += base

            if p_display == "hua_zhu_active":
                self.player_list[p_idx].score -= SICHUAN_ACTIVE_HUAZHU_PENALTY
                panel_changes[p_idx] -= SICHUAN_ACTIVE_HUAZHU_PENALTY

            if status.get(p_idx) == "ting" and noting_players:
                mf = tenpai_max_fan.get(p_idx, 0)
                mf_names = tenpai_max_fan_names.get(p_idx, [])
                base = self.calculation_service.Sichuan_base_from_fan(mf, mf_names)
                if base > 0:
                    for n_idx in noting_players:
                        self.player_list[n_idx].score -= base
                        panel_changes[n_idx] -= base
                        self.player_list[p_idx].score += base
                        panel_changes[p_idx] += base

            # 没叫/花猪开杠者：本副刮风下雨退税并入本家查叫面板（标“退税”、面板多 0.5s）
            panel_has_refund = False
            if status.get(p_idx) == "no_ting":
                for record in list(n_p.gang_score_records):
                    rc = self._refund_gang_record(record)
                    for k, v in rc.items():
                        panel_changes[k] = panel_changes.get(k, 0) + v
                    panel_has_refund = True

            player_scores = {p.player_index: p.score for p in self.player_list}
            is_final_panel = chajiao_idx == len(non_hu_ordered) - 1
            if is_final_panel:
                self._liuju_final_panel_shown = True
            liuju_status = {p_idx: p_display}
            liuju_hands = {p_idx: list(n_p.hand_tiles)}
            if self.blood_battle:
                player_action_record_sichuan_liuju_step(
                    self, "chajiao",
                    p_idx,
                    p_display,
                    json.dumps(liuju_hands[p_idx]),
                    [panel_changes.get(i, 0) for i in range(4)],
                    1 if is_final_panel else 0,
                )
            await broadcast_result(
                self, hu_class="liuju",
                liuju_step="chajiao",
                hepai_player_index=p_idx,
                hepai_player_combination_mask=n_p.combination_mask,
                liuju_status=liuju_status,
                liuju_hands=liuju_hands,
                score_changes=panel_changes,
                player_to_score=player_scores,
                liuju_status_final=is_final_panel,
                liuju_refund=True if panel_has_refund else None,
                round_continues=False,
            )
            if hasattr(self, "spectator_manager"):
                self.spectator_manager.record_tick(["chajiao"])
            if not is_final_panel:
                await asyncio.sleep(
                    sichuan_chajiao_panel_wait_seconds(is_final=False, has_refund=panel_has_refund)
                )

    def _hand_for_chajiao_eval(
        self, hand_tiles: List[int], combination_tiles: List[str], dingque_suit: int,
    ) -> List[int]:
        """14 张手牌流局时，枚举打掉一张后取理论最大番对应的 13 张（与查大叫规则一致）。"""
        hand = list(hand_tiles)
        if len(hand) % 3 != 2:
            return hand
        best_hand = hand[:]
        if best_hand:
            best_hand = list(best_hand)
            best_hand.remove(best_hand[0])
        best_mf = -1
        for tile in set(hand):
            test = list(hand)
            test.remove(tile)
            mf, _ = self.calculation_service.Sichuan_max_fan_for_chajiao(
                test, combination_tiles, dingque_suit,
            )
            if mf > best_mf:
                best_mf = mf
                best_hand = test
        return best_hand

    def _decide_next_dealer(self) -> int:
        if self.first_win_event:
            winners = self.first_win_event["winners"]
            if len(winners) > 1 and self.first_win_event.get("discarder") is not None:
                return self.first_win_event["discarder"]
            return winners[0]
        return self.dealer_index  # 流局连庄

    # ============ 准备阶段 ============

    async def _ready_phase(self, liuju: bool):
        if getattr(self, "_liuju_final_panel_shown", False):
            await run_sichuan_liuju_final_ready_phase(self, broadcast_ready_status)
            return
        if liuju:
            await self._ready_phase_immediate()
            return
        wait_time = shuhewei_ready_wait_seconds(0.0, True)
        ready_deadline = time.time() + wait_time
        self.action_dict = {}
        for player in self.player_list:
            if player.user_id <= 10:
                self.action_dict[player.player_index] = []
            else:
                self.action_dict[player.player_index] = ["ready"]
                player.remaining_time = math.ceil(wait_time)
        self.game_status = "waiting_ready"
        await broadcast_ready_status(self)
        while any(self.action_dict[i] for i in self.action_dict):
            for p in self.player_list:
                if self.action_dict.get(p.player_index):
                    p.remaining_time = max(0, int(ready_deadline - time.time()))
            if await self.wait_action() is False:
                break

    async def _ready_phase_immediate(self):
        """无流局结算面板时：广播准备并等待全员确认（无额外倒计时）。"""
        self.action_dict = {}
        for player in self.player_list:
            if player.user_id <= 10:
                self.action_dict[player.player_index] = []
            else:
                self.action_dict[player.player_index] = ["ready"]
                player.remaining_time = 0
        self.game_status = "waiting_ready"
        await broadcast_ready_status(self)
        while any(self.action_dict[i] for i in self.action_dict):
            if await self.wait_action() is False:
                break


# 挂载方法
SichuanGameState.wait_action = wait_action
SichuanGameState.broadcast_game_start = broadcast_game_start
SichuanGameState.broadcast_ask_hand_action = broadcast_ask_hand_action
SichuanGameState.broadcast_ask_other_action = broadcast_ask_other_action
SichuanGameState.broadcast_do_action = broadcast_do_action
SichuanGameState.broadcast_game_end = broadcast_game_end
SichuanGameState.broadcast_refresh_player_tag_list = broadcast_refresh_player_tag_list
SichuanGameState.reconnected_send_pending_ask = reconnected_send_pending_ask
