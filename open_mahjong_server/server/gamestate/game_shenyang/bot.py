"""沈阳牌效：保留特殊杠、报听和尾牌流程，普通行牌计算牌效。"""
from collections import Counter

from ...game_calculation.shenyang import rules as book
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable, _BOT_DELAY
from ..public.ai.guobiao_shanten import xiangting_yiban
from ..public.ai.smart_bot_logic import tile_to_34
from ..public.ai.bot_executor import run_room_bot_cpu, bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class


def _qualify_bonus(hand, melds):
    all_tiles = list(hand) + [t for c in melds for t in book.parse_meld(c).tiles]
    suits = {t // 10 for t in all_tiles if t in book.NUMBERS}
    pure = len(suits) == 1 and all(t in book.NUMBERS for t in all_tiles)
    suit_score = 3 if (len(suits) == 3 or pure) else len(suits)
    has_orphan = 1 if (set(all_tiles) & book.ORPHANS) else 0
    counts = Counter(hand)
    has_trip = 1 if (
        any(c[0] != "s" for c in melds)
        or any(v >= 3 for v in counts.values())
        or any(counts[d] >= 2 for d in book.DRAGONS)
    ) else 0
    return suit_score + has_orphan * 2 + has_trip


def _candidate_tiles(held):
    cands = set()
    for t, c in held.items():
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
                    if held[nid] < 4:
                        cands.add(nid)
    return cands


def _score(hand, melds, visible):
    hand = list(hand)
    held = Counter(hand)
    dist = xiangting_yiban(held, len(melds))
    if not melds:
        dist = min(dist, 6 - sum(n // 2 for n in held.values()))
    if dist <= 0:
        waits = book.waits(hand, melds)
        if waits:
            return (0, sum(max(0, 4-held[t]-visible[tile_to_34(t)]) for t in waits), _qualify_bonus(hand, melds))
    outs = sum(
        max(0, 4 - held[t] - visible[tile_to_34(t)])
        for t in _candidate_tiles(held)
        if held[t] < 4 and xiangting_yiban(Counter(hand + [t]), len(melds)) < dist
    )
    return (min(-1, -dist), _qualify_bonus(hand, melds), outs)


def choose_cut(hand, melds, visible, legal):
    choices = []
    for tile in sorted(legal):
        rest = list(hand)
        rest.remove(tile)
        choices.append((_score(rest, melds, visible), tile == hand[-1], tile))
    return max(choices)[2]


def choose_claim(hand, melds, visible, actions, tile):
    best, choice = _score(hand, melds, visible), "pass"
    for action in actions:
        needed = {"peng": [tile]*2, "gang": [tile]*3, "chi_left": [tile-2, tile-1],
                  "chi_mid": [tile-1, tile+1], "chi_right": [tile+1, tile+2]}.get(action)
        if needed is None:
            continue
        rest = list(hand)
        for t in needed:
            rest.remove(t)
        code = (f"s{sorted([tile, *needed])[1]}" if action.startswith("chi")
                else f"{'g' if action == 'gang' else 'k'}{tile}")
        codes = [*melds, code]
        if action != "gang":
            rest.remove(choose_cut(rest, codes, visible, set(rest)))
        score = _score(rest, codes, visible)
        if score > best:
            best, choice = score, action
    return choice


def visible_counts(state):
    visible = [0]*34
    for p in state.player_list:
        for tile in p.discard_tiles:
            visible[tile_to_34(tile)] += 1
        for code in p.combination_tiles:
            for tile in book.parse_meld(code).tiles:
                visible[tile_to_34(tile)] += 1
    return visible


@paced_bot(lambda: _BOT_DELAY)
async def shenyang_bot_action(state, index, actions, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    p = state.player_list[index]
    choice = next((a for a in actions if a.startswith("hu_")), None)
    tile = target = position = None
    if choice is None:
        choice = next((a for a in ("sy_reveal", "sy_pass", "sy_xi", "sy_wind") if a in actions), None)
    if choice is None:
        for action, kind in (("angang", "concealed"), ("jiagang", "added")):
            candidates = [t for t in sorted(set(p.hand_tiles)) if state.kong_allowed(index, t, kind)] if action in actions else []
            if candidates:
                choice, target = action, candidates[0]
                break
    if choice is None and ("cut" in actions or "riichi_cut" in actions):
        choice = "riichi_cut" if "riichi_cut" in actions and p.riichi_candidate_cuts else "cut"
        legal = set(p.riichi_candidate_cuts) if choice == "riichi_cut" else set(p.hand_tiles)
        if p.ready_locked:
            tile = p.last_drawn_tile
        else:
            tile = await run_room_bot_cpu(state, choose_cut, list(p.hand_tiles), list(p.combination_tiles),
                                         visible_counts(state), legal)
        if not bot_action_is_current(state, index, tick):
            return
        position = len(p.hand_tiles)-1 if p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
    if choice is None:
        river = state.player_list[state.current_player_index].discard_tiles
        choice = (await run_room_bot_cpu(state, choose_claim, list(p.hand_tiles), list(p.combination_tiles),
                                        visible_counts(state), tuple(actions), river[-1])
                  if river and any(a in actions for a in ("peng", "gang", "chi_left", "chi_mid", "chi_right")) else "pass")
    if bot_action_is_current(state, index, tick):
        await submit_bot_action(get_ai_action, state, index, choice,
            infer_bot_cut_class(p.hand_tiles, tile, position, draw_slot=has_draw_slot(p)) if tile is not None else None,
            tile, position, target)
