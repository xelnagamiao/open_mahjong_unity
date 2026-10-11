from collections import Counter

from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable, _BOT_DELAY
from ..public.ai.bot_executor import bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import infer_bot_cut_class


@paced_bot(lambda: _BOT_DELAY)
async def wuhan_bot_action(state, index, available, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    p = state.player_list[index]
    for action in ("hu_self", "hu_first", "hu_second", "hu_third", "gang", "peng", "chi_left", "chi_mid", "chi_right"):
        if action in available:
            await submit_bot_action(get_ai_action, state, index, action, None, None, None, None)
            return
    for action, kind in (("angang", "concealed"), ("jiagang", "added")):
        if action in available:
            target = next((t for t in sorted(set(p.hand_tiles)) if state.kong_allowed(index, t, kind)), None)
            if target is not None:
                await submit_bot_action(get_ai_action, state, index, action, None, None, None, target)
                return
    if "cut" not in available:
        if "pass" in available:
            await submit_bot_action(get_ai_action, state, index, "pass", None, None, None, None)
        return
    hand = list(p.hand_tiles)
    counts = Counter(hand)
    def keep(tile):
        if tile == state.joker:
            return 100
        if tile in state.skins:
            return -100
        return 4 * counts[tile] + sum((3 - abs(d)) * counts[tile + d] for d in (-2, -1, 1, 2)
            if tile < 40 and (tile + d) // 10 == tile // 10)
    tile = min(hand, key=keep)
    position = hand.index(tile)
    await submit_bot_action(get_ai_action, state, index, "cut",
        infer_bot_cut_class(hand, tile, position, draw_slot=p.has_draw_slot), tile, position, None)
