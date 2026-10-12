from collections import Counter
from ..game_hongkong.bot import distance
import time
from ..public.ai.pacing import wait_bot_delay


def choose_action(state, index):
    actions = state.action_dict[index]
    p = state.player_list[index]
    for action in ("hu_self", "hu", "buhua", "ready"):
        if action in actions:
            return dict(action_type=action)
    for action in ("angang", "jiagang"):
        if action in actions:
            return dict(action_type=action, target_tile=min(state.kong_tiles(index, action)))
    if "cut" in actions:
        candidates = []
        for tile in state.legal_discard_tiles(index):
            hand = list(p.hand_tiles)
            hand.remove(tile)
            counts = Counter(hand)
            paired = sum(v // 2 for v in counts.values())
            d = distance(hand, len(p.combination_tiles), 5)
            if state.rules.seven_pairs and len(p.combination_tiles) <= 1:
                d = min(d, 8 - paired)
            candidates.append((d, -int(tile == p.hand_tiles[-1]), tile))
        tile = min(candidates)[-1]
        position = len(p.hand_tiles) - 1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        return dict(action_type="cut", TileId=tile, cutIndex=position,
                    cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1)
    for action in ("gang", "peng", "chi_left", "chi_mid", "chi_right"):
        if action in actions:
            return dict(action_type=action)
    return dict(action_type="pass")


async def play_bot(state, index, tick):
    started = time.monotonic()
    data = choose_action(state, index)
    await wait_bot_delay(state, started)
    if state.server_action_tick != tick or index not in state.waiting_players_list:
        return
    action = data.pop("action_type")
    await state.submit_action(index, action, **data)
