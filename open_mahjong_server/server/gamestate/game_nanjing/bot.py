from collections import Counter
import time

from ..game_hongkong.bot import distance
from ..public.ai.pacing import wait_bot_delay
from ...game_calculation.nanjing.rules import waiting_tiles
from .actions import legal_cuts, kong_tiles


def shanten(hand, meld_count):
    value = distance(hand, meld_count, 4)
    return min(value, 6-sum(c//2 for c in Counter(hand).values())) if not meld_count else value


def choose_action(state,index,actions):
    p = state.player_list[index]
    for action in ("hu_self","hu","buhua","ready"):
        if action in actions:
            return dict(action_type=action)
    for action in ("angang","jiagang"):
        if action in actions:
            return dict(action_type=action,target_tile=min(kong_tiles(state,index,action)))
    if "cut" in actions:
        candidates = []
        for tile in sorted(legal_cuts(state,index)):
            hand = list(p.hand_tiles)
            hand.remove(tile)
            waits = waiting_tiles(hand,p.combination_tiles)
            candidates.append((shanten(hand,len(p.combination_tiles)),-len(waits),-int(tile==p.hand_tiles[-1]),tile))
        tile = min(candidates)[-1]
        position = max(i for i,t in enumerate(p.hand_tiles) if t==tile)
        return dict(action_type="cut",TileId=tile,cutIndex=position,cutClass=p.has_draw_slot and position==len(p.hand_tiles)-1)
    if "gang" in actions:
        return dict(action_type="gang")
    if "peng" in actions:
        tile = state.live_pending_window["tile"]
        hand = list(p.hand_tiles)
        hand.remove(tile)
        hand.remove(tile)
        after = min(shanten(hand[:n]+hand[n+1:],len(p.combination_tiles)+1) for n in range(len(hand)))
        if len(p.huapai_list)>=4 and after<shanten(p.hand_tiles,len(p.combination_tiles)) or state.contracts[index]:
            return dict(action_type="peng")
    return dict(action_type="pass")


async def play_bot(state,index,tick):
    started = time.monotonic()
    action = choose_action(state,index,list(state.action_dict[index]))
    if action["action_type"] != "pass":
        await wait_bot_delay(state,started)
    if tick==state.server_action_tick and index in state.waiting_players_list and state.action_queues[index].empty():
        try:
            await state.submit_action(index,**action)
        except ValueError:
            return
