"""Deterministic structural bot supporting both four and five melds.

Only the acting hand and public information enter the evaluator. Every chosen
action comes from the same authoritative window used by human clients.
"""

import asyncio
from collections import Counter

from .action_check import required_claim_tiles, forbidden_after_claim
from ..public.ai.auto_cut_policy import is_auto_cut_bot, choose_auto_cut_action
from ..public.ai.structural_shanten import distance
from ...game_calculation.hongkong.models import NUMBERS
from ...game_calculation.hongkong.solver import parse_meld, structural_waits
from ..public.ai.pacing import paced_bot, wait_before_bot_submit


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
    if is_auto_cut_bot(p):
        return choose_auto_cut_action(p, actions, state.legal_discard_tiles(index) if "cut" in actions else None)
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
