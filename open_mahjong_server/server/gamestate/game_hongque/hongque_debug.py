"""虹雀 Debug 固定牌例：通过 HONGQUE_DEBUG_SCENARIO 切换场景。

使用前须在 HongqueGameState.__init__ 中将 self.Debug = True。

场景：
- double_ron — 上家(seat3)首打 AX1，自家(seat0)与下家(seat1)都能荣和（两家和牌）。
  真人固定 seat0（自家），首家出牌者固定为上家 seat3。
- tactical_all_claims — 玩家1(seat0)首打 AX1；玩家2/3/4 都同时可以吃、碰、和，
  仅固定牌组与候选，所有真人操作均由对应客户端手动提交。
- ones_nines — 自家庄：起手多为 1 + GX9；下家 GY 顺；对家 AX 顺；上家多为 9。
  不强制首打，牌山剔除已发牌张以免重复。
"""

from __future__ import annotations

from .tile import full_deck

# 切换调试场景：改此常量即可
HONGQUE_DEBUG_SCENARIO = "ones_nines"

DEBUG_SCENARIOS = (
    "double_ron",
    "tactical_all_claims",
    "ones_nines",
)

# 真人固定座位（自家）；未列出的场景不旋转（保持进房顺序）
DEBUG_SELF_PLAYER_INDEX_BY_SCENARIO = {
    "double_ron": 0,
    "tactical_all_claims": 0,
    "ones_nines": 0,
}

# 场景庄家（首家出牌者）
DEBUG_DEALER_INDEX_BY_SCENARIO = {
    "double_ron": 3,
    "tactical_all_claims": 0,
    "ones_nines": 0,
}

# 两家和牌场景：上家首打 AX1，自家/下家都听 AX1
_DEBUG_RON_TILE = "AX1"
_TACTICAL_DISCARD_TILE = "AX1"


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
    elif scenario == "tactical_all_claims":
        _apply_tactical_all_claims(game_state.players)
        _fix_opening_draw_event(game_state)
    elif scenario == "ones_nines":
        _apply_ones_nines(game_state.players)
        _rebuild_wall_excluding_hands(game_state)
        _fix_opening_draw_event(game_state)


def get_debug_forced_discard(game_state, player_index: int):
    """Debug：首家（庄家）固定打出场景牌张，避免机器人随机出牌破坏牌例。"""
    scenario = resolve_debug_scenario(game_state)
    if scenario == "double_ron" and player_index == get_debug_dealer_index(game_state):
        return _DEBUG_RON_TILE
    if scenario == "tactical_all_claims" and player_index == get_debug_dealer_index(game_state):
        return _TACTICAL_DISCARD_TILE
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


def _apply_tactical_all_claims(players) -> None:
    """玩家1打 AX1；其余三家均同时具备吃、碰、和候选，且全牌局牌张唯一。"""
    players[0].hand = [
        _TACTICAL_DISCARD_TILE,
        "AX4", "AX5", "AX6", "AX7", "AX8", "AX9",
        "AY3", "AY4", "AY5", "AY6", "AY7",
    ]
    players[0].drawn_tile = "AY7"

    # 玩家2：AX2+AX3 吃；GY1+AY1 碰；AX1 到手后可和。
    players[1].hand = [
        "AX2", "AX3", "GY1", "AY1", "FY1",
        "BY4", "BY5", "BY6", "CY7", "CY8", "CY9",
    ]
    # 玩家3：AY2+BX3 吃；BX1+CX1 碰；AX1 到手后可和。
    players[2].hand = [
        "AY2", "BX3", "BX1", "CX1", "DX1",
        "DY4", "DY5", "DY6", "EY7", "EY8", "EY9",
    ]
    # 玩家4：BX2+CX3 吃；FX1+GX1 碰；AX1 到手后可和。
    players[3].hand = [
        "BX2", "CX3", "FX1", "GX1", "EX1",
        "FY4", "FY5", "FY6", "GY7", "GY8", "GY9",
    ]


def _apply_ones_nines(players) -> None:
    """自家(0)庄：12 张多为 1；下家 GY 顺；对家 AX 顺；上家多为 9。"""
    players[0].hand = [
        "AY1", "BX1", "BY1", "CX1", "CY1", "DX1",
        "DY1", "EX1", "EY1", "FX1", "FY1", "GX9",
    ]
    players[0].drawn_tile = "GX9"
    players[1].hand = [
        "GX1", "GX5",
        "GY1", "GY2", "GY3", "GY4", "GY5", "GY6", "GY7", "GY8", "GY9",
    ]
    players[2].hand = [
        "AX1", "AX2", "AX3", "AX4", "AX5", "AX6", "AX7", "AX8", "AX9",
        "GX7", "GX8",
    ]
    players[3].hand = [
        "AY9", "BX9", "BY9", "CX9", "CY9", "DX9",
        "DY9", "EX9", "EY9", "FX9", "FY9",
    ]


def _rebuild_wall_excluding_hands(game_state) -> None:
    """覆盖手牌后重建牌山，去掉已在手中的牌，避免后续摸到重复张。"""
    used = {tile for player in game_state.players for tile in player.hand}
    wall = [tile for tile in full_deck() if tile not in used]
    game_state._rng.shuffle(wall)
    game_state.wall = wall
