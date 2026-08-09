"""虹雀 Debug 固定牌例：通过 HONGQUE_DEBUG_SCENARIO 切换场景。

使用前须在 HongqueGameState.__init__ 中将 self.Debug = True。

场景：
- double_ron — 上家(seat3)首打 AX1，自家(seat0)与下家(seat1)都能荣和（两家和牌）。
  真人固定 seat0（自家），首家出牌者固定为上家 seat3。
"""

from __future__ import annotations

# 切换调试场景：改此常量即可
HONGQUE_DEBUG_SCENARIO = "double_ron"

DEBUG_SCENARIOS = (
    "double_ron",
)

# 真人固定座位（自家）；未列出的场景不旋转（保持进房顺序）
DEBUG_SELF_PLAYER_INDEX_BY_SCENARIO = {
    "double_ron": 0,
}

# 场景庄家（首家出牌者）：上家 seat3
DEBUG_DEALER_INDEX_BY_SCENARIO = {
    "double_ron": 3,
}

# 两家和牌场景：上家首打 AX1，自家/下家都听 AX1
_DEBUG_RON_TILE = "AX1"


def resolve_debug_scenario(game_state) -> str:
    scenario = getattr(game_state, "debug_scenario", None) or HONGQUE_DEBUG_SCENARIO
    if scenario not in DEBUG_SCENARIOS:
        raise ValueError(
            f"未知虹雀 debug_scenario={scenario!r}，可选: {', '.join(DEBUG_SCENARIOS)}"
        )
    return scenario


def get_debug_dealer_index(game_state) -> int:
    """Debug：场景指定的首家出牌者（庄家）。"""
    return DEBUG_DEALER_INDEX_BY_SCENARIO.get(resolve_debug_scenario(game_state), 0)


def apply_debug_player_seating(game_state) -> None:
    """Debug：将首个真人(user_id>=10)旋转到场景指定座位（自家=0）。"""
    scenario = resolve_debug_scenario(game_state)
    target = DEBUG_SELF_PLAYER_INDEX_BY_SCENARIO.get(scenario)
    if target is None:
        return
    human_idx = next(
        (i for i, player in enumerate(game_state.players) if player.user_id >= 10),
        None,
    )
    if human_idx is None:
        return
    shift = (target - human_idx) % 4
    if shift == 0:
        return
    players = game_state.players
    game_state.players = players[shift:] + players[:shift]
    game_state.player_list = game_state.players


def apply_hongque_debug_hands(game_state) -> None:
    """Debug：发牌后覆盖四家手牌为场景固定牌例。"""
    scenario = resolve_debug_scenario(game_state)
    if scenario == "double_ron":
        _apply_double_ron(game_state.players)
        _fix_opening_draw_event(game_state)


def get_debug_forced_discard(game_state, player_index: int):
    """Debug：首家（庄家）固定打出场景牌张，避免机器人随机出牌破坏牌例。"""
    scenario = resolve_debug_scenario(game_state)
    if scenario == "double_ron" and player_index == get_debug_dealer_index(game_state):
        return _DEBUG_RON_TILE
    return None


def _fix_opening_draw_event(game_state) -> None:
    """覆盖手牌后同步开局摸牌事件：摸牌张必须与庄家覆盖后手牌中的 drawn_tile 一致，
    否则客户端按原牌山张渲染，出现白牌/多一张。"""
    dealer = get_debug_dealer_index(game_state)
    drawn = game_state.players[dealer].drawn_tile
    for event in game_state.events:
        if event.get("type") == "draw" and event.get("player") == dealer:
            event["tile"] = drawn


def _apply_double_ron(players) -> None:
    """上家(3)（庄家）12 张含孤张 AX1，首打 AX1；自家(0)/下家(1)听 AX1，对家(2)不听。"""
    ron = _DEBUG_RON_TILE
    # 上家 / 庄家 / 首打：12 张唯一牌且不可和（不能天和），AX1 为孤张手切。
    # 注意：手牌必须全唯一，否则牌效 AI 的 mask_from_codes 会抛异常导致机器人等到超时。
    players[3].hand = [
        ron,
        "EY2", "DY8", "AX6", "FY5", "CY1", "FY3", "EY7", "EX9", "BX1", "AX5", "BY6",
    ]
    players[3].drawn_tile = "BY6"
    # 自家(0)：AX1 补成 AX1,AX2,AX3 → 4 组和牌
    players[0].hand = [
        "AX2", "AX3",
        "BX4", "BX5", "BX6",
        "CX4", "CX5", "CX6",
        "DX4", "DX5", "DX6",
    ]
    # 下家(1)：AX1 补成 AX1,AX2,AX3 → 4 组和牌
    players[1].hand = [
        "AX2", "AX3",
        "BX1", "BX2", "BX3",
        "CX7", "CX8", "CX9",
        "DX7", "DX8", "DX9",
    ]
    # 对家(2)：11 张唯一牌，AX1 无法成和（不能荣和）。
    players[2].hand = [
        "FX6", "GY9", "GY4", "DY2", "BY1", "EX8", "AX6", "DX7", "FY8", "BX1", "GY1",
    ]
