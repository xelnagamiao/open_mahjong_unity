import random
import asyncio
from typing import Any, Dict, List, Optional
import time
import logging
import math
from .action_check import check_action_after_cut, check_action_jiagang, check_action_buhua, check_action_hand_action, refresh_waiting_tiles, check_kokushi, check_jiuzhongjiupai
from .wait_action import wait_action
from .boardcast import (
    broadcast_game_start,
    broadcast_ask_hand_action,
    broadcast_ask_other_action,
    broadcast_do_action,
    broadcast_result,
    broadcast_game_end,
    broadcast_switch_seat,
    broadcast_refresh_player_tag_list,
    broadcast_ready_status,
    broadcast_shuhewei,
    reconnected_send_pending_ask,
)
from ..public.logic_common import get_index_relative_position, next_current_index, next_current_num, assign_strict_final_ranks
from .init_tiles import init_classical_tiles
from ..public.next_game_round import next_game_round_random_switchseat
from ..public.spectator_rules import too_many_ai_for_spectator
from ..public.game_record_manager import init_game_record, init_game_round, player_action_record_deal, player_action_record_angang, player_action_record_jiagang, player_action_record_chipenggang, player_action_record_hu, player_action_record_liuju, player_action_record_jiuzhongjiupai, player_action_record_shuhewei, player_action_record_round_end, end_game_record, build_score_changes_by_seat, build_score_changes_dict, capture_player_entry_order
from ..public.round_end_timing import liuju_ready_wait_seconds, shuhewei_ready_wait_seconds
from ...game_calculation.game_calculation_service import GameCalculationService
from ...database.db_manager import DatabaseManager
from ..public.random_seed_manager import setup_random_seed_system
from ...database.fulu_utils import record_fulu_rounds_for_players

logger = logging.getLogger(__name__)


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


class ClassicalPlayer:
    def __init__(self, user_id: int, username: str, tiles: list, remaining_time: int):
        self.user_id = user_id
        self.username = username
        self.is_bot = True if user_id <= 10 else False
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

        self.waiting_tiles = set[int]()
        self.record_counter = RecordCounter()
        self.score_history = []
        self.round_number_history = []

        self.title_used = 0
        self.profile_used = 0
        self.character_used = 0
        self.voice_used = 0
        self.has_draw_slot = False

    def get_tile(self, tiles_list, *, mark_draw_slot: bool = True):
        element = tiles_list.pop(0)
        self.hand_tiles.append(element)
        if mark_draw_slot:
            self.has_draw_slot = True

    def get_gang_tile(self, tiles_list, gamestate):
        if len(tiles_list) <= 1 or gamestate.backward_tiles_list_type == "single":
            element = tiles_list.pop(-1)
        else:
            element = tiles_list.pop(-2)
        self.hand_tiles.append(element)
        self.has_draw_slot = True
        gamestate.backward_tiles_list_type = "single" if gamestate.backward_tiles_list_type == "double" else "double"


class ClassicalGameState:
    def __init__(self, game_server, room_data: dict, calculation_service: GameCalculationService, db_manager: DatabaseManager, gamestate_id: str):
        self.game_server = game_server
        self.calculation_service = calculation_service
        self.db_manager = db_manager
        self.gamestate_id = gamestate_id
        self.game_record = {}
        self.game_task: Optional[asyncio.Task] = None
        self.player_list: List[ClassicalPlayer] = []
        player_settings = room_data.get("player_settings", {})
        for user_id in room_data["player_list"]:
            player_setting = player_settings.get(user_id, {})
            if user_id == 0:
                username = "麻雀罗伯特"
            elif user_id == 2:
                username = "牌效罗伯特"
            else:
                username = player_setting.get("username", f"用户{user_id}")
            player = ClassicalPlayer(user_id, username, [], room_data["round_timer"])
            player.title_used = player_setting.get("title_id", 1)
            player.profile_used = player_setting.get("profile_image_id", 1)
            player.character_used = player_setting.get("character_id", 1)
            player.voice_used = player_setting.get("voice_id", 1)
            self.player_list.append(player)

        self.room_id = room_data["room_id"]
        self.tips = room_data["tips"]
        self.max_round = room_data["game_round"]
        self.step_time = room_data["step_timer"]
        self.round_time = room_data["round_timer"]
        self.room_rule = room_data["room_rule"]
        self.room_type = room_data["room_type"]
        self.sub_rule = room_data.get("sub_rule")

        self.room_random_seed = room_data.get("random_seed", 0)
        self.open_cuohe = room_data.get("open_cuohe", False)
        self.show_moqie_hint = room_data.get("show_moqie_hint", False)
        self.hepai_limit = 1
        self.tourist_limit = room_data.get("tourist_limit", False)
        self.allow_spectator_config = room_data.get("allow_spectator", True)

        self.isPlayerSetRandomSeed = False

        self.tiles_list = []
        self.current_player_index = 0
        self.xunmu = 1
        self.master_seed = 0
        self.commitment = 0
        self.salt = ""
        self.round_random_seed = 0
        self.game_status = "waiting"
        self.server_action_tick = 0
        self.player_action_tick = 0
        self.current_round = 1
        self.round_index = 1
        self.result_dict = {}
        self.hu_class = None
        self.jiagang_tile = None
        self.temp_fan = []

        self.action_events: Dict[int, asyncio.Event] = {0: asyncio.Event(), 1: asyncio.Event(), 2: asyncio.Event(), 3: asyncio.Event()}
        self.action_queues: Dict[int, asyncio.Queue] = {0: asyncio.Queue(), 1: asyncio.Queue(), 2: asyncio.Queue(), 3: asyncio.Queue()}
        self.waiting_players_list = []

        self.action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
        self.action_priority: Dict[str, int] = {
            "hu_self": 6, "hu_first": 5, "hu_second": 4, "hu_third": 3,
            "peng": 2, "gang": 2,
            "chi_left": 1, "chi_mid": 1, "chi_right": 1,
            "jiuzhongjiupai": 6,
            "ready": 0,
            "pass": 0, "buhua": 0, "cut": 0, "angang": 0, "jiagang": 0, "deal_tile": 0, "deal_gang_tile": 0, "deal_buhua_tile": 0
        }

        self.backward_tiles_list_type = "double"
        self.dead_wall_count = 14

        self.Debug = False

        self.spectator_enabled = self.allow_spectator_config and not too_many_ai_for_spectator(self.player_list)
        from .spectator_manager import SpectatorManager
        self.spectator_manager = SpectatorManager(self, delay=180.0, enabled=self.spectator_enabled)
        # 实时观战者（由 FriendManager 维护，结构: List[RealtimeSpectator]）
        self.realtime_spectators = []

    async def send_to_realtime_spectators(self, player_index: int, response):
        from ..public.spectator_rules import deliver_realtime_spectator_message
        await deliver_realtime_spectator_message(self, player_index, response)

    async def player_disconnect(self, user_id: int):
        for p in self.player_list:
            if p.user_id == user_id:
                if "offline" not in p.tag_list:
                    p.tag_list.append("offline")
                    await broadcast_refresh_player_tag_list(self)
                break

        non_ai_players = [p for p in self.player_list if p.user_id >= 10]
        if non_ai_players:
            all_offline = all("offline" in p.tag_list for p in non_ai_players)
            if all_offline:
                logger.info(f"所有非AI玩家都已掉线，开始清理gamestate，room_id: {self.room_id}, gamestate_id: {self.gamestate_id}")
                await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)

    async def player_reconnect(self, user_id: int):
        for p in self.player_list:
            if p.user_id == user_id:
                if "offline" in p.tag_list:
                    p.tag_list.remove("offline")
                    await broadcast_refresh_player_tag_list(self)

                if user_id in self.game_server.user_id_to_connection:
                    from ...response import Response, GameInfo
                    player_conn = self.game_server.user_id_to_connection[user_id]

                    base_game_info = {
                        'room_id': self.room_id,
                        'gamestate_id': self.gamestate_id,
                        'tips': self.tips,
                        'current_player_index': self.current_player_index,
                        "action_tick": self.server_action_tick,
                        'max_round': self.max_round,
                        'tile_count': max(0, len(self.tiles_list) - self.dead_wall_count),
                        'commitment': self.commitment,
                        'salt': self.salt,
                        'current_round': self.current_round,
                        'step_time': self.step_time,
                        'round_time': self.round_time,
                        'room_type': self.room_type,
                        'room_rule': self.room_rule,
                        'sub_rule': self.sub_rule,
                        'hepai_limit': self.hepai_limit,
                        'open_cuohe': self.open_cuohe,
                        'show_moqie_hint': self.show_moqie_hint,
                        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed,
                        'players_info': []
                    }
                    from ..public.game_record_manager import build_player_entry_order_fields
                    base_game_info.update(build_player_entry_order_fields(self))

                    for player in self.player_list:
                        player_info = {
                            'user_id': player.user_id,
                            'username': player.username,
                            'hand_tiles_count': len(player.hand_tiles),
                            'hand_tiles': player.hand_tiles if player.user_id == user_id else None,
                            'discard_tiles': player.discard_tiles,
                            'discard_origin_tiles': player.discard_origin_tiles,
                            'combination_tiles': player.combination_tiles,
                            "combination_mask": player.combination_mask,
                            "huapai_list": player.huapai_list,
                            'remaining_time': player.remaining_time,
                            'player_index': player.player_index,
                            'original_player_index': player.original_player_index,
                            'score': player.score,
                            "title_used": player.title_used,
                            'profile_used': player.profile_used,
                            'character_used': player.character_used,
                            'voice_used': player.voice_used,
                            'score_history': player.score_history,
                            'round_number_history': player.round_number_history,
                            'tag_list': player.tag_list,
                        }
                        base_game_info['players_info'].append(player_info)

                    game_info = GameInfo(
                        **base_game_info,
                        self_hand_tiles=None
                    )

                    response = Response(
                        type="gamestate/classical/game_start",
                        success=True,
                        message="重连成功，游戏继续",
                        game_info=game_info
                    )

                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    logger.info(f"已向重连玩家 {p.username} 发送游戏状态信息")
                    await reconnected_send_pending_ask(self, user_id)
                break

    async def cleanup_game_state(self):
        await self.spectator_manager.cleanup()
        if self.game_task and not self.game_task.done():
            self.game_task.cancel()
            try:
                await self.game_task
            except asyncio.CancelledError:
                logger.info(f"已取消游戏循环任务，room_id: {self.room_id}")
            except Exception as e:
                logger.error(f"取消游戏循环任务时出错，room_id: {self.room_id}, 错误: {e}")

    async def run_game_loop(self):
        try:
            await self.game_loop_classical()
        except asyncio.CancelledError:
            logger.info(f"游戏循环被取消，room_id: {self.room_id}, gamestate_id: {self.gamestate_id}")
            raise
        except Exception as e:
            logger.error(
                f"游戏循环发生未捕获异常，room_id: {self.room_id}, gamestate_id: {self.gamestate_id}, 错误: {e}",
                exc_info=True
            )
            try:
                await self.cleanup_game_state()
            except Exception as cleanup_err:
                logger.error(
                    f"清理游戏状态时出错，room_id: {self.room_id}, gamestate_id: {self.gamestate_id}, 错误: {cleanup_err}",
                    exc_info=True
                )

    async def game_loop_classical(self):

        if not self.Debug:
            user_seed = self.room_random_seed if self.room_random_seed else None
            self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(user_seed)
            capture_player_entry_order(self)
            rng = random.Random(self.master_seed)
            rng.shuffle(self.player_list)
            for index, player in enumerate[ClassicalPlayer](self.player_list):
                player.player_index = index
                player.original_player_index = index
        else:
            self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system()
            capture_player_entry_order(self)
            for index, player in enumerate[ClassicalPlayer](self.player_list):
                player.player_index = index
                player.original_player_index = index

        init_game_record(self)
        self.game_record["game_title"]["sub_rule"] = self.sub_rule
        self.game_record["game_title"]["hepai_limit"] = self.hepai_limit

        while self.current_round <= self.max_round * 4:

            init_classical_tiles(self)
            self.backward_tiles_list_type = "double"

            await self.broadcast_game_start()
            init_game_round(self)

            self.game_status = "waiting_hand_action"
            self.current_player_index = 0
            self.dihe_possible = True

            # ===== 开局预检测轮：按玩家0-3顺序检查国士无双和九老峰回 =====
            pre_check_resolved = False
            for i in range(4):
                actions = []
                has_kokushi = check_kokushi(self.player_list[i].hand_tiles)
                has_jiuzhong = check_jiuzhongjiupai(self.player_list[i].hand_tiles)
                if has_kokushi:
                    logger.info(f"玩家{i}符合国士无双条件，手牌: {self.player_list[i].hand_tiles}")
                    self.result_dict["hu_self"] = (0, 300, [], ["国士无双"])
                    actions.append("hu_self")
                if has_jiuzhong:
                    logger.info(f"玩家{i}符合九老峰回条件，手牌: {self.player_list[i].hand_tiles}")
                    actions.append("jiuzhongjiupai")
                if not actions:
                    continue
                actions.append("pass")
                self.action_dict = {0: [], 1: [], 2: [], 3: []}
                self.action_dict[i] = actions
                self.current_player_index = i
                await self.broadcast_ask_hand_action()
                await self.wait_action()
                if self.game_status == "END":
                    pre_check_resolved = True
                    break
                if self.hu_class == "jiuzhongjiupai":
                    pre_check_resolved = True
                    break
                self.result_dict.pop("hu_self", None)

            if pre_check_resolved:
                pass
            else:
                # 正常流程：庄家摸第14张牌
                self.current_player_index = 0
                self.refresh_waiting_tiles(self.current_player_index)
                self.player_list[0].get_tile(self.tiles_list)
                player_action_record_deal(self, deal_tile=self.player_list[0].hand_tiles[-1], deal_type="d")
                await self.broadcast_do_action(
                    action_list=["deal_tile"],
                    action_player=0,
                    deal_tile=self.player_list[0].hand_tiles[-1],
                )
                logger.info(f"第一位行动玩家{self.current_player_index}的手牌等待牌为{self.player_list[self.current_player_index].waiting_tiles}")
                self.action_dict = check_action_hand_action(self, self.current_player_index, is_first_action=True)
                await self.broadcast_ask_hand_action()
                await self.wait_action()

                # 游戏主循环
                while self.game_status != "END":
                    match self.game_status:

                        case "deal_card":
                            if len(self.tiles_list) <= self.dead_wall_count:
                                self.game_status = "END"
                                break
                            self.next_current_index()
                            self.refresh_waiting_tiles(self.current_player_index)
                            self.player_list[self.current_player_index].get_tile(self.tiles_list)
                            player_action_record_deal(self, deal_tile=self.player_list[self.current_player_index].hand_tiles[-1], deal_type="d")
                            await self.broadcast_do_action(
                                action_list=["deal_tile"],
                                action_player=self.current_player_index,
                                deal_tile=self.player_list[self.current_player_index].hand_tiles[-1],
                            )
                            self.action_dict = check_action_hand_action(self, self.current_player_index)
                            self.game_status = "waiting_hand_action"

                        case "deal_card_after_gang":
                            self.dihe_possible = False
                            self.refresh_waiting_tiles(self.current_player_index)
                            self.player_list[self.current_player_index].get_gang_tile(self.tiles_list, self)
                            player_action_record_deal(self, deal_tile=self.player_list[self.current_player_index].hand_tiles[-1], deal_type="gd")
                            await self.broadcast_do_action(
                                action_list=["deal_gang_tile"],
                                action_player=self.current_player_index,
                                deal_tile=self.player_list[self.current_player_index].hand_tiles[-1],
                            )
                            self.action_dict = check_action_hand_action(self, self.current_player_index, is_get_gang_tile=True)
                            self.game_status = "waiting_hand_action"

                        case "waiting_hand_action":
                            await self.broadcast_ask_hand_action()
                            await self.wait_action()

                        case "waiting_action_after_cut":
                            await self.broadcast_ask_other_action()
                            await self.wait_action()

                        case "waiting_action_qianggang":
                            await self.broadcast_ask_other_action()
                            await self.wait_action()

                        case "onlycut_after_action":
                            print("onlycut_after_action")
                            self.action_dict = {0: [], 1: [], 2: [], 3: []}
                            self.action_dict[self.current_player_index].append("cut")
                            self.game_status = "waiting_hand_action"

                        case _:
                            logger.error(f"没有匹配到游戏状态: {self.game_status}")

            # ===== 结算 =====
            hu_score = None
            hu_fan = None
            hu_fu_fan_list = None
            hu_base_fu = None
            hepai_player_index = None

            scores_before = {player.original_player_index: player.score for player in self.player_list}

            if self.hu_class in ["hu_self", "hu_first", "hu_second", "hu_third"]:
                # 自摸
                if self.hu_class == "hu_self":
                    base_fu, total_fu, fu_fan_list, fan_list = self.result_dict["hu_self"]
                    hu_score = total_fu
                    hu_fan = fan_list
                    hu_fu_fan_list = fu_fan_list
                    hu_base_fu = base_fu
                    hepai_player_index = self.current_player_index
                    # 庄家收支x2：庄家自摸 → 三家各付 2*total_fu；闲家自摸 → 庄家付 2*total_fu，其余两家各付 total_fu
                    actual_hu_score = total_fu * 6 if hepai_player_index == 0 else total_fu * 4
                    self.result_dict = {}

                    self.player_list[hepai_player_index].record_counter.zimo_times += 1
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan)
                    self.player_list[hepai_player_index].record_counter.win_score += hu_score
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu

                # 荣和
                else:
                    if self.hu_class == "hu_first":
                        base_fu, total_fu, fu_fan_list, fan_list = self.result_dict["hu_first"]
                        hepai_player_index = next_current_num(self.current_player_index)
                    elif self.hu_class == "hu_second":
                        base_fu, total_fu, fu_fan_list, fan_list = self.result_dict["hu_second"]
                        hepai_player_index = next_current_num(self.current_player_index)
                        hepai_player_index = next_current_num(hepai_player_index)
                    else:
                        base_fu, total_fu, fu_fan_list, fan_list = self.result_dict["hu_third"]
                        hepai_player_index = next_current_num(self.current_player_index)
                        hepai_player_index = next_current_num(hepai_player_index)
                        hepai_player_index = next_current_num(hepai_player_index)

                    hu_score = total_fu
                    hu_fan = fan_list
                    hu_fu_fan_list = fu_fan_list
                    hu_base_fu = base_fu
                    # 庄家收支x2：庄家荣和 → 全部 2*total_fu * 3；闲家荣和 → 庄家付 2*total_fu，其余两家各付 total_fu
                    actual_hu_score = total_fu * 6 if hepai_player_index == 0 else total_fu * 4
                    self.result_dict = {}

                    self.player_list[hepai_player_index].record_counter.dianhe_times += 1
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan)
                    self.player_list[hepai_player_index].record_counter.win_score += hu_score
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu

                    self.player_list[self.current_player_index].record_counter.fangchong_times += 1
                    self.player_list[self.current_player_index].record_counter.fangchong_score += total_fu

            else:
                if self.hu_class != "jiuzhongjiupai":
                    self.hu_class = "liuju"
                liuju_score_changes = build_score_changes_dict(self.player_list, scores_before)
                await broadcast_result(self,
                                       hepai_player_index=None,
                                       player_to_score=None,
                                       hu_score=hu_score,
                                       hu_fan=None,
                                       hu_class=self.hu_class,
                                       hepai_player_hand=None,
                                       hepai_player_huapai=None,
                                       hepai_player_combination_mask=None,
                                       base_fu=None,
                                       fu_fan_list=None,
                                       score_changes=liuju_score_changes,
                                       )

            shuhewei_extra_wait = 0.0
            if self.hu_class != "jiuzhongjiupai":
                shuhewei_extra_wait = await self._settle_shuhewei(
                    hepai_player_index=hepai_player_index,
                    hepai_total_fu=hu_score,
                    hepai_fan=hu_fan,
                    hepai_fu_types=hu_fu_fan_list,
                    hu_class=self.hu_class,
                )

            record_fulu_rounds_for_players(self.player_list)

            for player in self.player_list:
                score_change = player.score - scores_before[player.original_player_index]
                if score_change > 0:
                    score_change_str = f"+{score_change:02d}"
                elif score_change < 0:
                    score_change_str = f"-{abs(score_change):02d}"
                else:
                    score_change_str = "0"
                player.score_history.append(score_change_str)
                player.round_number_history.append(self.current_round)

            score_changes = build_score_changes_by_seat(self.player_list, scores_before)

            if self.hu_class in ["hu_self", "hu_first", "hu_second", "hu_third"]:
                player_action_record_hu(self, hu_class=self.hu_class, hu_score=actual_hu_score,
                                        hu_fan=hu_fan, hepai_player_index=hepai_player_index,
                                        score_changes=score_changes,
                                        base_fu=hu_base_fu, fu_fan_list=hu_fu_fan_list)
            elif self.hu_class == "jiuzhongjiupai":
                player_action_record_jiuzhongjiupai(self)
            else:
                player_action_record_liuju(self)
            player_action_record_round_end(self)

            if self.hu_class == "jiuzhongjiupai":
                await asyncio.sleep(liuju_ready_wait_seconds())
            else:
                wait_time = shuhewei_ready_wait_seconds(
                    shuhewei_extra_wait,
                    self.hu_class in ["hu_self", "hu_first", "hu_second", "hu_third"],
                )
                ready_phase_deadline = time.time() + wait_time

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
                            p.remaining_time = max(0, int(ready_phase_deadline - time.time()))
                    if await wait_action(self) is False:
                        break

            is_dealer_win = (
                self.hu_class in ["hu_self", "hu_first", "hu_second", "hu_third"]
                and hepai_player_index == 0
            )
            next_game_round_random_switchseat(
                self,
                keep_current_round=is_dealer_win,
                keep_dealer_seat=is_dealer_win,
            )

            logger.info(f"重新开始下一局")

        logger.info("游戏结束")
        end_game_record(self)
        logger.info(f"最终游戏记录: {self.game_record}")

        assign_strict_final_ranks(self.player_list)

        await self.broadcast_game_end()

        if hasattr(self, 'spectator_manager'):
            await self.spectator_manager.send_final_record_and_close()

        match_type = f"{self.max_round}/4"
        game_id = self.db_manager.store_classical_game_record(
            self.game_record,
            self.player_list,
            self.room_type,
            match_type
        )

        has_ai_player = any(player.user_id <= 10 for player in self.player_list)
        if not has_ai_player and game_id:
            total_rounds = len(self.game_record.get("game_round", {}))
            self.db_manager.store_classical_game_stats(
                game_id,
                self.player_list,
                self.room_type,
                self.max_round,
                total_rounds
            )
            self.db_manager.store_classical_fan_stats(
                game_id,
                self.player_list,
                self.room_type,
                self.max_round
            )
        elif has_ai_player:
            logger.info(f'游戏记录包含AI玩家，跳过统计数据保存，game_id: {game_id}')

        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)

        if self.room_type == "match":
            await self.game_server.room_manager.destroy_room(self.room_id)
        else:
            await self.game_server.room_manager.finish_custom_game_room(self.room_id)
        logger.info(f"游戏实例已清理，room_id: {self.room_id},goodbye!")

    # ========== 数和尾结算 ==========

    _YAOJIU = {11, 19, 21, 29, 31, 39, 41, 42, 43, 44}
    _FANPAI = {45, 46, 47}
    _MELD_FU = {
        'k': {'normal': 2, 'yaojiu': 4, 'fanpai': 8},
        'K': {'normal': 4, 'yaojiu': 8, 'fanpai': 16},
        'g': {'normal': 8, 'yaojiu': 16, 'fanpai': 32},
        'G': {'normal': 16, 'yaojiu': 32, 'fanpai': 64},
    }
    _MELD_TAG = {
        'k': {'normal': "刻子", 'yaojiu': "幺九刻", 'fanpai': "番牌刻"},
        'K': {'normal': "暗刻", 'yaojiu': "幺九暗刻", 'fanpai': "番牌暗刻"},
        'g': {'normal': "明杠", 'yaojiu': "幺九明杠", 'fanpai': "番牌明杠"},
        'G': {'normal': "暗杠", 'yaojiu': "幺九暗杠", 'fanpai': "番牌暗杠"},
    }

    def _calc_player_fu(self, player) -> int:
        """根据玩家副露计算副数（仅计副露组合）"""
        fu, _ = self._calc_player_fu_detail(player)
        return fu

    def _calc_player_fu_detail(self, player) -> tuple[int, List[str]]:
        """根据玩家副露与手牌中的番牌对子计算副数与副种列表"""
        active_fanpai = set(self._FANPAI)
        wind_map = {0: 41, 1: 42, 2: 43, 3: 44}
        if player.player_index in wind_map:
            active_fanpai.add(wind_map[player.player_index])

        fu = 0
        fu_tag_count: Dict[str, int] = {}
        for combo in player.combination_tiles:
            sign = combo[0]
            try:
                tile = int(combo[1:])
            except ValueError:
                continue
            fu_map = self._MELD_FU.get(sign)
            tag_map = self._MELD_TAG.get(sign)
            if not fu_map:
                continue
            if tile in active_fanpai:
                fu += fu_map['fanpai']
                tag = tag_map['fanpai']
            elif tile in self._YAOJIU:
                fu += fu_map['yaojiu']
                tag = tag_map['yaojiu']
            else:
                fu += fu_map['normal']
                tag = tag_map['normal']
            fu_tag_count[tag] = fu_tag_count.get(tag, 0) + 1

        # 未和牌玩家的手牌副数：优先将 >=3 张同牌计为暗刻（普通/幺九/番牌），
        # 扣除后剩余恰好 2 张且为番牌者计番牌对。4 张同牌视作 1 组暗刻（多出 1 张不计分）。
        hand_tile_counter: Dict[int, int] = {}
        for tile in player.hand_tiles:
            hand_tile_counter[tile] = hand_tile_counter.get(tile, 0) + 1

        for tile, cnt in hand_tile_counter.items():
            if cnt < 3:
                continue
            if tile in active_fanpai:
                ankou_tag = "番牌暗刻"
                ankou_fu = self._MELD_FU['K']['fanpai']
            elif tile in self._YAOJIU:
                ankou_tag = "幺九暗刻"
                ankou_fu = self._MELD_FU['K']['yaojiu']
            else:
                ankou_tag = "暗刻"
                ankou_fu = self._MELD_FU['K']['normal']
            fu += ankou_fu
            fu_tag_count[ankou_tag] = fu_tag_count.get(ankou_tag, 0) + 1
            hand_tile_counter[tile] = cnt - 3

        hand_fanpai_pair_count = 0
        for tile, cnt in hand_tile_counter.items():
            if tile in active_fanpai and cnt == 2:
                hand_fanpai_pair_count += 1
        if hand_fanpai_pair_count > 0:
            fu += hand_fanpai_pair_count * 2
            fu_tag_count["番牌对"] = fu_tag_count.get("番牌对", 0) + hand_fanpai_pair_count

        fu_tags: List[str] = []
        for tag, cnt in fu_tag_count.items():
            fu_tags.append(tag if cnt == 1 else f"{tag}*{cnt}")
        return fu, fu_tags

    async def _settle_shuhewei(
        self,
        hepai_player_index,
        hepai_total_fu: Optional[int],
        hepai_fan: Optional[List[str]],
        hepai_fu_types: Optional[List[str]],
        hu_class: Optional[str],
    ) -> float:
        """数和尾结算：
        - 有和牌者时：和牌家向其余三家各收取其自身全额副数（不做比对）；
        - 其余未和牌者之间两两比对，副高者向副低者收取副差；
        - 流局时：四家之间两两比对副差；
        - 涉及庄家时该笔转账翻倍（庄家幺二）。
        """
        player_fu = {}
        player_fan: Dict[int, List[str]] = {}
        player_fu_types: Dict[int, List[str]] = {}
        for player in self.player_list:
            normal_fu, normal_fu_types = self._calc_player_fu_detail(player)
            player_fu[player.player_index] = normal_fu
            player_fan[player.player_index] = []
            player_fu_types[player.player_index] = normal_fu_types

        if hepai_player_index is not None and hepai_total_fu is not None:
            player_fu[hepai_player_index] = hepai_total_fu
            player_fan[hepai_player_index] = list(hepai_fan or [])
            player_fu_types[hepai_player_index] = list(hepai_fu_types or [])

        shuhewei_changes = {p.player_index: 0 for p in self.player_list}
        indices = [p.player_index for p in self.player_list]
        dealer_index = 0  # 古典麻将：座位 0 始终为庄家（连庄通过 keep_dealer_seat 保持）

        def _dealer_multiplier(receiver: int, payer: int) -> int:
            return 2 if (receiver == dealer_index or payer == dealer_index) else 1

        def _apply_transfer(receiver: int, payer: int, amount: int) -> None:
            shuhewei_changes[receiver] += amount
            shuhewei_changes[payer] -= amount

        # 和牌家收取其余三家各自的全额副数
        if hepai_player_index is not None:
            for payer in indices:
                if payer == hepai_player_index:
                    continue
                transfer = player_fu[payer] * _dealer_multiplier(hepai_player_index, payer)
                _apply_transfer(hepai_player_index, payer, transfer)

        # 未和牌者之间（流局时则为四家）两两比对副差
        for receiver in indices:
            if hepai_player_index is not None and receiver == hepai_player_index:
                continue
            receiver_fu = player_fu[receiver]
            for payer in indices:
                if payer == receiver:
                    continue
                if hepai_player_index is not None and payer == hepai_player_index:
                    continue
                if player_fu[payer] < receiver_fu:
                    transfer = (receiver_fu - player_fu[payer]) * _dealer_multiplier(receiver, payer)
                    _apply_transfer(receiver, payer, transfer)

        for player in self.player_list:
            player.score += shuhewei_changes[player.player_index]

        player_to_score = {p.player_index: p.score for p in self.player_list}
        reveal_wait = 0.0
        for idx in range(4):
            reveal_items = len(player_fu_types.get(idx, [])) + len(player_fan.get(idx, []))
            reveal_wait += reveal_items * 1.0 + 0.5

        logger.info(
            f"数和尾结算: player_fu={player_fu}, player_fu_types={player_fu_types}, "
            f"player_fan={player_fan}, changes={shuhewei_changes}, hu_class={hu_class}, hepai_player_index={hepai_player_index}"
        )
        player_action_record_shuhewei(
            self,
            player_fu,
            shuhewei_changes,
            player_fan,
            player_fu_types,
            hu_class or "",
            -1 if hepai_player_index is None else hepai_player_index,
        )
        await self.broadcast_shuhewei(
            player_fu,
            player_to_score,
            shuhewei_changes,
            player_fan,
            player_fu_types,
            hu_class,
            hepai_player_index,
            self.player_list[hepai_player_index].hand_tiles if hepai_player_index is not None else None,
            self.player_list[hepai_player_index].combination_mask if hepai_player_index is not None else None,
        )
        return reveal_wait

    # ========== 观战系统方法 ==========

    async def add_spectator(self, user_id: int, connection: Any):
        await self.spectator_manager.add_spectator(user_id, connection)

    async def remove_spectator(self, user_id: int):
        await self.spectator_manager.remove_spectator(user_id)


# 挂载广播方法
ClassicalGameState.wait_action = wait_action
ClassicalGameState.broadcast_game_start = broadcast_game_start
ClassicalGameState.broadcast_ask_hand_action = broadcast_ask_hand_action
ClassicalGameState.broadcast_ask_other_action = broadcast_ask_other_action
ClassicalGameState.broadcast_do_action = broadcast_do_action
ClassicalGameState.broadcast_result = broadcast_result
ClassicalGameState.broadcast_game_end = broadcast_game_end
ClassicalGameState.broadcast_switch_seat = broadcast_switch_seat
ClassicalGameState.broadcast_refresh_player_tag_list = broadcast_refresh_player_tag_list
ClassicalGameState.broadcast_shuhewei = broadcast_shuhewei
ClassicalGameState.reconnected_send_pending_ask = reconnected_send_pending_ask

ClassicalGameState.next_current_index = next_current_index
ClassicalGameState.refresh_waiting_tiles = refresh_waiting_tiles
