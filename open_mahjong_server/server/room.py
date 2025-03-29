from response import RoomResponse,Response

class Room: # 房间类
    # 初始化房间数据
    def __init__(self, room_id: str, host_name: str, room_name: str, game_time: int, player_list: list, password: str, game_server, cuttime: int):
        self.room_id = room_id
        self.room_name = room_name
        self.game_time = game_time
        self.player_list = player_list
        self.player_count = 1
        self.password = password
        self.game_server = game_server
        self.host_name = player_list[0]  # 房主总是第一个玩家
        self.cuttime = cuttime
    # 在房间有变化时广播消息给房间内所有玩家
    async def broadcast_to_players(self):
        self.host_name = self.player_list[0]  # 更新房主名字
        room_info = RoomResponse(
            player_list=self.player_list,
            player_count=self.player_count,
            host_name=self.host_name,
            game_time=self.game_time,
            room_id=self.room_id,
            room_name=self.room_name,
            cuttime=self.cuttime
        )
        response = Response(
            type="get_room_info",
            success=True,
            message="房间信息更新",
            room_info=room_info
        )

        # 使用 username_to_connection 映射直接获取连接
        for username in self.player_list:
            if username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[username]
                try:
                    print(f"正在广播给玩家 {username}")
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"广播成功")
                except Exception as e:
                    print(f"广播给玩家 {username} 失败: {e}")