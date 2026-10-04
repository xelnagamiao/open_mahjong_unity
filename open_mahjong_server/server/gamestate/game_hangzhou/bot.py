"""Public-information choices run outside the socket event loop."""

import asyncio
from collections import Counter
from copy import copy
from types import SimpleNamespace
import time

from ..public.ai.pacing import wait_bot_delay
from ...game_calculation.hangzhou.rules import JOKER, can_baotou, waiting_tiles
from .actions import CHOWS, claim_tiles, kong_tiles, legal_cuts


def _connections(hand):
    counts = Counter(hand)
    return (counts[JOKER] * 20 + sum(min(2, n - 1) * n * 3 for n in counts.values())
            + sum(n * (counts[t + 1] * 2 + counts[t + 2]) for t, n in counts.items() if t < 40 and t % 10 <= 7))


def choose_action(state, index, actions):
    p = state.player_list[index]
    for action in ("hu_self", "ready"):
        if action in actions:
            return dict(action_type=action)
    for action in ("angang", "jiagang"):
        if action in actions:
            return dict(action_type=action, target_tile=min(kong_tiles(state, index, action)))
    if "cut" in actions:
        candidates = []
        legal = legal_cuts(state, index)
        for tile in sorted(legal):
            rest = list(p.hand_tiles)
            rest.remove(tile)
            # Preserve jokers except when an actual all-waits hand can float.
            floating = tile == JOKER and can_baotou(rest, p.combination_tiles)
            outs = len(waiting_tiles(rest, p.combination_tiles)) if len(legal) > 1 else 0
            candidates.append((floating, outs, _connections(rest), tile == p.hand_tiles[-1], tile))
        tile = max(candidates)[-1]
        position = max(i for i, t in enumerate(p.hand_tiles) if t == tile)
        return dict(action_type="cut", TileId=tile, cutIndex=position,
                    cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1)
    if "gang" in actions:
        return dict(action_type="gang")
    # A claim must improve the held physical hand; no wall/opponent inspection.
    tile = state.live_pending_window.get("tile")
    before = _connections(p.hand_tiles)
    for action in actions:
        if action not in ("peng",) + CHOWS:
            continue
        rest = list(p.hand_tiles)
        for held in claim_tiles(action, tile):
            rest.remove(held)
        after = max(_connections(rest[:i] + rest[i + 1:]) for i in range(len(rest)))
        if after + 12 >= before and p.hand_tiles.count(JOKER) < 2:
            return dict(action_type=action)
    return dict(action_type="pass")


async def play_bot(state, index, tick):
    if tick != state.server_action_tick or index not in state.waiting_players_list:
        return
    started = time.monotonic()
    # A timeout/takeover may mutate the live state while this worker is
    # calculating. Freeze only the bot's hand and public action constraints.
    own = copy(state.player_list[index])
    own.hand_tiles, own.combination_tiles = list(own.hand_tiles), list(own.combination_tiles)
    players = [None] * 4
    players[index] = own
    locked = state.is_cai_locked(index)
    view = SimpleNamespace(player_list=players, tiles_list=[0] * len(state.tiles_list),
                           is_cai_locked=lambda _: locked, live_pending_window=dict(state.live_pending_window or {}))
    action = await asyncio.to_thread(choose_action, view, index, list(state.action_dict[index]))
    if action["action_type"] != "pass":
        await wait_bot_delay(state, started)
    if tick == state.server_action_tick and index in state.waiting_players_list and state.action_queues[index].empty():
        try:
            await state.submit_action(index, **action)
        except ValueError:
            return  # The user can take over or a vote can close this window.
