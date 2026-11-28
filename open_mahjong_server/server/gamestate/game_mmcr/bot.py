"""青雀牌效机器人（方案A）：结构向听 + 1番起和合法听校验，避免无番副露锁死。"""
from collections import Counter

from ...game_calculation.qingque.hepai import qingque_hepai_check
from ...game_calculation.qingque.tingpai import qingque_tingpai_check
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.ai.guobiao_shanten import shanten_qidui, shanten_shisanyao, xiangting_yiban
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _BOT_DELAY, _RON_HU_ACTIONS, _handle_buhua_round, _wait_until_actionable
from ..public.ai.smart_bot_logic import count_visible_tiles, should_accept_hu, tile_to_34
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class

_WIND_WAY = ("自风东", "自风南", "自风西", "自风北")


def _distance(hand, meld_count):
    counts = Counter(hand)
    d = xiangting_yiban(counts, meld_count)
    if meld_count == 0:
        d = min(d, shanten_qidui(counts), shanten_shisanyao(counts))
    return d


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


def _legal_waits_outs(hand, melds, visible, seat_wind_idx):
    waits = qingque_tingpai_check(list(hand), list(melds))
    if not waits:
        return 0
    held = Counter(hand)
    way = [_WIND_WAY[seat_wind_idx % 4]]
    outs = 0
    for w in waits:
        rem = 4 - held[w] - visible[tile_to_34(w)]
        if rem <= 0:
            continue
        score, _ = qingque_hepai_check(list(hand), list(melds), way, w)
        if score >= 1:
            outs += rem
    return outs


def _score(hand, melds, visible, seat_wind_idx):
    hand = list(hand)
    dist = _distance(hand, len(melds))
    if dist <= 0:
        legal_outs = _legal_waits_outs(hand, melds, visible, seat_wind_idx)
        if legal_outs > 0:
            return (0, legal_outs)
    held = Counter(hand)
    outs = sum(
        max(0, 4 - held[t] - visible[tile_to_34(t)])
        for t in _candidate_tiles(held)
        if held[t] < 4 and _distance(hand + [t], len(melds)) < dist
    )
    return (min(-1, -dist), outs)


def _best_cut(hand, melds, visible, seat_wind_idx):
    best = None
    seen = set()
    for i, tile in enumerate(hand):
        if tile in seen:
            continue
        seen.add(tile)
        rest = hand[:i] + hand[i + 1:]
        cand = (_score(rest, melds, visible, seat_wind_idx), tile == hand[-1], -tile, tile, i)
        if best is None or cand > best:
            best = cand
    return best[3], best[4], best[0]


def _choose_hand_action(hand, melds, visible, actions, seat_wind_idx):
    hand, melds, actions = list(hand), list(melds), set(actions)
    baseline = _score(hand, melds, visible, seat_wind_idx)
    if "angang" in actions:
        for tile in sorted(set(hand)):
            if hand.count(tile) >= 4:
                rest = [t for t in hand if t != tile]
                if _score(rest, melds + [f"G{tile}"], visible, seat_wind_idx) >= baseline:
                    return "angang", tile, -1
    if "jiagang" in actions:
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
                    if _score(rest, upgraded, visible, seat_wind_idx) >= baseline:
                        return "jiagang", tile, -1
    if "cut" in actions and hand:
        tile, idx, _ = _best_cut(hand, melds, visible, seat_wind_idx)
        return "cut", tile, idx
    return "pass", None, -1


def _choose_claim_action(hand, melds, visible, actions, cut_tile, seat_wind_idx):
    hand, melds, actions = list(hand), list(melds), set(actions)
    best_action = "pass"
    best_score = _score(hand, melds, visible, seat_wind_idx)
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
        if need_cut and rest:
            _, _, score = _best_cut(rest, next_melds, visible, seat_wind_idx)
        else:
            score = _score(rest, next_melds, visible, seat_wind_idx)
        # 副露不可落入 0 番假听（结构已听但无合法 >=1 番听口）
        if score > best_score:
            best_action, best_score = action, score
    return best_action


@paced_bot(lambda: _BOT_DELAY)
async def qingque_smart_bot_action(game_state, player_index: int, action_list: list, game_status: str):
    if not await _wait_until_actionable(game_state, player_index):
        return
    expected_tick = getattr(game_state, "server_action_tick", None)
    if not bot_action_is_current(game_state, player_index, expected_tick):
        return
    player = game_state.player_list[player_index]

    if game_status == "waiting_buhua_round":
        await _handle_buhua_round(game_state, player_index, action_list, player)
        return

    if game_status in ("waiting_hand_action", "onlycut_after_action"):
        if "buhua" in action_list:
            await submit_bot_action(get_ai_action, game_state, player_index, "buhua", None, None, None, None)
            return
        if "hu_self" in action_list and should_accept_hu(game_state, player_index, "hu_self"):
            await submit_bot_action(get_ai_action, game_state, player_index, "hu_self", None, None, None, None)
            return
        hand = list(player.hand_tiles)
        melds = list(getattr(player, "combination_tiles", []))
        visible = count_visible_tiles(game_state, player_index)
        action, tile, cut_index = await run_room_bot_cpu(
            game_state, _choose_hand_action, tuple(hand), tuple(melds), tuple(visible), tuple(action_list), player_index
        )
        if not bot_action_is_current(game_state, player_index, expected_tick):
            return
        if action == "cut" and tile is not None:
            is_moqie = infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player))
            await submit_bot_action(get_ai_action, game_state, player_index, "cut", is_moqie, tile, cut_index, None)
        elif action in ("angang", "jiagang"):
            await submit_bot_action(get_ai_action, game_state, player_index, action, None, None, None, tile)
        return

    if game_status == "waiting_action_qianggang":
        for hu in _RON_HU_ACTIONS:
            if hu in action_list and should_accept_hu(game_state, player_index, hu):
                await submit_bot_action(get_ai_action, game_state, player_index, hu, None, None, None, None)
                return
        if "pass" in action_list:
            await submit_bot_action(get_ai_action, game_state, player_index, "pass", None, None, None, None)
        return

    if game_status == "waiting_action_after_cut":
        for hu in _RON_HU_ACTIONS:
            if hu in action_list and should_accept_hu(game_state, player_index, hu):
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
            game_state,
            _choose_claim_action,
            tuple(hand),
            tuple(melds),
            tuple(visible),
            tuple(action_list),
            cut_tile,
            player_index,
        )
        if bot_action_is_current(game_state, player_index, expected_tick):
            await submit_bot_action(get_ai_action, game_state, player_index, choice, None, None, None, None)
