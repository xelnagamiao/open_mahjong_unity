"""广东机器人：仅使用当前权威动作，鬼牌不送入无鬼的34张牌效算法。"""

from collections import Counter

from ...game_calculation.guangdong.config import GHOSTS
from ...game_calculation.guangdong.rules import structural_waits
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable, _BOT_DELAY
from ..public.ai.bot_executor import run_room_bot_cpu, bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import infer_bot_cut_class, has_draw_slot


def choose_cut(hand, melds):
    """听牌优先进张数；未听时按成组/邻接保留价值，始终保留万能鬼。"""
    counts = Counter(hand)
    choices = []
    for tile in sorted(set(hand)):
        rest = list(hand)
        rest.remove(tile)
        waits = structural_waits(rest, melds)
        useful = sum(max(0, (1 if w in GHOSTS else 4) - rest.count(w)) for w in waits)
        neighbors = sum(counts.get(tile + step, 0) * (2 if abs(step) == 1 else 1)
                        for step in (-2, -1, 1, 2)
                        if tile < 40 and (tile + step) // 10 == tile // 10)
        keep = 100 if tile in GHOSTS else 4 * min(3, counts[tile] - 1) + neighbors
        choices.append(((bool(waits), useful, -keep, tile == hand[-1]), tile))
    return max(choices)[1]


@paced_bot(lambda: _BOT_DELAY)
async def guangdong_bot_action(state, index, available, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    win = next((a for a in available if a.startswith("hu_")), None)
    if win:
        await submit_bot_action(get_ai_action, state, index, win, None, None, None, None)
        return
    player = state.player_list[index]
    if status not in ("waiting_hand_action", "onlycut_after_action"):
        choice = "gang" if "gang" in available else "peng" if "peng" in available else "pass"
        if choice in available:
            await submit_bot_action(get_ai_action, state, index, choice, None, None, None, None)
        return
    hand = list(player.hand_tiles)
    for action, kind in (("angang", "concealed"), ("jiagang", "added")):
        if action in available:
            tile = next((t for t in sorted(set(hand)) if state.kong_allowed(index, t, kind)), None)
            if tile is not None:
                await submit_bot_action(get_ai_action, state, index, action, None, None, None, tile)
                return
    if "cut" in available and hand:
        tile = await run_room_bot_cpu(state, choose_cut, hand, tuple(player.combination_tiles))
        cut_index = hand.index(tile)
        if bot_action_is_current(state, index, tick):
            await submit_bot_action(get_ai_action, state, index, "cut",
                infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player)), tile, cut_index, None)
