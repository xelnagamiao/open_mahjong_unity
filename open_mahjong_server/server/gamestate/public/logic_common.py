# 通用逻辑方法 - 所有规则共享

def final_rank_sort_key(player) -> tuple:
    """终局排名排序：分数降序，同分按开局原始风位 original_player_index（东0→南1→西2→北3）。"""
    return (-player.score, player.original_player_index)


def sort_players_for_final_ranking(player_list) -> None:
    """按终局排名规则就地排序 player_list。"""
    player_list.sort(key=final_rank_sort_key)


def assign_strict_final_ranks(player_list) -> None:
    """终局严格名次 1-4（同分按原始风位拆分，不并列）。"""
    sort_players_for_final_ranking(player_list)
    for index, player in enumerate(player_list):
        player.record_counter.rank_result = index + 1


def assign_competition_final_ranks(player_list) -> None:
    """终局竞赛排名（同分同名次 1,2,2,4；组内顺序按原始风位）。"""
    sort_players_for_final_ranking(player_list)
    for index, player in enumerate(player_list):
        if index > 0 and player.score == player_list[index - 1].score:
            player.record_counter.rank_result = player_list[index - 1].record_counter.rank_result
        else:
            player.record_counter.rank_result = index + 1


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
    """递进当前玩家索引（不含巡目；国标请用 player_index_next）"""
    if self.current_player_index == 3:
        self.current_player_index = 0
    else:
        self.current_player_index += 1


def player_index_go_to(self, player_index: int):
    """ 通过action_history历史行动列表，保存此前所有的操作player_index，示例：[0,1,1,2,2,3,0,1,1]，其中0指东家，1、2、3指南西北家
        指针每次重新指向的时候判断
        1.开局1巡，亲家出牌列表为空不加巡目
        2.如果指针指向的是action_history[-1]，则Skip
        3.如果历史行动列表往前追溯时指向玩家小于上一个玩家，则巡目+1
        以下是示例情况
        A.南家补花 0 1 1 跳过
        B.亲家补花 0 0 跳过
        C.南家碰北家 3 1 加巡目
        D.轮到亲家摸牌 3 0 加巡目
    """
    history = self.action_history
    if (
        history
        and player_index != history[-1]
        and player_index < history[-1]
        and self.player_list[0].discard_tiles
    ):
        self.xunmu += 1
    history.append(player_index)
    self.current_player_index = player_index


def player_index_next(self):
    """历时：东→南→西→北→东，并更新巡目。"""
    player_index_go_to(self, 0 if self.current_player_index == 3 else self.current_player_index + 1)

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

