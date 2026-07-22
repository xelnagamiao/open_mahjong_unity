"""国标 Debug 固定牌例：通过 GUOBIAO_DEBUG_SCENARIO 切换场景。

使用前须在 GuobiaoGameState.__init__ 中将 self.Debug = True。

场景：
- chi_peng_protect — 鸣牌保护测试：A 首打 8 万，B 能吃、C 能碰、无人能和；真人固定 seat3
- tactical_claim   — 战鸣/旧保护测试（亲家首打 8 万；C 可荣和，保护会因和牌关闭）
- buhua_8flowers   — 仅 seat1 起手 1 张花；补花/首打从上家(seat0)开始
"""

# 切换调试场景：改此常量即可
GUOBIAO_DEBUG_SCENARIO = "chi_peng_protect"

DEBUG_SCENARIOS = ("chi_peng_protect", "tactical_claim", "buhua_8flowers")

# buhua_8flowers：真人固定 seat1；chi_peng_protect：真人固定 seat3（受保护观众）
DEBUG_SELF_PLAYER_INDEX_BY_SCENARIO = {
    "buhua_8flowers": 1,
    "chi_peng_protect": 3,
}
DEBUG_START_PLAYER_INDEX = 0

# 亲家首打 8 万（17）
_CLAIM_DISCARD_TILE = 17


def resolve_debug_scenario(game_state) -> str:
    scenario = getattr(game_state, "debug_scenario", None) or GUOBIAO_DEBUG_SCENARIO
    if scenario not in DEBUG_SCENARIOS:
        raise ValueError(
            f"未知国标 debug_scenario={scenario!r}，可选: {', '.join(DEBUG_SCENARIOS)}"
        )
    return scenario


def apply_debug_player_seating(game_state) -> None:
    """Debug：将首个真人(user_id>=10)旋转到场景指定座位。"""
    scenario = resolve_debug_scenario(game_state)
    target = DEBUG_SELF_PLAYER_INDEX_BY_SCENARIO.get(scenario)
    if target is None:
        return

    human_idx = next(
        (i for i, player in enumerate(game_state.player_list) if player.user_id >= 10),
        None,
    )
    if human_idx is None:
        return

    shift = (target - human_idx) % 4
    if shift == 0:
        return

    players = game_state.player_list
    game_state.player_list = players[shift:] + players[:shift]


def get_debug_buhua_start_index(game_state) -> int:
    if resolve_debug_scenario(game_state) == "buhua_8flowers":
        return DEBUG_START_PLAYER_INDEX
    return 0


def apply_guobiao_debug_hands(game_state) -> None:
    scenario = resolve_debug_scenario(game_state)
    if scenario == "buhua_8flowers":
        _apply_buhua_8flowers(game_state.player_list)
    elif scenario == "chi_peng_protect":
        _apply_chi_peng_protect(game_state.player_list)
    else:
        _apply_tactical_claim(game_state.player_list)


def _apply_chi_peng_protect(player_list) -> None:
    """A 打 8 万；B 吃(6+7万)；C 碰；无人荣和。真人 seat3 为受保护观众。

    A：三面子 + 两对 + 孤张 8 万 → 牌效 AI 会首打 8 万。
    B：可 chi_left，但进 8 万不成和型。
    C：对 8 万，可碰，但进第三张不成和型。
    D：无吃碰和。
    """
    discard = _CLAIM_DISCARD_TILE
    player_list[0].hand_tiles = [
        11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42, discard,
    ]
    player_list[1].hand_tiles = [
        15, 16, 18, 19, 24, 24, 26, 27, 28, 34, 35, 36, 44,
    ]
    player_list[2].hand_tiles = [
        22, 22, 22, 32, 32, 32, 33, 33, 33, 45, 46, discard, discard,
    ]
    player_list[3].hand_tiles = [
        14, 18, 21, 23, 25, 28, 34, 36, 38, 41, 43, 44, 47,
    ]


def _apply_tactical_claim(player_list) -> None:
    """原战鸣/鸣牌保护调试手牌（0.4.70 前 init_tiles 固定集）。C 可对 8 万荣和。"""
    discard = _CLAIM_DISCARD_TILE
    player_list[0].hand_tiles = [
        discard, 14, 18, 21, 23, 24, 25, 26, 27, 28, 31, 34, 35, 41,
    ]
    player_list[1].hand_tiles = [
        15, 16, 11, 11, 11, 12, 12, 12, 13, 13, 13, 19, 19,
    ]
    player_list[2].hand_tiles = [
        22, 22, 22, 32, 32, 32, 33, 33, 33, 17, 17, 45, 45,
    ]
    player_list[3].hand_tiles = [
        14, 18, 21, 23, 24, 25, 28, 34, 36, 38, 41, 42, 44,
    ]


def _apply_buhua_8flowers(player_list) -> None:
    """seat1 仅 1 张花；seat0 庄家 14 张无花；其余 13 张无花。"""
    player_list[0].hand_tiles = [
        11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 25,
    ]
    player_list[1].hand_tiles = [
        51, 21, 22, 23, 24, 25, 26, 27, 28, 29, 31, 32, 33,
    ]
    player_list[2].hand_tiles = [
        31, 32, 33, 34, 35, 36, 37, 38, 39, 41, 42, 43, 44,
    ]
    player_list[3].hand_tiles = [
        41, 42, 43, 44, 45, 46, 47, 11, 12, 13, 14, 15, 16,
    ]
