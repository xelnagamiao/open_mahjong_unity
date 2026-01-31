import random
import asyncio
from typing import Any, Dict, List, Optional
import time
import logging
import hashlib
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
)
from ..public.logic_common import get_index_relative_position, next_current_index, next_current_num, back_current_num
from ..public.init_game_tiles import init_qingque_tiles
from ..public.next_game_round import next_game_round
from ..public.game_record_manager import init_game_record,init_game_round,player_action_record_buhua,player_action_record_deal,player_action_record_cut,player_action_record_angang,player_action_record_jiagang,player_action_record_chipenggang,player_action_record_end,end_game_record,player_action_record_nextxunmu
from ...game_calculation.game_calculation_service import GameCalculationService
from ...database.db_manager import DatabaseManager

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
class QingquePlayer:
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

        self.title_used = 0 # 使用的称号ID
        self.profile_used = 0 # 使用的头像ID
        self.character_used = 0 # 使用的角色ID
        self.voice_used = 0 # 使用的音色ID

    def get_tile(self, tiles_list):
        element = tiles_list.pop(0) # 从牌堆中获取第一张牌
        self.hand_tiles.append(element)

    def get_gang_tile(self, tiles_list):
        element = tiles_list.pop() # 从牌堆中获取最后一张牌
        self.hand_tiles.append(element)

        # 游戏进程类
class QingqueGameState:
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
        self.player_list: List[QingquePlayer] = []
        player_settings = room_data.get("player_settings", {})
        for user_id in room_data["player_list"]:
            player_setting = player_settings.get(user_id, {})
            if user_id == 0:
                username = "麻雀罗伯特"
            else:
                username = player_setting.get("username", f"用户{user_id}")
            player = QingquePlayer(user_id, username, [], room_data["round_timer"])
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
        self.room_type = room_data["room_type"] # 房间规则
        self.room_random_seed = room_data.get("random_seed", 0) # 随机种子（默认为0）
        self.open_cuohe = room_data.get("open_cuohe", False) # 是否开启错和（默认为False）
        
        self.isPlayerSetRandomSeed = False # 是否玩家设置了随机种子

        # 初始化游戏状态
        self.tiles_list = [] # 牌堆
        self.current_player_index = 0 # 目前轮到的玩家
        self.xunmu = 0 # 巡目
        self.game_random_seed = 0 # 游戏随机种子(游戏结束后提供)
        self.round_random_seed = 0 # 局内随机种子(每局向玩家提供)
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
        "pass": 0,"buhua":0,"cut":0,"angang":0,"jiagang":0,"deal_tile":0,"deal_gang_tile":0,"deal_buhua_tile":0 # 其他优先级 最低优先级
        }

        # 如果您在管理自己规则内的分支，请不要将Debug = True 的配置上传到公共代码仓库 这一项单元配置不会得到review和测试
        self.Debug = True


    async def player_disconnect(self, user_id: int):
        """玩家掉线：增加 offline 标签并广播"""
        for p in self.player_list:
            if p.user_id == user_id:
                if "offline" not in p.tag_list:
                    p.tag_list.append("offline")
                    await broadcast_refresh_player_tag_list(self)
                break

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
                        'round_random_seed': self.round_random_seed,
                        'current_round': self.current_round,
                        'step_time': self.step_time,
                        'round_time': self.round_time,
                        'room_type': self.room_type,
                        'open_cuohe': self.open_cuohe,
                        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed,
                        'players_info': []
                    }
                    
                    # 构建玩家信息列表
                    for player in self.player_list:
                        player_info = {
                            'user_id': player.user_id,
                            'username': player.username,
                            'hand_tiles_count': len(player.hand_tiles),
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
                    
                    # 添加重连玩家的手牌
                    game_info = GameInfo(
                        **base_game_info,
                        self_hand_tiles=p.hand_tiles
                    )
                    
                    response = Response(
                        type="game_start_GB",
                        success=True,
                        message="重连成功，游戏继续",
                        game_info=game_info
                    )
                    
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    logger.info(f"已向重连玩家 {p.username} 发送游戏状态信息")
                break

    async def cleanup_game_state(self):
        """清理游戏状态协程：取消游戏循环任务（映射关系由 gamestate_manager 统一清理）"""
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
        - 负责运行实际的 game_loop_qingque
        - 捕获未处理异常并进行统一日志和清理
        """
        try:
            await self.game_loop_qingque()
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

    async def game_loop_qingque(self):

        if not self.Debug:
            # 生成完整游戏随机种子
            # 有效随机种子范围是 0 到 2^32 - 1（4,294,967,295）
            # 如果房间数据中提供了 random_seed 且不为 0，则使用它；否则使用时间生成
            if self.room_random_seed != 0:
                self.game_random_seed = self.room_random_seed
                self.isPlayerSetRandomSeed = True
            else:
                self.game_random_seed = int(time.time() * 1000000) % (2**32)
                self.isPlayerSetRandomSeed = False
            # 房间初始化 打乱玩家顺序（基于随机种子）
            # 测试时不打乱玩家顺序
            # 使用随机种子创建独立的随机数生成器来打乱玩家顺序
            rng = random.Random(self.game_random_seed)
            rng.shuffle(self.player_list)

            # 根据打乱的玩家顺序设置玩家索引
            for index, player in enumerate[QingquePlayer](self.player_list):
                player.player_index = index
                player.original_player_index = index

        else:
            # 测试
            self.isPlayerSetRandomSeed = False
            self.game_random_seed = int(time.time() * 1000000) % (2**32)
            # 测试时不打乱玩家顺序
            for index, player in enumerate[QingquePlayer](self.player_list):
                player.player_index = index
                player.original_player_index = index

        # 牌谱记录游戏头
        init_game_record(self)
        # 游戏主循环
        while self.current_round <= self.max_round * 4:

            init_qingque_tiles(self)  # 初始化牌山和手牌

            # 广播游戏开始
            await self.broadcast_game_start()
            
            # 牌谱记录对局头
            init_game_round(self)

            # 初始行为（青雀规则：无补花阶段，直接从庄家起手）
            self.game_status = "waiting_hand_action"  # 初始行动
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
                        player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
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
                        self.player_list[self.current_player_index].get_gang_tile(self.tiles_list) # 倒序摸牌
                        # 牌谱记录摸牌
                        player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                        # 广播摸牌操作
                        await self.broadcast_do_action(
                            action_list = ["deal_gang_tile"],
                            action_player = self.current_player_index,
                            deal_tile = self.player_list[self.current_player_index].hand_tiles[-1],
                        )
                        self.action_dict = check_action_hand_action(self,self.current_player_index,is_get_gang_tile=True) # 允许岭上
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
                # 自摸
                if self.hu_class == "hu_self":
                    hu_score, hu_fan = self.result_dict["hu_self"] # 获取和牌分数和番数
                    hepai_player_index = self.current_player_index # 和牌玩家等于当前玩家
                    base_point = self.calculation_service.GetBasePoint(hu_score)
                    base_point = int(base_point)
                    actual_hu_score = base_point * 4  # 自摸：基础分数 × 4
                    self.result_dict = {}
                    self.player_list[hepai_player_index].score += actual_hu_score # 三倍和牌分
                    actual_hu_score -= base_point
                    for i in self.player_list: # 其他玩家扣除和牌分
                        i.score -= base_point

                    # 记录玩家数据
                    self.player_list[hepai_player_index].record_counter.zimo_times += 1 # 增加自摸次数
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan) # 增加和牌番种
                    self.player_list[hepai_player_index].record_counter.win_score += hu_score # 增加和牌总番数
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu # 增加和牌总巡目

                # 荣和他家
                else:
                    # 荣和上家
                    if self.hu_class == "hu_first": 
                        hu_score, hu_fan = self.result_dict["hu_first"]
                        hepai_player_index = next_current_num(self.current_player_index) # 获取当前玩家的下家索引
                        logger.info(f"和牌玩家索引{hepai_player_index}")
                        base_point = self.calculation_service.GetBasePoint(hu_score)
                        base_point = int(base_point)
                        actual_hu_score = base_point * 3  # 点和：基础分数 × 3
                        self.player_list[hepai_player_index].score += actual_hu_score # 和牌玩家增加和牌分与8基础分
                        self.player_list[self.current_player_index].score -= actual_hu_score # 当前玩家扣除和牌分
                        self.result_dict = {}

                    # 荣和对家
                    elif self.hu_class == "hu_second":
                        hu_score, hu_fan = self.result_dict["hu_second"]
                        hepai_player_index = next_current_num(self.current_player_index)
                        hepai_player_index = next_current_num(hepai_player_index) # 获取下下家索引
                        logger.info(f"和牌玩家索引{hepai_player_index}")
                        base_point = self.calculation_service.GetBasePoint(hu_score)
                        base_point = int(base_point)
                        actual_hu_score = base_point * 3  # 点和：基础分数 × 3
                        self.result_dict = {}
                        self.player_list[hepai_player_index].score += actual_hu_score # 和牌玩家增加和牌分与8基础分
                        self.player_list[self.current_player_index].score -= actual_hu_score # 当前玩家扣除和牌分

                    # 荣和下家
                    else: # self.hu_class == "hu_third":
                        hu_score, hu_fan = self.result_dict["hu_third"]
                        hepai_player_index = next_current_num(self.current_player_index)
                        hepai_player_index = next_current_num(hepai_player_index)
                        hepai_player_index = next_current_num(hepai_player_index) # 获取下下下家索引
                        logger.info(f"和牌玩家索引{hepai_player_index}")
                        self.result_dict = {}
                        base_point = self.calculation_service.GetBasePoint(hu_score)
                        base_point = int(base_point)
                        actual_hu_score = base_point * 3  # 点和：基础分数 × 3
                        self.player_list[hepai_player_index].score += actual_hu_score # 和牌玩家增加和牌分与8基础分
                        self.player_list[self.current_player_index].score -= actual_hu_score # 当前玩家扣除和牌分
                    
                    # 记录玩家数据
                    self.player_list[hepai_player_index].record_counter.dianhe_times += 1 # 增加点和次数
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan) # 增加和牌番种
                    self.player_list[hepai_player_index].record_counter.win_score += hu_score # 增加和牌总番数
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu # 增加和牌总巡目

                    self.player_list[self.current_player_index].record_counter.fangchong_times += 1 # 增加放铳次数
                    self.player_list[self.current_player_index].record_counter.fangchong_score += actual_hu_score # 增加放铳总番数

                # 广播和牌结算结果
                # 获取所有人分数
                player_to_score = {}
                for i in self.player_list:
                    player_to_score[i.player_index] = i.score
                # 获取和牌显示中的 手牌 花牌 组合掩码
                he_hand = self.player_list[hepai_player_index].hand_tiles
                he_huapai = self.player_list[hepai_player_index].huapai_list
                he_combination_mask = self.player_list[hepai_player_index].combination_mask

                # 广播和牌结算结果（使用实际和牌分数，而不是番数）
                await broadcast_result(self,
                                       hepai_player_index = hepai_player_index, # 和牌玩家索引
                                       player_to_score = player_to_score, # 所有玩家分数
                                       hu_score = actual_hu_score, # 和牌分数（整数）
                                       hu_fan = hu_fan, # 和牌番种
                                       hu_class = self.hu_class, # 和牌类别
                                       hepai_player_hand = he_hand, # 和牌玩家手牌
                                       hepai_player_huapai = he_huapai, # 和牌玩家花牌列表
                                       hepai_player_combination_mask = he_combination_mask # 和牌玩家组合掩码
                                       )
                # 显示和牌传参
                print(f"hu_class: {self.hu_class}, result_dict: {self.result_dict}")
                print(f"player_list_hand_tiles: {self.player_list[hepai_player_index].hand_tiles}")
                print(f"player_list_huapai_list: {self.player_list[hepai_player_index].huapai_list}")
                print(f"player_list_combination_mask: {self.player_list[hepai_player_index].combination_mask}")
                
                # 记录玩家副露率
                for i in self.player_list:
                    # 检查combination_tiles中是否有以k、g、s开头的组合牌
                    has_fulu = any(combo.startswith("k") or combo.startswith("g") or combo.startswith("s") 
                                   for combo in i.combination_tiles)
                    if has_fulu:
                        i.record_counter.fulu_times += 1

            # 广播流局结算结果
            else:
                self.hu_class = "liuju"
                await broadcast_result(self,
                                       hepai_player_index = None, # 和牌玩家索引
                                       player_to_score = None, # 所有玩家分数
                                       hu_score = hu_score, # 和牌分数
                                       hu_fan = None, # 和牌番种
                                       hu_class = self.hu_class, # 和牌类别(流局)
                                       hepai_player_hand = None, # 和牌玩家手牌
                                       hepai_player_huapai = None, # 和牌玩家花牌列表
                                       hepai_player_combination_mask = None # 和牌玩家组合掩码
                                       )

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

            # 牌谱记录和牌
            player_action_record_end(self,hu_class = self.hu_class,hu_score = hu_score,hu_fan = hu_fan,hepai_player_index = hepai_player_index)
            
            if self.hu_class == "liuju":
                await asyncio.sleep(2) # 等待2秒后重新开始下一局
            else:
                await asyncio.sleep(len(hu_fan)*0.5 + 8) # 等待和牌番种时间与8秒后重新开始下一局

            # 开启下一局的准备工作
            next_game_round(self)   

            # 换位
            if self.current_round >= self.max_round*4:
                if self.current_round == 5 or self.current_round == 9 or self.current_round == 13:
                    await broadcast_switch_seat(self)
                    await asyncio.sleep(5)

            logger.info(f"重新开始下一局")
            # ↑ 重新开始下一局循环
        
        # 游戏结束所有局数
        logger.info("游戏结束")
        end_game_record(self)
        logger.info(f"最终游戏记录: {self.game_record}")

        # 按分数排序玩家
        self.player_list.sort(key=lambda x: x.score, reverse=True)
        for index, player in enumerate[QingquePlayer](self.player_list):
            player.record_counter.rank_result = index + 1

        # 发送游戏结算信息
        await self.broadcast_game_end() # 广播游戏结束信息

        """
        # 存储游戏牌谱
        game_id = self.db_manager.store_guobiao_game_record(
            self.game_record,
            self.player_list,
            self.room_type
        )
        
        # 检查是否包含AI玩家（user_id <= 10），如果没有AI玩家则保存统计数据
        has_ai_player = any(player.user_id <= 10 for player in self.player_list)
        if not has_ai_player and game_id:
            total_rounds = len(self.game_record.get("game_round", {}))
            # 存储基础统计数据
            self.db_manager.store_guobiao_game_stats(
                game_id,
                self.player_list,
                self.room_type,
                self.max_round,
                total_rounds
            )
            # 存储番种统计数据
            self.db_manager.store_guobiao_fan_stats(
                game_id,
                self.player_list,
                self.room_type,
                self.max_round
            )
        elif has_ai_player:
            logger.info(f'游戏记录包含AI玩家，跳过统计数据保存，game_id: {game_id}')
        """

        # 结束游戏生命周期：使用统一的清理方法
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
        
        # 销毁房间并广播离开房间消息
        await self.game_server.room_manager.destroy_room(self.room_id)
        logger.info(f"游戏实例已清理，room_id: {self.room_id},goodbye!")


# 挂载广播方法于GuobiaoGameState实例
QingqueGameState.wait_action = wait_action
QingqueGameState.broadcast_game_start = broadcast_game_start
QingqueGameState.broadcast_ask_hand_action = broadcast_ask_hand_action
QingqueGameState.broadcast_ask_other_action = broadcast_ask_other_action
QingqueGameState.broadcast_do_action = broadcast_do_action
QingqueGameState.broadcast_result = broadcast_result
QingqueGameState.broadcast_game_end = broadcast_game_end
QingqueGameState.broadcast_switch_seat = broadcast_switch_seat
QingqueGameState.broadcast_refresh_player_tag_list = broadcast_refresh_player_tag_list

# 挂载功能函数于GuobiaoGameState实例
QingqueGameState.next_current_index = next_current_index
QingqueGameState.refresh_waiting_tiles = refresh_waiting_tiles

