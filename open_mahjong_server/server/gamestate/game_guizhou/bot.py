"""A public-information bot. Decisions are constrained by the human action list."""

from collections import Counter
import time

from ..game_hongkong.bot import distance
from ..public.ai.pacing import wait_bot_delay
from ...game_calculation.guizhou.rules import waiting_tiles, meld_tiles
from .actions import legal_cuts, kong_tiles


def shanten(hand, meld_count):
    result = distance(hand, meld_count, 4)
    if not meld_count:
        counts = Counter(hand)
        # Four equal tiles are two pairs under this rulebook.
        result = min(result, 6-sum(n//2 for n in counts.values()))
    return result


def pick_cut(state, index, hand=None, meld_count=None, legal=None):
    p = state.player_list[index]
    hand = list(p.hand_tiles if hand is None else hand)
    meld_count = len(p.combination_tiles) if meld_count is None else meld_count
    legal = legal_cuts(state, index) if legal is None else legal
    visible = Counter()
    for other in state.player_list:
        view = state._player_view(other.player_index, index)
        visible.update(view["discard_tiles"])
        for code in view["combination_tiles"]:
            if code != "G0":
                visible.update(meld_tiles(code))
    candidates = []
    for tile in sorted(legal):
        rest = list(hand)
        rest.remove(tile)
        counts = Counter(rest)
        waits = waiting_tiles(rest, p.combination_tiles, theoretical=False) if meld_count == len(p.combination_tiles) else set()
        outs = sum(max(0, 4-counts[t]-visible[t]) for t in waits)
        connection = sum(c*min(2, c-1) for c in counts.values())
        connection += sum(counts[t]*(counts[t+1]+counts[t+2]) for t in counts if t % 10 <= 7)
        candidates.append((shanten(rest, meld_count), -outs, -connection, -int(tile == hand[-1]), tile))
    return min(candidates)[-1]


def choose_action(state, index, actions):
    p = state.player_list[index]
    for action in ("hu_self", "hu", "baoting_initial", "ready", "guizhou_ready"):
        if action in actions:
            return dict(action_type=action)
    for action in ("angang", "jiagang"):
        if action in actions:
            return dict(action_type=action, target_tile=min(kong_tiles(state, index, action)))
    if "cut" in actions:
        tile = pick_cut(state, index)
        position = max(i for i, t in enumerate(p.hand_tiles) if t == tile)
        return dict(action_type="cut", TileId=tile, cutIndex=position,
                    cutClass=p.has_draw_slot and position == len(p.hand_tiles)-1)
    if "gang" in actions:
        return dict(action_type="gang")
    if "peng" in actions:
        tile = state.live_pending_window["tile"]
        remaining = list(p.hand_tiles)
        remaining.remove(tile)
        remaining.remove(tile)
        count = len(p.combination_tiles)+1
        cut = pick_cut(state, index, remaining, count, set(remaining))
        remaining.remove(cut)
        if shanten(remaining, count) < shanten(p.hand_tiles, len(p.combination_tiles)):
            return dict(action_type="peng")
    return dict(action_type="pass")


async def play_bot(state, index, tick):
    started = time.monotonic()
    action = choose_action(state, index, list(state.action_dict[index]))
    if action["action_type"] != "pass":
        await wait_bot_delay(state, started)
    if tick == state.server_action_tick and index in state.waiting_players_list and state.action_queues[index].empty():
        try:
            await state.submit_action(index, **action)
        except ValueError:
            # The human/vote lifecycle may have advanced between the checks.
            return
