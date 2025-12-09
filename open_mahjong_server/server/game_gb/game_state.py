import random
import asyncio
from typing import Any, Dict, List, Optional
import time
from .action_check import check_action_after_cut,check_action_jiagang,check_action_buhua,check_action_hand_action,refresh_waiting_tiles
from .boardcast import broadcast_game_start,broadcast_ask_hand_action,broadcast_ask_other_action,broadcast_do_action,broadcast_result,broadcast_game_end
from .logic_handler import get_index_relative_position
from .game_record_manager import init_game_record,init_game_round,player_action_record_buhua,player_action_record_deal,player_action_record_cut,player_action_record_angang,player_action_record_jiagang,player_action_record_chipenggang,player_action_record_end,end_game_record,player_action_record_nextxunmu
from ..game_calculation.game_calculation_service import GameCalculationService
from ..database.db_manager import DatabaseManager

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
class ChinesePlayer:
    def __init__(self, user_id: int, username: str, tiles: list, remaining_time: int):
        self.user_id = user_id                        # 用户UID
        self.username = username                      # 玩家名（用于显示）
        self.hand_tiles = tiles                       # 手牌
        self.huapai_list = []                         # 花牌列表
        self.discard_tiles = []                       # 弃牌
        self.discard_origin = set[int]()              # 理论弃牌 (日麻中启用,避免在弃牌中吃碰卡牌以后的不准确)
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
        self.waiting_tiles = set[int]()               # 听牌
        self.record_counter = RecordCounter()          # 创建独立的记录计数器实例

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
class ChineseGameState:
    # chinesegamestate负责一个国标麻将对局进程，init属性包含游戏房间状态 player_list 包含玩家数据
    def __init__(self, game_server, room_data: dict, calculation_service: GameCalculationService, db_manager: DatabaseManager):
        # 传入游戏服务器
        self.game_server = game_server # 游戏服务器
        # 传入全局计算服务
        self.calculation_service = calculation_service
        # 传入数据库管理器 用于存储牌谱
        self.db_manager = db_manager
        # 创建牌谱管理器 用于存储牌谱
        self.game_record = {}
        # 创建玩家列表 包含chinesePlayer类
        self.player_list: List[ChinesePlayer] = []
        player_settings = room_data.get("player_settings", {})
        for user_id in room_data["player_list"]:
            player_setting = player_settings.get(user_id, {})
            username = player_setting.get("username", f"用户{user_id}")
            player = ChinesePlayer(user_id, username, [], room_data["round_timer"])
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
        "pass": 0,"buhua":0,"cut":0,"angang":0,"jiagang":0,"deal":0 # 其他优先级 最低优先级
        }

    # 获取下一个玩家索引 东 → 南 → 西 → 北 → 东 0 → 1 → 2 → 3 → 0
    def next_current_index(self):
        if self.current_player_index == 3:
            self.current_player_index = 0
        else:
            self.current_player_index += 1

    def next_current_num(self,num):
        if num == 3:
            return 0
        else:
            return num + 1

    def back_current_num(self,num):
        if num == 0:
            return 3
        else:
            return num - 1


    def init_game_tiles(self):
        # 标准牌堆
        sth_tiles_set = {
            11,12,13,14,15,16,17,18,19, # 万
            21,22,23,24,25,26,27,28,29, # 饼
            31,32,33,34,35,36,37,38,39, # 条
            41,42,43,44, # 东南西北
            45,46,47 # 中白发
        }
        # 花牌牌堆
        hua_tiles_set = {51,52,53,54,55,56,57,58} # 春夏秋冬 梅兰竹菊
        # 生成牌堆
        self.tiles_list = []
        for tile in sth_tiles_set:
            self.tiles_list.extend([tile] * 4)
        self.tiles_list.extend(hua_tiles_set)
        
        # 基于完整游戏随机种子和当前局数生成小局随机种子（与 init_game_tiles 保持一致）
        # 公式: (whole_game_random_seed * 1000 + current_round) % (2**32)
        self.round_random_seed = (self.game_random_seed * 1000 + self.current_round) % (2**32)
        random.seed(self.round_random_seed)
        random.shuffle(self.tiles_list)

        Text = True
        if Text:
            # 使用测试牌例
            self.player_list[0].hand_tiles = [11,12,13,14,15,21,21,21,22,22,22,23,23]
            self.player_list[1].hand_tiles = [11,11,11,12,12,12,13,13,13,14,14,14,15]
            self.player_list[2].hand_tiles = []
            self.player_list[3].hand_tiles = []

            # 删除牌山中测试牌例的卡牌
            for tile in self.player_list[0].hand_tiles:
                self.tiles_list.remove(tile)
            for tile in self.player_list[1].hand_tiles:
                self.tiles_list.remove(tile)
            for tile in self.player_list[2].hand_tiles:
                self.tiles_list.remove(tile)
            for tile in self.player_list[3].hand_tiles:
                self.tiles_list.remove(tile)

            self.tiles_list.remove(55)

            # 分配每位玩家13张牌        
            for player in self.player_list:
                if player.hand_tiles == []:
                    for _ in range(13):
                        player.get_tile(self.tiles_list)
            
            self.player_list[self.current_player_index].hand_tiles.append(55)

        else:
            # 分配每位玩家13张牌
            for player in self.player_list:
                for _ in range(13):
                    player.get_tile(self.tiles_list)
            # 庄家额外摸一张
            self.player_list[0].get_tile(self.tiles_list)

    def next_game_round(self):
        # 局数+1
        self.current_round += 1
        self.round_index += 1 
        self.current_player_index = 0
        self.xunmu = 0
        self.action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}

        # 清空花牌弃牌组合牌列表 重置时间
        self.hu_class = None
        for i in self.player_list:
            i.huapai_list = []
            i.discard_tiles = []
            i.waiting_tiles = set()
            i.combination_tiles = []
            i.remaining_time = self.round_time
            i.player_index = self.back_current_num(i.player_index) # 倒退玩家索引(0→3 1→0 2→1 3→2)

        # 如果需要座位换位
        if self.current_round in [5,9,13]:

            if self.current_round == 5:
                for i in self.player_list:
                    if i.original_player_index == 0: # 东起：东[南]北西
                        i.player_index = 1
                    elif i.original_player_index == 1: # 南起：南[东]西北
                        i.player_index = 0
                    elif i.original_player_index == 2: # 西起：西[北]东南
                        i.player_index = 3
                    elif i.original_player_index == 3: # 北起：北[西]南东
                        i.player_index = 2
                
            elif self.current_round == 9:
                for i in self.player_list:
                    if i.original_player_index == 0: # 东起：东南[北]西
                        i.player_index = 3
                    elif i.original_player_index == 1: # 南起：南东[西]北
                        i.player_index = 2
                    elif i.original_player_index == 2: # 西起：西北[东]南
                        i.player_index = 0
                    elif i.original_player_index == 3: # 北起：北西[南]东
                        i.player_index = 1
                
            elif self.current_round == 13:
                for i in self.player_list:
                    if i.original_player_index == 0: # 东起：东南北[西]
                        i.player_index = 2
                    elif i.original_player_index == 1: # 南起：南东西[北]
                        i.player_index = 3
                    elif i.original_player_index == 2: # 西起：西北东[南]
                        i.player_index = 1
                    elif i.original_player_index == 3: # 北起：北西南[东]
                        i.player_index = 0

        # 按照换位后的玩家顺序重新排列player_list
        for index,player in enumerate[ChinesePlayer](self.player_list):
            player.player_index = index

    async def game_loop_chinese(self):

        # 房间初始化 打乱玩家顺序
        # 测试时不打乱玩家顺序
        # random.shuffle(self.player_list)

        # 根据打乱的玩家顺序设置玩家索引
        for index, player in enumerate(self.player_list):
            player.player_index = index
            player.original_player_index = index
        # 生成完整游戏随机种子
        self.game_random_seed = int(time.time() * 1000000) % (2**32)
        # 牌谱记录游戏头
        init_game_record(self)

        # 游戏主循环
        while self.current_round <= self.max_round * 4:

            self.init_game_tiles() # 初始化牌山和手牌

            # 广播游戏开始
            await broadcast_game_start(self)
            
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
                        await broadcast_ask_hand_action(self) # 广播补花信息
                        # 如果玩家选择补花 则广播一次摸牌信息
                        if await self.wait_action():
                            max_tile = max(self.player_list[self.current_player_index].hand_tiles) # 获取最大牌
                            self.player_list[self.current_player_index].hand_tiles.remove(max_tile) # 从手牌中移除最大牌
                            self.player_list[self.current_player_index].huapai_list.append(max_tile) # 将最大牌加入花牌列表
                            self.player_list[self.current_player_index].get_tile(self.tiles_list) # 摸牌
                            # 牌谱记录补花
                            player_action_record_buhua(self,max_tile = max_tile)
                            # 牌谱记录摸牌
                            player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                            # 广播补花操作
                            print("buhua")
                            await broadcast_do_action(self,action_list = ["buhua","deal"],
                                                      action_player = self.current_player_index,
                                                      buhua_tile = max_tile,
                                                      deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                        # 如果玩家选择pass 则下一轮循环
                        else:
                            action_anymore = False
                    # 如果不能补花 则下一轮循环
                    else:
                        action_anymore = False

            # 初始行为
            self.game_status = "waiting_hand_action" # 初始行动
            self.current_player_index = 0 # 初始玩家索引

            refresh_waiting_tiles(self,self.current_player_index,is_first_action=True) # 检查手牌等待牌
            print(f"第一位行动玩家{self.current_player_index}的手牌等待牌为{self.player_list[self.current_player_index].waiting_tiles}")
            self.action_dict = check_action_hand_action(self,self.current_player_index,is_first_action=True) # 允许可执行的手牌操作
            await broadcast_ask_hand_action(self) # 广播手牌操作
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
                        refresh_waiting_tiles(self,self.current_player_index) # 摸牌前更新听牌
                        self.player_list[self.current_player_index].get_tile(self.tiles_list) # 摸牌
                        # 牌谱记录摸牌
                        player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                        # 广播摸牌操作
                        await broadcast_do_action(self,action_list = ["deal"],action_player = self.current_player_index,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                        self.action_dict = check_action_hand_action(self,self.current_player_index) # 允许可执行的手牌操作
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态

                    # 杠后摸牌操作：当前玩家进行摸牌
                    case "deal_card_after_gang": # 杠后发牌历时行为
                        refresh_waiting_tiles(self,self.current_player_index) # 摸牌前更新听牌
                        self.player_list[self.current_player_index].get_gang_tile(self.tiles_list) # 倒序摸牌
                        # 牌谱记录摸牌
                        player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                        # 广播摸牌操作
                        await broadcast_do_action(self,action_list = ["deal"],action_player = self.current_player_index,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                        self.action_dict = check_action_hand_action(self,self.current_player_index,is_get_gang_tile=True) # 允许岭上
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态
                    
                    # 补花摸牌操作：当前玩家进行摸牌
                    case "deal_card_after_buhua": # 补花后发牌历时行为
                        max_tile = max(self.player_list[self.current_player_index].hand_tiles) # 获取最大牌（花牌数字永远最大）
                        self.player_list[self.current_player_index].hand_tiles.remove(max_tile) # 从手牌中移除最大牌
                        refresh_waiting_tiles(self,self.current_player_index) # 摸牌前更新听牌
                        self.player_list[self.current_player_index].get_gang_tile(self.tiles_list) # 倒序摸牌
                        self.player_list[self.current_player_index].huapai_list.append(max_tile) # 将最大牌加入花牌列表
                        # 牌谱记录补花
                        player_action_record_buhua(self,max_tile = max_tile)
                        # 牌谱记录摸牌
                        player_action_record_deal(self,deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                        # 广播补花操作
                        await broadcast_do_action(self,action_list = ["buhua","deal"],
                                                  action_player = self.current_player_index,
                                                  buhua_tile = max_tile,
                                                  deal_tile = self.player_list[self.current_player_index].hand_tiles[-1])
                        self.action_dict = check_action_hand_action(self,self.current_player_index) # 允许可执行的手牌操作
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态
                        
                    # 等待手牌操作：
                    case "waiting_hand_action": # 摸牌,加杠,暗杠,补花后行为
                        await broadcast_ask_hand_action(self) # 广播手牌操作
                        await self.wait_action() # 等待手牌操作

                    # 等待鸣牌操作：
                    case "waiting_action_after_cut": # 出牌后询问吃碰杠和行为
                        await broadcast_ask_other_action(self) # 广播是否吃碰杠和
                        await self.wait_action() # 等待吃碰杠和操作

                    # 等待加杠操作：
                    case "waiting_action_qianggang": # 加杠后询问胡牌行为
                        await broadcast_ask_other_action(self) # 广播是否胡牌

                    # 等待手牌操作（仅切牌、吃碰后）：
                    case "onlycut_after_action": # 吃碰后切牌行为
                        self.action_dict = {0:[],1:[],2:[],3:[]}
                        self.action_dict[self.current_player_index].append("cut") # 吃碰后只允许切牌
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态

            # 卡牌摸完 或者有人和牌
            hu_score = None
            hu_fan = None
            hepai_player_index = None

            
            
            # 荣和
            if self.hu_class in ["hu_self","hu_first","hu_second","hu_third"]:
                # 自摸
                if self.hu_class == "hu_self":
                    hu_score, hu_fan = self.result_dict["hu_self"] # 获取和牌分数和番数
                    hepai_player_index = self.current_player_index # 和牌玩家等于当前玩家
                    self.player_list[hepai_player_index].score += hu_score*3 + 24 # 三倍和牌分与3*8基础分
                    self.result_dict = {}
                    for i in self.player_list: # 其他玩家扣除和牌分与8基础分
                        if i != hepai_player_index:
                            i.score -= hu_score + 8  


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
                        hepai_player_index = self.next_current_num(self.current_player_index) # 获取当前玩家的下家索引
                        print(f"和牌玩家索引{hepai_player_index}")
                        self.player_list[hepai_player_index].score += hu_score + 24 # 和牌玩家增加和牌分与8基础分
                        self.player_list[self.current_player_index].score -= hu_score # 当前玩家扣除和牌分
                        self.result_dict = {}

                    # 荣和对家
                    elif self.hu_class == "hu_second":
                        hu_score, hu_fan = self.result_dict["hu_second"]
                        hepai_player_index = self.next_current_num(self.current_player_index)
                        hepai_player_index = self.next_current_num(hepai_player_index) # 获取下下家索引
                        print(f"和牌玩家索引{hepai_player_index}")
                        self.result_dict = {}
                        self.player_list[hepai_player_index].score += hu_score + 24 # 和牌玩家增加和牌分与8基础分
                        self.player_list[self.current_player_index].score -= hu_score # 当前玩家扣除和牌分

                    # 荣和下家
                    else: # self.hu_class == "hu_third":
                        hu_score, hu_fan = self.result_dict["hu_third"]
                        hepai_player_index = self.next_current_num(self.current_player_index)
                        hepai_player_index = self.next_current_num(hepai_player_index)
                        hepai_player_index = self.next_current_num(hepai_player_index) # 获取下下下家索引
                        print(f"和牌玩家索引{hepai_player_index}")
                        self.result_dict = {}
                        self.player_list[hepai_player_index].score += hu_score + 24 # 和牌玩家增加和牌分与8基础分
                        self.player_list[self.current_player_index].score -= hu_score # 当前玩家扣除和牌分
                    
                    # 记录玩家数据
                    self.player_list[hepai_player_index].record_counter.dianhe_times += 1 # 增加点和次数
                    self.player_list[hepai_player_index].record_counter.recorded_fans.append(hu_fan) # 增加和牌番种
                    self.player_list[hepai_player_index].record_counter.win_score += hu_score # 增加和牌总番数
                    self.player_list[hepai_player_index].record_counter.win_turn += self.xunmu # 增加和牌总巡目

                    self.player_list[self.current_player_index].record_counter.fangchong_times += 1 # 增加放铳次数
                    self.player_list[self.current_player_index].record_counter.fangchong_score += hu_score # 增加放铳总番数

                    # 其他玩家扣除8基础分
                    for i in self.player_list: # 其他玩家扣除和牌分与8基础分
                        if i.player_index != hepai_player_index:
                            i.score -= 8

                # 广播和牌结算结果
                # 获取所有人分数
                player_to_score = {}
                for i in self.player_list:
                    player_to_score[i.player_index] = i.score
                # 获取和牌显示中的 手牌 花牌 组合掩码
                he_hand = self.player_list[hepai_player_index].hand_tiles
                he_huapai = self.player_list[hepai_player_index].huapai_list
                he_combination_mask = self.player_list[hepai_player_index].combination_mask

                # 广播和牌结算结果
                await broadcast_result(self,
                                       hepai_player_index = hepai_player_index, # 和牌玩家索引
                                       player_to_score = player_to_score, # 所有玩家分数
                                       hu_score = hu_score, # 和牌分数
                                       hu_fan = hu_fan, # 和牌番种
                                       hu_class = self.hu_class, # 和牌类别
                                       hepai_player_hand = he_hand, # 和牌玩家手牌
                                       hepai_player_huapai = he_huapai, # 和牌玩家花牌列表
                                       hepai_player_combination_mask = he_combination_mask # 和牌玩家组合掩码
                                       )
                
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
                                       hu_score = None, # 和牌分数
                                       hu_fan = None, # 和牌番种
                                       hu_class = self.hu_class, # 和牌类别(流局)
                                       hepai_player_hand = None, # 和牌玩家手牌
                                       hepai_player_huapai = None, # 和牌玩家花牌列表
                                       hepai_player_combination_mask = None # 和牌玩家组合掩码
                                       )

            # 牌谱记录和牌
            player_action_record_end(self,hu_class = self.hu_class,hu_score = hu_score,hu_fan = hu_fan,hepai_player_index = hepai_player_index)
            
            if self.hu_class == "liuju":
                await asyncio.sleep(3) # 等待3秒后重新开始下一局
            else:
                await asyncio.sleep(len(hu_fan)*0.5 + 10) # 等待和牌番种时间与10秒后重新开始下一局

            # 开启下一局的准备工作
            self.next_game_round()   
            print(f"重新开始下一局 game_record={self.game_record}")
            print("牌谱：",self.game_record)

            # ↑ 重新开始下一局循环
        
        # 游戏结束所有局数
        print("游戏结束")
        end_game_record(self)
        print(self.game_record)

        # 按分数排序玩家
        self.player_list.sort(key=lambda x: x.score, reverse=True)
        for index, player in enumerate[ChinesePlayer](self.player_list):
            player.record_counter.rank_result = index + 1

        # 发送游戏结算信息
        await broadcast_game_end(self) # 广播游戏结束信息

        # 存储游戏记录
        self.db_manager.store_game_record(self.game_record,self.player_list)

        # 结束游戏生命周期
        if self.room_id in self.game_server.room_id_to_ChineseGameState:
            del self.game_server.room_id_to_ChineseGameState[self.room_id]
            print(f"游戏实例已清理，room_id: {self.room_id},goodbye!")


    async def wait_action(self):
        
        self.waiting_players_list = [] # [2,3]
        used_time = 0 # 已用时间
        # 遍历所有可行动玩家，获取行动玩家列表和等待时间列表
        for player_index, action_list in self.action_dict.items():
            if action_list:  # 如果玩家有可用操作 将玩家加入列表并重置事件状态
                self.waiting_players_list.append(player_index)
                self.action_events[player_index].clear()

        # 如果等待玩家列表不为空且有玩家剩余时间小于(已用时间-步时)，则停止等待
        player_index = None # 保存操作玩家索引 (如果玩家有操作则左侧三个变量有值 否则为None)
        action_data = None # 保存操作数据
        action_type = None # 保存操作类型
        while self.waiting_players_list and any(self.player_list[i].remaining_time + self.step_time > used_time for i in self.waiting_players_list):
            # 给每个可行动者创建一个消息队列任务，同时创建一个计时器任务
            task_list = []  # 任务列表
            task_to_player = {}  # 任务与玩家的映射
            
            for player_index in self.waiting_players_list:
                # 为可以行动的玩家添加行动任务
                action_task = asyncio.create_task(self.action_events[player_index].wait())
                task_list.append(action_task)
                task_to_player[action_task] = player_index  # 建立映射 行动任务 → 玩家索引
            # 添加计时器任务
            timer_task = asyncio.create_task(asyncio.sleep(1)) # 等待1s
            task_list.append(timer_task)

            print(f"开始新一轮等待操作 waiting_players_list={self.waiting_players_list} action_dict={self.action_dict} used_time={used_time}")
            
            # 等待计时器完成1s等待或者任意玩家进行操作
            time_start = time.time()
            done, pending = await asyncio.wait(
                task_list,
                return_when=asyncio.FIRST_COMPLETED
            )
            time_end = time.time()

            # 取消未完成的任务
            for task in pending:
                task.cancel()

            # 处理完成的任务
            for task in done:
                # 计时器完成 增加已用时间 注意：这里不需要重置任务，因为下一次循环开始时会创建新的任务
                if task == timer_task: 
                    used_time += 1
                # 玩家操作完成，获取玩家索引
                else:
                    # 使用映射获取玩家索引
                    player_index = task_to_player[task]  
                    temp_action_data = await self.action_queues[player_index].get() # 获取操作数据
                    Temp_action_type = temp_action_data.get("action_type") # 获取操作类型

                    used_time += time_end - time_start # 服务器计算操作时间
                    used_int_time = int(used_time) # 变量整数时间
                    if used_int_time >= self.step_time: # 扣除玩家超出步时的时间
                        self.player_list[player_index].remaining_time -= (used_int_time - self.step_time)
                   
                    self.action_dict[player_index] = [] # 从可执行操作列表中移除操作
                    self.waiting_players_list.remove(player_index) # 从玩家等待列表中移除玩家
                    
                    # 检查当前操作是否是最高优先级的
                    do_interrupt = True
                    for temp_player_index in self.waiting_players_list:
                        for action in self.action_dict[temp_player_index]:
                            # 如果有其他更高优先级的操作，则继续等待
                            if self.action_priority[Temp_action_type] < self.action_priority[action]:
                                do_interrupt = False
                    
                    # 如果action_data为空，添加action_data
                    if not action_data: 
                        action_data = temp_action_data
                        action_type = Temp_action_type
                    # 如果操作类型优先级更高，则覆盖action_data
                    elif self.action_priority[Temp_action_type] > self.action_priority[action_type]:
                        action_data = temp_action_data
                        action_type = Temp_action_type

                    # 如果是最高优先级，中断等待
                    if do_interrupt:
                        self.waiting_players_list = [] # 清空等待列表，强制结束循环


        # 等待行为结束,开始处理操作,pass,超时逻辑
        # 如果操作是最高优先级的直接结束循环
        # 如果操作并非最高优先级的,在最高优先级取消或者超时后结束循环
        # 如果action_data有值,说明有操作,如果action_data无值,说明操作超时
        # 首先将超时玩家剩余时间归零
        if self.waiting_players_list:
            for i in self.waiting_players_list:
                self.player_list[i].remaining_time = 0
        # 情形处理
        match self.game_status:
            # 补花轮特殊case 只有在游戏开始时启用
            case "waiting_buhua_round":
                # 如果有操作
                if action_data:
                    # 等待补花阶段的action_type只能是buhua
                    if action_type == "buhua": 
                        if action_data:
                            return True # 补花以后如果能够补花继续询问
                        elif action_type == "pass":
                            return False # 如果玩家选择pass则停止该玩家补花
                        else: # 报错
                            raise ValueError("补花阶段action_data出现非buhua和pass的值")
                # 如果无操作结束补花 由于补花阶段是按索引进行循环补花的
                # 如果玩家不补花需要返回False 否则就无法同时处理 玩家同时需要补花2张和玩家拒绝补花的情况
                else:
                    return False
                
            # 摸牌后手牌case 包含 切牌cut 暗杠gang 加杠jiagang 自摸hu 补花buhua 其中自摸是终结条件 补花 加杠 暗杠是循环行为 切牌是转移/历时行为
            case "waiting_hand_action":
                if action_data:
                    if action_type == "cut": # 切牌
                        is_moqie = action_data.get("cutClass")  # 获取手模切布尔值
                        tile_id = action_data.get("TileId") # 获取切牌id
                        cut_tile_index = action_data.get("cutIndex") # 获取卡牌顺位
                        self.player_list[self.current_player_index].hand_tiles.remove(tile_id) # 从手牌中移除切牌
                        self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将切牌加入弃牌堆
                        # 牌谱记录切牌
                        player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                        # 广播切牌操作
                        if self.current_player_index == 0:
                            self.xunmu += 1
                            player_action_record_nextxunmu(self)
                            
                        await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie,cut_tile_index = cut_tile_index) # 广播切牌动画 切牌玩家索引 手模切 切牌id 操作帧
                        # 检查手牌操作 如果有切牌后操作则执行转移行为(询问其他玩家操作) 否则历时行为(下一个玩家摸牌)
                        refresh_waiting_tiles(self,self.current_player_index) # 更新听牌
                        self.action_dict = check_action_after_cut(self,tile_id)
                        
                        if any(self.action_dict[i] for i in self.action_dict):
                            self.game_status = "waiting_action_after_cut" # 转移行为
                        else:
                            self.game_status = "deal_card" # 历时行为

                    
                    elif action_type == "angang": 
                        # 暗杠
                        angang_tile = action_data.get("target_tile") # 获取暗杠牌
                        self.player_list[self.current_player_index].hand_tiles.remove(angang_tile) # 从手牌中移除暗杠牌
                        self.player_list[self.current_player_index].combination_tiles.append(f"G{angang_tile}") # 将暗杠牌加入组合牌
                        add_combination_mask = [2,angang_tile,2,angang_tile,2,angang_tile,2,angang_tile] # 组合掩码
                        self.player_list[self.current_player_index].combination_mask.append(add_combination_mask) # 添加组合掩码
                        # 牌谱记录暗杠
                        player_action_record_angang(self,angang_tile = angang_tile)
                        # 广播暗杠动画
                        await broadcast_do_action(self,action_list = ["angang"],
                                                  action_player = self.current_player_index,
                                                  combination_mask = add_combination_mask,
                                                  combination_target = f"G{angang_tile}") # 大写G代表暗杠
                        
                        # 切换到杠后发牌历时行为
                        self.game_status = "deal_card_after_gang"
                    
                    elif action_type == "jiagang": # 加杠
                        # 加杠
                        jiagang_tile = action_data.get("target_tile") # 获取加杠牌

                        combination_index = -1
                        # 寻找当前玩家的组合牌是否有k+加杠牌 有则将相同位置的索引记录
                        for i,combination in enumerate(self.player_list[self.current_player_index].combination_tiles):
                            if combination == f"k{jiagang_tile}":
                                combination_index = i
                                break

                        # 通过组合位置找到掩码位置
                        for i, mask in enumerate(self.player_list[self.current_player_index].combination_mask[combination_index]):
                            if mask == 1:  # 找到数组下标 [0,Tile,0,Tile,1,Tile] 获取1的位置 1代表碰牌横牌的位置
                                # 在碰牌横牌的后面插入加杠牌和3 如 结果[0,Tile,0,Tile,1,{Tile,3,}Tile]
                                self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, jiagang_tile)
                                self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, 3) # 插入3代表加杠牌
                                break

                        self.player_list[self.current_player_index].hand_tiles.remove(jiagang_tile) # 从手牌中移除加杠牌
                        self.player_list[self.current_player_index].combination_tiles.remove(f"k{jiagang_tile}") # 从组合牌中移除刻子
                        self.player_list[self.current_player_index].combination_tiles.append(f"g{jiagang_tile}") # 将明杠牌加入组合牌 (小写g代表明杠)

                        # 牌谱记录加杠
                        player_action_record_jiagang(self,jiagang_tile = jiagang_tile)

                        await broadcast_do_action(self,action_list = ["jiagang"],
                                                  action_player = self.current_player_index,
                                                  combination_mask = self.player_list[self.current_player_index].combination_mask[combination_index],
                                                  combination_target = f"k{jiagang_tile}"
                                                  ) # 广播加杠动画

                        self.jiagang_tile = jiagang_tile # 存储抢杠牌
                        self.action_dict = check_action_jiagang(self,jiagang_tile) # 检查是否有人可以抢杠
                        if any(self.action_dict[i] for i in self.action_dict):
                            self.game_status = "waiting_action_qianggang" # 如果有则执行 等待抢杠行为 转移行为
                        else:
                            self.game_status = "deal_card_after_gang" # 历时行为
                        return
                    
                    elif action_type == "buhua": 
                        # 补花
                        self.game_status = "deal_card_after_buhua"
                    
                    elif action_type == "hu_self": # 自摸
                        # 和牌 (自摸)
                        self.hu_class = "hu_self"
                        self.game_status = "END"
                    else:
                        print(f"摸牌后手牌阶段action_type出现非cut,angang,jiagang,buhua,hu_self的值: {action_type}")
                # 超时自动摸切
                else:
                    # 摸切 action_type == "cut"
                    is_moqie = True # 模切
                    tile_id = self.player_list[self.current_player_index].hand_tiles[-1] # 最后一张手牌是最晚摸到的牌 获取最后一张手牌
                    self.player_list[self.current_player_index].hand_tiles.remove(tile_id) # 从手牌中移除摸切牌
                    self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将摸切牌加入弃牌堆
                    # 牌谱记录摸切
                    player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                    # 广播摸切操作
                    if self.current_player_index == 0:
                        self.xunmu += 1
                        player_action_record_nextxunmu(self)
                    
                    await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie) # 广播摸切动画 摸切玩家索引 手模切 摸切牌id 操作帧
                    refresh_waiting_tiles(self,self.current_player_index) # 更新听牌

                    self.action_dict = check_action_after_cut(self,tile_id) # 检查手牌操作 如果有切牌后操作则执行转移行为(询问其他玩家操作) 否则历时行为(下一个玩家摸牌)
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut" # 转移行为
                    else:
                        self.game_status = "deal_card" # 历时行为
                    return
                
            # 切牌后手牌case 包含 吃 碰 杠 胡 其中吃碰杠是转移行为 胡是终结行为
            # 由于切后询问行为时的current_player_index还未进行历时操作 当前玩家弃牌堆的最后一张牌就是待吃碰杠和的牌
            case "waiting_action_after_cut":
                tile_id = self.player_list[self.current_player_index].discard_tiles[-1] # 获取操作牌
                combination_mask = []
                combination_target = ""
                if action_data:
                    refresh_waiting_tiles(self,player_index) # 更新听牌
                    if action_type == "chi_left": # [tile_id-2,tile_id-1,tile_id]
                        self.player_list[player_index].hand_tiles.remove(tile_id-1)
                        self.player_list[player_index].hand_tiles.remove(tile_id-2)
                        self.player_list[player_index].combination_tiles.append(f"s{tile_id-1}")
                        combination_target = f"s{tile_id-1}"
                        combination_mask = [1,tile_id,0,tile_id-1,0,tile_id-2]
                    elif action_type == "chi_mid": # [tile_id-1,tile_id,tile_id+1]
                        self.player_list[player_index].hand_tiles.remove(tile_id-1)
                        self.player_list[player_index].hand_tiles.remove(tile_id+1)
                        self.player_list[player_index].combination_tiles.append(f"s{tile_id}")
                        combination_target = f"s{tile_id}"
                        combination_mask = [1,tile_id,0,tile_id-1,0,tile_id+1]
                    elif action_type == "chi_right": # [tile_id,tile_id+1,tile_id+2]
                        self.player_list[player_index].hand_tiles.remove(tile_id+1)
                        self.player_list[player_index].hand_tiles.remove(tile_id+2)
                        self.player_list[player_index].combination_tiles.append(f"s{tile_id+1}")
                        combination_target = f"s{tile_id+1}"
                        combination_mask = [1,tile_id,0,tile_id+1,0,tile_id+2]
                    
                    elif action_type == "peng": # [tile_id',tile_id',tile_id]
                        self.player_list[player_index].hand_tiles.remove(tile_id)
                        self.player_list[player_index].hand_tiles.remove(tile_id)
                        self.player_list[player_index].combination_tiles.append(f"k{tile_id}")
                        relative_position = get_index_relative_position(self,player_index,self.current_player_index) # 获取相对位置 (操作者,出牌者)
                        combination_target = f"k{tile_id}"
                        if relative_position == "left":
                            combination_mask = [1,tile_id,0,tile_id,0,tile_id]
                        elif relative_position == "right":
                            combination_mask = [0,tile_id,0,tile_id,1,tile_id]
                        elif relative_position == "top":
                            combination_mask = [0,tile_id,1,tile_id,0,tile_id]

                    elif action_type == "gang": # [tile_id',tile_id,tile_id',tile_id]
                        self.player_list[player_index].hand_tiles.remove(tile_id)
                        self.player_list[player_index].hand_tiles.remove(tile_id)
                        self.player_list[player_index].hand_tiles.remove(tile_id)
                        self.player_list[player_index].combination_tiles.append(f"g{tile_id}")
                        relative_position = get_index_relative_position(self,player_index,self.current_player_index)
                        combination_target = f"g{tile_id}"
                        if relative_position == "left":
                            combination_mask = [1,tile_id,0,tile_id,0,tile_id,0,tile_id]
                        elif relative_position == "right":
                            combination_mask = [0,tile_id,0,tile_id,0,tile_id,1,tile_id]
                        elif relative_position == "top":
                            combination_mask = [0,tile_id,1,tile_id,0,tile_id,0,tile_id]
                    
                    elif action_type == "hu_first" or action_type == "hu_second" or action_type == "hu_third": # 终结行为 可能有多人胡的情况
                        # 和牌 （荣和）
                        self.player_list[player_index].hand_tiles.append(tile_id) # 将和牌牌加入手牌最后一张
                        self.hu_class = action_type
                        self.game_status = "END"
                        return
                    
                    # 如果发生吃碰杠而不是和牌 则发生转移行为
                    if action_type == "chi_left" or action_type == "chi_mid" or action_type == "chi_right" or action_type == "peng" or action_type == "gang":

                        self.player_list[self.current_player_index].discard_tiles.pop(-1) # 删除弃牌堆的最后一张
                        self.player_list[player_index].combination_mask.append(combination_mask) # 添加组合掩码
                        self.current_player_index = player_index # 转移行为后 当前玩家索引变为操作玩家索引
                        # 牌谱记录吃碰杠牌
                        player_action_record_chipenggang(self,action_type = action_type,chi_tile = tile_id,action_player = player_index)
                        # 广播吃碰杠动画
                        await broadcast_do_action(self,action_list = [action_type],action_player = self.current_player_index,combination_mask = combination_mask,combination_target = combination_target)
                        if action_type == "gang":
                            self.game_status = "deal_card_after_gang" # 转移行为
                        else:
                            self.game_status = "onlycut_after_action" # 转移行为
                        return
                    
                # 如果不吃碰杠或者超时则进行历时行为 继续下一个玩家摸牌
                self.game_status = "deal_card" # 历时行为
                return
            
            # 在转移行为以后只能进行切牌操作
            case "onlycut_after_action":
                if action_data:
                    if action_type == "cut": # 切牌
                        is_moqie = action_data.get("cutClass")  # 获取手模切布尔值
                        tile_id = action_data.get("TileId") # 获取切牌id
                        self.player_list[self.current_player_index].hand_tiles.remove(tile_id) # 从手牌中移除切牌
                        self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将切牌加入弃牌堆
                        # 牌谱记录切牌
                        player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                        # 广播切牌动画
                        await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie)
                        # 检查手牌操作 如果有切牌后操作则执行转移行为(询问其他玩家操作) 否则历时行为(下一个玩家摸牌)
                        refresh_waiting_tiles(self,self.current_player_index) # 更新听牌
                        self.action_dict = check_action_after_cut(self,tile_id)
                        if any(self.action_dict[i] for i in self.action_dict):
                            self.game_status = "waiting_action_after_cut" # 转移行为
                        else:
                            self.game_status = "deal_card" # 历时行为
                        return
                    else:
                        raise ValueError("在转移行为onlycut_afteraction阶段出现非cut的值")
                # 超时自动摸切
                else:
                    is_moqie = True # 摸切
                    tile_id = self.player_list[self.current_player_index].hand_tiles.pop(-1) # 最后一张手牌是最晚摸到的牌 获取最后一张手牌
                    self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将摸切牌加入弃牌堆
                    # 牌谱记录摸切
                    player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                    # 广播摸切动画
                    await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie)
                    refresh_waiting_tiles(self,self.current_player_index) # 更新听牌
                    self.action_dict = check_action_after_cut(self,tile_id) # 检查手牌操作 如果有切牌后操作则执行转移行为(询问其他玩家操作) 否则历时行为(下一个玩家摸牌)
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut" # 转移行为
                    else:
                        self.game_status = "deal_card" # 历时行为
                    return
                
            # 在加杠以后的case当中只包含和牌和pass一个选项 如果超时或者pass则进行历时行为
            case "waiting_action_qianggang":
                temp_jiagang_tile = self.jiagang_tile # 存储抢杠牌
                self.jiagang_tile = None # 删除抢杠牌
                if action_data:
                    if action_type == "hu_first" or action_type == "hu_second" or action_type == "hu_third": # 终结行为 可能有多人胡的情况
                        # 和牌 （荣和）
                        self.player_list[player_index].hand_tiles.append(temp_jiagang_tile) # 将和牌牌加入手牌最后一张
                        self.hu_class = action_type
                        self.game_status = "END"
                        return
                    elif action_type == "pass":
                        self.game_status = "deal_card" # 历时行为
                        return
                    else:
                        raise ValueError("抢杠和阶段action_type出现非hu和pass的值")
                # 超时放弃抢杠
                else:
                    self.game_status = "deal_card" # 历时行为
                    return


    # 获取玩家行动
    async def get_action(self, player_id: str, action_type: str, cutClass: bool, TileId: int,cutIndex: int,target_tile: int):
        try:
            # 检测行动合法性
            # 从游戏服务器的PlayerConnection中获取user_id
            player_conn = self.game_server.players.get(player_id)
            if not player_conn:
                print(f"玩家连接不存在: {player_id}")
                return
            if not player_conn.user_id:
                print(f"玩家未登录，无法执行操作: {player_id}")
                return
            user_id = player_conn.user_id
            
            # 查找对应的玩家和索引
            current_player = None
            player_index = -1
            # 通过比对user_id获取玩家索引
            for index, player in enumerate(self.player_list):
                if player.user_id == user_id:
                    current_player = player
                    player_index = index
                    break
            
            # 验证玩家是否在当前房间的玩家列表中
            if current_player is None:
                print(f"当前玩家不存在当前房间玩家列表中, user_id={user_id}, 可能是玩家操作发送到了错误的房间")
                return
            
            # 验证玩家索引是否有效
            if player_index not in [0, 1, 2, 3]:
                print(f"无效的玩家索引: {player_index}")
                return
            
            # 验证玩家是否在等待列表中（只有等待中的玩家才能执行操作）
            if player_index not in self.waiting_players_list:
                print(f"不是当前玩家的回合, player_index={player_index}, waiting_players_list={self.waiting_players_list}")
                return
            
            # 验证操作是否合法（检查操作是否在允许的操作列表中）
            if action_type not in self.action_dict.get(player_index, []):
                print(f"不是该玩家的合法行动, player_index={player_index}, action_type={action_type}, allowed_actions={self.action_dict.get(player_index, [])}")
                return

            # 操作合法，将操作数据放入队列
            if action_type == "cut": # 切牌操作
                await self.action_queues[player_index].put({
                    "action_type": action_type,
                    "cutClass": cutClass,
                    "TileId": TileId,
                    "cutIndex": cutIndex
                })
                # 设置事件
                self.action_events[player_index].set()
            else: # 其他指令操作（buhua, angang, jiagang, hu_self, chi_left, chi_mid, chi_right, peng, gang, hu_first, hu_second, hu_third, pass）
                await self.action_queues[player_index].put({
                    "action_type": action_type,
                    "target_tile": target_tile
                })
                # 设置事件
                self.action_events[player_index].set()
            
        except Exception as e:
            print(f"处理操作时发生错误: {e}", exc_info=True)
            raise Exception(f"处理操作时发生错误: {e}") # 出现问题时中断游戏
