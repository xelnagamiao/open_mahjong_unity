import random
import asyncio
from typing import Dict, List
import time
from .method_Action_Check import check_action_after_cut,check_action_jiagang,check_action_buhua,check_action_hand_action
from .method_Boardcast import broadcast_game_start,broadcast_ask_hand_action,broadcast_ask_other_action,broadcast_do_action


class ChinesePlayer:
    def __init__(self, username: str, tiles: list, remaining_time: int):
        self.username = username                      # 玩家名
        self.hand_tiles = tiles                       # 手牌
        self.huapai_list = []                         # 花牌列表
        self.discard_tiles = []                       # 弃牌
        self.discard_origin = None                    # 理论弃牌 (日麻中启用,避免在弃牌中吃碰卡牌以后的不准确)
        self.combination_tiles = []                   # 组合牌
        # combination_mask组合牌掩码 0代表竖 1代表横 2代表暗面 3代表上侧(加杠) 4代表空 因为普通的存储方式会造成掉线以后吃牌形状丢失 所以使用掩码存储
        # [1,13,0,11,0,12] = 吃上家 312m s12
        # [0,17,1,17,0,17] = 碰对家 777m k17
        # [1,22,1,22,0,22,0,22] = 加杠 2222p g22
        # [2,17,2,17,2,17,2,17] = 暗杠 7777m G17 (国标中17应使用0代替 避免暗杠信息泄露)
        self.combination_mask = []                    
        self.score = 0                                # 分数
        self.remaining_time = remaining_time          # 剩余时间 （局时）
        self.player_index = 0                         # 玩家索引 东南西北 0 1 2 3
        self.waiting_tiles = {}                       # 听牌
        self.result_hepai = []                        # 和牌结果
        

    def get_tile(self, tiles_list):
        element = tiles_list.pop(0)
        self.hand_tiles.append(element)

class ChineseGameState:
    # chinesegamestate负责一个国标麻将对局进程，init属性包含游戏房间状态 player_list 包含玩家数据
    def __init__(self, game_server, room_data: dict):
        self.game_server = game_server # 游戏服务器
        self.player_list: List[ChinesePlayer] = [] # 玩家列表 包含chinesePlayer类
        self.Chinese_Hepai_Check = self.game_server.room_manager.Chinese_Hepai_Check
        self.Chinese_Tingpai_Check = self.game_server.room_manager.Chinese_Tingpai_Check
        for player in room_data["player_list"]:
            self.player_list.append(ChinesePlayer(player,[],room_data["round_timer"]))

        self.room_id = room_data["room_id"] # 房间ID
        self.tips = room_data["tips"] # 是否提示
        self.max_round = room_data["game_round"] # 最大局数
        self.step_time = room_data["step_timer"] # 步时
        self.round_time = room_data["round_timer"] # 局时

        self.tiles_list = [] # 牌堆
        self.current_player_index = 0 # 目前轮到的玩家
        self.random_seed = 0 # 随机种子
        self.game_status = "waiting"  # waiting, playing, finished
        self.action_tick = 0 # 操作帧
        self.current_round = 1 # 目前小局数 (max_round * 4)

        # 用于玩家操作的事件和队列
        self.action_events:Dict[int,asyncio.Event] = {0:asyncio.Event(),1:asyncio.Event(),2:asyncio.Event(),3:asyncio.Event()}  # 玩家索引 -> Event
        self.action_queues:Dict[int,asyncio.Queue] = {0:asyncio.Queue(),1:asyncio.Queue(),2:asyncio.Queue(),3:asyncio.Queue()}  # 玩家索引 -> Queue
        self.waiting_players_list = [] # 等待操作的玩家列表

        # 所有check方法都返回action_dict字典
        self.action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]} # 玩家索引 -> 操作列表
        # 行为 -> 优先级 用于在多人共通等待行为时判断是否需要等待更高优先级玩家的操作或直接结束更低优先级玩家的等待
        self.action_priority:Dict[str,int] = {
        "hu": 3, "peng": 2, "gang": 2, 
        "chi_left": 1, "chi_mid": 1, "chi_right": 1, 
        "pass": 0,"buhua":0,"cut":0,
        "angang":0,"jiagang":0,"deal":0}

    

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

    def init_tiles(self):
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
        # 生成牌堆 tiles_list
        self.tiles_list = []
        for tile in sth_tiles_set:
            self.tiles_list.extend([tile] * 4)
        self.tiles_list.extend(hua_tiles_set)
        random.shuffle(self.tiles_list)

    def init_deal_tiles(self):
        # 分配每位玩家13张牌
        for player in self.player_list:
            for _ in range(13):
                player.get_tile(self.tiles_list)
        # 庄家额外摸一张
        self.player_list[0].get_tile(self.tiles_list)

    def get_index_relative_position(self,self_index,other_index):
        if self_index == 0:
            if other_index == 1:
                return "left"
            elif other_index == 2:
                return "top"
            elif other_index == 3:
                return "right"
        elif self_index == 1:
            if other_index == 0:
                return "right"
            elif other_index == 2:
                return "left"
            elif other_index == 3:
                return "top"
        elif self_index == 2:
            if other_index == 0:
                return "top"
            elif other_index == 1:
                return "right"
            elif other_index == 3:
                return "left"
        elif self_index == 3:
            if other_index == 0:
                return "left"
            elif other_index == 1:
                return "top"
            elif other_index == 2:
                return "right"

    async def game_loop_chinese(self):

        # 房间初始化 打乱玩家顺序
        random.shuffle(self.player_list)
        # 根据打乱的玩家顺序设置玩家索引
        for index, player in enumerate(self.player_list):
            player.player_index = index

        # 游戏主循环
        while self.current_round <= self.max_round * 4:

            self.init_tiles() # 初始化牌山
            self.init_deal_tiles() # 初始化手牌
            # 广播游戏开始
            await broadcast_game_start(self)
            
            # 遍历每个玩家,直到玩家选择pass或没有新的补花行为
            self.game_status = "waiting_buhua"
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
                            # deal 操作需要广播给所有玩家 因为deal操作是摸牌操作 需要让所有玩家都知道其他人摸牌了
                            self.action_dict = {0:["deal"],1:["deal"],2:["deal"],3:["deal"]}
                            await broadcast_ask_hand_action(self)
                            self.action_dict = {0:[],1:[],2:[],3:[]}
                        # 如果玩家选择pass 则下一轮循环
                        else:
                            action_anymore = False
                    # 如果不能补花 则下一轮循环
                    else:
                        action_anymore = False

            # 游戏主循环
            self.game_status = "waiting_hand_action" # 初始行动
            self.current_player_index = 0 # 初始玩家索引
            # 手动执行一次waiting_hand_action状态 因为庄家首次出牌不需要摸牌
            self.action_dict = check_action_hand_action(self,self.current_player_index,is_first_action=True) # 允许可执行的手牌操作
            # 第一次不发牌
            for i in self.action_dict:
                self.action_dict[i].remove("deal")
            await broadcast_ask_hand_action(self) # 广播手牌操作
            await self.wait_action() # 等待手牌操作

            while self.game_status != "END":
                match self.game_status:
                    
                    case "deal_card": # 无人吃碰杠和后发牌历时行为
                        self.next_current_index() # 切换到下一个玩家
                        self.player_list[self.current_player_index].get_tile(self.tiles_list) # 摸牌
                        self.game_status = "waiting_hand_action" # 切换到摸牌后状态
                        
                    case "waiting_hand_action": # 摸牌,加杠,暗杠,补花后行为
                        self.action_dict = check_action_hand_action(self,self.current_player_index) # 允许可执行的手牌操作
                        await broadcast_ask_hand_action(self) # 广播手牌操作
                        await self.wait_action() # 等待手牌操作

                    case "waiting_action_after_cut": # 出牌后询问吃碰杠和行为
                        await broadcast_ask_other_action(self) # 广播是否吃碰杠和
                        await self.wait_action() # 等待吃碰杠和操作

                    case "waiting_action_after_jiagang": # 加杠后询问胡牌行为
                        await broadcast_ask_other_action(self) # 广播是否胡牌

                    case "onlycut_afteraction": # 吃碰后行为
                        self.action_dict = {0:[],1:[],2:[],3:[]}
                        self.action_dict[self.current_player_index].append("cut") # 吃碰后只允许切牌
                        await self.wait_action()

            while self.game_status == "END": # 胡牌后计分行为 由于可能有多人和的情况 所以需要循环
                pass
            if self.game_status == "count_point": # 计分行为
                pass

            self.current_round += 1



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
                    action_data = await self.action_queues[player_index].get() # 获取操作数据
                    action_type = action_data.get("action_type") # 获取操作类型

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
                            if self.action_priority[action_type] < self.action_priority[action]:
                                do_interrupt = False
                    
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
            case "waiting_buhua":
                # 如果有操作
                if action_data:
                    # 等待补花阶段的action_type只能是buhua
                    if action_type == "buhua": 
                        if action_data:
                            max_tile = max(self.player_list[self.current_player_index].hand_tiles) # 获取最大牌（花牌数字永远最大）
                            self.player_list[self.current_player_index].get_tile(self.tiles_list) # 随后摸牌 否则可能补花摸到的牌
                            self.player_list[self.current_player_index].huapai_list.append(max_tile) # 将最大牌加入花牌列表
                            self.player_list[self.current_player_index].hand_tiles.remove(max_tile) # 从手牌中移除最大牌
                            await broadcast_do_action(self,action_list = ["buhua"],action_player = self.current_player_index,buhua_tile = max_tile) # 广播补花动画
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
                        cut_class = action_data.get("cutClass")  # 获取手模切布尔值
                        tile_id = action_data.get("TileId") # 获取切牌id
                        self.player_list[self.current_player_index].hand_tiles.remove(tile_id) # 从手牌中移除切牌
                        self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将切牌加入弃牌堆
                        await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = cut_class) # 广播切牌动画 切牌玩家索引 手模切 切牌id 操作帧
                        # 检查手牌操作 如果有切牌后操作则执行转移行为(询问其他玩家操作) 否则历时行为(下一个玩家摸牌)
                        self.action_dict = check_action_after_cut(self,tile_id)
                        if any(self.action_dict[i] for i in self.action_dict):
                            self.game_status = "waiting_action_after_cut" # 转移行为
                        else:
                            self.game_status = "deal_card" # 历时行为
                        return
                    elif action_type == "angang": # 暗杠
                        angang_tile = action_data.get("angang_tile") # 获取暗杠牌
                        self.player_list[self.current_player_index].hand_tiles.remove(angang_tile) # 从手牌中移除暗杠牌
                        self.player_list[self.current_player_index].combination_tiles.append(f"G{angang_tile}") # 将暗杠牌加入组合牌
                        self.player_list[self.current_player_index].get_tile(self.tiles_list) # 随后摸牌
                        self.player_list[self.current_player_index].combination_mask.append([2,0,2,0,2,0,2,0]) # 添加组合掩码
                        await broadcast_do_action(self,action_list = ["angang"],action_player = self.current_player_index) # 广播暗杠动画
                        # 暗杠属于循环行为 重新返回上层
                        return
                    elif action_type == "jiagang": # 加杠
                        # 加杠后重新检查手牌操作 广播手牌动作
                        jiagang_tile = action_data.get("jiagang_tile") # 获取加杠牌
                        # 替换组合掩码 找到组合位置
                        combination_index = -1
                        for i,combination in enumerate(self.player_list[self.current_player_index].combination_tiles):
                            if combination == f"k{jiagang_tile}":
                                combination_index = i
                                break
                        # 通过组合位置找到掩码位置
                        for i, mask in enumerate(self.player_list[self.current_player_index].combination_mask[combination_index]):
                            if mask == 1:  # 找到数组下标 [0,A,0,A,1,A] 获取1的位置 例如4
                                # 在索引4后插入加杠牌 再添加掩码1 结果[0,A,0,A,1,A,1,A]
                                self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, jiagang_tile)
                                self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, 1)
                                break  # 找到第一个1就停止，避免重复插入
                        self.player_list[self.current_player_index].hand_tiles.remove(jiagang_tile) # 从手牌中移除加杠牌
                        self.player_list[self.current_player_index].combination_tiles.remove(f"k{jiagang_tile}") # 从组合牌中移除刻子
                        self.player_list[self.current_player_index].combination_tiles.append(f"g{jiagang_tile}") # 将明杠牌加入组合牌
                        self.player_list[self.current_player_index].get_tile(self.tiles_list) # 随后摸牌
                        await broadcast_do_action(self,action_list = ["jiagang"],action_player = self.current_player_index,combination_mask = self.player_list[self.current_player_index].combination_mask[combination_index]) # 广播加杠动画
                        self.action_dict = check_action_jiagang(self,self.current_player_index) # 检查手牌操作
                        if any(self.action_dict[i] for i in self.action_dict):
                            self.game_status = "waiting_action_after_jiagang" # 转移行为
                        else:
                            self.game_status = "deal_card" # 历时行为
                        return
                    elif action_type == "buhua": # 补花
                        max_tile = max(self.player_list[self.current_player_index].hand_tiles) # 获取最大牌（花牌数字永远最大）
                        self.player_list[self.current_player_index].get_tile(self.tiles_list) # 随后摸牌 否则可能补花摸到的牌
                        self.player_list[self.current_player_index].huapai_list.append(max_tile) # 将最大牌加入花牌列表
                        self.player_list[self.current_player_index].hand_tiles.remove(max_tile) # 从手牌中移除最大牌
                        await broadcast_do_action(self,action_list = ["buhua"],action_player = self.current_player_index,buhua_tile = max_tile) # 广播补花动画
                        # 补花属于循环行为 重新返回上层
                        return
                    elif action_type == "hu": # 自摸
                        ### 等待完成
                        self.game_status = "END"
                        return
                    else:
                        print(f"摸牌后手牌阶段action_type出现非cut,angang,jiagang,buhua,hu的值: {action_type}")
                # 超时自动摸切
                else:
                    cut_class = True # 摸切
                    tile_id = self.player_list[self.current_player_index].hand_tiles.pop(-1) # 最后一张手牌是最晚摸到的牌 获取最后一张手牌
                    self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将摸切牌加入弃牌堆
                    await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = cut_class) # 广播摸切动画 摸切玩家索引 手模切 摸切牌id 操作帧
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
                if action_data:
                    if action_type == "chi_left": # [tile_id-2,tile_id-1,tile_id]
                        self.player_list[player_index].hand_tiles.remove(tile_id-1)
                        self.player_list[player_index].hand_tiles.remove(tile_id-2)
                        self.player_list[player_index].combination_tiles.append(f"s{tile_id-1}")
                        combination_mask = [1,tile_id,0,tile_id-1,0,tile_id-2]
                    elif action_type == "chi_mid": # [tile_id-1,tile_id,tile_id+1]
                        self.player_list[player_index].hand_tiles.remove(tile_id-1)
                        self.player_list[player_index].hand_tiles.remove(tile_id+1)
                        self.player_list[player_index].combination_tiles.append(f"s{tile_id}")
                        combination_mask = [1,tile_id,0,tile_id-1,0,tile_id+1]
                    elif action_type == "chi_right": # [tile_id,tile_id+1,tile_id+2]
                        self.player_list[player_index].hand_tiles.remove(tile_id+1)
                        self.player_list[player_index].hand_tiles.remove(tile_id+2)
                        self.player_list[player_index].combination_tiles.append(f"s{tile_id+1}")
                        combination_mask = [1,tile_id,0,tile_id+1,0,tile_id+2]
                    elif action_type == "peng": # [tile_id',tile_id',tile_id]
                        self.player_list[player_index].hand_tiles.remove(tile_id)
                        self.player_list[player_index].hand_tiles.remove(tile_id)
                        self.player_list[player_index].combination_tiles.append(f"k{tile_id}")
                        relative_position = self.get_index_relative_position(player_index,self.current_player_index) # 获取相对位置 (操作者,出牌者)
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
                        relative_position = self.get_index_relative_position(player_index,self.current_player_index)
                        if relative_position == "left":
                            combination_mask = [1,tile_id,0,tile_id,0,tile_id,0,tile_id]
                        elif relative_position == "right":
                            combination_mask = [0,tile_id,0,tile_id,0,tile_id,1,tile_id]
                        elif relative_position == "top":
                            combination_mask = [0,tile_id,1,tile_id,0,tile_id,0,tile_id]
                    elif action_type == "hu": # 终结行为 可能有多人胡的情况
                        ### 等待完成
                        self.game_status = "END"
                        return
                    # 如果发生吃碰杠而不是和牌 则发生转移行为
                    if action_type == "chi_left" or action_type == "chi_mid" or action_type == "chi_right" or action_type == "peng" or action_type == "gang":
                        self.player_list[self.current_player_index].discard_tiles.pop(-1) # 删除弃牌堆的最后一张
                        self.player_list[player_index].combination_mask.append(combination_mask) # 添加组合掩码
                        self.current_player_index = player_index # 转移行为后 当前玩家索引变为操作玩家索引
                        self.game_status = "onlycut_afteraction" # 转移行为
                        # 广播吃碰杠动画
                        await broadcast_do_action(self,action_list = [action_type],action_player = self.current_player_index,combination_mask = combination_mask)
                        return
                # 如果不吃碰杠或者超时则进行历时行为 继续下一个玩家摸牌
                self.game_status = "deal_card" # 历时行为
                return
            
            # 在转移行为以后只能进行切牌操作
            case "onlycut_afteraction":
                if action_data:
                    if action_type == "cut": # 切牌
                        cut_class = action_data.get("cutClass")  # 获取手模切布尔值
                        tile_id = action_data.get("TileId") # 获取切牌id
                        self.player_list[self.current_player_index].hand_tiles.remove(tile_id) # 从手牌中移除切牌
                        self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将切牌加入弃牌堆
                        # 广播切牌动画
                        await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = cut_class)
                        # 检查手牌操作 如果有切牌后操作则执行转移行为(询问其他玩家操作) 否则历时行为(下一个玩家摸牌)
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
                    cut_class = True # 摸切
                    tile_id = self.player_list[self.current_player_index].hand_tiles.pop(-1) # 最后一张手牌是最晚摸到的牌 获取最后一张手牌
                    self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将摸切牌加入弃牌堆
                    # 广播摸切动画
                    await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = cut_class)
                    self.action_dict = check_action_after_cut(self,tile_id) # 检查手牌操作 如果有切牌后操作则执行转移行为(询问其他玩家操作) 否则历时行为(下一个玩家摸牌)
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut" # 转移行为
                    else:
                        self.game_status = "deal_card" # 历时行为
                    return
                
            # 在加杠以后的case当中只包含和牌和pass一个选项 如果超时或者pass则进行历时行为
            case "waiting_action_after_jiagang":
                if action_data:
                    if action_type == "hu": # 终结行为 可能有多人胡的情况
                        ### 等待完成
                        self.game_status = "END"
                        return
                    elif action_type == "pass":
                        self.game_status = "deal_card" # 历时行为
                        return
                    else:
                        raise ValueError("抢杠和阶段action_type出现非hu和pass的值")
                else:
                    self.game_status = "deal_card" # 历时行为
                    return



    # 获取玩家行动
    async def get_action(self, player_id: str, action_type: str, cutClass: bool, TileId: int):
        try:
            # 检测行动合法性
            # 从游戏服务器的PlayerConnection中获取username
            player_conn = self.game_server.players[player_id]
            username = player_conn.username
            # 查找对应的玩家和索引
            current_player = None
            player_index = -1
            # 通过比对username获取玩家索引
            for index, player in enumerate(self.player_list):
                if player.username == username:
                    current_player = player
                    player_index = index
                    break
            if current_player is None: # 未找到用户名
                print(f"当前玩家不存在当前房间玩家列表中,可能是玩家操作发送到了错误的房间")
            elif player_index not in self.waiting_players_list:
                print(f"不是当前玩家的回合,可能是在错误的时间发送了消息")
                if action_type not in self.action_dict[player_index]:
                    print(f"不是该玩家的合法行动,可能是错误时间发送消息或者客户端程序出现错误")

            # 将操作数据放入队列
            if action_type == "cut": # 切牌操作
                await self.action_queues[player_index].put({
                    "action_type": action_type,
                    "cutClass": cutClass,
                    "TileId": TileId
                })
                # 设置事件
                self.action_events[player_index].set()
            elif player_index in [0,1,2,3]: # 指令操作
                await self.action_queues[player_index].put({
                    "action_type": action_type
                })
                # 设置事件
                self.action_events[player_index].set()
            else:
                raise Exception(f"操作错误: {player_index}")
            
        except Exception as e:
            print(f"处理操作时发生错误: {e}")
            raise  # 重新抛出异常，让调用者知道发生了错误

