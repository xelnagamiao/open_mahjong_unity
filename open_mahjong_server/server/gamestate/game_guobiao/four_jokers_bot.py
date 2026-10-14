"""Joker-aware bot: structural efficiency only, never speculative MCR scoring."""
from collections import Counter
from ...game_calculation import guobiao_sixteen as sixteen
from ...game_calculation import guobiao_four_jokers as rules
from ...game_calculation import guobiao_huahun as huahun
from ...game_calculation.jokers.models import TILES
from ...game_calculation.jokers.solver import GROUPS, _minimum_group_jokers
from ..public.ai.pacing import paced_bot, submit_bot_action, DEFAULT_BOT_DELAY
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.smart_bot_ai import _wait_until_actionable
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import infer_bot_cut_class


def _quality(hand, melds, visible, sub_rule=rules.SUB_RULE):
    profile = sixteen if sub_rule == sixteen.SUB_RULE else huahun if sub_rule == huahun.SUB_RULE else rules
    waits = profile.waiting_tiles(hand, melds)
    available = sum(max(0, (1 if t in huahun.JOKERS and sub_rule == huahun.SUB_RULE else 4)
                            - visible.get(t, 0) - hand.count(t)) for t in waits)
    if sub_rule == huahun.SUB_RULE:
        hand = huahun.normalize(hand)
    counts = bytes(hand.count(t) for t in TILES)
    needs = [_minimum_group_jokers(counts[a:b], False, seq) for a, b, seq in GROUPS]
    need = min(sum(needs) - needs[i] + _minimum_group_jokers(counts[a:b], True, seq)
               for i, (a, b, seq) in enumerate(GROUPS)) - hand.count(rules.JOKER)
    if not melds and sub_rule != sixteen.SUB_RULE:
        pair_need = sum(n % 2 for n in counts) - hand.count(rules.JOKER)
        need = min(need, pair_need)
    return bool(available), available, -need


def choose_cut(hand, melds=(), visible=None, sub_rule=rules.SUB_RULE):
    visible = visible or {}
    counts = Counter(hand)
    jokers = huahun.JOKERS if sub_rule == huahun.SUB_RULE else (rules.JOKER,)
    choices = [t for t in counts if t not in jokers] or list(counts)
    def value(tile):
        rest = list(hand); rest.remove(tile)
        link = counts[tile] * 4 + sum(counts.get(tile + d, 0) * (3 - abs(d))
            for d in (-2, -1, 1, 2) if tile < 40 and (tile + d) // 10 == tile // 10)
        return _quality(rest, melds, visible, sub_rule), -link, -tile
    return max(choices, key=value)


def choose_action(hand, melds, actions, visible, cut_tile=None, sub_rule=rules.SUB_RULE):
    for action in ("hu_self", "hu_first", "hu_second", "hu_third", "buhua"):
        if action in actions:
            return action, None
    if "angang" in actions:
        if sub_rule == huahun.SUB_RULE and huahun.can_flower_kong(hand):
            return "angang", 51
        tiles = sorted(t for t in set(hand) if t != rules.JOKER and hand.count(t) == 4)
        if tiles:
            return "angang", tiles[0]
    if "jiagang" in actions:
        tiles = sorted(int(m[1:]) for m in melds if m.startswith("k") and int(m[1:]) in hand)
        if tiles:
            return "jiagang", tiles[0]
    if "cut" in actions:
        return "cut", choose_cut(hand, melds, visible, sub_rule)
    # Preserve the concealed-hand fan route. Claim only when it improves a
    # live structural wait; no high-cost fan search for hypothetical branches.
    if cut_tile in TILES:
        baseline = _quality(hand, melds, visible, sub_rule)
        best = baseline
        action = "pass"
        for candidate, needed, code in (
            ("peng", (cut_tile, cut_tile), f"k{cut_tile}"),
            ("chi_left", (cut_tile-2, cut_tile-1), f"s{cut_tile-1}"),
            ("chi_mid", (cut_tile-1, cut_tile+1), f"s{cut_tile}"),
            ("chi_right", (cut_tile+1, cut_tile+2), f"s{cut_tile+1}")):
            if candidate not in actions:
                continue
            remaining = list(hand)
            for tile in needed:
                remaining.remove(tile)
            new_melds = tuple(melds) + (code,)
            remaining.remove(choose_cut(remaining, new_melds, visible, sub_rule))
            quality = _quality(remaining, new_melds, visible, sub_rule)
            if quality[0] and quality > best:
                best, action = quality, candidate
        return action, None
    return "pass", None


@paced_bot(lambda: DEFAULT_BOT_DELAY)
async def bot_action(state, index, actions, status):
    if not await _wait_until_actionable(state, index):
        return
    tick = state.server_action_tick
    player = state.player_list[index]
    visible = Counter(t for p in state.player_list for t in p.discard_tiles)
    for p in state.player_list:
        for meld in p.combination_tiles:
            if meld[0] == "G":
                continue
            tile = int(meld[1:])
            visible.update((tile-1, tile, tile+1) if meld[0] == "s" else [tile] * (4 if meld[0] == "g" else 3))
    source = state.player_list[state.current_player_index].discard_tiles
    cut_tile = source[-1] if source else None
    action, tile = await run_room_bot_cpu(state, choose_action, tuple(player.hand_tiles),
        tuple(player.combination_tiles), tuple(actions), dict(visible), cut_tile, state.sub_rule)
    if not bot_action_is_current(state, index, tick):
        return
    position = max((i for i, t in enumerate(player.hand_tiles) if t == tile), default=-1)
    await submit_bot_action(get_ai_action, state, index, action,
        infer_bot_cut_class(player.hand_tiles, tile, position, draw_slot=getattr(player, "has_draw_slot", False)) if action == "cut" else None,
        tile if action == "cut" else None, position if action == "cut" else None,
        tile if action != "cut" else None)
