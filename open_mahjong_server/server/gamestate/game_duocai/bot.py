import time
from collections import Counter

from ..public.ai.auto_cut_policy import is_auto_cut_bot, choose_auto_cut_action
from ..public.ai.pacing import wait_bot_delay
from ...game_calculation.duocai import tiles as t
from .actions import KINDS, legal_cuts


def efficiency(hand):
    counts = Counter(t.face(v) for v in hand if not t.joker(v))
    score = sum(5 * (n // 3) + 2 * (n % 3 == 2) for n in counts.values())
    for value in counts:
        if value < 40:
            score += min(counts[value], counts.get(value + 1, 0)) * 1.2
            score += min(counts[value], counts.get(value + 2, 0)) * .6
    score += sum(2 + len(t.DOMAIN[v]) / 34 for v in hand if t.joker(v) and v not in (t.CHOW, t.PUNG))
    score -= 6 if t.ODD in hand and t.EVEN in hand else 0
    score += sum(t.bonus(v) for v in hand) * .15
    return score


def best_cut(hand, allowed):
    candidates = []
    for tile in allowed:
        left = list(hand)
        left.remove(tile)
        candidates.append((efficiency(left), tile == hand[-1], -tile, tile))
    return max(candidates)[-1]


def choose_action(state, index, actions):
    p = state.player_list[index]
    if is_auto_cut_bot(p):
        return choose_auto_cut_action(p, actions, legal_cuts(state, index) if 'cut' in actions else None)
    for action in ('hu_self', 'hu', 'ready'):
        if action in actions:
            return dict(action_type=action)
    if 'riichi_cut' in actions:
        from .actions import riichi_cuts
        tile = best_cut(p.hand_tiles, riichi_cuts(tuple(p.hand_tiles), tuple(p.melds)))
        position = max(i for i, v in enumerate(p.hand_tiles) if v == tile)
        return dict(action_type='riichi_cut', TileId=tile, cutIndex=position, cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1)
    options = []
    for c in state.meld_choices.get(index, ()):
        action = KINDS.get(c.kind, c.kind)
        if action not in actions:
            continue
        remaining = list(p.hand_tiles)
        for value in c.take:
            remaining.remove(value)
        score = efficiency(remaining) + (5 if len(c.take) >= 2 else 1)
        if c.kind in ('angang', 'jiagang') or score >= efficiency(p.hand_tiles):
            options.append((score, c.key, action))
    if options:
        _, key, action = max(options)
        return dict(action_type=action, choice_id=key)
    if 'cut' in actions:
        tile = best_cut(p.hand_tiles, legal_cuts(state, index))
        position = max(i for i, v in enumerate(p.hand_tiles) if v == tile)
        return dict(action_type='cut', TileId=tile, cutIndex=position, cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1)
    return dict(action_type='pass')


async def play_bot(state, index, tick):
    await wait_bot_delay(state, time.monotonic())
    if tick != state.server_action_tick or index not in state.waiting_players_list or not state.action_queues[index].empty():
        return
    try:
        await state.submit_action(index, **choose_action(state, index, list(state.action_dict[index])))
    except ValueError:
        return
