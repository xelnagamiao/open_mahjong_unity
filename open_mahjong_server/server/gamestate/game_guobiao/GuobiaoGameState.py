import random
import asyncio
from typing import Any, Dict, List, Optional
import time
import logging
from .action_check import check_action_after_cut,check_action_jiagang,check_action_buhua,check_action_hand_action,refresh_waiting_tiles
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
from ..public.logic_common import get_index_relative_position, next_current_index, next_current_num, back_current_num, assign_competition_final_ranks
from .init_tiles import init_guobiao_tiles
from ..public.next_game_round import next_game_round_switchseat
from ..public.round_end_timing import liuju_ready_wait_seconds
from ..public.ready_phase import run_hu_result_ready_phase as run_synced_hu_ready_phase
from ..public.spectator_rules import too_many_ai_for_spectator
from ..public.game_record_manager import init_game_record,init_game_round,player_action_record_buhua,player_action_record_deal,player_action_record_cut,player_action_record_angang,player_action_record_jiagang,player_action_record_chipenggang,player_action_record_hu,player_action_record_liuju,player_action_record_round_end,end_game_record,build_score_changes_by_seat,build_score_changes_dict,capture_player_entry_order
from ...game_calculation.game_calculation_service import GameCalculationService
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
class GuobiaoPlayer:
    def __init__(self, user_id: int, username: str, tiles: list, remaining_time: int):
        self.user_id = user_id                        # 用户UID
        self.username = username                      # 玩家名（用于显示）
        self.is_bot = True if user_id <= 10 else False # 是否是机器人
        self.hand_tiles = tiles                       # 手牌
        self.huapai_list = []                         # 花牌列表
        self.discard_tiles = []                       # 弃牌
        self.discard_origin_tiles = []                # 理论弃牌
        self.combination_tiles = []                   # 组合牌 g明杠 k明刻 s吃顺 G暗杠
        # combination_mask组合牌掩码 0代表竖 1代表横 2代表暗面 3代表上侧(加杠) 4代表空 因为普通的存储方式会造成掉线以后吃牌形状丢失 所以使用掩码存储
        # [1,13,0,11,0,12] = 吃上家 312m s12
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
        self.round_number_history = []                 # 每行分数对应的局号(current_round)，与日麻对齐：错和会出现同一局号多行

        self.title_used = 0 # 使用的称号ID
        self.profile_used = 0 # 使用的头像ID
        self.character_used = 0 # 使用的角色ID
        self.voice_used = 0 # 使用的音色ID
        self.has_draw_slot = False  # 本巡是否刚摸入一张（吃碰杠后为 False）

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

    # 游戏进程类
class GuobiaoGameState:
    # GuobiaoGameState负责一个国标麻将对局进程，init属性包含游戏房间状态 player_list 包含玩家数据
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
        # 创建玩家列表 包含GuobiaoPlayer类
        self.player_list: List[GuobiaoPlayer] = []
        player_settings = room_data.get("player_settings", {})
        for user_id in room_data["player_list"]:
            player_setting = player_settings.get(user_id, {})
            if user_id == 0:
                username = "麻雀罗伯特"
            elif user_id == 2:
                username = "牌效罗伯特"
            else:
                username = player_setting.get("username", f"用户{user_id}")
            player = GuobiaoPlayer(user_id, username, [], room_data["round_timer"])
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
        # room_rule 表示具体规则（guobiao等），room_type 表示房间类型（custom/match等） sub_rule 表示子规则（guobiao/standard等）
        self.room_rule = room_data["room_rule"]
        self.room_type = room_data["room_type"]
        self.sub_rule = room_data.get("sub_rule", "guobiao/standard") # 子规则

        self.room_random_seed = room_data.get("random_seed", 0) # 随机种子（默认为0）
        self.open_cuohe = room_data.get("open_cuohe", False) # 是否开启错和（默认为False）
        self.show_moqie_hint = room_data.get("show_moqie_hint", False) # 是否显示手摸切灰显（默认为False）
        self.hepai_limit = room_data.get("hepai_limit", 8) # 起和番限制（默认8）
        self.tactical_call = room_data.get("tactical_call", False) # 战术鸣牌：开启后切牌/抢杠询问时附加 2 秒申请-打断阶段
        
        self.tourist_limit = room_data.get("tourist_limit", False) # 游客限制
        self.allow_spectator_config = room_data.get("allow_spectator", True) # 允许观战配置
        self.match_queue_type = room_data.get("match_queue_type", None) # 排位匹配队列类型
        
        self.isPlayerSetRandomSeed = False # 是否玩家设置了随机种子

        # 初始化游戏状态
        self.tiles_list = [] # 牌堆
        self.current_player_index = 0 # 目前轮到的玩家
        self.xunmu = 1 # 巡目
        self.master_seed: int = 0  # 主种子
        self.commitment: int = 0 # 承诺值
        self.salt = "" # 盐字符串
        self.round_random_seed = 0 # 局内随机种子
        self.game_status = "waiting"  # waiting, playing, finished
        self.server_action_tick = 0 # 操作帧
        self.player_action_tick = 0 # 玩家操作帧
        self.current_round = 1 # 游戏进程小局号(可能连庄)
        self.round_index = 1 ###### 实际局数索引(连续递增，用于日麻连庄情况等的内部计算 国标不使用)
        self.result_dict = {} # 结算结果 {hu_first:(int,list[str]),hu_second:(int,list[str]),hu_third:(int,list[str])}
        self.hu_class = None # 和牌玩家索引
        self.jiagang_tile = None # 抢杠牌 每次加杠时存储 waiting_jiagang_action 以后删除
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
        "chi_left": 1, "chi_mid": 1, "chi_right": 1,  # 吃牌优先级 次低优先级
        "ready": 0,  # 准备操作优先级 最低优先级
        "pass": 0,"buhua":0,"cut":0,"angang":0,"jiagang":0,"deal_tile":0,"deal_gang_tile":0,"deal_buhua_tile":0 # 其他优先级 最低优先级
        }

        
        self.backward_tiles_list_type = "double"

        # 如果您在管理自己规则内的分支，请不要将Debug = True 的配置上传到公共代码仓库 这一项单元配置不会得到review和测试
        self.Debug = False

        # 观战系统相关：含 3 个及以上 AI(uid<=10) 或配置禁用的对局禁用观战
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
        for p in self.player_list:
            if p.user_id == user_id:
                if "offline" not in p.tag_list:
                    p.tag_list.append("offline")
                    await broadcast_refresh_player_tag_list(self)
                break
        
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
                    
                    # 构建游戏信息
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
                        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed,
                        'players_info': []
                    }
                    from ..public.game_record_manager import build_player_entry_order_fields
                    base_game_info.update(build_player_entry_order_fields(self))
                    
                    # 构建玩家信息列表
                    from .combination_mask_view import get_combination_fields_for_viewer
                    reconnect_player_index = p.player_index
                    for player in self.player_list:
                        combo_tiles, combo_masks = get_combination_fields_for_viewer(player, reconnect_player_index)
                        player_info = {
                            'user_id': player.user_id,
                            'username': player.username,
                            'hand_tiles_count': len(player.hand_tiles),
                            'hand_tiles': player.hand_tiles if player.user_id == user_id else None,  # 只有自己可见手牌
                            'discard_tiles': player.discard_tiles,
                            'discard_origin_tiles': player.discard_origin_tiles,
                            'combination_tiles': combo_tiles,
                            'combination_mask': combo_masks,
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
                    
                    # 与 broadcast_game_start 保持一致：手牌通过 players_info[].hand_tiles 传递
                    game_info = GameInfo(
                        **base_game_info,
                        self_hand_tiles=None
                    )
                    
                    response = Response(
                        type="gamestate/guobiao/game_start",
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
        - 负责运行实际的 game_loop_chinese
        - 捕获未处理异常并进行统一日志和清理
        """
        try:
            await self.game_loop_chinese()
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

    async def game_loop_chinese(self):

        if not self.Debug:
            user_seed = self.room_random_seed if self.room_random_seed else None
            
            self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(user_seed)
            
            capture_player_entry_order(self)
            # 房间初始化 打乱玩家顺序（基于主种子）
            # 测试时不打乱玩家顺序
            # 使用随机种子创建独立的随机数生成器来打乱玩家顺序
            rng = random.Random(self.master_seed)
            rng.shuffle(self.player_list)

            # 根据打乱的玩家顺序设置玩家索引
            for index, player in enumerate[GuobiaoPlayer](self.player_list):
                player.player_index = index
                player.original_player_index = index

        else:
            # 测试
            self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system()
            capture_player_entry_order(self)
            # 测试时不打乱玩家顺序
            for index, player in enumerate[GuobiaoPlayer](self.player_list):
                player.player_index = index
                player.original_player_index = index

        # 牌谱记录游戏头
        init_game_record(self)
        # 牌谱/观战用：子规则与起和限制写入 game_title，客户端据此做番表显示
        self.game_record["game_title"]["sub_rule"] = self.sub_rule
        self.game_record["game_title"]["hepai_limit"] = self.hepai_limit
        # 游戏主循环
        while self.current_round <= self.max_round * 4:

            # 换位：仅在本局设置下会实际进行该风圈对局时广播（半庄不播西/北圈换位，末局后不推进局数）
            _switch_min_max_round = {5: 2, 9: 3, 13: 4}
            if self.current_round in _switch_min_max_round and self.max_round >= _switch_min_max_round[self.current_round]:
                await broadcast_switch_seat(self)
                await asyncio.sleep(4)

            # 记录结算前的分数（用于计算本局分数变化）
            scores_before = {player.original_player_index: player.score for player in self.player_list}

            init_guobiao_tiles(self) # 初始化牌山和手牌

            # 广播游戏开始
            await self.broadcast_game_start()
            
            # 牌谱记录对局头
            init_game_round(self)

            # 遍历每个玩家,直到玩家选择pass或没有新的补花行为
            self.game_status = "waiting_buhua_round"
            for i in range(0,4): # 按索引顺序遍历
                self.current_player_index = i
                action_anymore = True
                while action_anymore: # 如果单个玩家可以补花
                    self.action_dict = check_action_buhua(self, i)
                    # 检测是否可以补花 如果可以补花
                    if self.action_dict[i] != []: 
                        await self.broadcast_ask_hand_action() # 广播补花信息
                        # 如果玩家选择补花 则广播一次摸牌信息
                        if await self.wait_action():
                            max_tile = max(self.player_list[self.current_player_index].hand_tiles) # 获取最大牌
                            self.player_list[self.current_player_index].hand_tiles.remove(max_tile) # 从手牌中移除最大牌
                            self.player_list[self.current_player_index].huapai_list.append(max_tile) # 将最大牌加入花牌列表
                            self.player_list[self.current_player_index].get_gang_tile(self.tiles_list, self) # 补花后从牌山末尾倒序摸牌（与杠摸牌/局中补花一致）
                            # 牌谱记录补花
                            player_action_record_buhua(self,max_tile = max_tile,action_player = self.current_player_index)
                            # 牌谱记录摸牌
                            player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],deal_type = "bd")
                            # 广播补花操作（使用 deal_buhua_tile 作为补花摸牌标识）
                            await self.broadcast_do_action(
                                action_list = ["buhua","deal_buhua_tile"],
                                action_player = self.current_player_index,
                                buhua_tile = max_tile,
                                deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],
                            )
                        # 如果玩家选择pass 则下一轮循环
                        else:
                            action_anymore = False
                    # 如果不能补花 则下一轮循环
                    else:
                        action_anymore = False

            # 初始行为
            self.game_status = "waiting_hand_action" # 初始行动
            self.current_player_index = 0 # 初始玩家索引

            self.refresh_waiting_tiles(self.current_player_index, is_first_action=True) # 检查手牌等待牌
            logger.info(f"第一位行动玩家{self.current_player_index}的手牌等待牌为{self.player_list[self.current_player_index].waiting_tiles}")
            self.action_dict = check_action_hand_action(self,self.current_player_index,is_first_action=True) # 允许可执行的手牌操作
            await self.broadcast_ask_hand_action() # 广播手牌操作
            await self.wait_action() # 等待手牌操作

            # 游戏主循环
            while self.game_status != "END":
                match self.game_status:

                    # 普通摸牌操作：切换到下一个玩家进行摸牌
                    case "deal_card": # 无人吃碰杠和后发牌历时行为
                        if self.tiles_list == []: # 牌山已空
                            self.game_status = "END" # 结束游戏
                            break
                        self.next_current_index() # 切换到下一个玩家
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
                        self.refresh_waiting_tiles(self.current_player_index) # 摸牌前更新听牌
                        self.player_list[self.current_player_index].get_gang_tile(self.tiles_list, self) # 倒序摸牌
                        # 牌谱记录摸牌
                        player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],deal_type = "gd")
                        # 广播摸牌操作
                        await self.broadcast_do_action(
                            action_list = ["deal_gang_tile"],
                            action_player = self.current_player_index,
                            deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],
                        )
                        self.action_dict = check_action_hand_action(self,self.current_player_index,is_get_gang_tile=True) # 允许岭上
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态
                    
                    # 补花摸牌操作：当前玩家进行摸牌
                    case "deal_card_after_buhua": # 补花后发牌历时行为
                        max_tile = max(self.player_list[self.current_player_index].hand_tiles) # 获取最大牌（花牌数字永远最大）
                        self.player_list[self.current_player_index].hand_tiles.remove(max_tile) # 从手牌中移除最大牌
                        self.refresh_waiting_tiles(self.current_player_index) # 摸牌前更新听牌
                        self.player_list[self.current_player_index].get_gang_tile(self.tiles_list, self) # 倒序摸牌
                        self.player_list[self.current_player_index].huapai_list.append(max_tile) # 将最大牌加入花牌列表
                        # 牌谱记录补花
                        player_action_record_buhua(self,max_tile = max_tile,action_player = self.current_player_index)
                        # 牌谱记录摸牌
                        player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],deal_type = "bd")
                        # 广播补花操作
                        await self.broadcast_do_action(
                            action_list = ["buhua","deal_buhua_tile"],
                            action_player = self.current_player_index,
                            buhua_tile = max_tile,
                            deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],
                        )
                        self.action_dict = check_action_hand_action(self,self.current_player_index) # 允许可执行的手牌操作
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态
                        
                    # 等待手牌操作：
                    case "waiting_hand_action": # 摸牌,加杠,暗杠,补花后行为
                        await self.broadcast_ask_hand_action() # 广播手牌操作
                        await self.wait_action() # 等待手牌操作

                    # 等待鸣牌操作：
                    case "waiting_action_after_cut": # 出牌后询问吃碰杠和行为
                        await self.broadcast_ask_other_action() # 广播是否吃碰杠和
                        await self.wait_action() # 等待吃碰杠和操作

                    # 等待加杠操作：
                    case "waiting_action_qianggang": # 加杠后询问胡牌行为
                        await self.broadcast_ask_other_action() # 广播是否胡牌
                        await self.wait_action() # 等待抢杠操作

                    # 等待手牌操作（仅切牌、吃碰后）：
                    case "onlycut_after_action": # 吃碰后切牌行为
                        print("onlycut_after_action")
                        self.action_dict = {0:[],1:[],2:[],3:[]}
                        self.action_dict[self.current_player_index].append("cut") # 吃碰后只允许切牌
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态

                    # 玩家和牌操作
                    case "check_hepai":
                        logger.info(f"进入check_hepai case: hu_class={self.hu_class}, result_dict keys={list(self.result_dict.keys())}")
                        hu_score, hu_fan = self.result_dict[self.hu_class]

                        # 从 hu_fan 中获取花牌数量
                        huapai_count = sum(int(fan.split("*")[1]) for fan in hu_fan if fan.startswith("花牌*"))
                        
                        # 正确和牌则执行end程序（判断时减去花牌数量，使用可配置的起和番限制）
                        if hu_score - huapai_count >= self.hepai_limit:
                            self.game_status = "END"
                            break
                        # 错和则执行错和程序
                        else:
                            hepai_player_index = self.resolve_hepai_player_index(self.hu_class)
                            saved_hu_class = self.hu_class
                            for i in self.player_list:
                                if i.player_index == hepai_player_index:
                                    i.score -= 30
                                else:
                                    i.score += 10

                            # 牌谱记录错和（无 end 标记，游戏继续）
                            cuohe_hu_fan = hu_fan + ["错和"]
                            cuohe_score_changes = build_score_changes_by_seat(self.player_list, scores_before)
                            cuohe_score_changes_dict = {
                                p.original_player_index: cuohe_score_changes[p.player_index]
                                for p in self.player_list
                            }
                            player_action_record_hu(self, hu_class=self.hu_class, hu_score=hu_score,
                                                    hu_fan=cuohe_hu_fan, hepai_player_index=hepai_player_index,
                                                    score_changes=cuohe_score_changes)
                            if hasattr(self, 'spectator_manager'):
                                self.spectator_manager.record_tick([self.hu_class, hepai_player_index, hu_score, cuohe_hu_fan, cuohe_score_changes])
                            # 错和与日麻对齐：作为计分板独立一行，记录本次罚分与所属局号(current_round)；
                            # 由于错和不推进 current_round，故本局后续真正和牌会出现同一局号的第二行。
                            for player in self.player_list:
                                cuohe_change = player.score - scores_before[player.original_player_index]
                                if cuohe_change > 0:
                                    player.score_history.append(f"+{cuohe_change:02d}")
                                elif cuohe_change < 0:
                                    player.score_history.append(f"-{abs(cuohe_change):02d}")
                                else:
                                    player.score_history.append("0")
                                player.round_number_history.append(self.current_round)
                            # 更新 scores_before 避免后续结算重复计算错和罚分
                            for player in self.player_list:
                                scores_before[player.original_player_index] = player.score

                            # 广播错和结算结果
                            player_to_score = {}
                            for i in self.player_list:
                                player_to_score[i.player_index] = i.score
                            await self.broadcast_result(
                                                hepai_player_index = hepai_player_index,
                                                player_to_score = player_to_score,
                                                hu_score = hu_score,
                                                hu_fan = cuohe_hu_fan,
                                                hu_class = self.hu_class,
                                                hepai_player_hand = self.player_list[hepai_player_index].hand_tiles,
                                                hepai_player_huapai = self.player_list[hepai_player_index].huapai_list,
                                                hepai_player_combination_mask = self.player_list[hepai_player_index].combination_mask,
                                                score_changes = cuohe_score_changes_dict,
                                                )
                            # 与正常和牌相同：结算面板 + ready，全部确认后再恢复手牌并续局
                            await self.run_hu_result_ready_phase(len(cuohe_hu_fan))
                            await self.apply_cuohe_resume_after_ready(hepai_player_index, saved_hu_class)

                    # 如果没有匹配到
                    case _:
                        logger.error(f"没有匹配到游戏状态: {self.game_status}")

            # 卡牌摸完 或者有人和牌
            hu_score = None
            hu_fan = None
            hepai_player_index = None

            # 荣和
            if self.hu_class in ["hu_self","hu_first","hu_second","hu_third"]:
                is_xiaolin = (self.sub_rule == "guobiao/xiaolin")
                is_kshen = (self.sub_rule == "guobiao/kshen")

                # 自摸
                if self.hu_class == "hu_self":
                    hu_score, hu_fan = self.result_dict["hu_self"]
                    hepai_player_index = self.current_player_index
                    self.result_dict = {}

                    if is_xiaolin or is_kshen:
                        # 小林规/K神规自摸：对另外三家各付 n，无基础 8 分
                        self.player_list[hepai_player_index].score += hu_score * 3
                        for i in self.player_list:
                            if i.player_index != hepai_player_index:
                                i.score -= hu_score
                    else:
                        # 标准国标自摸
                        self.player_list[hepai_player_index].score += hu_score*4 + 32
                        for i in self.player_list:
                            i.score -= hu_score + 8

                    self.player_list[hepai_player_index].record_counter.zimo_times += 1
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan)
                    self.player_list[hepai_player_index].record_counter.win_score += hu_score
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu

                # 荣和他家
                else:
                    if self.hu_class == "hu_first":
                        hu_score, hu_fan = self.result_dict["hu_first"]
                    elif self.hu_class == "hu_second":
                        hu_score, hu_fan = self.result_dict["hu_second"]
                    else:  # hu_third
                        hu_score, hu_fan = self.result_dict["hu_third"]
                    hepai_player_index = self.resolve_hepai_player_index(self.hu_class)
                    logger.info(f"和牌玩家索引{hepai_player_index}")
                    self.result_dict = {}

                    if is_xiaolin:
                        # 小林规点和：全铳制，点和 x2，无基础 8 分
                        self.player_list[hepai_player_index].score += hu_score * 2
                        self.player_list[self.current_player_index].score -= hu_score * 2
                    elif is_kshen:
                        # K神规点和：12 分以下三家各付 n；12 分以上两家各付 12，放炮者付 3n-12
                        fangpao_index = self.current_player_index
                        self.player_list[hepai_player_index].score += hu_score * 3
                        if hu_score < 12:
                            for i in self.player_list:
                                if i.player_index != hepai_player_index:
                                    i.score -= hu_score
                        else:
                            for i in self.player_list:
                                if i.player_index != hepai_player_index and i.player_index != fangpao_index:
                                    i.score -= 12
                            self.player_list[fangpao_index].score -= hu_score * 3 - 12
                    else:
                        # 标准国标荣和
                        self.player_list[hepai_player_index].score += hu_score + 24
                        self.player_list[self.current_player_index].score -= hu_score
                        for i in self.player_list:
                            if i.player_index != hepai_player_index:
                                i.score -= 8
                    
                    self.player_list[hepai_player_index].record_counter.dianhe_times += 1
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan)
                    self.player_list[hepai_player_index].record_counter.win_score += hu_score
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu

                    self.player_list[self.current_player_index].record_counter.fangchong_times += 1
                    self.player_list[self.current_player_index].record_counter.fangchong_score += hu_score

                # 广播和牌结算结果
                # 获取所有人分数
                player_to_score = {}
                for i in self.player_list:
                    player_to_score[i.player_index] = i.score
                # 获取和牌显示中的 手牌 花牌 组合掩码
                he_hand = self.player_list[hepai_player_index].hand_tiles
                he_huapai = self.player_list[hepai_player_index].huapai_list
                he_combination_mask = self.player_list[hepai_player_index].combination_mask

                score_changes_dict = build_score_changes_dict(self.player_list, scores_before)
                from .combination_mask_view import build_revealed_angang_masks
                revealed_angang = build_revealed_angang_masks(self.player_list)

                # 广播和牌结算结果
                await broadcast_result(self,
                                       hepai_player_index = hepai_player_index, # 和牌玩家索引
                                       player_to_score = player_to_score, # 所有玩家分数
                                       hu_score = hu_score, # 和牌分数
                                       hu_fan = hu_fan, # 和牌番种
                                       hu_class = self.hu_class, # 和牌类别
                                       hepai_player_hand = he_hand, # 和牌玩家手牌
                                       hepai_player_huapai = he_huapai, # 和牌玩家花牌列表
                                       hepai_player_combination_mask = he_combination_mask, # 和牌玩家组合掩码
                                       score_changes = score_changes_dict,
                                       revealed_angang_masks = revealed_angang,
                                       )
                # 显示和牌传参
                print(f"hu_class: {self.hu_class}, result_dict: {self.result_dict}")
                print(f"player_list_hand_tiles: {self.player_list[hepai_player_index].hand_tiles}")
                print(f"player_list_huapai_list: {self.player_list[hepai_player_index].huapai_list}")
                print(f"player_list_combination_mask: {self.player_list[hepai_player_index].combination_mask}")

            # 广播流局结算结果
            else:
                self.hu_class = "liuju"
                liuju_score_changes = build_score_changes_dict(self.player_list, scores_before)
                from .combination_mask_view import build_revealed_angang_masks
                revealed_angang = build_revealed_angang_masks(self.player_list)
                await broadcast_result(self,
                                       hepai_player_index = None, # 和牌玩家索引
                                       player_to_score = None, # 所有玩家分数
                                       hu_score = None, # 和牌分数
                                       hu_fan = None, # 和牌番种
                                       hu_class = self.hu_class, # 和牌类别(流局)
                                       hepai_player_hand = None, # 和牌玩家手牌
                                       hepai_player_huapai = None, # 和牌玩家花牌列表
                                       hepai_player_combination_mask = None, # 和牌玩家组合掩码
                                       score_changes = liuju_score_changes,
                                       revealed_angang_masks = revealed_angang,
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
                # 与日麻对齐：记录该行对应的局号，供计分板局名列与预测占位使用
                player.round_number_history.append(self.current_round)

            # 牌谱记录本局各玩家分数变化 [p0, p1, p2, p3] 按 original_player_index 排列
            score_changes = build_score_changes_by_seat(self.player_list, scores_before)

            # 牌谱记录和牌/流局 + end 标记
            if self.hu_class in ["hu_self","hu_first","hu_second","hu_third"]:
                player_action_record_hu(self, hu_class=self.hu_class, hu_score=hu_score,
                                        hu_fan=hu_fan, hepai_player_index=hepai_player_index,
                                        score_changes=score_changes)
                if hasattr(self, 'spectator_manager'):
                    self.spectator_manager.record_tick([self.hu_class, hepai_player_index, hu_score, hu_fan, score_changes])
            else:
                player_action_record_liuju(self)
                if hasattr(self, 'spectator_manager'):
                    self.spectator_manager.record_tick(["liuju"])
            player_action_record_round_end(self)
            if hasattr(self, 'spectator_manager'):
                self.spectator_manager.record_tick(["end"])
            
            # 根据和牌类型处理等待逻辑
            if self.hu_class == "liuju":
                await asyncio.sleep(liuju_ready_wait_seconds())
            else:
                fan_count = len(hu_fan) if hu_fan else 0
                await self.run_hu_result_ready_phase(fan_count)

            if self.current_round < self.max_round * 4:
                next_game_round_switchseat(self)
                logger.info("重新开始下一局")
            else:
                logger.info("最后一局结束，不再推进局数")
                break
            # ↑ 重新开始下一局循环
        
        # 游戏结束所有局数
        logger.info("游戏结束")
        end_game_record(self)
        logger.info(f"最终游戏记录: {self.game_record}")

        # 终局排名：同分并列（竞赛排名 1,2,2,4），同分组内按开局原始风位排序
        assign_competition_final_ranks(self.player_list)
        player_count = len(self.player_list)

        # 排位赛 PT 计算（同名次均摊其占用名次区间的加/扣分）
        is_match = (self.room_type == "match")
        match_queue_type = getattr(self, 'match_queue_type', None)
        if is_match and match_queue_type:
            from ...match.rank_calculator import calculate_pt, apply_pt, parse_queue_type
            parsed = parse_queue_type(match_queue_type)
            if parsed:
                tier, game_type = parsed
                for index, player in enumerate(self.player_list):
                    # 计算该玩家所在并列分组占用的名次区间 [start+1, end+1]（1-indexed 名次）
                    start = index
                    while start > 0 and self.player_list[start - 1].score == player.score:
                        start -= 1
                    end = index
                    while end < player_count - 1 and self.player_list[end + 1].score == player.score:
                        end += 1

                    rank_data = self.db_manager.get_rank_data(player.user_id)
                    old_rank = rank_data["guobiao_rank"] if rank_data else "10级"
                    old_score = rank_data["guobiao_score"] if rank_data else 0
                    # 对占用名次区间内每个名次分别算 PT 后取平均，实现同名次均摊加/扣分
                    pt_values = [
                        calculate_pt(tier, game_type, pos, old_rank)
                        for pos in range(start + 1, end + 2)
                    ]
                    pt = round(sum(pt_values) / len(pt_values), 2)
                    new_rank, new_score = apply_pt(old_rank, old_score, pt)
                    # 存储到 player 对象供广播使用
                    player.pt = pt
                    player.rank_before = old_rank
                    player.score_before = old_score
                    player.rank_after = new_rank
                    player.score_after = new_score
                    # 更新数据库
                    self.db_manager.update_rank_data(player.user_id, new_rank, new_score)
                    logger.info(f"排位 PT: {player.username} rank {player.record_counter.rank_result} (名次区间 {start + 1}-{end + 1}), pt={pt}, {old_rank}({old_score}) -> {new_rank}({new_score})")

        # 发送游戏结算信息
        await self.broadcast_game_end() # 广播游戏结束信息
        
        # 对局结束后：一次性下发完整牌谱给观战者，并结束观战增量服务
        if hasattr(self, 'spectator_manager'):
            await self.spectator_manager.send_final_record_and_close()
        
        # 存储游戏牌谱
        if is_match and match_queue_type:
            from ...match.rank_calculator import queue_type_to_match_type
            match_type = queue_type_to_match_type(match_queue_type)
        else:
            match_type = f"{self.max_round}/4"
        game_id = self.db_manager.store_guobiao_game_record(
            self.game_record,
            self.player_list,
            self.room_type,
            match_type
        )
        
        # 判断是否应该保存对局数据和番种统计
        # 小林规或修改了起和番限制的对局不保存统计数据，仅保存牌谱
        is_xiaolin = (self.sub_rule == "guobiao/xiaolin")
        is_kshen = (self.sub_rule == "guobiao/kshen")
        is_custom_hepai = (self.hepai_limit != 8)
        has_ai_player = any(player.user_id <= 10 for player in self.player_list)
        
        if is_xiaolin or is_kshen:
            rule_label = "小林规" if is_xiaolin else "K神规"
            logger.info(f'{rule_label}对局，仅保存牌谱，跳过统计数据保存，game_id: {game_id}')
        elif is_custom_hepai:
            logger.info(f'自定义起和番限制({self.hepai_limit})，仅保存牌谱，跳过统计数据保存，game_id: {game_id}')
        elif has_ai_player:
            logger.info(f'游戏记录包含AI玩家，跳过统计数据保存，game_id: {game_id}')
        elif game_id:
            total_rounds = len(self.game_record.get("game_round", {}))
            self.db_manager.store_guobiao_game_stats(
                game_id,
                self.player_list,
                self.room_type,
                self.max_round,
                total_rounds
            )
            self.db_manager.store_guobiao_fan_stats(
                game_id,
                self.player_list,
                self.room_type,
                self.max_round
            )

        # 结束游戏生命周期：使用统一的清理方法。
        # 排位匹配对局的匹配状态（承诺锁 / 游戏中人数 / 房间号）统一在 cleanup_game_state_complete
        # 内通过 match_manager.release_match 释放，无需在此单独处理。
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)

        if self.room_type == "match":
            # 匹配对局不依赖房间系统（未注册到 room_manager.rooms），无需销毁房间
            pass
        else:
            await self.game_server.room_manager.finish_custom_game_room(self.room_id)
        logger.info(f"游戏实例已清理，room_id: {self.room_id},goodbye!")

    def resolve_hepai_player_index(self, hu_class: str) -> int:
        """与局终正和一致：由切牌者与 hu_class 推算实际和牌玩家座位索引。"""
        if hu_class == "hu_self":
            return self.current_player_index
        idx = next_current_num(self.current_player_index)
        if hu_class == "hu_first":
            return idx
        idx = next_current_num(idx)
        if hu_class == "hu_second":
            return idx
        return next_current_num(idx)

    async def run_hu_result_ready_phase(self, fan_count: int) -> None:
        """结算展示时长内进入 waiting_ready，与正常和牌局终流程一致。"""
        await run_synced_hu_ready_phase(self, fan_count, broadcast_ready_status)

    async def apply_cuohe_resume_after_ready(self, hepai_player_index: int, hu_class: str) -> None:
        """错和 ready 结束后：陪打标记、撤销荣和误加入的手牌、回到本局继续打牌。"""
        self.player_list[hepai_player_index].tag_list.append("peida")
        await self.broadcast_refresh_player_tag_list()

        if hu_class == "hu_self":
            self.action_dict = check_action_hand_action(self, self.current_player_index)
            self.game_status = "waiting_hand_action"
        elif hu_class in ("hu_first", "hu_second", "hu_third"):
            cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
            hepai_hand = self.player_list[hepai_player_index].hand_tiles
            if hepai_hand and hepai_hand[-1] == cut_tile:
                hepai_hand.pop()
            elif cut_tile in hepai_hand:
                hepai_hand.remove(cut_tile)
            self.action_dict = check_action_after_cut(self, cut_tile)
            if any(self.action_dict[i] for i in self.action_dict):
                self.game_status = "waiting_action_after_cut"
            else:
                self.game_status = "deal_card"
        else:
            logger.error(f"错和续局未知 hu_class={hu_class}")
            self.game_status = "deal_card"

        for player in self.player_list:
            refresh_waiting_tiles(self, player.player_index)
        self.hu_class = ""
        self.result_dict = {}

    # ========== 观战系统方法（委托给观战管理器） ==========
    
    async def add_spectator(self, user_id: int, connection: Any):
        """添加观战玩家"""
        await self.spectator_manager.add_spectator(user_id, connection)
    
    async def remove_spectator(self, user_id: int):
        """移除观战玩家"""
        await self.spectator_manager.remove_spectator(user_id)


# 挂载广播方法于GuobiaoGameState实例
GuobiaoGameState.wait_action = wait_action
GuobiaoGameState.broadcast_game_start = broadcast_game_start
GuobiaoGameState.broadcast_ask_hand_action = broadcast_ask_hand_action
GuobiaoGameState.broadcast_ask_other_action = broadcast_ask_other_action
GuobiaoGameState.broadcast_do_action = broadcast_do_action
GuobiaoGameState.broadcast_result = broadcast_result
GuobiaoGameState.broadcast_game_end = broadcast_game_end
GuobiaoGameState.broadcast_switch_seat = broadcast_switch_seat
GuobiaoGameState.broadcast_refresh_player_tag_list = broadcast_refresh_player_tag_list
GuobiaoGameState.reconnected_send_pending_ask = reconnected_send_pending_ask

# 挂载功能函数于GuobiaoGameState实例
GuobiaoGameState.next_current_index = next_current_index
GuobiaoGameState.refresh_waiting_tiles = refresh_waiting_tiles

