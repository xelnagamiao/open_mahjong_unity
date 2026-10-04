"""清混碰牌效：只朝清/混一色、碰碰和及全字牌的合法起和形推进。"""
from collections import Counter
from functools import lru_cache

from ...game_calculation.shanghai.qinghunpeng import TILES, meld_tiles
from ..public.ai.guobiao_shanten import xiangting_yiban
from ..public.ai.smart_bot_logic import tile_to_34


@lru_cache(maxsize=32768)
def _shanten(hand, melds):
    counts = Counter(hand)
    exposed = Counter(tile for meld in melds for tile in meld_tiles(meld))
    suits = {tile // 10 for tile in exposed if tile < 40}
    best = 99
    # 显式传副露数；其它花色不能贡献面子、搭子或雀头。
    for suit in (1, 2, 3):
        if suits - {suit}:
            continue
        useful = [tile for tile in hand if tile // 10 == suit or tile >= 40]
        best = min(best, xiangting_yiban(Counter(useful), len(melds)))
    if all(meld[0] != 's' for meld in melds):
        needed = 4 - len(melds)
        available = [tile for tile in TILES if exposed[tile] == 0]
        for pair in available:
            groups = sorted((min(3, counts[tile]) for tile in available if tile != pair), reverse=True)
            missing = 3 * needed + 2 - min(2, counts[pair]) - sum(groups[:needed])
            best = min(best, missing - 1)
    # 乱风向不要求通常四面子结构。
    if not suits:
        best = min(best, 13 - 3 * len(melds) - sum(count for tile, count in counts.items() if tile >= 40))
    return best


def shanten(hand, melds=()):
    return _shanten(tuple(sorted(hand)), tuple(sorted(melds)))


def evaluate(hand, melds, visible):
    distance = shanten(hand, melds)
    counts = Counter(hand)
    acceptance = 0
    for tile in TILES:
        remaining = 4 - counts[tile] - visible[tile_to_34(tile)]
        if remaining > 0 and shanten([*hand, tile], melds) < distance:
            acceptance += remaining
    return -distance, acceptance


def best_cut(hand, melds, visible, forbidden=()):
    candidates = [(evaluate(hand[:i] + hand[i+1:], melds, visible), tile, i)
                  for i, tile in enumerate(hand) if tile not in forbidden and tile not in hand[:i]]
    if not candidates:
        return hand[-1], len(hand) - 1
    _, tile, index = max(candidates, key=lambda entry: entry[0])
    return tile, index


def choose_hand_action(hand, melds, visible, actions, forbidden=()):
    hand, melds = list(hand), list(melds)
    before = evaluate(hand, melds, visible)
    for tile in sorted(set(hand)):
        if 'angang' in actions and hand.count(tile) == 4:
            after = [t for t in hand if t != tile]
            if evaluate(after, melds + [f'G{tile}'], visible) >= before:
                return 'angang', tile, 0
        if 'jiagang' in actions and f'k{tile}' in melds:
            after = hand.copy()
            after.remove(tile)
            upgraded = [f'g{tile}' if m == f'k{tile}' else m for m in melds]
            if evaluate(after, upgraded, visible) >= before:
                return 'jiagang', tile, 0
    tile, index = best_cut(hand, melds, visible, forbidden)
    return 'cut', tile, index


def choose_claim(hand, melds, visible, actions, tile):
    hand, melds = list(hand), list(melds)
    best, best_score = 'pass', evaluate(hand, melds, visible)
    options = [('peng', [tile]*2, f'k{tile}'), ('gang', [tile]*3, f'g{tile}'),
               ('chi_left', [tile-2,tile-1], f's{tile-1}'),
               ('chi_mid', [tile-1,tile+1], f's{tile}'),
               ('chi_right', [tile+1,tile+2], f's{tile+1}')]
    for action, used, meld in options:
        if action not in actions or any(hand.count(t) < used.count(t) for t in used):
            continue
        after = hand.copy()
        for t in used:
            after.remove(t)
        next_melds = melds + [meld]
        if action != 'gang':
            _, index = best_cut(after, next_melds, visible)
            after.pop(index)
        score = evaluate(after, next_melds, visible)
        if score > best_score:
            best, best_score = action, score
    return best
