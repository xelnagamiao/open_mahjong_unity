"""Bots use their own hand and public physical tiles, never the hidden wall."""

import time
from collections import Counter

from ...game_calculation.wenzhou.rules import waiting_tiles
from ..public.ai.pacing import wait_bot_delay
from .actions import CHOWS, kong_tiles, natural, needed_tiles, physical_for


def connections(hand,caishen):
    counts = Counter(natural(t,caishen) for t in hand if t != caishen)
    pairs = sum(min(2,n-1)*n for n in counts.values())
    runs = sum(counts[t]*(counts[t+1]*2+counts[t+2]) for t in counts if t < 40 and t % 10 <= 7)
    # Three caishen is itself a winning route; keep this resource in short hands.
    return 12*hand.count(caishen)+2*pairs+runs


def pick_cut(state,index):
    p = state.player_list[index]
    visible = Counter([state.indicator])
    for other in state.player_list:
        view = state._player_view(other.player_index,index)
        visible.update(view["discard_tiles"])
        for mask in view["combination_mask"]:
            visible.update(t for t in mask[1::2] if t)
    candidates = []
    for tile in sorted(set(p.hand_tiles)):
        rest = list(p.hand_tiles)
        rest.remove(tile)
        waits = waiting_tiles(rest,p.meld_records,caishen=state.caishen)
        held = Counter(rest)
        outs = sum(max(0,4-held[t]-visible[t]) for t in waits)
        candidates.append((-bool(waits),-outs,-connections(rest,state.caishen),-int(tile == p.hand_tiles[-1]),tile))
    return min(candidates)[-1]


def choose_action(state,index,actions):
    p = state.player_list[index]
    for action in ("hu_self","hu","ready"):
        if action in actions:
            return dict(action_type=action)
    for action in ("angang","jiagang"):
        if action in actions:
            return dict(action_type=action,target_tile=min(kong_tiles(state,index,action)))
    if "cut" in actions:
        tile = pick_cut(state,index)
        position = max(i for i,t in enumerate(p.hand_tiles) if t == tile)
        return dict(action_type="cut",TileId=tile,cutIndex=position,
                    cutClass=p.has_draw_slot and position == len(p.hand_tiles)-1)
    if "gang" in actions:
        return dict(action_type="gang")
    # Don't destroy an existing wait. Otherwise open only a complete natural set.
    if waiting_tiles(p.hand_tiles,p.meld_records,caishen=state.caishen):
        return dict(action_type="pass")
    for action in ("peng",)+CHOWS:
        if action in actions:
            tile = state.live_pending_window["tile"]
            held = physical_for(p.hand_tiles,needed_tiles(action,natural(tile,state.caishen)),state.caishen)
            if held:
                return dict(action_type=action)
    return dict(action_type="pass")


async def play_bot(state,index,tick):
    started = time.monotonic()
    action = choose_action(state,index,list(state.action_dict[index]))
    if action["action_type"] != "pass":
        await wait_bot_delay(state,started)
    if tick == state.server_action_tick and index in state.waiting_players_list and state.action_queues[index].empty():
        try:
            await state.submit_action(index,**action)
        except ValueError:
            return
