import random
import asyncio
from typing import Any, Dict, List, Optional
import time
import logging
from .action_check import check_action_after_cut,check_action_after_gang_forced_cut,check_action_after_batch_gang_forced_cut,check_action_jiagang,check_action_hand_action,check_hepai,check_only_cut,refresh_waiting_tiles,filter_open_kong_replacement_actions
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
from ..public.logic_common import get_index_relative_position, next_current_index, next_current_num, back_current_num, assign_strict_final_ranks
from ..public.hand_slot_utils import clear_draw_slot, normalize_tile
from ..public.claim_protection import begin_claim_protection_interval
from .init_tiles import init_changsha_tiles
from ..public.next_game_round import next_game_round_random_switchseat as next_game_round_changsha_switchseat
from ..public.round_end_timing import (
    hu_result_ready_wait_seconds,
    liuju_ready_wait_seconds,
)
from ..public.spectator_rules import too_many_ai_for_spectator
from ..public.vote_manager import vote_checkpoint
from ..public.game_record_manager import init_game_record,init_game_round,player_action_record_deal,player_action_record_cut,player_action_record_angang,player_action_record_jiagang,player_action_record_chipenggang,player_action_record_hu,player_action_record_liuju,player_action_record_round_end,end_game_record,build_score_changes_by_seat,build_score_changes_dict,capture_player_entry_order
from ...game_calculation.game_calculation_service import GameCalculationService
from ...game_calculation.changsha.changsha_hepai_check import (
    INITIAL_HU_NAMES,
    changsha_initial_hu_reveal_tiles,
    changsha_base_from_fans,
    evaluate_changsha_initial_hu,
)
from ...database.db_manager import DatabaseManager
from ..public.random_seed_manager import setup_random_seed_system
from ...database.fulu_utils import record_fulu_rounds_for_players

logger = logging.getLogger(__name__)

# 牌谱记录类
class RecordCounter:
    def __init__(self):
        self.fulu_times = 0 # 副露次数
        self.recorded_fans = [] # 和了的番种列表
        self.rank_result = 0 # 最终排名
        self.zimo_times = 0 # 自摸次数
        self.dianhe_times = 0 # 点和次数
        self.fangchong_times = 0 # 放铳次数
        self.fangchong_score = 0 # 总放铳番数
        self.win_turn = 0 # 总和牌巡目
        self.win_score = 0 # 总和牌番数

# 玩家类
class ChangshaPlayer:
    def __init__(self, user_id: int, username: str, tiles: list, remaining_time: int):
        self.user_id = user_id                        # 用户UID
        self.username = username                      # 玩家名（用于显示）
        self.is_bot = True if user_id <= 10 else False # 是否是机器人
        self.hand_tiles = tiles                       # 手牌
        self.huapai_list = []                         # 花牌列表（长沙规则恒为空，保留字段兼容客户端）
        self.discard_tiles = []                       # 弃牌
        self.discard_origin_tiles = []                # 理论弃牌
        self.combination_tiles = []                   # 组合牌 g明杠 k明刻 G暗杠
        # combination_mask组合牌掩码 0代表竖 1代表横 2代表暗面 3代表上侧(加杠) 4代表空
        # [0,17,1,17,0,17] = 碰对家 777m k17
        # [1,22,1,22,0,22,0,22] = 加杠 2222p g22
        # [2,17,2,17,2,17,2,17] = 暗杠 7777m G17 (国标中17应使用0代替 避免暗杠信息泄露)
        self.combination_mask = []
        self.score = 0                                # 分数
        self.remaining_time = remaining_time          # 剩余时间 （局时）
        self.player_index = 0                         # 玩家索引 东南西北 0 1 2 3
        self.original_player_index = 0                # 原始玩家索引 东南西北 0 1 2 3
        self.tag_list = []                                  # peida,diaoxiang 存储玩家tag

        self.waiting_tiles = set[int]()               # 听牌
        self.record_counter = RecordCounter()          # 创建独立的记录计数器实例
        self.score_history = []                        # 分数历史变化列表，每局记录 +？、-？ 或 0

        self.title_used = 0 # 使用的称号ID
        self.profile_used = 0 # 使用的头像ID
        self.character_used = 0 # 使用的角色ID
        self.voice_used = 0 # 使用的音色ID
        self.has_draw_slot = False
        self.open_kong_locked = False

    def get_tile(self, tiles_list, *, mark_draw_slot: bool = True):
        element = tiles_list.pop(0) # 从牌堆中获取第一张牌
        self.hand_tiles.append(element)
        if mark_draw_slot:
            self.has_draw_slot = True

    def get_gang_tile(self, tiles_list, gamestate):
        if len(tiles_list) <= 1 or gamestate.backward_tiles_list_type == "single":
            element = tiles_list.pop(-1) # 从牌堆中获取倒数第一张牌
        else:
            element = tiles_list.pop(-2) # 从牌堆中获取倒数第二张牌
        self.hand_tiles.append(element)
        self.has_draw_slot = True
        # 切换倒序摸牌状态
        gamestate.backward_tiles_list_type = "single" if gamestate.backward_tiles_list_type == "double" else "double"
        return element

        # 游戏进程类
class ChangshaGameState:
    # ChangshaGameState负责一个长沙麻将对局进程，init属性包含游戏房间状态 player_list 包含玩家数据
    def __init__(self, game_server, room_data: dict, calculation_service: GameCalculationService, db_manager: DatabaseManager, gamestate_id: str):
        # 传入游戏服务器
        self.game_server = game_server # 游戏服务器
        # 传入全局计算服务
        self.calculation_service = calculation_service
        # 传入数据库管理器 用于存储牌谱
        self.db_manager = db_manager
        # gamestate_id（游戏状态唯一标识）
        self.gamestate_id = gamestate_id
        # 创建牌谱管理器 用于存储牌谱
        self.game_record = {}
        # game_loop_chinese循环任务引用
        self.game_task: Optional[asyncio.Task] = None
        # 创建玩家列表 包含ChangshaPlayer类
        self.player_list: List[ChangshaPlayer] = []
        player_settings = room_data.get("player_settings", {})
        for user_id in room_data["player_list"]:
            player_setting = player_settings.get(user_id, {})
            if user_id == 0:
                username = "麻雀罗伯特"
            elif user_id == 2:
                username = "牌效罗伯特"
            else:
                username = player_setting.get("username", f"用户{user_id}")
            player = ChangshaPlayer(user_id, username, [], room_data["round_timer"])
            # 初始化玩家使用的设置数据
            player.title_used = player_setting.get("title_id", 1)
            player.profile_used = player_setting.get("profile_image_id", 1)
            player.character_used = player_setting.get("character_id", 1)
            player.voice_used = player_setting.get("voice_id", 1)
            self.player_list.append(player)

        # 初始化房间配置
        self.room_id = room_data["room_id"] # 房间ID
        self.tips = room_data["tips"] # 是否提示
        self.max_round = room_data["game_round"] # 最大局数
        self.step_time = room_data["step_timer"] # 步时
        self.round_time = room_data["round_timer"] # 局时
        # room_rule 表示具体规则（changsha等），room_type 表示房间类型（custom/match等） sub_rule 表示子规则（changsha/classic_double_bird等）
        self.room_rule = room_data["room_rule"]
        self.room_type = room_data["room_type"]
        self.sub_rule = room_data.get("sub_rule", "changsha/classic_double_bird") # 子规则
        self.match_tier = room_data.get("match_tier")
        self.event_id = room_data.get("event_id")

        self.room_random_seed = room_data.get("random_seed", 0) # 随机种子（默认为0）
        self.open_cuohe = room_data.get("open_cuohe", False) # 是否开启错和（默认为False）
        self.show_moqie_hint = room_data.get("show_moqie_hint", False) # 是否显示手摸切灰显（默认为False）
        self.tactical_call = room_data.get("tactical_call", False) # 战术鸣牌
        self.claim_protection = room_data.get("claim_protection", True) # 鸣牌保护
        self.tactical_pre_grace_delay = room_data.get("tactical_pre_grace_delay", 0.5)
        self.tactical_grace_seconds = room_data.get("tactical_grace_seconds", 5.0)
        self.claim_protect_delay = room_data.get("claim_protect_delay", 1.3)
        self.claim_meld_followup_gap = room_data.get("claim_meld_followup_gap", 0.7)
        self.claim_meld_post_gap = room_data.get("claim_meld_post_gap", 0.5)
        self.hepai_limit = 1 # 长沙起和限制固定为1
        self.tourist_limit = room_data.get("tourist_limit", False) # 游客限制
        self.allow_spectator_config = room_data.get("allow_spectator", True) # 允许观战配置
        self.open_kong_replacement_count = int(room_data.get("open_kong_replacement_count", 2))
        self.open_kong_replacement_count = min(4, max(1, self.open_kong_replacement_count))
        self.bird_count = int(room_data.get("bird_count", 2))
        if self.bird_count not in (0, 1, 2, 4):
            self.bird_count = 2
        self.dealer_bird = room_data.get("dealer_bird", True)
        self.base_score_no_dealer = bool(room_data.get("base_score_no_dealer", False))
        self.small_hu_score = max(1, int(room_data.get("small_hu_score", 2)))
        self.big_hu_score = max(1, int(room_data.get("big_hu_score", 8)))
        self.initial_hu_enabled = {
            INITIAL_HU_NAMES["siXi"]: room_data.get("initial_hu_si_xi", True),
            INITIAL_HU_NAMES["banBanHu"]: room_data.get("initial_hu_ban_ban_hu", True),
            INITIAL_HU_NAMES["queYiSe"]: room_data.get("initial_hu_que_yi_se", True),
            INITIAL_HU_NAMES["liuLiuShun"]: room_data.get("initial_hu_liu_liu_shun", True),
            INITIAL_HU_NAMES["sanTong"]: room_data.get("initial_hu_san_tong", True),
        }

        self.isPlayerSetRandomSeed = False # 是否玩家设置了随机种子

        # 初始化游戏状态
        self.tiles_list = [] # 牌堆
        self.current_player_index = 0 # 目前轮到的玩家
        self.xunmu = 1 # 巡目
        self.master_seed: int = 0  # 主种子
        self.salt = "" # 盐字符串
        self.commitment: int = 0 # 承诺值
        self.round_random_seed = 0 # 局内随机种子
        self.game_status = "waiting"  # waiting, playing, finished
        self.server_action_tick = 0 # 操作帧
        self.player_action_tick = 0 # 玩家操作帧
        self.current_round = 1 # 游戏进程小局号(可能连庄)
        self.round_index = 1 ###### 实际局数索引(连续递增，用于日麻连庄情况等的内部计算 国标不使用)
        self.result_dict = {} # 结算结果 {hu_first:(int,list[str]),hu_second:(int,list[str]),hu_third:(int,list[str])}
        self.hu_class = None # 和牌玩家索引
        self.jiagang_tile = None # 抢杠牌 每次加杠时存储 waiting_jiagang_action 以后删除
        self.last_draw_was_gang = False # 用于判断杠上炮情境
        self.pending_gang_replacement_count = 0
        self.pending_gang_forced_discard = False
        self.forced_cut_tile = None
        self.forced_cut_tiles = []
        self.pending_gang_replacement_hu_player_index = None
        self.pending_gang_replacement_hu_tile = None
        self.pending_gang_replacement_hu_hand = None
        self.current_claim_cut_tile = None
        self.initial_hu_types = {}
        self.sea_bottom_candidates = []
        self.player_passed_hu_base = {}
        self.temp_fan = [] ###### 临时番数 不启用 暂时通过不同的和牌检测和给和牌检测传递is_first or if tiles_list == [] 来计算额外加减的役

        # 用于玩家操作的事件和队列
        self.action_events:Dict[int,asyncio.Event] = {0:asyncio.Event(),1:asyncio.Event(),2:asyncio.Event(),3:asyncio.Event()}  # 玩家索引 -> Event
        self.action_queues:Dict[int,asyncio.Queue] = {0:asyncio.Queue(),1:asyncio.Queue(),2:asyncio.Queue(),3:asyncio.Queue()}  # 玩家索引 -> Queue
        self.waiting_players_list = [] # 等待操作的玩家列表

        # 所有check方法都返回action_dict字典
        self.action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]} # 玩家索引 -> 操作列表
        # 行为 -> 优先级 用于在多人共通等待行为时判断是否需要等待更高优先级玩家的操作或直接结束更低优先级玩家的等待
        self.action_priority:Dict[str,int] = {
        "hu_self": 6, "hu_first": 5, "hu_second": 4, "hu_third": 3,  # 和牌优先级 三种优先级对应多人和牌时的优先权
        "peng": 2, "gang": 2,  # 碰杠优先级 次高优先级
        "chi_left": 1, "chi_mid": 1, "chi_right": 1,
        "ready": 0,  # 准备操作优先级 最低优先级
        "pass": 0,"cut":0,"buzhang":0,"angang":0,"jiagang":0,"deal_tile":0,"deal_gang_tile":0,
        "initial_hu": 0, "sea_bottom": 0 # 其他优先级 最低优先级
        }

        self.backward_tiles_list_type = "double"

        from ..public.claim_protection import init_claim_protection_state
        init_claim_protection_state(self)

        # 如果您在管理自己规则内的分支，请不要将Debug = True 的配置上传到公共代码仓库 这一项单元配置不会得到review和测试
        self.Debug = False

        # 观战系统相关：含 bot(uid<=10) 或配置禁用的对局禁用观战
        self.spectator_enabled = self.allow_spectator_config and not too_many_ai_for_spectator(self.player_list)
        from .spectator_manager import SpectatorManager
        self.spectator_manager = SpectatorManager(self, delay=180.0, enabled=self.spectator_enabled)
        # 实时观战者（由 FriendManager 维护，结构: List[RealtimeSpectator]）
        self.realtime_spectators = []

    async def send_to_realtime_spectators(self, player_index: int, response):
        from ..public.spectator_rules import deliver_realtime_spectator_message
        await deliver_realtime_spectator_message(self, player_index, response)

    async def player_disconnect(self, user_id: int):
        """玩家掉线：增加 offline 标签并广播，如果所有非AI玩家都offline则销毁gamestate"""
        newly_offline = False
        for p in self.player_list:
            if p.user_id == user_id:
                if "offline" not in p.tag_list:
                    p.tag_list.append("offline")
                    newly_offline = True
                    await broadcast_refresh_player_tag_list(self)
                break

        if newly_offline:
            from ..public.offline import schedule_offline_auto_on_disconnect
            schedule_offline_auto_on_disconnect(self, user_id)

        # 检查所有非AI玩家（user_id >= 10）是否都offline
        non_ai_players = [p for p in self.player_list if p.user_id >= 10]
        if non_ai_players:  # 如果有非AI玩家
            all_offline = all("offline" in p.tag_list for p in non_ai_players)
            if all_offline:
                logger.info(f"所有非AI玩家都已掉线，开始清理gamestate，room_id: {self.room_id}, gamestate_id: {self.gamestate_id}")
                await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)

    async def player_reconnect(self, user_id: int):
        """玩家重连：移除 offline 标签并广播，然后向该玩家发送游戏状态"""
        for p in self.player_list:
            if p.user_id == user_id:
                if "offline" in p.tag_list:
                    p.tag_list.remove("offline")
                    await broadcast_refresh_player_tag_list(self)

                # 向重连的玩家单独发送游戏开始信息
                if user_id in self.game_server.user_id_to_connection:
                    from ...response import Response, GameInfo
                    player_conn = self.game_server.user_id_to_connection[user_id]

                    # 构建游戏信息（与 broadcast_game_start 一致，固定含 sub_rule、hepai_limit）
                    base_game_info = {
                        'room_id': self.room_id,
                        'gamestate_id': self.gamestate_id,
                        'tips': self.tips,
                        'current_player_index': self.current_player_index,
                        "action_tick": self.server_action_tick,
                        'max_round': self.max_round,
                        'tile_count': len(self.tiles_list),
                        'commitment': self.commitment,  # 承诺值
                        'salt': self.salt,  # 盐字符串
                        'current_round': self.current_round,
                        'step_time': self.step_time,
                        'round_time': self.round_time,
                        'room_type': self.room_type,
                        'room_rule': self.room_rule,
                        'sub_rule': self.sub_rule,
                        'hepai_limit': self.hepai_limit,
                        'open_cuohe': self.open_cuohe,
                        'show_moqie_hint': self.show_moqie_hint,
                        'tactical_call': getattr(self, 'tactical_call', False),
                        'claim_protection': getattr(self, 'claim_protection', False),
                        'open_kong_replacement_count': getattr(self, 'open_kong_replacement_count', 2),
                        'initial_hu_si_xi': getattr(self, 'initial_hu_enabled', {}).get(INITIAL_HU_NAMES["siXi"], True),
                        'initial_hu_ban_ban_hu': getattr(self, 'initial_hu_enabled', {}).get(INITIAL_HU_NAMES["banBanHu"], True),
                        'initial_hu_que_yi_se': getattr(self, 'initial_hu_enabled', {}).get(INITIAL_HU_NAMES["queYiSe"], True),
                        'initial_hu_liu_liu_shun': getattr(self, 'initial_hu_enabled', {}).get(INITIAL_HU_NAMES["liuLiuShun"], True),
                        'initial_hu_san_tong': getattr(self, 'initial_hu_enabled', {}).get(INITIAL_HU_NAMES["sanTong"], True),
                        'bird_count': getattr(self, 'bird_count', 2),
                        'dealer_bird': getattr(self, 'dealer_bird', True),
                        'base_score_no_dealer': getattr(self, 'base_score_no_dealer', False),
                        'small_hu_score': getattr(self, 'small_hu_score', 2),
                        'big_hu_score': getattr(self, 'big_hu_score', 8),
                        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed,
                        'players_info': []
                    }
                    from ..public.game_record_manager import build_player_entry_order_fields
                    base_game_info.update(build_player_entry_order_fields(self))

                    # 构建玩家信息列表
                    for player in self.player_list:
                        player_info = {
                            'user_id': player.user_id,
                            'username': player.username,
                            'hand_tiles_count': len(player.hand_tiles),
                            'hand_tiles': player.hand_tiles if player.user_id == user_id else None,  # 只有自己可见手牌
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
                            'initial_hu_types': getattr(self, 'initial_hu_types', {}).get(player.player_index, []),
                        }
                        base_game_info['players_info'].append(player_info)

                    # 与 broadcast_game_start 保持一致：手牌通过 players_info[].hand_tiles 传递
                    game_info = GameInfo(
                        **base_game_info,
                        self_hand_tiles=None
                    )

                    response = Response(
                        type="gamestate/changsha/game_start",
                        success=True,
                        message="重连成功，游戏继续",
                        game_info=game_info
                    )

                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    logger.info(f"已向重连玩家 {p.username} 发送游戏状态信息")
                    await reconnected_send_pending_ask(self, user_id)
                break

    async def cleanup_game_state(self):
        """清理游戏状态协程：取消游戏循环任务（映射关系由 gamestate_manager 统一清理）"""
        from ..public.outbound_pipe import close_outbound_pipes
        close_outbound_pipes(self)
        # 清理观战管理器
        await self.spectator_manager.cleanup()

        # 取消游戏循环任务
        if self.game_task and not self.game_task.done():
            self.game_task.cancel()
            try:
                await self.game_task
            except asyncio.CancelledError:
                logger.info(f"已取消游戏循环任务，room_id: {self.room_id}")
            except Exception as e:
                logger.error(f"取消游戏循环任务时出错，room_id: {self.room_id}, 错误: {e}")

    async def run_game_loop(self):
        """
        顶层游戏循环包装：
        - 负责运行实际的 game_loop_changsha
        - 捕获未处理异常并进行统一日志和清理
        """
        try:
            await self.game_loop_changsha()
        except asyncio.CancelledError:
            # 任务被外部正常取消（例如房间销毁），不视为错误
            logger.info(f"游戏循环被取消，room_id: {self.room_id}, gamestate_id: {self.gamestate_id}")
            raise
        except Exception as e:
            # 捕获所有未处理异常，避免任务静默失败
            logger.error(
                f"游戏循环发生未捕获异常，room_id: {self.room_id}, gamestate_id: {self.gamestate_id}, 错误: {e}",
                exc_info=True
            )
            try:
                # 出错时尝试执行清理逻辑
                await self.cleanup_game_state()
            except Exception as cleanup_err:
                logger.error(
                    f"清理游戏状态时出错，room_id: {self.room_id}, gamestate_id: {self.gamestate_id}, 错误: {cleanup_err}",
                    exc_info=True
                )

    def _draw_changsha_birds(self, count: int = 2) -> List[int]:
        birds = []
        for _ in range(count):
            if not self.tiles_list:
                break
            birds.append(self.tiles_list.pop(0))
        return birds

    @staticmethod
    def _is_sea_bottom_win(fan_list: List[str]) -> bool:
        return any("海底" in name for name in (fan_list or []))

    def _sea_bottom_bird_tile(self, winner: int) -> Optional[int]:
        winner_hand = self._player_by_index(winner).hand_tiles
        return winner_hand[-1] if winner_hand else None

    @staticmethod
    def _format_changsha_bird_tile(tile: int) -> str:
        suit = tile // 10
        rank = tile % 10
        suit_name = {1: "万", 2: "筒", 3: "条"}.get(suit)
        rank_name = {
            1: "一",
            2: "二",
            3: "三",
            4: "四",
            5: "五",
            6: "六",
            7: "七",
            8: "八",
            9: "九",
        }.get(rank)
        if suit_name is None or rank_name is None:
            return str(tile)
        return f"{rank_name}{suit_name}"

    @staticmethod
    def _changsha_bird_seat(tile: int, origin: int = 0) -> int:
        return (origin + ((tile % 10) - 1)) % 4

    def _player_by_index(self, player_index: int) -> ChangshaPlayer:
        for player in self.player_list:
            if player.player_index == player_index:
                return player
        raise ValueError(f"未找到座位 {player_index} 的玩家")

    def _reset_changsha_round_state(self) -> None:
        self.pending_gang_replacement_count = 0
        self.pending_gang_forced_discard = False
        self.forced_cut_tile = None
        self.forced_cut_tiles = []
        self.pending_gang_replacement_hu_player_index = None
        self.pending_gang_replacement_hu_tile = None
        self.pending_gang_replacement_hu_hand = None
        self.current_claim_cut_tile = None
        self.sea_bottom_player_index = None
        self.sea_bottom_candidates = []
        self.initial_hu_types = {}
        self.player_passed_hu_base = {}
        for player in self.player_list:
            player.open_kong_locked = False

    def _detect_initial_hu_types(self) -> None:
        self.initial_hu_types = {}
        for player in self.player_list:
            types = [
                hu_type
                for hu_type in evaluate_changsha_initial_hu(player.hand_tiles)
                if self.initial_hu_enabled.get(hu_type, True)
            ]
            if types:
                self.initial_hu_types[player.player_index] = types

    def _sea_bottom_waiting_candidates(self) -> List[int]:
        candidates = []
        for offset in range(1, 5):
            player_index = (self.current_player_index + offset) % 4
            player = self._player_by_index(player_index) if hasattr(self, "_player_by_index") else self.player_list[player_index]
            if "peida" in player.tag_list:
                logger.info("长沙海底跳过陪打玩家 player=%s", player_index)
                continue
            waiting_tiles = self.calculation_service.Changsha_tingpai_check(
                player.hand_tiles,
                player.combination_tiles,
            )
            if waiting_tiles != player.waiting_tiles:
                player.waiting_tiles = waiting_tiles
                logger.info("长沙海底重算听牌 player=%s waiting_tiles=%s", player_index, waiting_tiles)
            else:
                logger.info("长沙海底检查 player=%s waiting_tiles=%s", player_index, waiting_tiles)
            if player.waiting_tiles:
                candidates.append(player_index)
        return candidates

    def _next_sea_bottom_player(self) -> Optional[int]:
        candidates = ChangshaGameState._sea_bottom_waiting_candidates(self)
        return candidates[0] if candidates else None

    def _prepare_next_sea_bottom_choice(self) -> bool:
        while self.sea_bottom_candidates:
            player_index = self.sea_bottom_candidates.pop(0)
            player = self._player_by_index(player_index)
            if "peida" in player.tag_list:
                continue
            waiting_tiles = self.calculation_service.Changsha_tingpai_check(
                player.hand_tiles,
                player.combination_tiles,
            )
            player.waiting_tiles = waiting_tiles
            if waiting_tiles:
                self.current_player_index = player_index
                self.action_dict = {0: [], 1: [], 2: [], 3: []}
                self.action_dict[player_index] = ["sea_bottom", "pass"]
                self.game_status = "waiting_sea_bottom"
                return True
        self.action_dict = {0: [], 1: [], 2: [], 3: []}
        return False

    def _roll_initial_hu_dice(self, player_index: int) -> List[int]:
        seed = f"{self.round_random_seed}:{self.current_round}:initial_hu:{player_index}"
        rng = random.Random(seed)
        return [rng.randint(1, 6), rng.randint(1, 6)]

    @staticmethod
    def _initial_hu_dice_seat(origin_index: int, die: int) -> int:
        return (origin_index + die - 1) % 4

    def _changsha_bird_origin(self, winner: int) -> int:
        return 0 if self.dealer_bird else winner

    def _changsha_base_from_fans(self, fan_list: List[str], dealer_related: bool = False) -> int:
        return changsha_base_from_fans(
            fan_list,
            dealer_related=dealer_related,
            small_hu_score=self.small_hu_score,
            big_hu_score=self.big_hu_score,
            base_score_no_dealer=self.base_score_no_dealer,
        )

    def _score_initial_hu(self, winner: int, hu_types: List[str]):
        dice = self._roll_initial_hu_dice(winner)
        bird_origin = self._changsha_bird_origin(winner)
        bird_seats = [self._initial_hu_dice_seat(bird_origin, die) for die in dice]
        payers = [p.player_index for p in self.player_list if p.player_index != winner]
        total_win = 0
        total_base = 0
        payer_details = []

        for payer in payers:
            dealer_related = winner == 0 or payer == 0
            base = self._changsha_base_from_fans(["小胡"], dealer_related)
            hit_count = sum(1 for seat in bird_seats if seat in (winner, payer))
            multiplier = 2 ** hit_count
            payment = base * multiplier
            self._player_by_index(winner).score += payment
            self._player_by_index(payer).score -= payment
            total_win += payment
            total_base += base
            payer_details.append({
                "payer": payer,
                "base": base,
                "hit_count": hit_count,
                "multiplier": multiplier,
                "payment": payment,
            })

        fan_display = list(hu_types or [])
        fan_display.extend([
            "起手胡",
            "骰子:" + ",".join(str(die) for die in dice),
            "骰子鸟位:" + ",".join(str(seat) for seat in bird_seats),
        ])
        return {
            "actual_hu_score": total_win,
            "base_score": total_base,
            "dice": dice,
            "bird_seats": bird_seats,
            "fan_display": fan_display,
            "payer_details": payer_details,
        }

    async def _settle_initial_hu(self, player_index: int) -> None:
        hu_types = list(self.initial_hu_types.get(player_index, []))
        if not hu_types:
            return
        scores_before = {player.original_player_index: player.score for player in self.player_list}
        score_info = self._score_initial_hu(player_index, hu_types)
        winner = self._player_by_index(player_index)
        winner.record_counter.zimo_times += 1
        winner.record_counter.recorded_fans.append(score_info["fan_display"])
        winner.record_counter.win_score += score_info["actual_hu_score"]
        player_to_score = {player.player_index: player.score for player in self.player_list}
        score_changes_dict = build_score_changes_dict(self.player_list, scores_before)
        await broadcast_result(
            self,
            hepai_player_index=player_index,
            player_to_score=player_to_score,
            hu_score=score_info["actual_hu_score"],
            hu_fan=score_info["fan_display"],
            hu_class="initial_hu",
            hepai_player_hand=changsha_initial_hu_reveal_tiles(winner.hand_tiles, hu_types),
            hepai_player_huapai=winner.huapai_list,
            hepai_player_combination_mask=winner.combination_mask,
            score_changes=score_changes_dict,
            initial_hu_dice=score_info["dice"],
            initial_hu_bird_seats=score_info["bird_seats"],
            initial_hu_payer_details=score_info["payer_details"],
            round_continues=True,
        )

    async def _resolve_initial_hu_choices(self) -> None:
        initial_players = sorted(self.initial_hu_types)
        for player_index in initial_players:
            if not self.initial_hu_types.get(player_index):
                continue
            self.current_player_index = player_index
            self.action_dict = {0: [], 1: [], 2: [], 3: []}
            self.action_dict[player_index] = ["initial_hu", "pass"]
            self.game_status = "waiting_initial_hu"
            await self.broadcast_ask_hand_action()
            await self.wait_action()
        self.current_player_index = 0
        self.action_dict = {0: [], 1: [], 2: [], 3: []}

    async def _take_sea_bottom_tile(self, player_index: int) -> None:
        """翻开海底牌并按自摸优先、其余玩家荣和次之的顺序处理。"""
        if not self.tiles_list:
            logger.warning("长沙海底取牌失败: player=%s tiles_list empty", player_index)
            self.game_status = "END"
            return
        self.current_player_index = player_index
        self.sea_bottom_player_index = player_index
        self.last_draw_was_gang = False
        self.pending_gang_forced_discard = False
        self.pending_gang_replacement_count = 0
        self.pending_gang_replacement_hu_player_index = None
        self.pending_gang_replacement_hu_tile = None
        self.pending_gang_replacement_hu_hand = None
        self.forced_cut_tile = None
        self.forced_cut_tiles = []
        player = self._player_by_index(player_index) if hasattr(self, "_player_by_index") else self.player_list[player_index]
        sea_bottom_tile = self.tiles_list.pop(0)

        original_result_dict = dict(getattr(self, "result_dict", {}))
        self.result_dict = dict(original_result_dict)
        player.hand_tiles.append(sea_bottom_tile)
        self.action_dict = {0: [], 1: [], 2: [], 3: []}
        check_hepai(self, self.action_dict, sea_bottom_tile, player_index, "handgot")
        self_win = "hu_self" in self.action_dict.get(player_index, [])
        if not self_win:
            player.hand_tiles.pop()

        player.discard_tiles.append(sea_bottom_tile)
        player_action_record_cut(self, cut_tile=sea_bottom_tile, is_moqie=True)
        if hasattr(self, "clear_hu_pass_after_own_discard"):
            self.clear_hu_pass_after_own_discard(player_index)
        await self.broadcast_do_action(
            action_list=["cut"],
            action_player=player_index,
            cut_tile=sea_bottom_tile,
            cut_class=True,
            sea_bottom_discard=True,
        )

        if self_win:
            self.hu_class = "hu_self"
            self.game_status = "END"
            return

        self.result_dict = dict(original_result_dict)
        refresh_waiting_tiles(self, player_index)
        pre_action_dict = self._filter_sea_bottom_ron_actions(check_action_after_cut(self, sea_bottom_tile))
        self.current_claim_cut_tile = sea_bottom_tile
        self.action_dict = pre_action_dict
        if any(self.action_dict[i] for i in self.action_dict):
            begin_claim_protection_interval(self, self.action_dict, self.current_player_index)
            self.game_status = "waiting_action_after_cut"
        else:
            self.current_claim_cut_tile = None
            self.game_status = "END"

    def _filter_sea_bottom_ron_actions(self, action_dict: Dict[int, List[str]]) -> Dict[int, List[str]]:
        filtered = {0: [], 1: [], 2: [], 3: []}
        for seat, actions in (action_dict or {}).items():
            hu_actions = [action for action in actions if action in ("hu_first", "hu_second", "hu_third")]
            if hu_actions:
                filtered[seat] = hu_actions + ["pass"]
        return filtered

    def _make_player_next_dealer(self, dealer_index: int) -> None:
        for player in self.player_list:
            player.player_index = (player.player_index - dealer_index) % 4
        self.player_list.sort(key=lambda x: x.player_index)

    def record_hu_pass(self, player_index: int, allowed_actions: List[str]) -> None:
        hu_bases = []
        for action in allowed_actions:
            if action.startswith("hu_") and action in self.result_dict:
                result = self.result_dict.get(action)
                if result and result[0] >= 1:
                    hu_bases.append(result[0])
        if not hu_bases:
            return
        previous = self.player_passed_hu_base.get(player_index, 0)
        self.player_passed_hu_base[player_index] = max(previous, max(hu_bases))

    def clear_hu_pass_after_own_discard(self, player_index: int) -> None:
        self.player_passed_hu_base.pop(player_index, None)

    def _is_open_kong_ready_after_declared(self, player: ChangshaPlayer, tile: int) -> bool:
        normal_tile = normalize_tile(tile)
        remaining = list(player.hand_tiles)
        melds = list(player.combination_tiles)
        for i, meld in enumerate(melds):
            if not meld.startswith("k"):
                continue
            try:
                meld_tile = normalize_tile(int(meld[1:]))
            except ValueError:
                continue
            if meld_tile != normal_tile:
                continue
            removed = False
            for hand_tile in list(remaining):
                if normalize_tile(hand_tile) == normal_tile:
                    remaining.remove(hand_tile)
                    removed = True
                    break
            if not removed:
                return False
            melds[i] = f"g{normal_tile}"
            return bool(self.calculation_service.Changsha_tingpai_check(remaining, melds))

        for _ in range(4):
            removed = False
            for hand_tile in list(remaining):
                if normalize_tile(hand_tile) == normal_tile:
                    remaining.remove(hand_tile)
                    removed = True
                    break
            if not removed:
                return False
        melds.append(f"G{normal_tile}")
        return bool(self.calculation_service.Changsha_tingpai_check(remaining, melds))

    def prepare_gang_replacement(self, count: int, forced_discard: bool = False) -> None:
        self.pending_gang_replacement_count = max(0, int(count))
        self.pending_gang_forced_discard = forced_discard and self.pending_gang_replacement_count > 0
        self.forced_cut_tile = None
        self.forced_cut_tiles = []
        self.pending_gang_replacement_hu_player_index = None
        self.pending_gang_replacement_hu_tile = None
        self.pending_gang_replacement_hu_hand = None
        self.current_claim_cut_tile = None

    def _remember_gang_replacement_hu_hand(self, player_index: int, pre_draw_hand_tiles: List[int], hu_tile: Any) -> None:
        hu_tiles = list(hu_tile) if isinstance(hu_tile, list) else [hu_tile]
        self.pending_gang_replacement_hu_player_index = player_index
        self.pending_gang_replacement_hu_tile = hu_tiles[-1] if hu_tiles else None
        self.pending_gang_replacement_hu_hand = list(pre_draw_hand_tiles) + hu_tiles

    def _collect_gang_replacement_hu_result(
        self,
        player_index: int,
        pre_draw_hand_tiles: List[int],
        deal_tiles: List[int],
        pre_draw_waiting_tiles,
    ) -> bool:
        """逐张校验开杠补牌，手牌牌型去重，并按可胡张数累计杠上开花。"""
        player = self._player_by_index(player_index) if hasattr(self, "_player_by_index") else self.player_list[player_index]
        full_hand_tiles = player.hand_tiles
        original_result_dict = dict(getattr(self, "result_dict", {}))
        winning_tiles = []
        aggregate_fans = []
        seen_hand_fans = set()

        try:
            for candidate_deal_tile in deal_tiles:
                if candidate_deal_tile not in pre_draw_waiting_tiles:
                    continue
                player.hand_tiles = list(pre_draw_hand_tiles) + [candidate_deal_tile]
                candidate_action_dict = {0: [], 1: [], 2: [], 3: []}
                self.result_dict = dict(original_result_dict)
                check_hepai(self, candidate_action_dict, candidate_deal_tile, player_index, "handgot", False, True)
                if "hu_self" not in candidate_action_dict.get(player_index, []):
                    continue
                result = self.result_dict.get("hu_self")
                if not result or result[0] < 1:
                    continue
                winning_tiles.append(candidate_deal_tile)
                for fan_name in result[1] or []:
                    if fan_name == "杠上开花" or fan_name in seen_hand_fans:
                        continue
                    seen_hand_fans.add(fan_name)
                    aggregate_fans.append(fan_name)
        finally:
            player.hand_tiles = full_hand_tiles
            self.result_dict = dict(original_result_dict)

        if not winning_tiles:
            return False

        aggregate_fans.extend(["杠上开花"] * len(winning_tiles))
        base_calculator = getattr(self.calculation_service, "Changsha_base_from_fans", None)
        score = base_calculator(aggregate_fans) if base_calculator is not None else 0
        self.result_dict["hu_self"] = (score, aggregate_fans)
        self._remember_gang_replacement_hu_hand(player_index, pre_draw_hand_tiles, winning_tiles)
        return True

    def _hepai_display_hand(self, player_index: int) -> List[int]:
        if (
            self.hu_class == "hu_self"
            and getattr(self, "pending_gang_replacement_hu_player_index", None) == player_index
            and getattr(self, "pending_gang_replacement_hu_hand", None)
        ):
            return list(self.pending_gang_replacement_hu_hand)
        return self.player_list[player_index].hand_tiles

    def next_status_after_claim_window(self) -> str:
        if self.pending_gang_forced_discard and self.pending_gang_replacement_count > 0:
            self.forced_cut_tile = None
            self.forced_cut_tiles = []
            self.pending_gang_replacement_hu_player_index = None
            self.pending_gang_replacement_hu_tile = None
            self.pending_gang_replacement_hu_hand = None
            self.current_claim_cut_tile = None
            return "deal_card_after_gang"
        self.pending_gang_replacement_count = 0
        self.pending_gang_forced_discard = False
        self.forced_cut_tile = None
        self.forced_cut_tiles = []
        self.pending_gang_replacement_hu_player_index = None
        self.pending_gang_replacement_hu_tile = None
        self.pending_gang_replacement_hu_hand = None
        self.current_claim_cut_tile = None
        if getattr(self, "tiles_list", None) == []:
            return "END"
        return "deal_card"

    async def force_cut_gang_replacement_tiles(self) -> None:
        forced_tiles = list(getattr(self, "forced_cut_tiles", []) or [])
        forced_tile = getattr(self, "forced_cut_tile", None)
        if not forced_tiles and forced_tile is not None:
            forced_tiles = [forced_tile]
        if not forced_tiles:
            self.pending_gang_forced_discard = False
            self.pending_gang_replacement_hu_player_index = None
            self.pending_gang_replacement_hu_tile = None
            self.pending_gang_replacement_hu_hand = None
            self.game_status = "deal_card"
            return

        player = self.player_list[self.current_player_index]
        for tile in forced_tiles:
            if tile in player.hand_tiles:
                player.hand_tiles.remove(tile)
            else:
                logger.warning(
                    "Missing forced gang replacement tile in hand: player=%s tile=%s hand=%s",
                    self.current_player_index,
                    tile,
                    player.hand_tiles,
                )
        clear_draw_slot(player)
        self.forced_cut_tile = None
        self.forced_cut_tiles = []
        self.pending_gang_replacement_hu_player_index = None
        self.pending_gang_replacement_hu_tile = None
        self.pending_gang_replacement_hu_hand = None

        for tile in forced_tiles:
            player.discard_tiles.append(tile)
            player_action_record_cut(self, cut_tile=tile, is_moqie=True)

        if hasattr(self, "clear_hu_pass_after_own_discard"):
            self.clear_hu_pass_after_own_discard(self.current_player_index)
        if self.current_player_index == 0:
            self.xunmu += 1
        refresh_waiting_tiles(self, self.current_player_index)
        pre_action_dict = check_action_after_batch_gang_forced_cut(self, forced_tiles)
        self.last_draw_was_gang = False
        begin_claim_protection_interval(self, pre_action_dict, self.current_player_index)
        broadcast_cut_tile = getattr(self, "current_claim_cut_tile", None) or forced_tiles[-1]
        await self.broadcast_do_action(
            action_list=["cut"],
            action_player=self.current_player_index,
            cut_tile=broadcast_cut_tile,
            cut_tiles=forced_tiles if len(forced_tiles) > 1 else None,
            cut_class=True,
        )
        self.action_dict = pre_action_dict
        if any(self.action_dict[i] for i in self.action_dict):
            self.game_status = "waiting_action_after_cut"
        else:
            self.game_status = self.next_status_after_claim_window()

    def _score_changsha_win(self, winner: int, fan_list: List[str], is_zimo: bool, discarder: int = None):
        birds = self._draw_changsha_birds(self.bird_count)
        if not birds and self.bird_count > 0 and self._is_sea_bottom_win(fan_list):
            sea_bottom_bird = self._sea_bottom_bird_tile(winner)
            if sea_bottom_bird is not None:
                birds = [sea_bottom_bird]
        bird_origin = self._changsha_bird_origin(winner)
        bird_seats = [self._changsha_bird_seat(tile, bird_origin) for tile in birds]
        payers = [p.player_index for p in self.player_list if p.player_index != winner] if is_zimo else [discarder]
        total_win = 0
        total_base = 0
        payer_details = []

        for payer in payers:
            dealer_related = winner == 0 or payer == 0
            base = self._changsha_base_from_fans(fan_list, dealer_related)
            hit_count = sum(1 for seat in bird_seats if seat in (winner, payer))
            multiplier = 2 ** hit_count
            payment = base * multiplier
            self._player_by_index(winner).score += payment
            self._player_by_index(payer).score -= payment
            total_win += payment
            total_base += base
            payer_details.append({
                "payer": payer,
                "base": base,
                "hit_count": hit_count,
                "multiplier": multiplier,
                "payment": payment,
            })

        fan_display = list(fan_list or [])
        if birds:
            bird_text = "鸟牌:" + ",".join(self._format_changsha_bird_tile(tile) for tile in birds)
            active_seats = {winner, *payers}
            hit_birds = [
                self._format_changsha_bird_tile(tile)
                for tile, seat in zip(birds, bird_seats)
                if seat in active_seats
            ]
            hit_text = "中鸟:" + (",".join(hit_birds) if hit_birds else "无")
            multiplier_text = "扎鸟倍数:x" + str(max((item["multiplier"] for item in payer_details), default=1))
            fan_display.extend([bird_text, hit_text, multiplier_text])

        return {
            "actual_hu_score": total_win,
            "base_score": total_base,
            "birds": birds,
            "bird_seats": bird_seats,
            "fan_display": fan_display,
            "payer_details": payer_details,
        }

    async def game_loop_changsha(self):

        if not self.Debug:
            # 生成完整游戏随机种子
            user_seed = self.room_random_seed if self.room_random_seed else None
            self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(user_seed)
            capture_player_entry_order(self)
            # 房间初始化 打乱玩家顺序（基于主种子）
            # 测试时不打乱玩家顺序
            # 使用随机种子创建独立的随机数生成器来打乱玩家顺序
            rng = random.Random(self.master_seed)
            rng.shuffle(self.player_list)

            # 根据打乱的玩家顺序设置玩家索引
            for index, player in enumerate(self.player_list):
                player.player_index = index
                player.original_player_index = index

        else:
            # 测试
            self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system()
            capture_player_entry_order(self)
            # 测试时不打乱玩家顺序
            for index, player in enumerate(self.player_list):
                player.player_index = index
                player.original_player_index = index

        # 牌谱记录游戏头
        init_game_record(self)
        # 牌谱/观战用：子规则与起和限制写入 game_title，客户端据此做番表显示
        self.game_record["game_title"]["sub_rule"] = self.sub_rule
        self.game_record["game_title"]["hepai_limit"] = self.hepai_limit
        if self.match_tier is not None:
            self.game_record["game_title"]["match_tier"] = self.match_tier
        if self.event_id is not None:
            self.game_record["game_title"]["event_id"] = self.event_id
        # 游戏主循环
        while self.current_round <= self.max_round * 4:

            self._reset_changsha_round_state()
            init_changsha_tiles(self)  # 初始化牌山和手牌
            self.backward_tiles_list_type = "double" # 重置倒序摸牌状态
            self.last_draw_was_gang = False
            self.current_player_index = 0 # 初始玩家索引
            self.dihe_possible = True # 地和标志：庄家首次切牌且未暗杠时，被他家荣和即为地和
            self._detect_initial_hu_types()

            # 广播游戏开始
            await self.broadcast_game_start()

            # 牌谱记录对局头
            init_game_round(self)

            if self.initial_hu_types:
                await self._resolve_initial_hu_choices()

            # 初始行为（长沙规则：庄家开局已为14张）
            self.game_status = "waiting_hand_action"  # 初始行动
            self.refresh_waiting_tiles(self.current_player_index, is_first_action=True) # 用庄家前13张手牌计算听牌
            logger.info(f"第一位行动玩家{self.current_player_index}的手牌等待牌为{self.player_list[self.current_player_index].waiting_tiles}")
            self.action_dict = check_action_hand_action(self,self.current_player_index,is_first_action=True) # 允许可执行的手牌操作（天和检测）
            await self.broadcast_ask_hand_action() # 广播手牌操作
            await self.wait_action() # 等待手牌操作

            # 游戏主循环
            while self.game_status != "END":
                await vote_checkpoint(self)
                match self.game_status:

                    # 普通摸牌操作：切换到下一个玩家进行摸牌
                    case "deal_card": # 无人碰杠和后发牌历时行为
                        if self.tiles_list == []: # 牌山已空
                            self.game_status = "END" # 结束游戏
                            break
                        if len(self.tiles_list) == 1:
                            self.sea_bottom_candidates = self._sea_bottom_waiting_candidates()
                            if not self._prepare_next_sea_bottom_choice():
                                self.game_status = "END"
                            continue
                        else:
                            self.next_current_index() # 切换到下一个玩家
                        self.last_draw_was_gang = False
                        self.refresh_waiting_tiles(self.current_player_index) # 摸牌前更新听牌
                        self.player_list[self.current_player_index].get_tile(self.tiles_list) # 摸牌
                        # 牌谱记录摸牌
                        player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],deal_type = "d")
                        # 广播摸牌操作
                        await self.broadcast_do_action(
                            action_list = ["deal_tile"],
                            action_player = self.current_player_index,
                            deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],
                        )
                        self.action_dict = check_action_hand_action(self,self.current_player_index) # 允许可执行的手牌操作
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态

                    # 杠后摸牌操作：当前玩家进行摸牌
                    case "deal_card_after_gang": # 杠后发牌历时行为
                        if self.tiles_list == []:
                            self.game_status = "END"
                            break
                        if self.pending_gang_replacement_count <= 0:
                            self.prepare_gang_replacement(1, False)
                        self.dihe_possible = False # 庄家暗杠/加杠/明杠后，地和不再可能
                        self.last_draw_was_gang = True
                        self.refresh_waiting_tiles(self.current_player_index)
                        player = self.player_list[self.current_player_index]
                        pre_draw_waiting_tiles = set(player.waiting_tiles)
                        pre_draw_hand_tiles = list(player.hand_tiles)
                        deal_tiles = []
                        draw_count = max(1, self.pending_gang_replacement_count)
                        for _ in range(draw_count):
                            if self.tiles_list == []:
                                break
                            deal_item = self.player_list[self.current_player_index].get_gang_tile(self.tiles_list, self)
                            deal_tiles.append(deal_item)
                            player_action_record_deal(self,deal_tile = deal_item,deal_type = "gd")
                        if not deal_tiles:
                            self.game_status = "END"
                            break
                        deal_tile = deal_tiles[-1]
                        self.pending_gang_replacement_count = 0
                        await self.broadcast_do_action(
                            action_list = ["deal_gang_tile"],
                            action_player = self.current_player_index,
                            deal_tile = deal_tile,
                            deal_tiles = deal_tiles,
                        )
                        self.action_dict = check_action_hand_action(self,self.current_player_index,is_get_gang_tile=True)
                        self.action_dict[self.current_player_index] = [
                            action for action in self.action_dict.get(self.current_player_index, []) if action != "hu_self"
                        ]
                        if self._collect_gang_replacement_hu_result(
                            self.current_player_index,
                            pre_draw_hand_tiles,
                            deal_tiles,
                            pre_draw_waiting_tiles,
                        ):
                            self.action_dict[self.current_player_index].append("hu_self")
                        if self.pending_gang_forced_discard:
                            self.forced_cut_tile = deal_tile
                            self.forced_cut_tiles = list(deal_tiles)
                            actions = self.action_dict.get(self.current_player_index, [])
                            self.action_dict[self.current_player_index] = filter_open_kong_replacement_actions(actions)
                            if self.action_dict[self.current_player_index]:
                                self.action_dict[self.current_player_index].append("pass")
                                self.game_status = "waiting_hand_action"
                            else:
                                await self.force_cut_gang_replacement_tiles()
                            continue
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态

                    # 等待手牌操作：
                    case "waiting_hand_action": # 摸牌,加杠,暗杠,补花后行为
                        await self.broadcast_ask_hand_action() # 广播手牌操作
                        await self.wait_action() # 等待手牌操作

                    # 等待鸣牌操作：
                    case "waiting_action_after_cut": # 出牌后询问碰杠和行为
                        await self.broadcast_ask_other_action() # 广播是否碰杠和
                        await self.wait_action() # 等待碰杠和操作

                    case "waiting_sea_bottom":
                        await self.broadcast_ask_hand_action()
                        await self.wait_action()

                    # 等待加杠操作：
                    case "waiting_action_qianggang": # 加杠后询问胡牌行为
                        await self.broadcast_ask_other_action() # 广播是否胡牌
                        await self.wait_action() # 等待抢杠操作

                    # 等待手牌操作（碰后：切牌 / 暗杠 / 加杠）
                    case "onlycut_after_action":
                        self.action_dict = check_only_cut(self, self.current_player_index)
                        self.game_status = "waiting_hand_action"

                    # 如果没有匹配到
                    case _:
                        logger.error(f"没有匹配到游戏状态: {self.game_status}")

            # 卡牌摸完 或者有人和牌
            hu_score = None
            hu_fan = None
            hepai_player_index = None

            # 记录结算前的分数（用于计算本局分数变化）
            scores_before = {player.original_player_index: player.score for player in self.player_list}


            # 荣和
            if self.hu_class in ["hu_self","hu_first","hu_second","hu_third"]:
                if self.hu_class == "hu_self":
                    hu_score, hu_fan = self.result_dict["hu_self"] # 获取和牌分数和番数
                    hepai_player_index = self.current_player_index # 和牌玩家等于当前玩家
                    score_info = self._score_changsha_win(hepai_player_index, hu_fan, True)
                    actual_hu_score = score_info["actual_hu_score"]
                    hu_fan = score_info["fan_display"]

                    # 记录玩家数据
                    self.player_list[hepai_player_index].record_counter.zimo_times += 1 # 增加自摸次数
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan) # 增加和牌番种
                    self.player_list[hepai_player_index].record_counter.win_score += actual_hu_score # 增加和牌总分数
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu # 增加和牌总巡目

                # 荣和他家
                else:
                    # 荣和上家
                    if self.hu_class == "hu_first":
                        hu_score, hu_fan = self.result_dict["hu_first"]
                        hepai_player_index = next_current_num(self.current_player_index) # 获取当前玩家的下家索引

                    # 荣和对家
                    elif self.hu_class == "hu_second":
                        hu_score, hu_fan = self.result_dict["hu_second"]
                        hepai_player_index = next_current_num(self.current_player_index)
                        hepai_player_index = next_current_num(hepai_player_index) # 获取下下家索引

                    # 荣和下家
                    else: # self.hu_class == "hu_third":
                        hu_score, hu_fan = self.result_dict["hu_third"]
                        hepai_player_index = next_current_num(self.current_player_index)
                        hepai_player_index = next_current_num(hepai_player_index)
                        hepai_player_index = next_current_num(hepai_player_index) # 获取下下下家索引

                    logger.info(f"和牌玩家索引{hepai_player_index}")
                    score_info = self._score_changsha_win(
                        hepai_player_index,
                        hu_fan,
                        False,
                        self.current_player_index
                    )
                    actual_hu_score = score_info["actual_hu_score"]
                    hu_fan = score_info["fan_display"]

                    # 记录玩家数据
                    self.player_list[hepai_player_index].record_counter.dianhe_times += 1 # 增加点和次数
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan) # 增加和牌番种
                    self.player_list[hepai_player_index].record_counter.win_score += actual_hu_score # 增加和牌总分数
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu # 增加和牌总巡目

                    self.player_list[self.current_player_index].record_counter.fangchong_times += 1 # 增加放铳次数
                    self.player_list[self.current_player_index].record_counter.fangchong_score += actual_hu_score # 增加放铳总番数

                self.result_dict = {}

                # 广播和牌结算结果
                # 获取所有人分数
                player_to_score = {}
                for i in self.player_list:
                    player_to_score[i.player_index] = i.score
                # 获取和牌显示中的 手牌 花牌 组合掩码
                he_hand = self._hepai_display_hand(hepai_player_index)
                he_huapai = self.player_list[hepai_player_index].huapai_list
                he_combination_mask = self.player_list[hepai_player_index].combination_mask

                score_changes_dict = build_score_changes_dict(self.player_list, scores_before)

                # 广播和牌结算结果（使用实际和牌分数，而不是番数）
                await broadcast_result(self,
                                       hepai_player_index = hepai_player_index, # 和牌玩家索引
                                       player_to_score = player_to_score, # 所有玩家分数
                                       hu_score = actual_hu_score, # 和牌分数（整数）
                                       hu_fan = hu_fan, # 和牌番种
                                       hu_class = self.hu_class, # 和牌类别
                                       hepai_player_hand = he_hand, # 和牌玩家手牌
                                       hepai_player_huapai = he_huapai, # 和牌玩家花牌列表
                                       hepai_player_combination_mask = he_combination_mask, # 和牌玩家组合掩码
                                       score_changes = score_changes_dict,
                                       next_status = (
                                           "match_end"
                                           if self.current_round >= self.max_round * 4
                                           else "round_end_by_ready"
                                       ),
                                       )

            # 广播流局结算结果
            else:
                self.hu_class = "liuju"
                liuju_score_changes = build_score_changes_dict(self.player_list, scores_before)
                await broadcast_result(self,
                                       hepai_player_index = None, # 和牌玩家索引
                                       player_to_score = None, # 所有玩家分数
                                       hu_score = hu_score, # 和牌分数
                                       hu_fan = None, # 和牌番种
                                       hu_class = self.hu_class, # 和牌类别(流局)
                                       hepai_player_hand = None, # 和牌玩家手牌
                                       hepai_player_huapai = None, # 和牌玩家花牌列表
                                       hepai_player_combination_mask = None, # 和牌玩家组合掩码
                                       score_changes = liuju_score_changes,
                                       next_status = (
                                           "match_end"
                                           if self.current_round >= self.max_round * 4
                                           else "round_end_by_ready"
                                       ),
                                       )

            record_fulu_rounds_for_players(self.player_list)

            # 记录分数变更到每个玩家的 score_history
            # 计算每个玩家本局的分数变化并记录
            for player in self.player_list:
                score_change = player.score - scores_before[player.original_player_index]
                # 格式化为 +00、-00 或 0
                if score_change > 0:
                    score_change_str = f"+{score_change:02d}"
                elif score_change < 0:
                    score_change_str = f"-{abs(score_change):02d}"  # 负数如 -05
                else:
                    score_change_str = "0"
                player.score_history.append(score_change_str)

            # 牌谱记录本局各玩家分数变化 [p0, p1, p2, p3] 按 original_player_index 排列
            score_changes = build_score_changes_by_seat(self.player_list, scores_before)

            # 牌谱记录和牌/流局 + end 标记
            if self.hu_class in ["hu_self","hu_first","hu_second","hu_third"]:
                player_action_record_hu(self, hu_class=self.hu_class, hu_score=hu_score,
                                        hu_fan=hu_fan, hepai_player_index=hepai_player_index,
                                        score_changes=score_changes)
            else:
                player_action_record_liuju(self)
            player_action_record_round_end(self)

            # 根据和牌类型处理等待逻辑（整场最后一局跳过 waiting_ready，由客户端 match_end 收尾）
            self.next_status = (
                "match_end"
                if self.current_round >= self.max_round * 4
                else "round_end_by_ready"
            )
            if self.next_status != "match_end":
                if self.hu_class == "liuju":
                    await asyncio.sleep(liuju_ready_wait_seconds())
                else:
                    fan_count = len(hu_fan) if hu_fan else 0
                    wait_time = hu_result_ready_wait_seconds(fan_count)
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

            if self.current_round < self.max_round * 4:
                # 开启下一局的准备工作
                if self.hu_class in ["hu_self","hu_first","hu_second","hu_third"] and hepai_player_index is not None:
                    self._make_player_next_dealer(hepai_player_index)
                    next_game_round_changsha_switchseat(self, keep_dealer_seat=True)
                elif self.hu_class == "liuju" and self.sea_bottom_player_index is not None:
                    self._make_player_next_dealer(self.sea_bottom_player_index)
                    next_game_round_changsha_switchseat(self, keep_dealer_seat=True)
                else:
                    next_game_round_changsha_switchseat(self)
                logger.info(f"重新开始下一局")
            else:
                logger.info("最后一局结束，不再推进局数")
                break
            # ↑ 重新开始下一局循环

        # 游戏结束所有局数
        logger.info("游戏结束")
        end_game_record(self)
        logger.info(f"最终游戏记录: {self.game_record}")

        # 终局排名：同分按开局原始风位（东0→南1→西2→北3）拆分
        assign_strict_final_ranks(self.player_list)

        # 发送游戏结算信息
        await self.broadcast_game_end() # 广播游戏结束信息

        # 对局结束后：一次性下发完整牌谱给观战者，并结束观战增量服务
        if hasattr(self, 'spectator_manager'):
            await self.spectator_manager.send_final_record_and_close()

        # 存储游戏牌谱
        match_type = f"{self.max_round}/4"
        game_id = self.db_manager.store_changsha_game_record(
            self.game_record,
            self.player_list,
            self.room_type,
            match_type
        )

        has_ai_player = any(player.user_id <= 10 for player in self.player_list)
        if self.room_type == "events":
            logger.info(f'比赛场对局，仅保存牌谱，跳过统计数据保存，game_id: {game_id}')
        elif has_ai_player:
            logger.info(f'游戏记录包含AI玩家，跳过统计数据保存，game_id: {game_id}')
        elif game_id and hasattr(self.db_manager, "store_changsha_game_stats"):
            total_rounds = len(self.game_record.get("game_round", {}))
            self.db_manager.store_changsha_game_stats(
                game_id, self.player_list, self.room_type, self.max_round, total_rounds
            )

        # 结束游戏生命周期：使用统一的清理方法
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)

        if self.room_type == "match":
            await self.game_server.room_manager.destroy_room(self.room_id)
        else:
            await self.game_server.room_manager.finish_custom_game_room(self.room_id)
        logger.info(f"游戏实例已清理，room_id: {self.room_id},goodbye!")


    # ========== 观战系统方法（委托给观战管理器） ==========

    async def add_spectator(self, user_id: int, connection: Any):
        """添加观战玩家"""
        await self.spectator_manager.add_spectator(user_id, connection)

    async def remove_spectator(self, user_id: int):
        """移除观战玩家"""
        await self.spectator_manager.remove_spectator(user_id)


# 挂载广播方法于ChangshaGameState实例
ChangshaGameState.wait_action = wait_action
ChangshaGameState.broadcast_game_start = broadcast_game_start
ChangshaGameState.broadcast_ask_hand_action = broadcast_ask_hand_action
ChangshaGameState.broadcast_ask_other_action = broadcast_ask_other_action
ChangshaGameState.broadcast_do_action = broadcast_do_action
ChangshaGameState.broadcast_result = broadcast_result
ChangshaGameState.broadcast_game_end = broadcast_game_end
ChangshaGameState.broadcast_switch_seat = broadcast_switch_seat
ChangshaGameState.broadcast_refresh_player_tag_list = broadcast_refresh_player_tag_list
ChangshaGameState.reconnected_send_pending_ask = reconnected_send_pending_ask

# 挂载功能函数于ChangshaGameState实例
ChangshaGameState.next_current_index = next_current_index
ChangshaGameState.refresh_waiting_tiles = refresh_waiting_tiles
ChangshaGameState.check_action_after_gang_forced_cut = check_action_after_gang_forced_cut
ChangshaGameState.check_action_after_batch_gang_forced_cut = check_action_after_batch_gang_forced_cut
