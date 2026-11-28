"""标准长沙牌效机器人（方案A）：遵循258作将与大胡和型（七对/将将胡/清一色/碰碰胡）。"""
from collections import Counter

from ...game_calculation.changsha.changsha_hepai_check import _JIANG_PAIR_TILES, _expand_meld
from ...game_calculation.changsha.changsha_tingpai_check import Changsha_Tingpai_Check
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.ai.guobiao_shanten import xiangting_yiban
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _BOT_DELAY, _RON_HU_ACTIONS, _wait_until_actionable
from ..public.ai.smart_bot_logic import count_visible_tiles, tile_to_34
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class

_TINGPAI = Changsha_Tingpai_Check()


def _distance(hand, melds):
    counts = Counter(hand)
    value = xiangting_yiban(counts, len(melds))
    if not melds:
        value = min(value, 6 - sum(n // 2 for n in counts.values()))
    if all(t in _JIANG_PAIR_TILES for code in melds for t in _expand_meld(code)):
        value = min(value, 13 - 3 * len(melds) - sum(n for t, n in counts.items() if t in _JIANG_PAIR_TILES))
    return value


def _candidate_tiles(held):
    cands = set()
    for t, c in held.items():
        if c <= 0:
            continue
        if c < 4:
            cands.add(t)
        suit, num = t // 10, t % 10
        if suit in (1, 2, 3):
            for d in (-2, -1, 1, 2):
                n2 = num + d
                if 1 <= n2 <= 9:
                    nid = suit * 10 + n2
                    if held[nid] < 4:
                        cands.add(nid)
    return cands


def _score(hand, melds, visible):
    hand = list(hand)
    dist = _distance(hand, melds)
    held = Counter(hand)
    if dist <= 0:
        waits = _TINGPAI.tingpai_check(hand, list(melds))
        if waits:
            return (0, sum(max(0, 4 - held[t] - visible[tile_to_34(t)]) for t in waits), 1)
    outs = sum(
        max(0, 4 - held[t] - visible[tile_to_34(t)])
        for t in _candidate_tiles(held)
        if held[t] < 4 and _distance(hand + [t], melds) < dist
    )
    has_258_pair = 1 if any(c >= 2 and t in _JIANG_PAIR_TILES for t, c in held.items()) else 0
    return (min(-1, -dist), outs, has_258_pair)


def _best_cut(hand, melds, visible):
    best = None
    seen = set()
    for i, tile in enumerate(hand):
        if tile in seen:
            continue
        seen.add(tile)
        rest = hand[:i] + hand[i + 1:]
        cand = (_score(rest, melds, visible), tile == hand[-1], -tile, tile, i)
        if best is None or cand > best:
            best = cand
    return best[3], best[4], best[0]


def _choose_hand_action(hand, melds, visible, actions):
    hand, melds, actions = list(hand), list(melds), set(actions)
    baseline = _score(hand, melds, visible)
    for action in ("buzhang", "angang", "jiagang"):
        if action not in actions:
            continue
        if action in ("buzhang", "jiagang"):
            for code in melds:
                if code.startswith("k"):
                    try:
                        tile = int(code[1:])
                    except ValueError:
                        continue
                    if tile in hand:
                        rest = hand[:]
                        rest.remove(tile)
                        upgraded = [f"g{tile}" if m == code else m for m in melds]
                        if _score(rest, upgraded, visible) >= baseline:
                            return action, tile, -1
        if action in ("buzhang", "angang"):
            for tile in sorted(set(hand)):
                if hand.count(tile) >= 4:
                    rest = [t for t in hand if t != tile]
                    if _score(rest, melds + [f"G{tile}"], visible) >= baseline:
                        return action, tile, -1
    if "cut" in actions and hand:
        tile, idx, _ = _best_cut(hand, melds, visible)
        return "cut", tile, idx
    return "pass", None, -1


def _choose_claim_action(hand, melds, visible, actions, cut_tile):
    hand, melds, actions = list(hand), list(melds), set(actions)
    best_action = "pass"
    best_score = _score(hand, melds, visible)
    options = (
        ("peng", (cut_tile, cut_tile), f"k{cut_tile}", True),
        ("gang", (cut_tile, cut_tile, cut_tile), f"g{cut_tile}", False),
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
        score = _best_cut(rest, next_melds, visible)[2] if need_cut and rest else _score(rest, next_melds, visible)
        if score > best_score:
            best_action, best_score = action, score
    return best_action


@paced_bot(lambda: _BOT_DELAY)
async def changsha_smart_bot_action(game_state, player_index: int, action_list: list, game_status: str):
    if not await _wait_until_actionable(game_state, player_index):
        return
    expected_tick = getattr(game_state, "server_action_tick", None)
    if not bot_action_is_current(game_state, player_index, expected_tick):
        return
    player = game_state.player_list[player_index]

    if game_status in ("waiting_initial_hu", "waiting_sea_bottom"):
        action = next((a for a in ("initial_hu", "sea_bottom", "pass") if a in action_list), None)
        if action:
            await submit_bot_action(get_ai_action, game_state, player_index, action, None, None, None, None)
        return

    if game_status in ("waiting_hand_action", "onlycut_after_action"):
        if "hu_self" in action_list:
            await submit_bot_action(get_ai_action, game_state, player_index, "hu_self", None, None, None, None)
            return
        hand = list(player.hand_tiles)
        melds = list(getattr(player, "combination_tiles", []))
        visible = count_visible_tiles(game_state, player_index)
        action, tile, cut_index = await run_room_bot_cpu(
            game_state, _choose_hand_action, tuple(hand), tuple(melds), tuple(visible), tuple(action_list)
        )
        if not bot_action_is_current(game_state, player_index, expected_tick):
            return
        if action == "cut" and tile is not None:
            is_moqie = infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player))
            await submit_bot_action(get_ai_action, game_state, player_index, "cut", is_moqie, tile, cut_index, None)
        elif action in ("buzhang", "angang", "jiagang"):
            await submit_bot_action(get_ai_action, game_state, player_index, action, None, None, None, tile)
        return

    if game_status == "waiting_action_qianggang":
        hu = next((a for a in _RON_HU_ACTIONS if a in action_list), None)
        choice = hu or ("pass" if "pass" in action_list else None)
        if choice:
            await submit_bot_action(get_ai_action, game_state, player_index, choice, None, None, None, None)
        return

    if game_status == "waiting_action_after_cut":
        hu = next((a for a in _RON_HU_ACTIONS if a in action_list), None)
        if hu:
            await submit_bot_action(get_ai_action, game_state, player_index, hu, None, None, None, None)
            return
        discards = game_state.player_list[game_state.current_player_index].discard_tiles
        cut_tile = discards[-1] if discards else None
        if cut_tile is None:
            if "pass" in action_list:
                await submit_bot_action(get_ai_action, game_state, player_index, "pass", None, None, None, None)
            return
        hand = list(player.hand_tiles)
        melds = list(getattr(player, "combination_tiles", []))
        visible = count_visible_tiles(game_state, player_index)
        choice = await run_room_bot_cpu(
            game_state, _choose_claim_action, tuple(hand), tuple(melds), tuple(visible), tuple(action_list), cut_tile
        )
        if bot_action_is_current(game_state, player_index, expected_tick):
            await submit_bot_action(get_ai_action, game_state, player_index, choice, None, None, None, None)
