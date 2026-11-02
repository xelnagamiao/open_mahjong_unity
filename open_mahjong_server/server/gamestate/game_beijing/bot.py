import time
from collections import Counter
from functools import lru_cache
from ..public.ai.auto_cut_policy import is_auto_cut_bot, choose_auto_cut_action
from ..public.ai.pacing import wait_bot_delay
from ..public.ai.guobiao_shanten import xiangting_yiban, shanten_qidui
from ...game_calculation.beijing import rules as book
from .actions import legal_cuts, kong_tiles, claim_tiles


@lru_cache(maxsize=10000)
def distance(hand, meld_count, hun):
    counts = Counter(hand)
    wild = counts.pop(hun, 0)
    value = xiangting_yiban(counts, meld_count)
    if not meld_count:
        value = min(value, shanten_qidui(counts))
    return max(-1, value - wild)


def efficiency(hand, meld_count, hun, visible):
    hand = tuple(sorted(hand))
    level = distance(hand, meld_count, hun)
    copies = Counter(hand) + visible
    outs = sum(max(0, 4 - copies[t]) for t in book.TILES
               if distance(tuple(sorted((*hand, t))), meld_count, hun) < level)
    return -level, outs


def best_cut(hand, meld_count, hun, visible):
    choices = []
    for tile in set(hand):
        remaining = list(hand)
        remaining.remove(tile)
        choices.append((efficiency(remaining, meld_count, hun, visible), tile != hun, tile))
    quality, _, tile = max(choices)
    return tile, quality


def choose_action(state, index, actions):
    p = state.player_list[index]
    if is_auto_cut_bot(p):
        return choose_auto_cut_action(p, actions, legal_cuts(state, index) if "cut" in actions else None)
    for action in ("hu_self", "hu", "hun_gang", "tianting", "ready"):
        if action in actions:
            return dict(action_type=action)
    visible = Counter([state.indicator, *(t for other in state.player_list for t in other.discard_tiles),
                       *(t for other in state.player_list for code in other.combination_tiles for t in book.meld_tiles(code))])
    meld_count = len(p.combination_tiles)
    if "cut" in actions:
        tile, quality = best_cut(p.hand_tiles, meld_count, state.hun, visible)
        for action in ("angang", "jiagang"):
            for candidate in sorted(kong_tiles(state, index, action)) if action in actions else ():
                rest = list(p.hand_tiles)
                for _ in range(4 if action == "angang" else 1):
                    rest.remove(candidate)
                if efficiency(rest, meld_count + int(action == "angang"), state.hun, visible) >= quality:
                    return dict(action_type=action, target_tile=candidate)
        position = max(i for i, t in enumerate(p.hand_tiles) if t == tile)
        return dict(action_type="cut", TileId=tile, cutIndex=position, cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1)
    baseline = efficiency(p.hand_tiles, meld_count, state.hun, visible)
    candidates = []
    for action in ("gang", "peng", "chi_left", "chi_mid", "chi_right"):
        if action in actions:
            rest = list(p.hand_tiles)
            for tile in claim_tiles(action, state.live_pending_window["tile"]):
                rest.remove(tile)
            quality = efficiency(rest, meld_count + 1, state.hun, visible) if action == "gang" else best_cut(rest, meld_count + 1, state.hun, visible)[1]
            closed = all(code.startswith("G") for code in p.combination_tiles)
            if quality > baseline and (not closed or quality[0] > baseline[0]):
                candidates.append((quality, action))
    if candidates:
        return dict(action_type=max(candidates)[1])
    return dict(action_type="pass")


async def play_bot(state, index, tick):
    await wait_bot_delay(state, time.monotonic())
    if tick != state.server_action_tick or index not in state.waiting_players_list or not state.action_queues[index].empty():
        return
    try:
        await state.submit_action(index, **choose_action(state, index, list(state.action_dict[index])))
    except ValueError:
        return
