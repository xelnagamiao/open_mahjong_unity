import random
import asyncio
from typing import Dict, Optional, List
from response import Cut_response,GameInfo, Response, Ask_action_info,Action_info,Ask_hand_action_info,Buhua_animation_info
import time  # 直接导入整个模块




class ChinesePlayer:
    def __init__(self, username: str, tiles: list):
        self.username = username        # 玩家名
        self.hand_tiles = tiles         # 手牌
        self.hand_tiles_count = len(tiles) # 手牌数量
        self.discard_tiles = []         # 弃牌
        self.combination_tiles = []     # 组合牌
        self.waiting_tiles = []         # 听牌
        self.score = 0                  # 分数
        self.remaining_time = 20        # 剩余时间 （局时）
        self.current_player_index = 0   # 当前玩家索引 东南西北
        self.huapai_list = []          # 花牌列表

    def get_tile(self, tiles_list):
        element = tiles_list.pop(0)
        self.hand_tiles.append(element)

class ChineseGameState:
    # gamestate负责一个游戏对局进程，init属性包含游戏房间状态 player_list 包含玩家数据
    def __init__(self, room_id: str, room):
        self.room_id = room_id # 房间号
        self.room = room # 房间类
        
        self.player_list: List[ChinesePlayer] = [] # 玩家列表 包含chinesePlayer类
        self.tiles_list = [] # 牌堆
        self.current_player_index = 0 # 目前轮到的玩家
        self.random_seed = 0 # 随机种子
        self.game_status = "waiting"  # waiting, playing, finished
        self.cuttime = room.cuttime # 切牌时间
        self.game_time = room.game_time # 游戏时间
        self.current_round = 0 # 第几轮 # 默认4轮

        # 用于玩家操作的事件和队列
        self.action_events:Dict[int,asyncio.Event] = {0:asyncio.Event(),1:asyncio.Event(),2:asyncio.Event(),3:asyncio.Event()}  # 玩家索引 -> Event
        self.action_queues:Dict[int,asyncio.Queue] = {0:asyncio.Queue(),1:asyncio.Queue(),2:asyncio.Queue(),3:asyncio.Queue()}  # 玩家索引 -> Queue
        self.action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]} # 玩家索引 -> 操作字典
        # 行为 -> 优先级
        self.action_priority:Dict[str,int] = {
        "hu": 3, "peng": 2, "gang": 2, 
        "chi_left": 1, "chi_mid": 1, "chi_right": 1, 
        "pass": 0,"buhua":0,"cut":0,
        "angang":0,"jiagang":0}
# 麻将组合 -> 排斥的下集麻将组合列表
# 九莲宝灯 幺九刻-1
# 三风刻的三个刻子不计幺九刻
# 箭刻不计幺九刻
# 花牌不计起和番

        
            
        

    async def next_current_index(self):
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

    async def broadcast_game_start(self):
        """广播游戏开始信息"""
        # 基础游戏信息
        base_game_info = {
            'player_list': [p.username for p in self.player_list], # 玩家列表
            'player_positions': [  # 转换为列表格式
                {'username': p.username, 'position': p.current_player_index}
                for p in self.player_list
            ],
            'current_player_index': self.current_player_index, # 当前轮到的玩家索引
            'tile_count': len(self.tiles_list), # 牌山剩余牌数
            'random_seed': self.random_seed, # 随机种子
            'game_status': self.game_status, # 游戏状态
            'corrent_round': self.current_round, # 当前轮数
            'cuttime': self.cuttime, # 切牌时间
            'game_time': self.game_time, # 游戏时间
            'players_info': [] # ↓玩家信息
        }

        # 为每个玩家准备信息
        for player in self.player_list: # 遍历玩家列表
            player_info = {
                'username': player.username, # 用户名
                'hand_tiles_count': len(player.hand_tiles), # 手牌数量
                'discard_tiles': player.discard_tiles, # 弃牌
                'combination_tiles': player.combination_tiles, # 组合
                'remaining_time': player.remaining_time, # 剩余时间
                'current_player_index': player.current_player_index, # 东南西北位置
                'score': player.score # 分数
            }
            base_game_info['players_info'].append(player_info) # 将字典添加到列表中

        # 为每个玩家发送消息
        for current_player in self.player_list:
            try:
                # 如果player_list中有玩家在self.room.game_server.username_to_connection:
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    
                    # 将游戏信息字典转换为 GameInfo 类 并添加 self_hand_tiles 字段
                    game_info = GameInfo(
                        **base_game_info,
                        self_hand_tiles=current_player.hand_tiles  # 只包含当前玩家的手牌
                    )

                    response = Response(
                        type="game_start_chinese",
                        success=True,
                        message="游戏开始",
                        game_info=game_info
                    )
                    
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 发送游戏开始信息")
            except Exception as e:
                print(f"向玩家 {current_player.username} 发送消息失败: {e}")

    async def broadcast_cut_tiles(self, current_player_index: int, cut_class: bool, cut_tiles: int):
        """广播切牌信息"""
        for current_player in self.player_list:
            try:
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    
                    cut_info = Cut_response(
                        cut_player_index=current_player_index,
                        cut_class=cut_class,
                        cut_tiles=cut_tiles
                    )

                    response = Response(
                        type="cut_tiles_chinese",
                        success=True,
                        message="切牌信息",
                        cut_info=cut_info
                    )
                    
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播切牌信息")
            except Exception as e:
                print(f"向玩家 {current_player.username} 广播切牌信息失败: {e}")


    async def broadcast_buhua_animation(self):
        # 遍历列表时获取索引
        for i in range(0,3):
            if self.player_list[i].username in self.room.game_server.username_to_connection:
                player_conn = self.room.game_server.username_to_connection[self.player_list[i].username]
                response = Response(
                    type="broadcast_buhua_animation",
                    success=True,
                    message="广播补花动画",
                    buhua_animation_info=Buhua_animation_info(
                        player_index=i,
                        deal_tiles=self.player_list[i].hand_tiles[-1],
                        remain_tiles=len(self.tiles_list)
                    )
                )
                await player_conn.websocket.send_json(response.model_dump(exclude_none=True))
                print(f"已向玩家 {self.player_list[i].username} 广播补花动画")

    # 广播询问手牌操作 补花 加杠 暗杠 自摸 出牌
    async def broadcast_hand_action(self):
        # 遍历列表时获取索引
        for i, current_player in enumerate(self.player_list):
            if i == self.current_player_index:
                # 对当前玩家发送包含摸牌信息的消息
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    response = Response(
                        type="broadcast_hand_action",
                        success=True,
                        message="发牌，并询问手牌操作",
                        ask_hand_action_info = Ask_hand_action_info(
                            remaining_time=current_player.remaining_time,
                            player_index= self.current_player_index,
                            deal_tiles=self.player_list[i].hand_tiles[-1],
                            remain_tiles=len(self.tiles_list),
                            action_list=self.action_dict[i]
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播手牌操作信息")
            else:
                # 向其余玩家发送通用消息
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    response = Response(
                        type="broadcast_hand_action",
                        success=True,
                        message="发牌，并询问手牌操作",
                        deal_tile_info = Ask_hand_action_info(
                            remaining_time=current_player.remaining_time,
                            player_index= self.current_player_index,
                            deal_tiles=0,
                            remain_tiles=len(self.tiles_list),
                            action_list=self.action_dict[i]
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播手牌操作信息")





    async def broadcast_ask_action(self):
        # 遍历列表时获取索引
        for i, current_player in enumerate(self.player_list):
            if self.action_dict[i] != []:
                # 发送询问行动信息
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    response = Response(
                        type="ask_action_chinese",
                        success=True,
                        message="询问操作",
                        ask_action_info = Ask_action_info(
                            remaining_time=current_player.remaining_time,
                            action_list=[item for item in self.action_dict[i]],
                            cut_tile=self.player_list[self.current_player_index].discard_tiles[-1]
                        )
                    )
                    print(response)
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播询问操作信息")
            else:
                # 发送通用信息
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    response = Response(
                        type="ask_action_chinese",
                        success=True,
                        message="询问操作",
                        ask_action_info = Ask_action_info(
                            remaining_time=current_player.remaining_time,
                            action_list=[],
                            cut_tile=self.player_list[self.current_player_index].discard_tiles[-1]
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播询问操作信息")

    async def broadcast_action(self, action_type,remove_tile):
        # 遍历列表时获取索引
        if action_type == "pass":
            remove_tile = 0
        for i, current_player in enumerate(self.player_list):
            # 发送通用信息
            if current_player.username in self.room.game_server.username_to_connection:
                player_conn = self.room.game_server.username_to_connection[current_player.username]
                response = Response(
                    type="do_action_chinese",
                    success=True,
                    message="返回操作内容",
                    action_info = Action_info(
                        remaining_time = current_player.remaining_time,
                        do_action_type = action_type,
                        current_player_index = self.current_player_index,
                        tile_id = remove_tile
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                print(f"已向玩家 {current_player.username} 广播操作信息")

    # 切牌后检查操作
    def check_action_after_cut(self,cut_tile):
        # 如果下家有C+1和C-1，则可以吃
        temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
        next_player_index = self.next_current_num(self.current_player_index)
        if cut_tile <= 40:
            # left 左侧吃牌 [a-2,a-1,a]
            if cut_tile-2 in self.player_list[next_player_index].hand_tiles:
                if cut_tile-1 in self.player_list[next_player_index].hand_tiles:
                    if next_player_index not in temp_action_dict:
                        temp_action_dict[next_player_index] = []
                    temp_action_dict[next_player_index].append("chi_left")
            # mid 中间吃牌 [a-1,a,a+1]
        if cut_tile-1 in self.player_list[next_player_index].hand_tiles:
            if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                    if next_player_index not in temp_action_dict:
                        temp_action_dict[next_player_index] = []
                    temp_action_dict[next_player_index].append("chi_mid")
            # right 右侧吃牌 [a,a+1,a+2]
            if cut_tile+2 in self.player_list[next_player_index].hand_tiles:
                if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                    if next_player_index not in temp_action_dict:
                        temp_action_dict[next_player_index] = []
                    temp_action_dict[next_player_index].append("chi_right")
        # 如果任意一家有C=2，则可以碰 如果C=3，则可以杠
        for item in self.player_list:
            if item.hand_tiles.count(cut_tile) == 2:
                if item.current_player_index not in temp_action_dict:
                    temp_action_dict[item.current_player_index] = []
                temp_action_dict[item.current_player_index].append("peng")
                if item.hand_tiles.count(cut_tile) == 3:
                    temp_action_dict[item.current_player_index].append("gang")
        # 如果该牌是任意家的等待牌，则可以胡
        for item in self.player_list:
            if cut_tile in item.waiting_tiles:
                if item.current_player_index not in temp_action_dict:
                    temp_action_dict[item.current_player_index] = []
                temp_action_dict[item.current_player_index].append("hu")
        # 出牌玩家不可对自己的出牌进行操作
        temp_action_dict[self.current_player_index] = []
        return temp_action_dict
    # 杠后检查操作
    async def check_action_after_jiagang(self,gang_tile):
        # 如果该牌是任意家的等待牌，则可以抢杠和
        temp_action_dict = {}
        for item in self.player_list:
            if gang_tile in item.waiting_tiles:
                if item.current_player_index not in temp_action_dict:
                    temp_action_dict[item.current_player_index] = []
                temp_action_dict[item.current_player_index].append("hu")
        return temp_action_dict
    # 检查补花操作
    async def check_action_buhua(self,player_index):
        temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
        if any(carditem >= 50 for carditem in self.player_list[player_index].hand_tiles):
            temp_action_dict[player_index].append("buhua")
        return temp_action_dict

    async def check_action_hand_action(self,player_index):
        temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
        if any(carditem >= 50 for carditem in self.player_list[player_index].hand_tiles):
            temp_action_dict[player_index].append("buhua")
        if self.player_list[player_index].hand_tiles[-1] in self.player_list[player_index].waiting_tiles:
            temp_action_dict[player_index].append("hu")
        if any(self.player_list[player_index].hand_tiles.count(carditem) == 4 for carditem in self.player_list[player_index].hand_tiles):
            temp_action_dict[player_index].append("angang")
        for combination_tile in self.player_list[player_index].combination_tiles:
            if combination_tile[0] == "k":
                jiagang_index = int(combination_tile[1:])  # 提取所有数字
                if jiagang_index in self.player_list[player_index].hand_tiles:
                    temp_action_dict[player_index].append("jiagang")
        temp_action_dict[player_index].append("cut")
        return temp_action_dict

    async def game_loop_chinese(self):

        # 房间初始化

        # 创建chineseplayer类 添加入game_state.player_list
        for username in self.room.player_list:
            # 创建玩家对象
            player = ChinesePlayer(
                username=username,
                tiles=[]
            )
            self.player_list.append(player)
        # 打乱玩家顺序
        random.shuffle(self.player_list)
        # 枚举game_state的player_list 设置current_player_index,也就是东南西北
        for index, player in enumerate(self.player_list):
            player.current_player_index = index
        self.init_tiles() # 初始化牌山
        self.init_deal_tiles() # 初始化手牌


        
        for gameloopround in range(0,self.game_time):

            # 单局游戏初始化

            await self.broadcast_game_start()
            for index, player in enumerate(self.player_list):
                player.current_player_index = index
            # 遍历每个玩家,直到玩家选择pass或没有新的补花行为
            self.game_status = "waiting_buhua"

            for i in range(0,3):
                action_anymore = True
                while action_anymore:
                    self.action_dict = await self.check_action_buhua(i)
                    if self.action_dict[i] != []:
                        await self.broadcast_hand_action()
                        action_anymore = await self.wait_action() # 如果玩家补花了则继续检查能不能再补花
                    else:
                        action_anymore = False
                await self.next_current_index()

            # 游戏主循环 
            self.game_status = "waiting_hand_action" # 初始行动

            while self.game_status != "END":
                match self.game_status:
                    case "deal_card": # 无人吃碰杠和后发牌历时行为
                        await self.next_current_index()
                        self.player_list[self.current_player_index].get_tile(self.tiles_list)
                        self.game_status = "waiting_hand_action"
                    case "waiting_hand_action": # 摸牌,加杠,暗杠,补花后循环行为
                        self.action_dict[self.current_player_index].append("cut")
                        await self.check_hand_action()
                        await self.broadcast_deal_tile()
                        await self.wait_action()
                    case "waiting_action_after_cut": # 出牌后询问吃碰杠和行为
                        await self.broadcast_ask_action()
                        await self.wait_action()
                    case "waiting_action_after_jiagang": # 加杠后询问胡牌行为
                        pass
                    case "onlycut_afteraction": # 吃碰后行为
                        await self.broadcast_deal_tile()
                        await self.wait_action()
            while self.game_status == "END": # 胡牌后计分行为 由于可能有多人和的情况 所以需要循环
                pass
            if self.game_status == "count_point": # 计分行为
                pass




                


    async def cut_tiles(self, player_id: str, cutClass: bool, TileId: int):
        try:
            # 检查行动合法性 并获取玩家索引
            player_index = self.check_action_index(player_id,"cut")
            # 将操作数据放入队列
            await self.action_queues[player_index].put({
                "action_type": "cut",
                "cutClass": cutClass,
                "TileId": TileId
            })
            # 设置事件
            self.action_events[player_index].set()
        except Exception as e:
            print(f"处理切牌操作时发生错误: {e}")
    # 检查行动合法性
    def check_action_index(self,player_id,action_type):
        # check_action_index方法通过player_id获取玩家索引，确保不会有人替别人出牌，并且检测行动合法性
        player_conn = self.room.game_server.players[player_id]
        username = player_conn.username
        # 查找对应的玩家和索引
        current_player = None
        player_index = -1
        for i, p in enumerate(self.player_list):
            if p.username == username:
                current_player = p
                player_index = i
                break
        if current_player is None:
            return -2 # 当前玩家不存在玩家列表中
        if player_index != self.current_player_index:
            return -3 # 不是当前玩家的回合
        if action_type not in self.action_dict[player_index]:
            return -4 # 不是该玩家的合法行动
        return player_index # 返回玩家索引


    async def wait_action(self):
        
        waiting_players_list = [] # [2,3]
        used_time = 0 # 已用时间
        # 遍历所有可行动玩家，获取行动玩家列表和等待时间列表
        for player_index, action_list in self.action_dict.items():
            if action_list:  # 如果玩家有可用操作 将玩家加入列表并重置事件状态
                self.action_dict[player_index].append("pass") # [cut,pass]
                waiting_players_list.append(player_index)
                self.action_events[player_index].clear()

        # 如果等待玩家列表不为空且有玩家剩余时间小于(已用时间-步时)，则停止等待
        while waiting_players_list and any(self.player_list[i].remaining_time + self.cuttime > used_time for i in waiting_players_list):
            # 给每个可行动者创建一个消息队列任务，同时创建一个计时器任务
            task_list = []  # 任务列表
            task_to_player = {}  # 任务与玩家的映射
            
            for player_index in waiting_players_list:
                action_task = asyncio.create_task(self.action_events[player_index].wait())
                task_list.append(action_task)
                task_to_player[action_task] = player_index  # 在这里建立映射 用以通过任务获取玩家索引
            
            timer_task = asyncio.create_task(asyncio.sleep(1))
            task_list.append(timer_task)

            print(f"开始新一轮等待操作 waiting_players_list={waiting_players_list} action_dict={self.action_dict} used_time={used_time}")
            
            # 等待任意任务完成
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
                if task == timer_task:
                    # 计时器完成，增加已用时间
                    used_time += 1
                    # 注意：这里不需要重置任务，因为下一次循环开始时会创建新的任务
                else:
                    # 玩家操作完成，获取玩家索引
                    player_index = task_to_player[task]  # 使用映射获取玩家索引
                    
                    # 获取操作数据
                    action_data = await self.action_queues[player_index].get()
                    action_type = action_data.get("action_type")

                    used_time += time_end - time_start # 服务器计算操作时间
                    used_int_time = int(used_time) # 变量整数时间
                    if used_int_time >= self.cuttime:
                        self.player_list[player_index].remaining_time -= (used_int_time - self.cuttime)
                   
                    self.action_dict[player_index] = [] # 从可执行操作列表中移除操作
                    waiting_players_list.remove(player_index) # 从玩家等待列表中移除玩家
                    
                    # 检查当前操作是否是最高优先级的
                    do_interrupt = True
                    for temp_player_index in waiting_players_list:
                        for action in self.action_dict[temp_player_index]:
                            # 如果有其他更高优先级的操作，则继续等待
                            if self.action_priority[action_type] < self.action_priority[action]:
                                do_interrupt = False
                    
                    # 如果是最高优先级，中断等待
                    if do_interrupt:
                        waiting_players_list = [] # 清空等待列表，强制结束循环
                        match self.game_status:
                            # 补花轮特殊case 是循环行为
                            case "waiting_buhua":
                                if action_type == "buhua":
                                    self.player_list[player_index].get_tile(self.tiles_list)
                                    self.huapai_list.append(max(self.player_list[player_index].hand_tiles))
                                    self.player_list[player_index].hand_tiles.remove(max(self.player_list[player_index].hand_tiles))
                                    await self.broadcast_buhua_animation()
                                    return True # 补花以后如果能够补花继续询问
                                elif action_type == "pass":
                                    return False # pass表示不要补花
                             #手牌行为包含 切牌 暗杠 加杠 自摸 补花 其中自摸是终结条件 补花 加杠 暗杠是循环行为 切牌是转移/历时行为
                            case "waiting_hand_action":
                                if action_type == "cut":
                                    cut_class = action_data.get("cutClass")  # 布尔值
                                    tile_id = action_data.get("TileId") 
                                    if tile_id in self.player_list[player_index].hand_tiles:
                                        if cut_class:
                                            self.player_list[player_index].hand_tiles.remove(tile_id)
                                            self.player_list[player_index].discard_tiles.append(tile_id)
                                            remove_tile = tile_id
                                        else:
                                            remove_tile = self.player_list[player_index].hand_tiles.pop(-1)
                                            self.player_list[player_index].discard_tiles.append(remove_tile)
                                    await self.broadcast_cut_tiles(player_index,cut_class,tile_id)
                                    self.action_dict = self.check_action_after_cut(remove_tile)
                                    if any(self.action_dict[i] for i in self.action_dict):
                                        self.game_status = "waiting_action_after_cut" # 转移行为
                                    else:
                                        self.game_status = "deal_card" # 历时行为

                                elif action_type == "angang":
                                    # 暗杠后重新检查手牌操作 广播手牌动作
                                    angang_tile = action_data.get("angang_tile")
                                    self.player_list[player_index].hand_tiles.remove(angang_tile)
                                    self.player_list[player_index].combination_tiles.append(f"g{angang_tile}")
                                    self.player_list[player_index].get_tile(self.tiles_list)
                                    await self.check_hand_action(player_index)
                                    await self.broadcast_deal_tile()

                                elif action_type == "jiagang":
                                    # 加杠后重新检查手牌操作 广播手牌动作
                                    jiagang_tile = action_data.get("jiagang_tile")
                                    self.player_list[player_index].hand_tiles.remove(jiagang_tile)
                                    self.player_list[player_index].combination_tiles.remove(f"k{jiagang_tile}")
                                    self.player_list[player_index].combination_tiles.append(f"g{jiagang_tile}")
                                    self.player_list[player_index].get_tile(self.tiles_list)
                                    await self.check_hand_action(player_index)
                                    await self.broadcast_deal_tile()

                                elif action_type == "buhua":
                                    # 补花后重新检查手牌操作 广播手牌动作
                                    self.player_list[player_index].get_tile(self.tiles_list)
                                    self.player_list[player_index].hand_tiles.remove(max(self.player_list[player_index].hand_tiles))
                                    await self.check_hand_action(player_index)
                                    await self.broadcast_deal_tile()

                                elif action_type == "hu":
                                    # 胡牌后结束本轮
                                    pass
                                # "waiting_hand_action" 不存在pass行为

                            case "waiting_action_after_cut": # 吃 碰 杠 荣和 pass 其中吃碰杠是转移行为 荣和是终结行为 pass是历时行为
                                remove_tile = self.player_list[self.current_player_index].discard_tiles[-1] # 获取最后一张牌
                                if action_type == "chi_left":
                                    self.player_list[player_index].hand_tiles.remove(remove_tile-1)
                                    self.player_list[player_index].hand_tiles.remove(remove_tile-2)
                                    self.player_list[player_index].combination_tiles.append(f"s{remove_tile+1}")
                                if action_type == "chi_mid":
                                    self.player_list[player_index].hand_tiles.remove(remove_tile-1)
                                    self.player_list[player_index].hand_tiles.remove(remove_tile+1)
                                    self.player_list[player_index].combination_tiles.append(f"s{remove_tile}")
                                if action_type == "chi_right":
                                    self.player_list[player_index].hand_tiles.remove(remove_tile+1)
                                    self.player_list[player_index].hand_tiles.remove(remove_tile+2)
                                    self.player_list[player_index].combination_tiles.append(f"s{remove_tile-1}")
                                if action_type == "peng":
                                    self.player_list[player_index].hand_tiles.remove(remove_tile)
                                    self.player_list[player_index].hand_tiles.remove(remove_tile)
                                    self.player_list[player_index].combination_tiles.append(f"k{remove_tile}")
                                if action_type == "gang":
                                    self.player_list[player_index].hand_tiles.remove(remove_tile)
                                    self.player_list[player_index].hand_tiles.remove(remove_tile)
                                    self.player_list[player_index].hand_tiles.remove(remove_tile)
                                    self.player_list[player_index].combination_tiles.append(f"g{remove_tile}")
                                if action_type == "hu": # 终结行为 可能有多人胡的情况
                                    pass 
                                if action_type == "chi_left" or action_type == "chi_mid" or action_type == "chi_right" or action_type == "peng" or action_type == "gang":
                                    # 只保留行动者的cut方法 吃碰杠后只能出牌
                                    self.current_player_index = player_index
                                    for playeritem in self.action_dict:
                                        if playeritem == player_index:
                                            self.action_dict[playeritem] = ["cut"]
                                        else:
                                            self.action_dict[playeritem] = []
                                    await self.broadcast_action(action_type,remove_tile)
                                    self.game_status = "onlycut_afteraction" # 转移行为
                                if action_type == "pass":
                                    self.game_status = "deal_card" # 历时行为
                                
                            case "onlycut_afteraction":
                                if action_type == "cut":
                                    cut_class = action_data.get("cutClass")  # 布尔值
                                    tile_id = action_data.get("TileId") 
                                    if tile_id in self.player_list[player_index].hand_tiles:
                                        if cut_class:
                                            self.player_list[player_index].hand_tiles.remove(tile_id)
                                            self.player_list[player_index].discard_tiles.append(tile_id)
                                            remove_tile = tile_id
                                        else:
                                            remove_tile = self.player_list[player_index].hand_tiles.pop(-1)
                                            self.player_list[player_index].discard_tiles.append(remove_tile)
                                    await self.broadcast_cut_tiles(player_index,cut_class,tile_id)
                                    self.action_dict = self.check_action_after_cut(remove_tile)
                                    if any(self.action_dict[i] for i in self.action_dict):
                                        self.game_status = "waiting_action_after_cut" # 转移行为
                                    else:
                                        self.game_status = "deal_card" # 历时行为
                            
                            case "waiting_action_after_plusgang": # 抢杠和
                                if action_type == "hu":
                                    pass
                                elif action_type == "pass":
                                    return False
                                else:
                                    return True
        # 如果超时 直接剩余时间归零
        if waiting_players_list:
            for i in waiting_players_list:
                self.player_list[i].remaining_time = 0
            match self.game_status:
                # 如果补花轮超时 则直接返回False
                case "waiting_buhua":
                    return False
                case "waiting_hand_action":
                # "waiting_hand_action"超时 只可能是切牌行为
                    cut_class = True  # 超时自动摸切
                    tile_id = action_data.get("TileId") 
                    if tile_id in self.player_list[player_index].hand_tiles:
                        if cut_class:
                            self.player_list[player_index].hand_tiles.remove(tile_id)
                            self.player_list[player_index].discard_tiles.append(tile_id)
                            remove_tile = tile_id
                        else:
                            remove_tile = self.player_list[player_index].hand_tiles.pop(-1)
                            self.player_list[player_index].discard_tiles.append(remove_tile)
                    self.action_dict = self.check_action_after_cut(remove_tile)
                    await self.broadcast_cut_tiles(player_index,cut_class,tile_id)
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut"
                    else:
                        self.game_status = "deal_card"
                case "onlycut_afteraction":
                    # "onlycut_afteraction"超时 只可能是切牌行为
                    cut_class = False  # 超时自动手切 因为吃碰杠后不可能摸切
                    tile_id = self.player_list[player_index].hand_tiles[-1]
                    if tile_id in self.player_list[player_index].hand_tiles:
                        if cut_class:
                            self.player_list[player_index].hand_tiles.remove(tile_id)
                            self.player_list[player_index].discard_tiles.append(tile_id)
                            remove_tile = tile_id
                        else:
                            remove_tile = self.player_list[player_index].hand_tiles.pop(-1)
                            self.player_list[player_index].discard_tiles.append(remove_tile)
                    self.action_dict = self.check_action_after_cut(remove_tile)
                    await self.broadcast_cut_tiles(player_index,cut_class,tile_id)
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut"
                    else:
                        self.game_status = "deal_card"
                case "waiting_action_after_cut":
                    # 如果"waiting_action_after_cut"超时 只可能是历时行为
                    self.game_status = "deal_card"



    async def get_action(self, player_id: str, action_type: str):
        try:
            # 检查行动合法性 并获取玩家索引
            player_index = self.check_action_index(player_id,action_type)
            # 将操作数据放入队列
            if player_index in [0,1,2,3]:
                await self.action_queues[player_index].put({
                    "action_type": action_type
                })
                # 设置事件
                self.action_events[player_index].set()
            else:
                error_message = ""
                if player_index == -1:
                    error_message = "check_action_index发生错误"
                elif player_index == -2:
                    error_message = "当前玩家不存在玩家列表中"
                elif player_index == -3:
                    error_message = "不是当前玩家的回合"
                elif player_index == -4:
                    error_message = "不是该玩家的合法行动"
                raise Exception(f"操作错误: {error_message}")
        except Exception as e:
            print(f"处理操作时发生错误: {e}")
            raise  # 重新抛出异常，让调用者知道发生了错误
    

