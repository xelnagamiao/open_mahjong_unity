"""国标牌效机器人（user_id=2，方案A轻量版）。

与高性能罗伯特（user_id=3）保持清晰难度梯度：
- 使用 guobiao_shanten 识别国标全部五种和型（一般型/七对/十三幺/全不靠/组合龙）；
- 仅在 0 向听（听牌期）校验起和番（hepai_limit），避免走出不足起和番的假听或无番副露锁死；
- 不做一向听前瞻、死一向听重塑、仅自摸折算或做牌风格加权。
"""
from collections import Counter

from ...game_calculation.gb_tingpai_check import Chinese_Tingpai_Check
from ...game_calculation.guobiao_hepai_check import Chinese_Hepai_Check
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.ai.guobiao_shanten import counts_from_tiles, effective_tiles, guobiao_shanten, normalize_tile
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _BOT_DELAY, _RON_HU_ACTIONS, _handle_buhua_round, _wait_until_actionable
from ..public.ai.smart_bot_logic import count_visible_tiles, should_accept_hu, tile_to_34
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class

_TINGPAI = None
_HEPAI = None
_WIND_LABELS = ("东", "南", "西", "北")


def _checkers():
    global _TINGPAI, _HEPAI
    if _TINGPAI is None:
        _TINGPAI = Chinese_Tingpai_Check()
    if _HEPAI is None:
        _HEPAI = Chinese_Hepai_Check()
    return _TINGPAI, _HEPAI


def _qualifying_tenpai_outs(hand, melds, visible, min_fan, round_wind, seat_wind):
    tingpai, hepai = _checkers()
    waits = tingpai.tingpai_check(list(hand), list(melds))
    if not waits:
        return 0
    held = Counter(hand)
    base_way = [f"场风{_WIND_LABELS[round_wind % 4]}", f"自风{_WIND_LABELS[seat_wind % 4]}"]
    if len(waits) == 1:
        base_way.append("和单张")
    outs = 0
    for w in waits:
        rem = 4 - held[w] - visible[tile_to_34(w)]
        if rem <= 0:
            continue
        if min_fan <= 1:
            outs += rem
            continue
        # 点和或自摸任一达到起和番即视为有效合法听口
        score_ron, _ = hepai.hepai_check(list(hand) + [w], list(melds), base_way + ["点和"], w)
        if score_ron >= min_fan:
            outs += rem
            continue
        score_tsumo, _ = hepai.hepai_check(list(hand) + [w], list(melds), base_way + ["自摸"], w)
        if score_tsumo >= min_fan:
            outs += rem
    return outs


def _score(hand, melds, visible, min_fan, round_wind, seat_wind):
    norm = [normalize_tile(t) for t in hand]
    counts = counts_from_tiles(norm)
    dist = guobiao_shanten(counts, len(melds), specials=True)
    if dist <= 0:
        legal_outs = _qualifying_tenpai_outs(norm, melds, visible, min_fan, round_wind, seat_wind)
        if legal_outs > 0:
            return (0, legal_outs)
        # 假听（不足起和番）降级为 -1，优先寻找能达起和番的拆张
        eff = effective_tiles(counts, len(melds), specials=True)
        outs = sum(max(0, 4 - counts.get(t, 0) - visible[tile_to_34(t)]) for t in eff)
        return (-1, outs)
    eff = effective_tiles(counts, len(melds), specials=True)
    outs = sum(max(0, 4 - counts.get(t, 0) - visible[tile_to_34(t)]) for t in eff)
    return (-dist, outs)


def _best_cut(hand, melds, visible, min_fan, round_wind, seat_wind):
    best = None
    seen = set()
    for i, tile in enumerate(hand):
        if tile in seen:
            continue
        seen.add(tile)
        rest = hand[:i] + hand[i + 1:]
        cand = (_score(rest, melds, visible, min_fan, round_wind, seat_wind), tile == hand[-1], -tile, tile, i)
        if best is None or cand > best:
            best = cand
    return best[3], best[4], best[0]


def _choose_hand_action(hand, melds, visible, actions, min_fan, round_wind, seat_wind):
    hand, melds, actions = list(hand), list(melds), set(actions)
    baseline = _score(hand, melds, visible, min_fan, round_wind, seat_wind)
    if "angang" in actions:
        for tile in sorted(set(hand)):
            if hand.count(tile) >= 4:
                rest = [t for t in hand if t != tile]
                if _score(rest, melds + [f"G{normalize_tile(tile)}"], visible, min_fan, round_wind, seat_wind) >= baseline:
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
                    if _score(rest, upgraded, visible, min_fan, round_wind, seat_wind) >= baseline:
                        return "jiagang", tile, -1
    if "cut" in actions and hand:
        tile, idx, _ = _best_cut(hand, melds, visible, min_fan, round_wind, seat_wind)
        return "cut", tile, idx
    return "pass", None, -1


def _choose_claim_action(hand, melds, visible, actions, cut_tile, min_fan, round_wind, seat_wind):
    hand, melds, actions = list(hand), list(melds), set(actions)
    cut_norm = normalize_tile(cut_tile)
    best_action = "pass"
    best_score = _score(hand, melds, visible, min_fan, round_wind, seat_wind)
    options = (
        ("peng", (cut_norm, cut_norm), f"k{cut_norm}", True),
        ("gang", (cut_norm, cut_norm, cut_norm), f"g{cut_norm}", False),
        ("chi_left", (cut_norm - 2, cut_norm - 1), f"s{cut_norm - 1}", True),
        ("chi_mid", (cut_norm - 1, cut_norm + 1), f"s{cut_norm}", True),
        ("chi_right", (cut_norm + 1, cut_norm + 2), f"s{cut_norm + 1}", True),
    )
    for action, needed, code, need_cut in options:
        if action not in actions:
            continue
        rest = [normalize_tile(t) for t in hand]
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
            _, _, score = _best_cut(rest, next_melds, visible, min_fan, round_wind, seat_wind)
        else:
            score = _score(rest, next_melds, visible, min_fan, round_wind, seat_wind)
        if score > best_score:
            best_action, best_score = action, score
    return best_action


def _winds(game_state, player_index):
    round_wind = int(getattr(game_state, "round_wind", 0) or 0) % 4
    dealer_idx = int(getattr(game_state, "dealer_index", 0) or 0)
    seat_wind = (player_index - dealer_idx) % max(1, len(getattr(game_state, "player_list", ())) or 4)
    return round_wind, seat_wind


@paced_bot(lambda: _BOT_DELAY)
async def guobiao_efficiency_action(game_state, player_index: int, action_list: list, game_status: str):
    if not await _wait_until_actionable(game_state, player_index):
        return
    expected_tick = getattr(game_state, "server_action_tick", None)
    if not bot_action_is_current(game_state, player_index, expected_tick):
        return
    player = game_state.player_list[player_index]

    if game_status == "waiting_buhua_round":
        await _handle_buhua_round(game_state, player_index, action_list, player)
        return

    if game_status == "waiting_flower_choice":
        if "hu_flower" in action_list:
            await submit_bot_action(get_ai_action, game_state, player_index, "hu_flower", None, None, None, None)
        return

    min_fan = int(getattr(game_state, "hepai_limit", 8) or 0)
    round_wind, seat_wind = _winds(game_state, player_index)

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
            game_state, _choose_hand_action, tuple(hand), tuple(melds), tuple(visible),
            tuple(action_list), min_fan, round_wind, seat_wind,
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
            game_state, _choose_claim_action, tuple(hand), tuple(melds), tuple(visible),
            tuple(action_list), cut_tile, min_fan, round_wind, seat_wind,
        )
        if bot_action_is_current(game_state, player_index, expected_tick):
            await submit_bot_action(get_ai_action, game_state, player_index, choice, None, None, None, None)
