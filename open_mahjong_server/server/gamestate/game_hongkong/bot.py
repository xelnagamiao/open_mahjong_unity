"""Deterministic structural bot supporting both four and five melds.

Only the acting hand and public information enter the evaluator. Every chosen
action comes from the same authoritative window used by human clients.
"""

import asyncio
from collections import Counter
from functools import lru_cache

from .action_check import required_claim_tiles, forbidden_after_claim
from ...game_calculation.compact_counter import pack_counter
from ...game_calculation.hongkong.models import NUMBERS
from ...game_calculation.hongkong.solver import parse_meld, structural_waits
from ..public.ai.pacing import paced_bot, wait_before_bot_submit


@lru_cache(maxsize=65536)
def _partials(key):
    if not key:
        return ((0,0,0),)
    # Sorted tile/count bytes keep the same 65,536 states without retaining
    # a separate tuple object for every tile in every cached sub-hand.
    counts = dict(zip(key[::2], key[1::2]))
    tile,count = key[:2]
    def rest(used):
        next_counts = dict(counts)
        for t in used:
            next_counts[t] -= 1
        # Subtraction preserves the canonical order inherited from key.
        return bytes(value for t,c in next_counts.items() if c for value in (t,c))
    result = set(_partials(rest((tile,))))
    if count>=3:
        result.update((m+1,t,p) for m,t,p in _partials(rest((tile,)*3)))
    if tile in NUMBERS and tile%10<=7 and counts.get(tile+1) and counts.get(tile+2):
        result.update((m+1,t,p) for m,t,p in _partials(rest((tile,tile+1,tile+2))))
    if count>=2:
        for m,t,p in _partials(rest((tile,tile))):
            result.add((m,t+1,p))
            if not p:
                result.add((m,t,1))
    if tile in NUMBERS:
        for step in (1,2):
            if tile%10+step<=9 and counts.get(tile+step):
                result.update((m,t+1,p) for m,t,p in _partials(rest((tile,tile+step))))
    return tuple(sorted(a for a in result if not any(a!=b and all(x<=y for x,y in zip(a,b)) for b in result)))


def distance(hand,meld_count,total_melds):
    needed = total_melds-meld_count
    return min(2*needed-2*m-min(t,max(0,needed-m))-p
               for m,t,p in _partials(pack_counter(Counter(hand))))


def pick_cut(state,index,*,hand=None,meld_count=None,forbidden=None):
    p = state.player_list[index]
    hand = list(p.hand_tiles if hand is None else hand)
    meld_count = len(p.combination_tiles) if meld_count is None else meld_count
    legal = set(hand)-set(forbidden or ()) if forbidden is not None else state.legal_discard_tiles(index)
    visible = Counter()
    for other in state.player_list:
        visible.update(other.discard_tiles)
        for code in other.combination_tiles:
            if code.startswith("G") and not state.rules.open_concealed_kong and other.player_index!=index:
                continue
            visible.update(parse_meld(code).tiles)
    choices = []
    for tile in sorted(legal):
        remaining = list(hand)
        remaining.remove(tile)
        shanten = distance(remaining,meld_count,state.rules.structure.meld_count)
        counts = Counter(remaining)
        waits = structural_waits(remaining,p.combination_tiles,state.rules) if hand==p.hand_tiles else set()
        outs = sum(max(0,4-visible[t]-counts[t]) for t in waits)
        connections = sum(c*min(2,c-1)*2 for c in counts.values())
        connections += sum(counts[t]*(counts[t+1]+counts[t+2]) for t in NUMBERS if t%10<=7)
        choices.append((shanten,-outs,-connections,-int(tile==hand[-1]),tile))
    return min(choices)[-1]


def choose_action(state,index,actions):
    p = state.player_list[index]
    for action in ("hu_self","hu","buhua","ding_initial","ready"):
        if action in actions:
            return dict(action_type=action)
    if "riichi" in actions:
        return dict(action_type="riichi")
    if "angang" in actions:
        return dict(action_type="angang",target_tile=min(t for t,c in Counter(p.hand_tiles).items() if c==4))
    if "jiagang" in actions:
        return dict(action_type="jiagang",target_tile=min(t for t in p.hand_tiles if f"k{t}" in p.combination_tiles))
    if "cut" in actions:
        tile = pick_cut(state,index)
        position = max(i for i,t in enumerate(p.hand_tiles) if t==tile)
        return dict(action_type="cut",TileId=tile,cutIndex=position,cutClass=p.has_draw_slot and position==len(p.hand_tiles)-1)
    candidates = [a for a in actions if a in ("peng","gang","chi_left","chi_mid","chi_right")]
    if candidates:
        tile = state.live_pending_window["tile"]
        before = distance(p.hand_tiles,len(p.combination_tiles),state.rules.structure.meld_count)
        for action in candidates:
            if state.rules.is_old and action.startswith("chi"):
                continue  # Preserve concealed-hand scoring in the no-flower default.
            remaining = list(p.hand_tiles)
            for t in required_claim_tiles(action,tile):
                remaining.remove(t)
            if action=="gang":
                return dict(action_type=action)
            cut = pick_cut(state,index,hand=remaining,meld_count=len(p.combination_tiles)+1,forbidden=forbidden_after_claim(action,tile))
            remaining.remove(cut)
            after = distance(remaining,len(p.combination_tiles)+1,state.rules.structure.meld_count)
            if after<before:
                return dict(action_type=action)
    return dict(action_type="pass")


@paced_bot()
async def play_bot(state,index,actions,phase,tick):
    decision = choose_action(state,index,actions)
    action = decision.pop("action_type")
    if await wait_before_bot_submit(state,index,action):
        await state.submit_action(index,action,**decision)
