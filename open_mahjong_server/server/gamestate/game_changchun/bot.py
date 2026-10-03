"""A paced, legal-action bot; never consults another player's hidden tiles."""
from ..public.ai.pacing import paced_bot,submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable,_BOT_DELAY
from ..public.ai.smart_bot_logic import find_best_cut,tile_to_34
from ..public.ai.bot_executor import run_room_bot_cpu,bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import infer_bot_cut_class,has_draw_slot
from ...game_calculation.changchun.rules import parse_meld


def visible_counts(state,index):
    visible=[0]*34
    for player in state.player_list:
        for tile in player.discard_tiles:
            visible[tile_to_34(tile)]+=1
        for code in player.combination_tiles:
            if code.startswith("G") and player.player_index!=index: continue
            for tile in parse_meld(code).physical: visible[tile_to_34(tile)]+=1
    if state.bao_visible(index): visible[tile_to_34(state.cc_bao_tile)]+=1
    return visible


@paced_bot(lambda:_BOT_DELAY)
async def changchun_bot_action(state,index,available,status):
    tick=state.server_action_tick
    if not await _wait_until_actionable(state,index) or not bot_action_is_current(state,index,tick): return
    async def submit(action,target=None,tile=None,cut_index=None):
        await submit_bot_action(get_ai_action,state,index,action,
            infer_bot_cut_class(state.player_list[index].hand_tiles,tile,cut_index,
                draw_slot=has_draw_slot(state.player_list[index])) if tile is not None else None,
            tile,cut_index,target)
    win=next((a for a in available if a.startswith("hu_")),None)
    if win: return await submit(win)
    for action in ("cc_change_bao","cc_draw","cc_pass"):
        if action in available: return await submit(action)
    player=state.player_list[index]
    if status not in ("waiting_hand_action","onlycut_after_action"):
        return await submit("gang" if "gang" in available else "peng" if "peng" in available else "pass")
    for action,builder in (("cc_special",state.special_candidates),("cc_added",state.special_added_candidates)):
        if action in available:
            candidates=builder(index)
            if candidates: return await submit(action,candidates[0]["token"])
    hand=list(player.hand_tiles)
    for action,kind in (("angang","concealed"),("jiagang","added")):
        if action in available:
            tile=next((t for t in sorted(set(hand)) if state.kong_allowed(index,t,kind)),None)
            if tile is not None: return await submit(action,tile)
    action="cut"
    if player.ready_locked:
        tile,cut_index=player.last_drawn_tile,len(hand)-1
    elif "riichi_cut" in available and player.riichi_candidate_cuts:
        visible=visible_counts(state,index)
        tile=max(sorted(player.riichi_candidate_cuts),key=lambda t:sum(
            max(0,4-visible[tile_to_34(w)]-hand.count(w)) for w in player.riichi_candidate_cuts[t]))
        cut_index,action=hand.index(tile),"riichi_cut"
    else:
        tile,cut_index=await run_room_bot_cpu(state,find_best_cut,hand,len(player.combination_tiles),visible_counts(state,index),set())
    if bot_action_is_current(state,index,tick): await submit(action,tile=tile,cut_index=cut_index)
