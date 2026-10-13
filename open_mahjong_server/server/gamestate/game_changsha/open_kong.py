import random


def normalize_open_kong_count(value):
    """旧房间的1/3张配置归一为2张；新房间另由创建校验只接受2/4张。"""
    count = int(value or 2)
    return count if count in (2, 4) else 2


def roll_open_kong_dice(round_seed, current_round, sequence):
    """每次开杠独立重掷，同时保持同一种子牌谱可以复现。"""
    generator = random.Random(f"{round_seed}:{current_round}:open_kong:{sequence}")
    return [generator.randint(1, 6), generator.randint(1, 6)]


def open_kong_tile_positions(tile_count, replacement_count, dice, tail_half_stack=False):
    """从尾部按现存牌墩定位；补张留下的尾端半墩与头部半墩都独立计数。"""
    stacks = []
    end = tile_count
    if tail_half_stack and end:
        stacks.append([end - 1])
        end -= 1
    while end > 0:
        stacks.append(list(range(max(0, end - 2), end)))
        end -= 2
    # 只有起始墩不存在才改用小点数，不能因下一墩不足而重选已有起始墩。
    start_stack = sum(dice)
    if start_stack > len(stacks):
        start_stack = min(dice)
    if start_stack > len(stacks):
        return []
    selected = stacks[start_stack - 1:start_stack - 1 + replacement_count // 2]
    # 按位置保留最后一张海底，同牌面的其他牌仍可以开出。
    return [position for stack in selected for position in stack if position != tile_count - 1]
