import random
import asyncio
from typing import Dict, Optional, List
from response import Cut_response, Deal_tile_info, GameInfo, Response




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

        # 新增：用于玩家操作的事件和队列
        self.action_events = {}  # 玩家索引 -> Event
        self.action_queues = {}  # 玩家索引 -> Queue
        self.action_dict = {} # 玩家索引 -> 操作字典

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

    def deal_initial_tiles(self):
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

    async def wait_cut_action(self):
        """
        wait_action方法使用消息队列,接受cut_tiles传递的event状态和queue数据
        获取当前玩家,找到当前玩家的索引,将当前玩家的索引添加进入action_events和action_queues中,其中action_events
        代表当前玩家是否出牌,是一个事件布尔值,action_queues代表具体的操作数据,是一个队列
        每当循环开始,重置事件布尔值,并计算总计时时间(切牌时间 + 剩余时间)
        根据总等待时间进行计时循环,添加两个任务,等待1秒的time_task和等待玩家操作action_task
        await asyncio.wait 监听一个任务列表,通过设置return_when=asyncio.FIRST_COMPLETED 
        决定当任意一个任务完成时,决定返回完成的任务为第一个列表,随后则可以通过for task in pending 取消未完成的任务
        如果是action_task完成,则获取操作数据,处理出牌操作,如果time_task一直完成,直到总等待时间结束,则自动出牌。
        """
        # 获取当前玩家
        current_player = self.player_list[self.current_player_index]
        
        # 初始化事件和队列
        if self.current_player_index not in self.action_events:
            self.action_events[self.current_player_index] = asyncio.Event()
            self.action_queues[self.current_player_index] = asyncio.Queue()
        
        # 重置事件状态
        self.action_events[self.current_player_index].clear()
        
        # 标记是否已出牌
        is_cut = False
        
        # 计算总计时时间（切牌时间 + 剩余时间）
        total_time = self.cuttime + current_player.remaining_time
        used_time = 0
        # 变量初始化，确保广播时有值
        cut_tile = None  
        cut_class = True
        
        try:
            for _ in range(total_time):
                # 创建两个任务：等待1秒和等待玩家操作
                timer_task = asyncio.create_task(asyncio.sleep(1))
                action_task = asyncio.create_task(self.action_events[self.current_player_index].wait())
                # 等待任意一个任务完成
                done, pending = await asyncio.wait(
                    [timer_task, action_task],
                    return_when=asyncio.FIRST_COMPLETED
                )
                # 取消未完成的任务
                for task in pending:
                    task.cancel()
                # 检查是否收到玩家操作
                if action_task in done:
                    action_data = await self.action_queues[self.current_player_index].get()
                    cut_class = action_data.get("cutClass")  # 布尔值
                    tile_id = action_data.get("TileId")     # 现在是整数类型
                    if tile_id in current_player.hand_tiles:
                        current_player.hand_tiles.remove(tile_id)
                        current_player.discard_tiles.append(tile_id)
                        cut_tile = tile_id
                        is_cut = True
                        break
                    else:
                        print(f"找不到牌 {tile_id} 在玩家 {current_player.username} 的手牌中")
                        continue
                else:
                    used_time += 1
                    print(f"used_time={used_time}")
            # 如果is_cut为False,则自动出牌
            if not is_cut:
                cut_tile = current_player.hand_tiles[-1]
                current_player.hand_tiles.pop()
                current_player.discard_tiles.append(cut_tile)
            if used_time >= self.cuttime:
                current_player.remaining_time -= (used_time - self.cuttime)
            # 获取操作字典
            self.action_dict = await self.action_check_after_cut(cut_tile)
            # 广播切牌信息
            print(self.action_dict)
            """
            if self.action_dict:
                self.broadcast_cut_tiles(self.current_player_index,cut_class,cut_tile)
                
            else:
                self.broadcast_cut_tiles(self.current_player_index,cut_class,cut_tile)
            """
            self.action_dict = {}
            self.action_dict[self.next_current_num(self.current_player_index)] = "cut"
            await self.broadcast_cut_tiles(self.current_player_index, cut_class, cut_tile)

        except Exception as e:
            print(f"等待切牌操作时发生错误: {e}")
        finally:
            print("出牌结束")

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

    async def broadcast_deal_tile(self):
        # 遍历列表时获取索引
        for i, current_player in enumerate(self.player_list):
            if i == self.current_player_index:
                # 发送实际卡牌信息
                self.player_list[i].get_tile(self.tiles_list)
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    response = Response(
                        type="deal_tile_chinese",
                        success=True,
                        message="发牌",
                        deal_tile_info = Deal_tile_info(
                            remaining_time=current_player.remaining_time,
                            deal_player_index= self.current_player_index,
                            deal_tiles=self.player_list[i].hand_tiles[-1],
                            remain_tiles=len(self.tiles_list)
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播发牌信息")
            else:
                # 发送通用信息
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    response = Response(
                        type="deal_tile_chinese",
                        success=True,
                        message="发牌",
                        deal_tile_info = Deal_tile_info(
                            remaining_time=current_player.remaining_time,
                            deal_player_index= self.current_player_index,
                            deal_tiles=0,
                            remain_tiles=len(self.tiles_list)
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播发牌信息")
    # 切牌后检查操作
    async def action_check_after_cut(self,cut_tile):
        # 如果下家有C+1和C-1，则可以吃
        temp_action_dict = {}
        next_player_index = self.next_current_num(self.current_player_index)
        if cut_tile-1 in self.player_list[next_player_index].hand_tiles:
            if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                temp_action_dict["chi"] = next_player_index
        # 如果任意一家有C=2，则可以碰 如果C=3，则可以杠
        for item in self.player_list:
            if item.hand_tiles.count(cut_tile) == 2:
                temp_action_dict["peng"] = item.current_player_index
                if item.hand_tiles.count(cut_tile) == 3:
                    temp_action_dict["gang"] = item.current_player_index
        # 如果该牌是任意家的等待牌，则可以胡
        for item in self.player_list:
            if cut_tile in item.waiting_tiles:
                temp_action_dict["hu"] = item.current_player_index
        return temp_action_dict
    # 杠后检查操作
    async def action_check_after_gang(self,gang_tile):
        # 如果该牌是任意家的等待牌，则可以抢杠和
        temp_action_dict = {}
        for item in self.player_list:
            if gang_tile in item.waiting_tiles:
                temp_action_dict["hu"] = item.current_player_index
        return temp_action_dict

    async def game_loop_chinese(self):

        # 2. 遍历room.player_list 创建chineseplayer类 添加入game_state.player_list
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
        
        # 3. 初始化游戏
        self.init_tiles()
        self.deal_initial_tiles()
        self.game_status = "playing"
        self.action_dict[self.current_player_index] = "cut"

        # 4. 广播游戏开始
        await self.broadcast_game_start()
        
        # 5. 游戏主循环
        while self.game_status == "playing":
            # 等待当前玩家的操作
            await self.wait_cut_action()
            # 移动到下一个玩家
            await self.next_current_index()
            # 发牌并广播
            await self.broadcast_deal_tile()

    async def cut_tiles(self, player_id: str, cutClass: bool, TileId: int):
        try:
            # 不使用cut_player_index，因为这样可能会有人替别人出牌
            # 使用self访问实例属性然后找到username
            player_index = self.check_action_index(player_id)
            
            # 检查是否是当前玩家的回合
            if player_index != self.current_player_index:
                return
            # 检查是否是合法行动
            if self.action_dict[player_index] != "cut":
                return
            
            # 将操作数据放入队列
            await self.action_queues[player_index].put({
                "cutClass": cutClass,
                "TileId": TileId
            })
            
            self.action_events[player_index].set()
        except Exception as e:
            print(f"处理切牌操作时发生错误: {e}")
    # 检查行动合法性
    def check_action_index(self,player_id):
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
            return -2
        return player_index
