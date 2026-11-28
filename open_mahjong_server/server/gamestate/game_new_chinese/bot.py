import time
from collections import Counter
from functools import lru_cache

from ...game_calculation.new_chinese import rules
from ..public.ai.auto_cut_policy import choose_auto_cut_action, is_auto_cut_bot
from ..public.ai.guobiao_shanten import shanten_shisanyao, xiangting_yiban
from ..public.ai.pacing import wait_bot_delay
from .actions import CHOWS, after_meld, kong_tiles, legal_cuts


@lru_cache(maxsize=16384)
def distance(hand, melds):
    counts = Counter(hand)
    value = xiangting_yiban(counts, melds)
    if not melds:
        value = min(value, 6 - sum(n // 2 for n in counts.values()), shanten_shisanyao(counts))
    return value


def _candidate_tiles(own):
    cands = set()
    for t, c in own.items():
        if c <= 0:
            continue
        if c < 4:
            cands.add(t)
        if t < 40:
            suit, num = t // 10, t % 10
            for d in (-2, -1, 1, 2):
                n2 = num + d
                if 1 <= n2 <= 9:
                    nid = suit * 10 + n2
                    if own[nid] < 4:
                        cands.add(nid)
    return cands


def efficiency(hand, melds, visible, codes=(), hepai_limit=0):
    hand = tuple(sorted(hand))
    level = distance(hand, melds)
    own = Counter(hand)
    if level <= 0 and hepai_limit > 0:
        legal_waits = rules.waits(hand, codes, hepai_limit=hepai_limit)
        if legal_waits:
            outs = sum(max(0, 4 - own[t] - visible[t]) for t in legal_waits)
            if outs > 0:
                return 0, outs
        level = max(1, level)
    outs = sum(max(0, 4 - own[t] - visible[t]) for t in _candidate_tiles(own) if own[t] + visible[t] < 4
               and distance(tuple(sorted((*hand, t))), melds) < level)
    return -level, outs


def best_cut(hand, melds, choices, visible, codes=(), hepai_limit=0):
    def quality(tile):
        rest = list(hand)
        rest.remove(tile)
        return efficiency(rest, melds, visible, codes, hepai_limit), tile == hand[-1], tile
    return max((quality(t) for t in choices), key=lambda item: item)


def choose(state, index, offered):
    p = state.player_list[index]
    if is_auto_cut_bot(p):
        return choose_auto_cut_action(p, offered)
    for action in ("hu", "hu_self", "ready"):
        if action in offered:
            return dict(action_type=action)
    if "exchange_three" in offered:
        return dict(action_type="pass")
    visible = state.visible_counts()
    melds = len(p.combination_tiles)
    codes = tuple(p.combination_tiles)
    hepai_limit = int(getattr(state, "hepai_limit", 0) or 0)
    if "cut" in offered or "riichi_cut" in offered:
        declare = "riichi_cut" in offered
        choices = set(state.candidate_cuts) if declare else legal_cuts(state, index)
        quality, _, tile = best_cut(p.hand_tiles, melds, choices, visible, codes, hepai_limit)
        for kind in ("angang", "jiagang"):
            for t in sorted(kong_tiles(state, index, kind)) if kind in offered else ():
                rest = list(p.hand_tiles)
                for _ in range(4 if kind == "angang" else 1):
                    rest.remove(t)
                next_codes = codes + ((f"G{t}",) if kind == "angang" else ())
                if efficiency(rest, melds + (kind == "angang"), visible, next_codes, hepai_limit) >= quality:
                    return dict(action_type=kind, target_tile=t)
        position = max(i for i, t in enumerate(p.hand_tiles) if t == tile)
        return dict(action_type="riichi_cut" if declare else "cut", TileId=tile, cutIndex=position,
                    cutClass=p.has_draw_slot and position == len(p.hand_tiles)-1)
    claims = []
    for action in offered:
        kind = action.removesuffix("_ting")
        if kind not in (*CHOWS, "peng", "gang"):
            continue
        rest, next_codes = after_meld(p, kind, state.live_pending_window["tile"])
        quality = (efficiency(rest, len(next_codes), visible, tuple(next_codes), hepai_limit)
                   if kind == "gang"
                   else best_cut(rest, len(next_codes), set(rest), visible, tuple(next_codes), hepai_limit)[0])
        claims.append((action.endswith("_ting"), quality, action))
    if claims:
        declare, quality, action = max(claims)
        if declare or quality > efficiency(p.hand_tiles, melds, visible, codes, hepai_limit):
            return dict(action_type=action)
    return dict(action_type="pass")


async def play_bot(state, index, tick):
    await wait_bot_delay(state, time.monotonic())
    if tick == state.server_action_tick and index in state.waiting_players_list and state.action_queues[index].empty():
        await state.submit_action(index, **choose(state, index, state.action_dict[index]))
