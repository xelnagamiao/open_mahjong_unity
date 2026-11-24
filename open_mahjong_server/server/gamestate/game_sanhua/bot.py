import time

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
        return -distance(ordinary, melds) + hand.count(state.joker)
    def best_cut(hand, melds, legal=None):
        return max((quality(hand[:i] + hand[i+1:], melds), i) for i, tile in enumerate(hand)
                   if legal is None or tile in legal)
    if "cut" in actions:
        _, i = best_cut(p.hand_tiles, len(p.combination_tiles), state.cut_tiles(seat))
        return dict(action_type="cut", TileId=p.hand_tiles[i], cutIndex=i,
                    cutClass=p.has_draw_slot and i == len(p.hand_tiles)-1)
    best = quality(p.hand_tiles, len(p.combination_tiles))
    chosen = "pass"
    for action in actions:
        if action in (*CHOWS, "peng", "gang"):
            hand, melds = after_meld(p, action, state.live_pending_window["tile"])
            score = quality(hand, len(melds)) if action == "gang" else best_cut(hand, len(melds))[0]
            if score >= best:
                best, chosen = score, action
    return dict(action_type=chosen)


async def play(state, seat, tick):
    await wait_bot_delay(state, time.monotonic())
    if tick == state.server_action_tick and seat in state.waiting_players_list and state.action_queues[seat].empty():
        await state.submit_action(seat, **choose(state, seat, state.action_dict[seat]))
