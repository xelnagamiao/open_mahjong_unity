import json
from collections import Counter
from pathlib import Path

NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
HONORS = tuple(range(41, 48))
FACES = NUMBERS + HONORS
ORPHANS = tuple(t for t in FACES if t >= 40 or t % 10 in (1, 9))
CATALOG = {d['id']: d for d in json.loads(Path(__file__).with_name('tiles.json').read_text(encoding='utf-8'))}
JOKERS = tuple(CATALOG)
CHOW, PUNG, EYES, RYANMEN, EDGE_CLOSED, OLD_YOUNG, MANGAN, ODD, EVEN, EMPEROR = range(5030, 5040)
BONUS_FACES = tuple(t for t in NUMBERS if t % 10 in (3, 7))
RED_FACES = (41, 42, 43, 44, 46)
PHYSICAL = FACES + tuple(1000 + t for t in BONUS_FACES) + tuple(2000 + t for t in BONUS_FACES) + tuple(3000 + t for t in RED_FACES) + JOKERS
DOMAIN = {t: tuple(CATALOG[t]['domain']) if t in CATALOG else (t % 1000,) for t in PHYSICAL}


def joker(tile):
    return tile in CATALOG


def face(tile):
    return 0 if joker(tile) else tile % 1000


def unusual_back(tile):
    return joker(tile) or tile in {3000 + t for t in RED_FACES}


def bonus(tile):
    return -1 if 2000 <= tile < 3000 else 1 if 1000 <= tile < 2000 or 3000 <= tile < 4000 else 0


def normal_pack():
    pack = [t for t in FACES for _ in range(4)]
    for t in BONUS_FACES:
        pack.remove(t)
        pack.remove(t)
        pack.extend((1000 + t, 2000 + t))
    for t in RED_FACES:
        pack.remove(t)
        pack.append(3000 + t)
    return pack


SUPPLY = dict(Counter(normal_pack()), **{})
SUPPLY.update({t: 1 for t in JOKERS})


def valid_physical(tiles):
    counts = Counter(tiles)
    return sum(n for t, n in counts.items() if joker(t)) <= 8 and all(type(t) is int and t in SUPPLY and n <= SUPPLY[t] for t, n in counts.items())


def allows(tile, logical, kind, position=0, *, start=0, external=False, declared=''):
    if logical not in DOMAIN.get(tile, ()):
        return False
    if not joker(tile):
        return True
    if tile in (MANGAN, ODD, EVEN):
        return not external
    if tile == CHOW:
        return external and kind == 'sequence'
    if tile == PUNG:
        return external and (kind == 'triplet' or kind == 'kong' and declared == 'peng')
    if tile == EYES:
        return kind in ('pair', 'triplet', 'kong')
    if tile == RYANMEN:
        return kind == 'sequence' and (position == 0 and start % 10 <= 6 or position == 2 and start % 10 >= 2)
    if tile == EDGE_CLOSED:
        return kind == 'sequence' and (position == 1 or position == 2 and start % 10 == 1 or position == 0 and start % 10 == 7)
    if tile == OLD_YOUNG:
        return kind == 'sequence' and start % 10 in (1, 7)
    return True


def dora(indicator):
    if joker(indicator):
        return 0
    value = face(indicator)
    if value < 40:
        return value // 10 * 10 + value % 10 % 9 + 1
    cycle = HONORS[:4] if value < 45 else (45, 47, 46)
    return cycle[(cycle.index(value) + 1) % len(cycle)]
