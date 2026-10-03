"""血流牌效：同时满足缺门与三/四副面子，避免朝三门牌的无效听牌推进。"""
from ..public.ai.pacing import paced_bot, submit_bot_action
from collections import Counter
from functools import lru_cache
from itertools import combinations

from ..public.ai.guobiao_shanten import xiangting_yiban
from ..public.ai.smart_bot_ai import smart_bot_action, _wait_until_actionable, _BOT_DELAY
from ..public.ai.smart_bot_logic import count_visible_tiles, tile_to_34
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class

TILES = tuple(suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10))


@lru_cache(maxsize=32768)
def _shanten(hand, melds, meld_count, dingque):
    exposed_suits = {int(m[1:]) // 10 for m in melds}
    best = 99
    for excluded in (dingque,) if dingque in (1, 2, 3) else (1, 2, 3):
        if excluded in exposed_suits:
            continue
        useful = Counter(t for t in hand if t // 10 != excluded)
        # 公共一般型算法以四副面子为目标；弃三张缺少的面子作为已完成计入。
        best = min(best, xiangting_yiban(useful, len(melds) + 4 - meld_count))
        if meld_count == 4 and not melds:
            pairs = sum(count // 2 for count in useful.values())
            singles = sum(count % 2 for count in useful.values())
            best = min(best, 13 - 2 * pairs - min(7 - pairs, singles))
    return best


def shanten(hand, melds=(), meld_count=3, dingque=0):
    return _shanten(tuple(sorted(hand)), tuple(sorted(melds)), meld_count, dingque)


def evaluate(hand, melds, visible, meld_count, dingque):
    distance = shanten(hand, melds, meld_count, dingque)
    counts = Counter(hand)
    acceptance = 0
    for tile in TILES:
        remaining = 4 - counts[tile] - visible[tile_to_34(tile)]
        if remaining > 0 and shanten([*hand, tile], melds, meld_count, dingque) < distance:
            acceptance += remaining
    return -distance, acceptance


def best_cut(hand, melds, visible, meld_count=3, dingque=0):
    forced = dingque in (1, 2, 3) and any(t // 10 == dingque for t in hand)
    candidates = [(evaluate(hand[:i] + hand[i+1:], melds, visible, meld_count, dingque), tile, i)
                  for i, tile in enumerate(hand)
                  if tile not in hand[:i] and (not forced or tile // 10 == dingque)]
    _, tile, index = max(candidates, key=lambda entry: entry[0])
    return tile, index


def choose_opening(hand, exchange=False):
    """保留能组成合法缺门牌的部分；不按万筒条枚举顺序盲弃前三张。"""
    hand = list(hand)
    choices = set()
    for suit in (1, 2, 3):
        choices.update(combinations(sorted(t for t in hand if t // 10 == suit), 3))
    def score(chosen):
        after = list(hand)
        for tile in chosen:
            after.remove(tile)
        counts = Counter(after)
        # 换回牌尚未知，用保留牌的搭子与对子作为同向听时的选择依据。
        connectivity = sum(min(2, count) for count in counts.values() if count >= 2)
        connectivity += sum(min(count, counts.get(tile+1, 0)) for tile, count in counts.items() if tile % 10 < 9)
        return -shanten(after, (), 4 if exchange else 3), connectivity
    return list(max(sorted(choices), key=score)) if choices else hand[:3]


def choose_hand(hand, melds, visible, actions, meld_count, dingque, locked=False):
    hand, melds = list(hand), list(melds)
    before = evaluate(hand, melds, visible, meld_count, dingque)
    for tile in sorted(set(hand)):
        if tile // 10 == dingque:
            continue
        if 'angang' in actions and hand.count(tile) == 4:
            after = [t for t in hand if t != tile]
            if evaluate(after, melds + [f'G{tile}'], visible, meld_count, dingque) >= before:
                return 'angang', tile, 0
        if 'jiagang' in actions and f'k{tile}' in melds:
            after = hand.copy()
            after.remove(tile)
            upgraded = [f'g{tile}' if m == f'k{tile}' else m for m in melds]
            if evaluate(after, upgraded, visible, meld_count, dingque) >= before:
                return 'jiagang', tile, 0
    tile, index = (hand[-1], len(hand)-1) if locked else best_cut(hand, melds, visible, meld_count, dingque)
    return 'cut', tile, index


def choose_claim(hand, melds, visible, actions, tile, meld_count=3, dingque=0):
    hand, melds = list(hand), list(melds)
    best, best_score = 'pass', evaluate(hand, melds, visible, meld_count, dingque)
    for action, count, sign in (('peng', 2, 'k'), ('gang', 3, 'g')):
        if action not in actions or hand.count(tile) < count or tile // 10 == dingque:
            continue
        next_melds = melds + [f'{sign}{tile}']
        if len({int(m[1:]) // 10 for m in next_melds}) == 3:
            continue
        after = hand.copy()
        for _ in range(count):
            after.remove(tile)
        if action == 'peng':
            _, cut_index = best_cut(after, next_melds, visible, meld_count, dingque)
            after.pop(cut_index)
        score = evaluate(after, next_melds, visible, meld_count, dingque)
        if score > best_score:
            best, best_score = action, score
    return best


@paced_bot(lambda: _BOT_DELAY)
async def xueliu_smart_bot_action(state, index, actions, status):
    if not getattr(state, 'is_xueliu', False):
        return await smart_bot_action(state, index, actions, status)
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    player = state.player_list[index]
    choice = next((a for a in actions if a.startswith('hu')), None)
    tile, cut_index = None, None
    if choice is None:
        visible = count_visible_tiles(state)
        # 连胡区中的牌已移出牌墙，也属于公开的不可再摸牌。
        for p in state.player_list:
            for t in p.huapai_list:
                visible[tile_to_34(t)] += 1
            if not state.xueliu_exchange:
                for t in p.xueliu_throw_tiles:
                    visible[tile_to_34(t)] += 1
        args = (list(player.hand_tiles), list(player.combination_tiles), visible, tuple(actions))
        if status in ('waiting_hand_action', 'onlycut_after_action'):
            choice, tile, cut_index = await run_room_bot_cpu(state, choose_hand, *args,
                state.xueliu_meld_count, player.dingque_suit, player.post_hu_lock)
        elif status == 'waiting_action_after_cut':
            cut_tile = state.player_list[state.current_player_index].discard_tiles[-1]
            choice = await run_room_bot_cpu(state, choose_claim, *args, cut_tile,
                state.xueliu_meld_count, player.dingque_suit)
        else:
            choice = 'pass'
    if bot_action_is_current(state, index, tick):
        await submit_bot_action(get_ai_action, state, index, choice,
            infer_bot_cut_class(player.hand_tiles, tile, cut_index, draw_slot=has_draw_slot(player)) if choice == 'cut' else None,
            tile if choice == 'cut' else None, cut_index, tile if choice != 'cut' else None)
