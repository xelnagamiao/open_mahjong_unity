import time
from ..public.ai.auto_cut_policy import is_auto_cut_bot, choose_auto_cut_action
from collections import Counter
from ..public.ai.pacing import wait_bot_delay
from ...game_calculation.nanchang import rules as book
from .actions import legal_cuts,kong_tiles


def choose_action(state,index,actions):
    p = state.player_list[index]
    if is_auto_cut_bot(p):
        return choose_auto_cut_action(p, actions, legal_cuts(state,index) if "cut" in actions else None)
    for action in ("hu_self","hu","nc_jingdiao","ready","nc_draw"):
        if action in actions:
            return dict(action_type=action)
    for action in ("angang","jiagang"):
        if action in actions:
            return dict(action_type=action,target_tile=min(kong_tiles(state,index,action)))
    if "cut" in actions:
        counts = Counter(p.hand_tiles)
        jing = book.jing_tiles(state.indicator)
        tile = min(legal_cuts(state,index),key=lambda t:(t in jing,counts[t]*4+
                   sum(counts[x] for seq in book.SEQUENCES_BY_TILE[t] for x in seq if x != t),-t))
        position = max(i for i,t in enumerate(p.hand_tiles) if t == tile)
        return dict(action_type="cut",TileId=tile,cutIndex=position,cutClass=p.has_draw_slot and position == len(p.hand_tiles)-1)
    if "gang" in actions:
        return dict(action_type="gang")
    if "peng" in actions:
        return dict(action_type="peng")
    return dict(action_type="pass")


async def play_bot(state,index,tick):
    await wait_bot_delay(state,time.monotonic())
    if tick != state.server_action_tick or index not in state.waiting_players_list or not state.action_queues[index].empty():
        return
    try:
        await state.submit_action(index,**choose_action(state,index,list(state.action_dict[index])))
    except ValueError:
        return
