"""敲麻机器人：合法报听、锁手与食替约束下使用公共牌效计算。"""
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import smart_bot_action, _wait_until_actionable, _BOT_DELAY
from ..public.ai.smart_bot_logic import find_best_cut, count_visible_tiles
from ..public.ai.bot_executor import run_room_bot_cpu, bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class


@paced_bot(lambda: _BOT_DELAY)
async def shanghai_smart_bot_action(state, index, actions, status):
    is_qinghunpeng = getattr(state, "sub_rule", "") == "shanghai/qinghunpeng"
    if is_qinghunpeng and status == "waiting_action_after_cut":
        from .qinghunpeng_bot import choose_claim
        tick = state.server_action_tick
        if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
            return
        player = state.player_list[index]
        choice = next((a for a in actions if a.startswith("hu")), None)
        if choice is None:
            tile = state.player_list[state.current_player_index].discard_tiles[-1]
            choice = await run_room_bot_cpu(state, choose_claim, list(player.hand_tiles),
                list(player.combination_tiles), count_visible_tiles(state), tuple(actions), tile)
        if bot_action_is_current(state, index, tick):
            await submit_bot_action(get_ai_action, state, index, choice, None, None, None, None)
        return
    if status not in ("waiting_hand_action", "onlycut_after_action"):
        return await smart_bot_action(state, index, actions, status)
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    player = state.player_list[index]
    if "buhua" in actions:
        await submit_bot_action(get_ai_action, state, index, "buhua", None, None, None, None)
        return
    if "hu_self" in actions:
        await submit_bot_action(get_ai_action, state, index, "hu_self", None, None, None, None)
        return
    hand = list(player.hand_tiles)
    if is_qinghunpeng:
        from .qinghunpeng_bot import choose_hand_action
        action, tile, cut_index = await run_room_bot_cpu(state, choose_hand_action,
            hand, list(player.combination_tiles), count_visible_tiles(state), tuple(actions),
            set(player.kuikae_forbidden_tiles))
        if not bot_action_is_current(state, index, tick):
            return
        await submit_bot_action(get_ai_action, state, index, action,
            infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player)) if action == "cut" else None,
            tile if action == "cut" else None, cut_index, tile if action != "cut" else None)
        return
    candidates = player.riichi_candidate_cuts
    forbidden = set(player.kuikae_forbidden_tiles)
    action = "cut"
    if "riichi_cut" in actions and candidates:
        forbidden.update(set(hand) - set(candidates))
        action = "riichi_cut"
    if player.ready_locked:
        tile, cut_index = hand[-1], len(hand) - 1
    else:
        tick = state.server_action_tick
        tile, cut_index = await run_room_bot_cpu(
            state, find_best_cut, hand, len(player.combination_tiles),
            count_visible_tiles(state), forbidden,
        )
        if not bot_action_is_current(state, index, tick):
            return
    await submit_bot_action(get_ai_action, state, index, action,
                        infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player)),
                        tile, cut_index, None)
