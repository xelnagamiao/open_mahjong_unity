"""广东机器人：仅使用当前权威动作，鬼牌不送入无鬼的34张牌效算法。"""

from collections import Counter

from ...game_calculation.guangdong.config import DOUBLES_SUB_RULE, GHOSTS
from ...game_calculation.guangdong.rules import structural_waits, waiting_scores
from ...game_calculation.guangdong.scoring import Context
from ..public.ai.structural_shanten import distance
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _wait_until_actionable, _BOT_DELAY
from ..public.ai.bot_executor import run_room_bot_cpu, bot_action_is_current
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import infer_bot_cut_class, has_draw_slot


def choose_cut(hand, melds):
    """听牌优先进张数；未听时按成组/邻接保留价值，始终保留万能鬼。"""
    counts = Counter(hand)
    choices = []
    for tile in sorted(set(hand)):
        rest = list(hand)
        rest.remove(tile)
        waits = structural_waits(rest, melds)
        useful = sum(max(0, (1 if w in GHOSTS else 4) - rest.count(w)) for w in waits)
        neighbors = sum(counts.get(tile + step, 0) * (2 if abs(step) == 1 else 1)
                        for step in (-2, -1, 1, 2)
                        if tile < 40 and (tile + step) // 10 == tile // 10)
        keep = 100 if tile in GHOSTS else 4 * min(3, counts[tile] - 1) + neighbors
        choices.append(((bool(waits), useful, -keep, tile == hand[-1]), tile))
    return max(choices)[1]


def doubles_shape(hand, melds, context):
    waits = [row["tile"] for row in waiting_scores(hand, melds, context=context) if row["ron"] or row["self_draw"]]
    if waits:
        copies = Counter(hand)
        for code in melds:
            copies[int(code[1:])] += 3 if code[0] == "k" else 4
        return 0, sum((1 if t in GHOSTS else 4) - copies[t] for t in waits)
    natural = [t for t in hand if t not in GHOSTS]
    ghosts = len(hand) - len(natural)
    steps = distance(natural, len(melds), 4)
    if not melds:
        counts = Counter(natural)
        terminals = [11,19,21,29,31,39,*range(41,48)]
        steps = min(steps, 6 - sum(n // 2 for n in counts.values()),
                    13 - sum(t in counts for t in terminals) - int(any(counts[t] >= 2 for t in terminals)))
    return -max(1, steps - ghosts), 0


def doubles_claim(hand, melds, tile, available, discarded_ghosts=0, chicken_available=True):
    context = Context(doubles=True, discarded_ghosts=discarded_ghosts, chicken_available=chicken_available)
    before = doubles_shape(hand, melds, context)
    if not melds and before[0] == 0:
        return "pass"
    best, choice = before, "pass"
    for action, size, code in (("gang", 3, "g"), ("peng", 2, "k")):
        if action not in available or hand.count(tile) < size:
            continue
        rest = list(hand)
        for _ in range(size):
            rest.remove(tile)
        after = (*melds, f"{code}{tile}")
        if action == "peng":
            discard = choose_cut(rest, after)
            rest.remove(discard)
        quality = doubles_shape(rest, after, context)
        if quality > best or action == "gang" and melds and quality == best:
            best, choice = quality, action
    return choice


@paced_bot(lambda: _BOT_DELAY)
async def guangdong_bot_action(state, index, available, status):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    win = next((a for a in available if a.startswith("hu_")), None)
    if win:
        await submit_bot_action(get_ai_action, state, index, win, None, None, None, None)
        return
    player = state.player_list[index]
    if status not in ("waiting_hand_action", "onlycut_after_action"):
        choice = "gang" if "gang" in available else "peng" if "peng" in available else "pass"
        if getattr(state, "sub_rule", None) == DOUBLES_SUB_RULE and choice != "pass":
            river = state.player_list[state.current_player_index].discard_tiles
            choice = await run_room_bot_cpu(state, doubles_claim, tuple(player.hand_tiles),
                tuple(player.combination_tiles), river[-1], tuple(available), state.team_ghosts(index),
                player.team_id not in state.chicken_teams) if river else "pass"
        if choice in available and bot_action_is_current(state, index, tick):
            await submit_bot_action(get_ai_action, state, index, choice, None, None, None, None)
        return
    hand = list(player.hand_tiles)
    for action, kind in (("angang", "concealed"), ("jiagang", "added")):
        if action in available:
            tile = next((t for t in sorted(set(hand)) if state.kong_allowed(index, t, kind)), None)
            if tile is not None:
                await submit_bot_action(get_ai_action, state, index, action, None, None, None, tile)
                return
    if "cut" in available and hand:
        tile = await run_room_bot_cpu(state, choose_cut, hand, tuple(player.combination_tiles))
        cut_index = len(hand) - 1 if state.sub_rule == DOUBLES_SUB_RULE and tile == hand[-1] else hand.index(tile)
        if bot_action_is_current(state, index, tick):
            await submit_bot_action(get_ai_action, state, index, "cut",
                infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player)), tile, cut_index, None)
