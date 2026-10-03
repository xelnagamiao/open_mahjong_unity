"""Public-information choices, constrained by the same offers as human play."""

from collections import Counter
import time

from ..game_hongkong.bot import distance
from ..public.ai.pacing import wait_bot_delay
from ...game_calculation.yixing.rules import NUMBERS, parse_meld, waiting_tiles
from .actions import CHOWS, legal_cuts, kong_tiles, claim_tiles, forbidden_after_chow


def shanten(hand,meld_count,seven_pairs):
    result = distance(hand,meld_count,4)
    if seven_pairs and not meld_count:
        result = min(result,6-sum(n//2 for n in Counter(hand).values()))
    return result


def pick_cut(state,index,hand=None,meld_count=None,legal=None):
    p = state.player_list[index]
    hand = list(p.hand_tiles if hand is None else hand)
    meld_count = len(p.combination_tiles) if meld_count is None else meld_count
    legal = legal_cuts(state,index) if legal is None else legal
    visible = Counter()
    for other in state.player_list:
        view = state._player_view(other.player_index,index)
        visible.update(view["discard_tiles"])
        for code in view["combination_tiles"]:
            if code != "G0":
                visible.update(parse_meld(code).tiles)
    candidates = []
    for tile in sorted(legal):
        rest = list(hand)
        rest.remove(tile)
        counts = Counter(rest)
        waits = waiting_tiles(rest,p.combination_tiles,**state.detailed_config) if meld_count == len(p.combination_tiles) else set()
        outs = sum(max(0,4-counts[t]-visible[t]) for t in waits)
        connections = sum(c*min(2,c-1)*2 for c in counts.values())
        connections += sum(counts[t]*(counts[t+1]+counts[t+2]) for t in NUMBERS if t % 10 <= 7)
        candidates.append((shanten(rest,meld_count,state.detailed_config["seven_pairs"]),-outs,-connections,-int(tile == hand[-1]),tile))
    return min(candidates)[-1]


def choose_action(state,index,actions):
    p = state.player_list[index]
    for action in ("hu_self","hu","buhua","ready"):
        if action in actions:
            return dict(action_type=action)
    if "yixing_last_draw" in actions:
        # Evaluate possible outcomes without reading the actual last wall tile.
        from ...game_calculation.yixing.rules import score_hand, TILES
        take = any(score_hand(p.hand_tiles+[t],p.combination_tiles,p.huapai_list,
                   winning_tile=t,self_draw=True,seat_wind=41+index,sea_bottom=True,**state.detailed_config) for t in TILES)
        return dict(action_type="yixing_last_draw" if take else "pass")
    for action in ("angang","jiagang"):
        if action in actions:
            return dict(action_type=action,target_tile=min(kong_tiles(state,index,action)))
    if "cut" in actions:
        tile = pick_cut(state,index)
        position = max(i for i,t in enumerate(p.hand_tiles) if t == tile)
        return dict(action_type="cut",TileId=tile,cutIndex=position,cutClass=p.has_draw_slot and position == len(p.hand_tiles)-1)
    if "gang" in actions:
        return dict(action_type="gang")
    before = shanten(p.hand_tiles,len(p.combination_tiles),state.detailed_config["seven_pairs"])
    for action in actions:
        if action not in ("peng",)+CHOWS:
            continue
        tile = state.live_pending_window["tile"]
        remaining = list(p.hand_tiles)
        for t in claim_tiles(action,tile):
            remaining.remove(t)
        count = len(p.combination_tiles)+1
        legal = set(remaining)-forbidden_after_chow(action,tile,len(p.hand_tiles))
        cut = pick_cut(state,index,remaining,count,legal)
        remaining.remove(cut)
        # Avoid opening a hand that has no credible route to the flower minimum.
        available = len(p.huapai_list) + sum(2 for t in p.hand_tiles if t >= 41)
        if available >= 2 and shanten(remaining,count,False) < before:
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
            return  # A vote or human takeover may already have moved the window.
