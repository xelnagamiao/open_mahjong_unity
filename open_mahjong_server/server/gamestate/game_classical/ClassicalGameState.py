import random
import asyncio
from typing import Any, Dict, List, Optional
import time
import logging
import hashlib
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
    reconnected_send_pending_ask,
)
from ..public.logic_common import get_index_relative_position, next_current_index, next_current_num
from .init_tiles import init_classical_tiles
from ..public.next_game_round import next_game_round_random_switchseat
from ..public.game_record_manager import init_game_record, init_game_round, player_action_record_deal, player_action_record_angang, player_action_record_jiagang, player_action_record_chipenggang, player_action_record_hu, player_action_record_liuju, player_action_record_jiuzhongjiupai, player_action_record_round_end, end_game_record
from ...game_calculation.game_calculation_service import GameCalculationService
from ...database.db_manager import DatabaseManager

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

        self.title_used = 0
        self.profile_used = 0
        self.character_used = 0
        self.voice_used = 0

    def get_tile(self, tiles_list):
        element = tiles_list.pop(0)
        self.hand_tiles.append(element)

    def get_gang_tile(self, tiles_list, gamestate):
        if len(tiles_list) <= 1 or gamestate.backward_tiles_list_type == "single":
            element = tiles_list.pop(-1)
        else:
            element = tiles_list.pop(-2)
        self.hand_tiles.append(element)
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
        self.hepai_limit = 1
        self.tourist_limit = room_data.get("tourist_limit", False)
        self.allow_spectator_config = room_data.get("allow_spectator", True)

        self.isPlayerSetRandomSeed = False

        self.tiles_list = []
        self.current_player_index = 0
        self.xunmu = 1
        self.game_random_seed = 0
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

        self.Debug = False

        self.spectator_enabled = self.allow_spectator_config and not any(player.user_id <= 10 for player in self.player_list)
        from .spectator_manager import SpectatorManager
        self.spectator_manager = SpectatorManager(self, delay=180.0, enabled=self.spectator_enabled)

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
                        'tile_count': len(self.tiles_list),
                        'round_random_seed': self.round_random_seed,
                        'current_round': self.current_round,
                        'step_time': self.step_time,
                        'round_time': self.round_time,
                        'room_type': self.room_type,
                        'room_rule': self.room_rule,
                        'sub_rule': self.sub_rule,
                        'hepai_limit': self.hepai_limit,
                        'open_cuohe': self.open_cuohe,
                        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed,
                        'players_info': []
                    }

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
            if self.room_random_seed != 0:
                self.game_random_seed = self.room_random_seed
                self.isPlayerSetRandomSeed = True
            else:
                self.game_random_seed = int(time.time() * 1000000) % (2**32)
                self.isPlayerSetRandomSeed = False
            rng = random.Random(self.game_random_seed)
            rng.shuffle(self.player_list)
            for index, player in enumerate[ClassicalPlayer](self.player_list):
                player.player_index = index
                player.original_player_index = index
        else:
            self.isPlayerSetRandomSeed = False
            self.game_random_seed = int(time.time() * 1000000) % (2**32)
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

            # ===== 开局预检测轮：按玩家0-3顺序检查国士无双和九种九牌 =====
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
                    logger.info(f"玩家{i}符合九种九牌条件，手牌: {self.player_list[i].hand_tiles}")
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
                            if self.tiles_list == []:
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
                    actual_hu_score = total_fu * 3
                    self.result_dict = {}
                    self.player_list[hepai_player_index].score += actual_hu_score
                    for i in self.player_list:
                        if i.player_index != hepai_player_index:
                            i.score -= total_fu

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
                    actual_hu_score = total_fu * 3
                    self.result_dict = {}
                    logger.info(f"和牌玩家索引{hepai_player_index}")
                    self.player_list[hepai_player_index].score += actual_hu_score
                    self.player_list[self.current_player_index].score -= actual_hu_score

                    self.player_list[hepai_player_index].record_counter.dianhe_times += 1
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan)
                    self.player_list[hepai_player_index].record_counter.win_score += hu_score
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu

                    self.player_list[self.current_player_index].record_counter.fangchong_times += 1
                    self.player_list[self.current_player_index].record_counter.fangchong_score += actual_hu_score

                player_to_score = {}
                for i in self.player_list:
                    player_to_score[i.player_index] = i.score
                he_hand = self.player_list[hepai_player_index].hand_tiles
                he_huapai = self.player_list[hepai_player_index].huapai_list
                he_combination_mask = self.player_list[hepai_player_index].combination_mask

                await broadcast_result(self,
                                       hepai_player_index=hepai_player_index,
                                       player_to_score=player_to_score,
                                       hu_score=actual_hu_score,
                                       hu_fan=hu_fan,
                                       hu_class=self.hu_class,
                                       hepai_player_hand=he_hand,
                                       hepai_player_huapai=he_huapai,
                                       hepai_player_combination_mask=he_combination_mask,
                                       base_fu=hu_base_fu,
                                       fu_fan_list=hu_fu_fan_list,
                                       )

                print(f"hu_class: {self.hu_class}, result_dict: {self.result_dict}")
                print(f"player_list_hand_tiles: {self.player_list[hepai_player_index].hand_tiles}")
                print(f"base_fu: {hu_base_fu}, total_fu: {hu_score}, fu_fan_list: {hu_fu_fan_list}, fan_list: {hu_fan}")

                for i in self.player_list:
                    has_fulu = any(combo.startswith("k") or combo.startswith("g") or combo.startswith("s")
                                   for combo in i.combination_tiles)
                    if has_fulu:
                        i.record_counter.fulu_times += 1

            else:
                if self.hu_class != "jiuzhongjiupai":
                    self.hu_class = "liuju"
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
                                       )

            for player in self.player_list:
                score_change = player.score - scores_before[player.original_player_index]
                if score_change > 0:
                    score_change_str = f"+{score_change:02d}"
                elif score_change < 0:
                    score_change_str = f"-{abs(score_change):02d}"
                else:
                    score_change_str = "0"
                player.score_history.append(score_change_str)

            score_changes = [0, 0, 0, 0]
            for player in self.player_list:
                score_changes[player.original_player_index] = player.score - scores_before[player.original_player_index]

            if self.hu_class in ["hu_self", "hu_first", "hu_second", "hu_third"]:
                player_action_record_hu(self, hu_class=self.hu_class, hu_score=actual_hu_score,
                                        hu_fan=hu_fan, hepai_player_index=hepai_player_index,
                                        score_changes=score_changes,
                                        base_fu=hu_base_fu, fu_fan_list=hu_fu_fan_list)
                if hasattr(self, 'spectator_manager'):
                    self.spectator_manager.record_tick([self.hu_class, hepai_player_index, actual_hu_score, hu_fan, score_changes])
            elif self.hu_class == "jiuzhongjiupai":
                player_action_record_jiuzhongjiupai(self)
                if hasattr(self, 'spectator_manager'):
                    self.spectator_manager.record_tick(["jiuzhongjiupai"])
            else:
                player_action_record_liuju(self)
                if hasattr(self, 'spectator_manager'):
                    self.spectator_manager.record_tick(["liuju"])
            player_action_record_round_end(self)
            if hasattr(self, 'spectator_manager'):
                self.spectator_manager.record_tick(["end"])

            if self.hu_class in ["liuju", "jiuzhongjiupai"]:
                await asyncio.sleep(2)
            else:
                fan_count = len(hu_fan) if hu_fan else 0
                wait_time = fan_count * 0.5 + 8 + 0.5
                ready_phase_deadline = time.time() + wait_time

                self.action_dict = {}
                for player in self.player_list:
                    if player.user_id <= 10:
                        self.action_dict[player.player_index] = []
                    else:
                        self.action_dict[player.player_index] = ["ready"]
                        player.remaining_time = int(wait_time)

                self.game_status = "waiting_ready"
                await broadcast_ready_status(self)
                while any(self.action_dict[i] for i in self.action_dict):
                    for p in self.player_list:
                        if self.action_dict.get(p.player_index):
                            p.remaining_time = max(0, int(ready_phase_deadline - time.time()))
                    if await wait_action(self) is False:
                        break

            next_game_round_random_switchseat(self)

            logger.info(f"重新开始下一局")

        logger.info("游戏结束")
        end_game_record(self)
        logger.info(f"最终游戏记录: {self.game_record}")

        self.player_list.sort(key=lambda x: x.score, reverse=True)
        for index, player in enumerate[ClassicalPlayer](self.player_list):
            player.record_counter.rank_result = index + 1

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

        await self.game_server.room_manager.destroy_room(self.room_id)
        logger.info(f"游戏实例已清理，room_id: {self.room_id},goodbye!")

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
ClassicalGameState.reconnected_send_pending_ask = reconnected_send_pending_ask

ClassicalGameState.next_current_index = next_current_index
ClassicalGameState.refresh_waiting_tiles = refresh_waiting_tiles
