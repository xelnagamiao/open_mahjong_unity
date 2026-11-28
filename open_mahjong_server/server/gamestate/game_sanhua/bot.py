from collections import Counter
import time

from ...game_calculation.sanhua import rules as sanhua_rules
from ..public.ai.auto_cut_policy import choose_auto_cut_action, is_auto_cut_bot
from ..public.ai.pacing import wait_bot_delay
from ..game_new_chinese.bot import distance
from ..game_new_chinese.actions import CHOWS, after_meld


def choose(state, seat, actions):
    p = state.player_list[seat]
    if is_auto_cut_bot(p):
        return choose_auto_cut_action(p, actions, state.cut_tiles(seat))
    for action in ("hu_self", "hu", "ready"):
        if action in actions:
            return dict(action_type=action)
    for action in ("angang", "jiagang"):
        if action in actions:
            return dict(action_type=action, target_tile=min(state.kong_tiles(seat, action)))
    if "baoqing" in actions and all(t == state.joker or t // 10 == p.calls[0] for t in p.hand_tiles):
        return dict(action_type="baoqing")

    def quality(hand, melds):
        ordinary = tuple(sorted(t for t in hand if t != state.joker))
        jokers = hand.count(state.joker)
        counts = Counter(ordinary)
        suits = {t // 10 for t in ordinary if t < 40}
        # 三花小屁和限单癞 + 258将 + 开口；非清一色/七对方向时，多余癞子不额外加分
        big_lean = len(suits) <= 1 or (melds == 0 and sum(c // 2 for c in counts.values()) >= 5)
        eff_jokers = jokers if big_lean else min(1, jokers)
        has_258_pair = 1 if any(c >= 2 and t in sanhua_rules.JIANG for t, c in counts.items()) else 0
        return (-distance(ordinary, melds) + eff_jokers, has_258_pair, -max(0, jokers - 1) if not big_lean else jokers)

    def best_cut(hand, melds, legal=None):
        return max((quality(hand[:i] + hand[i+1:], melds), i) for i, tile in enumerate(hand)
                   if legal is None or tile in legal)
    if "cut" in actions:
        _, i = best_cut(p.hand_tiles, len(p.combination_tiles), state.cut_tiles(seat))
        return dict(action_type="cut", TileId=p.hand_tiles[i], cutIndex=i,
                    cutClass=p.has_draw_slot and i == len(p.hand_tiles)-1)
    best = quality(p.hand_tiles, len(p.combination_tiles))
    opened = any(not c.startswith("G") for c in p.combination_tiles)
    chosen = "pass"
    for action in actions:
        if action in (*CHOWS, "peng", "gang"):
            hand, melds = after_meld(p, action, state.live_pending_window["tile"])
            score = quality(hand, len(melds)) if action == "gang" else best_cut(hand, len(melds))[0]
            # 小屁和需开口：未开口时持平向听即可开口，已开口后严格不降
            if score > best or (not opened and score[0] >= best[0] and chosen == "pass"):
                best, chosen = score, action
    return dict(action_type=chosen)


async def play(state, seat, tick):
    await wait_bot_delay(state, time.monotonic())
    if tick == state.server_action_tick and seat in state.waiting_players_list and state.action_queues[seat].empty():
        await state.submit_action(seat, **choose(state, seat, state.action_dict[seat]))
