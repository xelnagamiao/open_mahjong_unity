"""山西机器人只使用己手与公开牌，沿用公共限速与后台牌效执行。"""
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable, _BOT_DELAY
from ..public.ai.smart_bot_logic import find_best_cut, hand_to_34array
from ..public.ai.bot_executor import run_room_bot_cpu, bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class


@paced_bot(lambda: _BOT_DELAY)
async def shanxi_smart_bot_action(state, index, actions, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    player = state.player_list[index]
    hu = next((a for a in actions if a.startswith("hu_")), None)
    if hu:
        await submit_bot_action(get_ai_action, state, index, hu, None, None, None, None)
        return
    if "cut" not in actions:
        action = "gang" if "gang" in actions else "peng" if "peng" in actions else "pass"
        await submit_bot_action(get_ai_action, state, index, action, None, None, None, None)
        return
    hand = list(player.hand_tiles)
    for action, kind in (("angang", "G"), ("jiagang", "added")):
        if action not in actions:
            continue
        tile = next((t for t in sorted(set(hand)) if state.kong_allowed(index, t, kind)), None)
        if tile is not None:
            await submit_bot_action(get_ai_action, state, index, action, None, None, None, tile)
            return
    candidates = player.riichi_candidate_cuts if "riichi_cut" in actions else {}
    action = "riichi_cut" if candidates else "cut"
    if player.ready_locked:
        tile, cut_index = hand[-1], len(hand) - 1
    else:
        forbidden = set(hand) - set(candidates) if candidates else set()
        tile, cut_index = await run_room_bot_cpu(state, find_best_cut, hand,
            len(player.combination_tiles), hand_to_34array(state.known_public_tiles(index)), forbidden)
        if not bot_action_is_current(state, index, tick):
            return
    await submit_bot_action(get_ai_action, state, index, action,
        infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player)), tile, cut_index, None)
