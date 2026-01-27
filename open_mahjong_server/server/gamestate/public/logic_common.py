# 通用逻辑方法 - 所有规则共享
# 输入自身索引和他家索引，获取相对位置
def get_index_relative_position(self_index: int, other_index: int) -> str:
    """
    获取两个玩家之间的相对位置
    
    Args:
        self_index: 自身玩家索引 (0-3)
        other_index: 其他玩家索引 (0-3)
    
    Returns:
        相对位置字符串: "left", "right", "top", "self"
    """
    if self_index == 0:
        if other_index == 1:
            return "right"
        elif other_index == 2:
            return "top"
        elif other_index == 3:
            return "left"
        elif other_index == 0:
            return "self"
    elif self_index == 1:
        if other_index == 0:
            return "left"
        elif other_index == 2:
            return "right"
        elif other_index == 3:
            return "top"
        elif other_index == 1:
            return "self"
    elif self_index == 2:
        if other_index == 0:
            return "top"
        elif other_index == 1:
            return "left"
        elif other_index == 3:
            return "right"
        elif other_index == 2:
            return "self"
    elif self_index == 3:
        if other_index == 0:
            return "right"
        elif other_index == 1:
            return "top"
        elif other_index == 2:
            return "left"
        elif other_index == 3:
            return "self"

# 递进下一个玩家索引 东 → 南 → 西 → 北 → 东 0 → 1 → 2 → 3 → 0
def next_current_index(self):
    """递进当前玩家索引"""
    if self.current_player_index == 3:
        self.current_player_index = 0
    else:
        self.current_player_index += 1

# 输入玩家索引，获取下一个玩家索引
def next_current_num(num: int) -> int:
    """
    获取下一个玩家索引
    
    Args:
        num: 当前玩家索引 (0-3)
    
    Returns:
        下一个玩家索引 (0-3)
    """
    if num == 3:
        return 0
    else:
        return num + 1

# 倒退玩家索引 用于实现回合数前进 可放心使用
def back_current_num(num: int) -> int:
    """
    倒退玩家索引
    
    Args:
        num: 当前玩家索引 (0-3)
    
    Returns:
        上一个玩家索引 (0-3)
    """
    if num == 0:
        return 3
    else:
        return num - 1

