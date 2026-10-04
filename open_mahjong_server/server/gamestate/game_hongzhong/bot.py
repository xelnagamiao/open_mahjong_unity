"""红中机器人使用权威动作窗口；癞子听牌计算在公共 CPU 执行器中运行。"""
from collections import Counter

from ...game_calculation.hongzhong import rules as book
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable, _BOT_DELAY
from ..public.ai.bot_executor import run_room_bot_cpu, bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import infer_bot_cut_class, has_draw_slot


def best_discard(hand, melds, visible):
    """先选最大剩余听口；未听时保留红中、对子与相邻搭子。"""
    counts = Counter(hand)
    scores = {}
    for tile in sorted(counts):
        remaining = list(hand)
        remaining.remove(tile)
        waits = book.waits(remaining, melds)
        outs = sum(max(0, 4 - visible.get(t, 0) - remaining.count(t)) for t in waits)
        # 癞子至少不比任何数牌差；同听口数时优先保留。
        keep = 100 if tile == book.JOKER else 3 * (counts[tile] - 1)
        if tile != book.JOKER:
            keep += sum((3 - abs(delta)) * counts[tile + delta]
                        for delta in (-2, -1, 1, 2)
                        if (tile + delta) // 10 == tile // 10)
        scores[tile] = (bool(waits), outs, len(waits), -keep)
    tile = max(scores, key=scores.get)
    return tile, hand.index(tile)


@paced_bot(lambda: _BOT_DELAY)
async def hongzhong_bot_action(state, index, available, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    if "hu_self" in available:
        await submit_bot_action(get_ai_action, state, index, "hu_self", None, None, None, None)
        return
    if status not in ("waiting_hand_action", "onlycut_after_action"):
        action = next((a for a in ("gang", "peng", "pass") if a in available), None)
        if action:
            await submit_bot_action(get_ai_action, state, index, action, None, None, None, None)
        return
    player = state.player_list[index]
    hand = list(player.hand_tiles)
    for action, kind in (("angang", "concealed"), ("jiagang", "added")):
        if action in available:
            tile = next((t for t in sorted(set(hand)) if state.kong_allowed(index, t, kind)), None)
            if tile is not None:
                await submit_bot_action(get_ai_action, state, index, action, None, None, None, tile)
                return
    if "cut" not in available or not hand:
        return
    visible = Counter(t for p in state.player_list for t in p.discard_tiles)
    visible.update(t for p in state.player_list for meld in p.combination_tiles for t in book.meld_tiles(meld))
    tile, cut_index = await run_room_bot_cpu(state, best_discard, hand, list(player.combination_tiles), visible)
    if bot_action_is_current(state, index, tick):
        await submit_bot_action(get_ai_action, state, index, "cut",
            infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player)), tile, cut_index, None)
