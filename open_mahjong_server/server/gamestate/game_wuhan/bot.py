"""武汉麻将牌效机器人（方案A）：优先清皮子、遵循258作将、平胡限单癞及开口/合法听口。"""
from collections import Counter

from ...game_calculation.wuhan import rules as book
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.ai.guobiao_shanten import xiangting_yiban
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _BOT_DELAY, _wait_until_actionable
from ..public.hand_slot_utils import infer_bot_cut_class

_JIANG_SET = frozenset(book.JIANG)


def _wuhan_score(hand, melds, indicator, visible):
    joker, skins = book.special_tiles(indicator)
    skin_count = sum(1 for t in hand if t in skins)
    if skin_count > 0:
        # 手里含皮子无法和牌，必须最先处理掉
        return (-100 - skin_count, 0, 0, 0)

    jokers = hand.count(joker)
    natural = [t for t in hand if t != joker]
    counts = Counter(natural)
    # 武汉平胡限最多 1 张癞子；若 >=2 张癞子且未成大胡方向，不再额外奖励多余癞子
    suits = {t // 10 for t in natural if t < 40} | {
        t // 10 for c in melds for t in book.meld_tiles(c) if t < 40
    }
    big_lean = len(suits) <= 1 or all(t in book.FENG for t in natural) or all(t in _JIANG_SET for t in natural)
    eff_jokers = jokers if big_lean else min(1, jokers)
    dist = max(-1, xiangting_yiban(counts, len(melds)) - eff_jokers)

    if dist <= 0:
        waits = book.waits(hand, melds, indicator)
        if waits:
            held = Counter(hand)
            legal_outs = 0
            for w in waits:
                rem = 4 - held[w] - visible.get(w, 0)
                if rem <= 0:
                    continue
                if book.hand_detail(list(hand) + [w], melds, indicator) is not None:
                    legal_outs += rem
            if legal_outs > 0:
                return (0, legal_outs, 1, 1)

    has_258_pair = 1 if any(c >= 2 and t in _JIANG_SET for t, c in counts.items()) else 0
    # 简单连张与258将偏好作为同向听并列项
    conn = sum(3 * min(2, c - 1) for c in counts.values())
    conn += sum(
        counts[t] * (counts[t + 1] * 2 + counts[t + 2])
        for t in counts
        if t < 40 and t % 10 <= 7
    )
    return (min(-1, -dist), has_258_pair, conn, -max(0, jokers - 1) if not big_lean else jokers)


def _choose_cut(hand, melds, indicator, visible):
    joker, skins = book.special_tiles(indicator)
    # 优先扔皮子
    for i, t in enumerate(hand):
        if t in skins:
            return t, i
    best = None
    seen = set()
    for i, tile in enumerate(hand):
        if tile in seen:
            continue
        seen.add(tile)
        rest = hand[:i] + hand[i + 1:]
        cand = (_wuhan_score(rest, melds, indicator, visible), tile != joker, tile == hand[-1], -tile, tile, i)
        if best is None or cand > best:
            best = cand
    return best[4], best[5]


def _choose_claim(hand, melds, indicator, visible, actions, cut_tile):
    hand, melds, actions = list(hand), list(melds), set(actions)
    baseline = _wuhan_score(hand, melds, indicator, visible)
    opened = book.open_count(melds) > 0
    best_action = "pass"
    best_score = baseline
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
            _, idx = _choose_cut(rest, next_melds, indicator, visible)
            rest = rest[:idx] + rest[idx + 1:]
        score = _wuhan_score(rest, next_melds, indicator, visible)
        # 武汉平胡必须开口：未开口时持平向听即可开口；已开口后需严格变好
        if score > best_score or (not opened and score[0] >= baseline[0] and best_action == "pass"):
            best_action, best_score = action, score
    return best_action


def _visible_counts(state):
    vis = Counter([state.indicator])
    for p in state.player_list:
        vis.update(p.discard_tiles)
        for code in p.combination_tiles:
            if not code.startswith("G"):
                vis.update(book.meld_tiles(code))
    return dict(vis)


@paced_bot(lambda: _BOT_DELAY)
async def wuhan_bot_action(state, index, available, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    p = state.player_list[index]
    win = next((a for a in ("hu_self", "hu_first", "hu_second", "hu_third") if a in available), None)
    if win:
        await submit_bot_action(get_ai_action, state, index, win, None, None, None, None)
        return
    for action, kind in (("angang", "concealed"), ("jiagang", "added")):
        if action in available:
            target = next((t for t in sorted(set(p.hand_tiles)) if state.kong_allowed(index, t, kind)), None)
            if target is not None:
                await submit_bot_action(get_ai_action, state, index, action, None, None, None, target)
                return
    if "cut" in available:
        hand = list(p.hand_tiles)
        melds = list(p.combination_tiles)
        vis = _visible_counts(state)
        tile, position = await run_room_bot_cpu(state, _choose_cut, tuple(hand), tuple(melds), state.indicator, vis)
        if bot_action_is_current(state, index, tick):
            await submit_bot_action(
                get_ai_action, state, index, "cut",
                infer_bot_cut_class(hand, tile, position, draw_slot=p.has_draw_slot), tile, position, None,
            )
        return
    claim_ops = [a for a in ("gang", "peng", "chi_left", "chi_mid", "chi_right") if a in available]
    if claim_ops:
        river = state.player_list[state.current_player_index].discard_tiles
        cut_tile = river[-1] if river else None
        if cut_tile is not None:
            vis = _visible_counts(state)
            choice = await run_room_bot_cpu(
                state, _choose_claim, tuple(p.hand_tiles), tuple(p.combination_tiles),
                state.indicator, vis, tuple(available), cut_tile,
            )
            if bot_action_is_current(state, index, tick):
                await submit_bot_action(get_ai_action, state, index, choice, None, None, None, None)
            return
        await submit_bot_action(get_ai_action, state, index, claim_ops[0], None, None, None, None)
        return
    if "pass" in available:
        await submit_bot_action(get_ai_action, state, index, "pass", None, None, None, None)
