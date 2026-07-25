"""国标 Debug 固定牌例：通过 GUOBIAO_DEBUG_SCENARIO 切换场景。

使用前须在 GuobiaoGameState.__init__ 中将 self.Debug = True。

场景：
- qi_dui_tenpai_1m — 东亲（seat0）七对听 1 万：14 张=六对+孤张1万+废张9万；打出 9 万后听 11
- chi_peng_protect — 鸣牌保护测试：A 首打 8 万，B 能吃、C 能碰、无人能和；真人固定 seat3
- tactical_claim   — 战鸣/旧保护测试（亲家首打 8 万；C 可荣和，保护会因和牌关闭）
- buhua_8flowers   — 仅 seat1 起手 1 张花；补花/首打从上家(seat0)开始
"""

# 切换调试场景：改此常量即可
GUOBIAO_DEBUG_SCENARIO = "tactical_claim"

DEBUG_SCENARIOS = (
    "qi_dui_tenpai_1m",
    "chi_peng_protect",
    "tactical_claim",
    "buhua_8flowers",
)

# 真人固定座位；未列出的场景不旋转（保持进房顺序）
DEBUG_SELF_PLAYER_INDEX_BY_SCENARIO = {
    "qi_dui_tenpai_1m": 0,  # 东 / 亲家
    "buhua_8flowers": 1,
    "chi_peng_protect": 3,
}
DEBUG_START_PLAYER_INDEX = 0

# 亲家首打 8 万（17）— chi_peng_protect / tactical_claim
_CLAIM_DISCARD_TILE = 17
# 七对听牌目标：1 万
_QI_DUI_WAIT_TILE = 11
# 庄家开局多出的废张：打出后进入七对听 1 万
_QI_DUI_JUNK_TILE = 19


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
    elif scenario == "qi_dui_tenpai_1m":
        _apply_qi_dui_tenpai_1m(game_state.player_list)
    else:
        _apply_tactical_claim(game_state.player_list)


def prepare_debug_wall(game_state) -> None:
    """发牌扣山后的牌山微调（仅部分场景）。"""
    if resolve_debug_scenario(game_state) != "qi_dui_tenpai_1m":
        return
    # 剩余 1 万尽量顶到牌山前部，便于摸进自摸测听
    wall = game_state.tiles_list
    ones = [t for t in wall if t == _QI_DUI_WAIT_TILE]
    rest = [t for t in wall if t != _QI_DUI_WAIT_TILE]
    game_state.tiles_list[:] = ones + rest


def _apply_qi_dui_tenpai_1m(player_list) -> None:
    """东亲（seat0）七对听 1 万。

    庄家 14 张 = 六对 + 孤张 1 万(11) + 废张 9 万(19)。
    打出 19 后：6 对 + 单 11 → 听 11。
    seat1 带一张 11，便于打出给庄家荣和；牌山剩余 11 顶前（见 prepare_debug_wall）。
    """
    wait = _QI_DUI_WAIT_TILE
    junk = _QI_DUI_JUNK_TILE
    # 东 / 亲家：12,12 13,13 14,14 15,15 16,16 17,17 + 11 + 19
    player_list[0].hand_tiles = [
        wait,
        12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17,
        junk,
    ]
    # 南：一张 11 可打出；其余饼子面子型，避免再占万子对子
    player_list[1].hand_tiles = [
        wait, 21, 22, 23, 24, 25, 26, 27, 28, 29, 41, 42, 43,
    ]
    # 西：条子
    player_list[2].hand_tiles = [
        31, 32, 33, 34, 35, 36, 37, 38, 39, 44, 45, 46, 47,
    ]
    # 北：剩余饼/字，无 1 万
    player_list[3].hand_tiles = [
        21, 22, 23, 24, 25, 26, 27, 28, 29, 41, 42, 43, 44,
    ]


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
