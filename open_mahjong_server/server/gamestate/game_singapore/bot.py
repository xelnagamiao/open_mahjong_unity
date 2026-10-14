"""Bots use the exact legal action window used by connected players."""
from ..public.ai.get_action import get_ai_action
from ..public.ai.smart_bot_logic import find_best_cut, count_visible_tiles
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable, _BOT_DELAY
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu


@paced_bot(lambda: _BOT_DELAY)
async def singapore_bot_action(state, index, action_list, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick): return
    p = state.player_list[index]
    action = next((a for a in ("ready", "hu_self", "hu_first", "hu_second", "hu_third", "buhua",
        "angang", "jiagang", "cut", "gang", "peng", "chi_left", "chi_mid", "chi_right") if a in action_list), "pass")
    tile = cut_index = target = None
    if action == "cut":
        tile, cut_index = await run_room_bot_cpu(state, find_best_cut, list(p.hand_tiles), len(p.combination_tiles), count_visible_tiles(state), set())
    elif action in ("angang", "jiagang"):
        candidates = state._kong_candidates(index, action == "jiagang")
        if not candidates: return
        target = candidates[0]
    if not bot_action_is_current(state, index, tick): return
    await submit_bot_action(get_ai_action, state, index, action,
        infer_bot_cut_class(p.hand_tiles, tile, cut_index, draw_slot=has_draw_slot(p)) if tile is not None else False,
        tile, cut_index, target)
