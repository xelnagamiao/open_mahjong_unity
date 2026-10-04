"""推倒和机器人只从本次权威动作窗口中选择，遵守公共机器人节奏。"""

from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable, _BOT_DELAY
from ..public.ai.smart_bot_logic import find_best_cut, count_visible_tiles, tile_to_34
from ..public.ai.bot_executor import run_room_bot_cpu, bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import infer_bot_cut_class, has_draw_slot


@paced_bot(lambda: _BOT_DELAY)
async def tuidao_bot_action(state, index, available, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    win = next((a for a in available if a.startswith("hu_")), None)
    if win:
        await submit_bot_action(get_ai_action, state, index, win, None, None, None, None)
        return
    player = state.player_list[index]
    hand = list(player.hand_tiles)
    if status not in ("waiting_hand_action", "onlycut_after_action"):
        choice = "gang" if "gang" in available else "pass"
        if choice in available:
            await submit_bot_action(get_ai_action, state, index, choice, None, None, None, None)
        return
    for action, kind in (("angang", "concealed"), ("jiagang", "added")):
        if action in available:
            tile = next((t for t in sorted(set(hand)) if state.kong_allowed(index, t, kind)), None)
            if tile is not None:
                await submit_bot_action(get_ai_action, state, index, action, None, None, None, tile)
                return
    action = "cut"
    if player.ready_locked:
        tile, cut_index = player.last_drawn_tile, len(hand)-1
    elif "riichi_cut" in available and player.riichi_candidate_cuts:
        candidates = player.riichi_candidate_cuts
        visible = count_visible_tiles(state)
        tile = max(sorted(candidates), key=lambda t: sum(max(0, 4-visible[tile_to_34(w)]-hand.count(w)) for w in candidates[t]))
        cut_index, action = hand.index(tile), "riichi_cut"
    else:
        tile, cut_index = await run_room_bot_cpu(state, find_best_cut, hand, len(player.combination_tiles),
                                                count_visible_tiles(state), set())
    if bot_action_is_current(state, index, tick):
        await submit_bot_action(get_ai_action, state, index, action,
            infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player)), tile, cut_index, None)
