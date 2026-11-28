"""新加坡麻将牌效机器人（方案A）：结构向听 + >=1台合法听校验，避免无台副露破平胡。"""
from collections import Counter

from ...game_calculation.singapore import rules as book
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.ai.guobiao_shanten import xiangting_yiban
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _BOT_DELAY, _wait_until_actionable
from ..public.ai.smart_bot_logic import count_visible_tiles, tile_to_34
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class


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


def _score(hand, melds, flowers, visible, seat, round_wind):
    hand = list(hand)
    held = Counter(hand)
    dist = xiangting_yiban(held, len(melds))
    if dist <= 0:
        waits = book.structural_waits(tuple(sorted(hand)), tuple(melds))
        if waits:
            legal_outs = 0
            for w in waits:
                rem = 4 - held[w] - visible[tile_to_34(w)]
                if rem <= 0:
                    continue
                if book.score(hand + [w], melds, flowers, winning_tile=w, self_draw=True,
                              seat=seat, round_wind=round_wind) is not None:
                    legal_outs += rem
            if legal_outs > 0:
                return (0, legal_outs)
    outs = sum(
        max(0, 4 - held[t] - visible[tile_to_34(t)])
        for t in _candidate_tiles(held)
        if held[t] < 4 and xiangting_yiban(Counter(hand + [t]), len(melds)) < dist
    )
    return (min(-1, -dist), outs)


def _best_cut(hand, melds, flowers, visible, seat, round_wind):
    best = None
    seen = set()
    for i, tile in enumerate(hand):
        if tile in seen:
            continue
        seen.add(tile)
        rest = hand[:i] + hand[i + 1:]
        cand = (_score(rest, melds, flowers, visible, seat, round_wind), tile == hand[-1], -tile, tile, i)
        if best is None or cand > best:
            best = cand
    return best[3], best[4], best[0]


def _has_open_tai_path(hand, melds, flowers, seat, round_wind):
    if book.bonus_fans(flowers, seat):
        return True
    yakuhai = {45, 46, 47, 41 + seat, round_wind}
    for code in melds:
        if code[0] in ("k", "K", "g", "G") and int(code[1:]) in yakuhai:
            return True
    if all(code[0] in ("s", "S") for code in melds):
        return True  # 平胡路线仍在
    if all(code[0] in ("k", "K", "g", "G") for code in melds):
        counts = Counter(hand)
        if len(melds) + sum(1 for c in counts.values() if c >= 2) >= 4:
            return True
    all_tiles = list(hand) + [int(c[1:]) for c in melds]
    suits = {t // 10 for t in all_tiles if t < 40}
    if len(suits) <= 1:
        return True
    return False


def _choose_claim(hand, melds, flowers, visible, actions, cut_tile, seat, round_wind):
    hand, melds, actions = list(hand), list(melds), set(actions)
    best_action = "pass"
    best_score = _score(hand, melds, flowers, visible, seat, round_wind)
    options = (
        ("gang", (cut_tile, cut_tile, cut_tile), f"g{cut_tile}", False),
        ("peng", (cut_tile, cut_tile), f"k{cut_tile}", True),
        ("chi_left", (cut_tile - 2, cut_tile - 1), f"s{cut_tile - 1}", True),
        ("chi_mid", (cut_tile - 1, cut_tile + 1), f"s{cut_tile}", True),
        ("chi_right", (cut_tile + 1, cut_tile + 2), f"s{cut_tile + 1}", True),
    )
    for action, needed, code, need_cut in options:
        if action not in actions:
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
        next_melds = melds + [code]
        if need_cut and rest:
            _, idx, score = _best_cut(rest, next_melds, flowers, visible, seat, round_wind)
            after_hand = rest[:idx] + rest[idx + 1:]
        else:
            after_hand = rest
            score = _score(after_hand, next_melds, flowers, visible, seat, round_wind)
        if not _has_open_tai_path(after_hand, next_melds, flowers, seat, round_wind):
            continue
        if score > best_score:
            best_action, best_score = action, score
    return best_action


@paced_bot(lambda: _BOT_DELAY)
async def singapore_bot_action(state, index, action_list, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    p = state.player_list[index]
    priority = next((a for a in ("ready", "hu_self", "hu_first", "hu_second", "hu_third", "buhua") if a in action_list), None)
    if priority:
        await submit_bot_action(get_ai_action, state, index, priority, False, None, None, None)
        return
    for action in ("angang", "jiagang"):
        if action in action_list:
            candidates = state._kong_candidates(index, action == "jiagang")
            if candidates:
                await submit_bot_action(get_ai_action, state, index, action, False, None, None, candidates[0])
                return
    seat = (index - int(getattr(state, "dealer_index", 0) or 0)) % 4
    round_wind = 41 + (int(getattr(state, "round_wind", 0) or 0) % 4)
    flowers = tuple(getattr(p, "huapai_list", ()))
    if "cut" in action_list:
        hand = list(p.hand_tiles)
        tile, cut_index, _ = await run_room_bot_cpu(
            state, _best_cut, tuple(hand), tuple(p.combination_tiles), flowers,
            tuple(count_visible_tiles(state)), seat, round_wind,
        )
        if bot_action_is_current(state, index, tick):
            await submit_bot_action(
                get_ai_action, state, index, "cut",
                infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(p)),
                tile, cut_index, None,
            )
        return
    claim_ops = [a for a in ("gang", "peng", "chi_left", "chi_mid", "chi_right") if a in action_list]
    if claim_ops:
        river = state.player_list[state.current_player_index].discard_tiles
        cut_tile = river[-1] if river else None
        if cut_tile is not None:
            choice = await run_room_bot_cpu(
                state, _choose_claim, tuple(p.hand_tiles), tuple(p.combination_tiles), flowers,
                tuple(count_visible_tiles(state)), tuple(action_list), cut_tile, seat, round_wind,
            )
            if bot_action_is_current(state, index, tick):
                await submit_bot_action(get_ai_action, state, index, choice, False, None, None, None)
            return
    if "pass" in action_list:
        await submit_bot_action(get_ai_action, state, index, "pass", False, None, None, None)
