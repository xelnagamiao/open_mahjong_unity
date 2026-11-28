"""长春麻将牌效机器人（方案A）：遵循三门齐、幺九及刻子/箭将门槛，不盲目碰杠拆资格。"""
from collections import Counter

from ...game_calculation.changchun import rules as book
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.ai.guobiao_shanten import xiangting_yiban
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _BOT_DELAY, _wait_until_actionable
from ..public.ai.smart_bot_logic import tile_to_34
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class


def visible_counts(state, index):
    visible = [0] * 34
    for player in state.player_list:
        for tile in player.discard_tiles:
            visible[tile_to_34(tile)] += 1
        for code in player.combination_tiles:
            if code.startswith("G") and player.player_index != index:
                continue
            for tile in book.parse_meld(code).physical:
                visible[tile_to_34(tile)] += 1
    if state.bao_visible(index):
        visible[tile_to_34(state.cc_bao_tile)] += 1
    return visible


def _qualify_bonus(hand, codes):
    melds = [book.parse_meld(c) for c in codes]
    all_tiles = list(hand) + [t for m in melds for t in m.logical]
    suits = len({t // 10 for t in all_tiles if t in book.NUMBERS})
    has_orphan = 1 if (set(all_tiles) & book.ORPHANS) else 0
    counts = Counter(hand)
    has_trip_or_dragon = 1 if (
        any(m.kind != "sequence" for m in melds)
        or any(c >= 3 for c in counts.values())
        or any(counts[d] >= 2 for d in book.DRAGONS)
    ) else 0
    return suits + has_orphan * 2 + has_trip_or_dragon


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


def _score(hand, codes, visible):
    hand = list(hand)
    held = Counter(hand)
    dist = xiangting_yiban(held, len(codes))
    if not codes:
        dist = min(dist, 6 - sum(n // 2 for n in held.values()))
    if dist <= 0:
        waits = book.waits(hand, codes)
        if waits:
            outs = sum(max(0, 4 - held[t] - visible[tile_to_34(t)]) for t in waits)
            return (0, outs, _qualify_bonus(hand, codes))
    outs = sum(
        max(0, 4 - held[t] - visible[tile_to_34(t)])
        for t in _candidate_tiles(held)
        if held[t] < 4 and xiangting_yiban(Counter(hand + [t]), len(codes)) < dist
    )
    return (min(-1, -dist), _qualify_bonus(hand, codes), outs)


def choose_cut(hand, codes, visible):
    best = None
    seen = set()
    for i, tile in enumerate(hand):
        if tile in seen:
            continue
        seen.add(tile)
        rest = hand[:i] + hand[i + 1:]
        cand = (_score(rest, codes, visible), tile == hand[-1], -tile, tile, i)
        if best is None or cand > best:
            best = cand
    return best[3], best[4]


def choose_claim(hand, codes, visible, available, cut_tile):
    hand, codes, available = list(hand), list(codes), set(available)
    best_action = "pass"
    best_score = _score(hand, codes, visible)
    options = (
        ("gang", (cut_tile, cut_tile, cut_tile), f"g{cut_tile}", False),
        ("peng", (cut_tile, cut_tile), f"k{cut_tile}", True),
        ("chi_left", (cut_tile - 2, cut_tile - 1), f"s{cut_tile - 1}", True),
        ("chi_mid", (cut_tile - 1, cut_tile + 1), f"s{cut_tile}", True),
        ("chi_right", (cut_tile + 1, cut_tile + 2), f"s{cut_tile + 1}", True),
    )
    for action, needed, code, need_cut in options:
        if action not in available:
            continue
        rest = hand[:]
        valid = True
        for t in needed:
            if t not in rest:
                valid = False
                break
            rest.remove(t)
        if not valid:
            continue
        next_codes = codes + [code]
        if need_cut and rest:
            _, idx = choose_cut(rest, next_codes, visible)
            rest = rest[:idx] + rest[idx + 1:]
        score = _score(rest, next_codes, visible)
        if score > best_score:
            best_action, best_score = action, score
    return best_action


@paced_bot(lambda: _BOT_DELAY)
async def changchun_bot_action(state, index, available, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return

    async def submit(action, target=None, tile=None, cut_index=None):
        await submit_bot_action(
            get_ai_action, state, index, action,
            infer_bot_cut_class(
                state.player_list[index].hand_tiles, tile, cut_index,
                draw_slot=has_draw_slot(state.player_list[index]),
            ) if tile is not None else None,
            tile, cut_index, target,
        )

    win = next((a for a in available if a.startswith("hu_")), None)
    if win:
        return await submit(win)
    for action in ("cc_change_bao", "cc_draw", "cc_pass"):
        if action in available:
            return await submit(action)
    player = state.player_list[index]
    if status not in ("waiting_hand_action", "onlycut_after_action"):
        river = state.player_list[state.current_player_index].discard_tiles
        cut_tile = river[-1] if river else None
        if cut_tile is not None and any(a in available for a in ("gang", "peng", "chi_left", "chi_mid", "chi_right")):
            choice = await run_room_bot_cpu(
                state, choose_claim, tuple(player.hand_tiles), tuple(player.combination_tiles),
                tuple(visible_counts(state, index)), tuple(available), cut_tile,
            )
            if bot_action_is_current(state, index, tick):
                return await submit(choice)
            return
        return await submit("pass")
    for action, builder in (("cc_special", state.special_candidates), ("cc_added", state.special_added_candidates)):
        if action in available:
            candidates = builder(index)
            if candidates:
                return await submit(action, candidates[0]["token"])
    hand = list(player.hand_tiles)
    for action, kind in (("angang", "concealed"), ("jiagang", "added")):
        if action in available:
            tile = next((t for t in sorted(set(hand)) if state.kong_allowed(index, t, kind)), None)
            if tile is not None:
                return await submit(action, tile)
    action = "cut"
    if player.ready_locked:
        tile, cut_index = player.last_drawn_tile, len(hand) - 1
    elif "riichi_cut" in available and player.riichi_candidate_cuts:
        visible = visible_counts(state, index)
        tile = max(
            sorted(player.riichi_candidate_cuts),
            key=lambda t: sum(max(0, 4 - visible[tile_to_34(w)] - hand.count(w)) for w in player.riichi_candidate_cuts[t]),
        )
        cut_index, action = hand.index(tile), "riichi_cut"
    else:
        tile, cut_index = await run_room_bot_cpu(
            state, choose_cut, tuple(hand), tuple(player.combination_tiles), tuple(visible_counts(state, index))
        )
    if bot_action_is_current(state, index, tick):
        await submit(action, tile=tile, cut_index=cut_index)
