import time
from ..public.ai.auto_cut_policy import is_auto_cut_bot, choose_auto_cut_action
from ..game_hongkong.bot import distance
from ..public.ai.pacing import wait_bot_delay
from ...game_calculation.fuzhou import rules as book
from .actions import legal_cuts, kong_tiles


def choose_action(state,index,actions):
    p = state.player_list[index]
    if is_auto_cut_bot(p):
        return choose_auto_cut_action(p, actions, legal_cuts(state,index) if "cut" in actions else None)
    for action in ("fuzhou_flower_win","hu_self","sanjin","hu","qiangjin","buhua","ready"):
        if action in actions:
            result = dict(action_type=action)
            return result
    for action in ("angang","jiagang"):
        if action in actions:
            return dict(action_type=action,target_tile=min(kong_tiles(state,index,action)))
    if "cut" in actions:
        choices = []
        for tile in sorted(legal_cuts(state,index)):
            hand = list(p.hand_tiles)
            hand.remove(tile)
            waits = book.waiting_tiles(hand,p.combination_tiles,jin=state.jin)
            natural = [t for t in hand if t!=state.jin]
            d = max(-1,distance(natural,len(p.combination_tiles),5)-hand.count(state.jin))
            choices.append((0 if waits else 1,d,-len(waits),tile==state.jin,-int(tile==p.hand_tiles[-1]),tile))
        tile = min(choices)[-1]
        position = max(i for i,t in enumerate(p.hand_tiles) if t==tile)
        return dict(action_type="cut",TileId=tile,cutIndex=position,cutClass=p.has_draw_slot and position==len(p.hand_tiles)-1)
    if "gang" in actions:
        return dict(action_type="gang")
    return dict(action_type="pass")


async def play_bot(state,index,tick):
    started = time.monotonic()
    action = choose_action(state,index,list(state.action_dict[index]))
    automatic_opening_flower = state.opening_replacement and action["action_type"]=="buhua"
    if action["action_type"]!="pass" and not automatic_opening_flower:
        await wait_bot_delay(state,started)
    if tick==state.server_action_tick and index in state.waiting_players_list and state.action_queues[index].empty():
        try:
            await state.submit_action(index,**action)
        except ValueError:
            return
