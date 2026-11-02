import random
from collections import Counter

from . import tiles as t


class TwinDecks:
    def __init__(self, seed):
        self.rng = random.Random(seed)
        self.active = 0
        self.packs, self.reserves, self.retired = [], [], [[], []]
        self.round_number = 0
        for _ in range(2):
            choices = list(t.JOKERS)
            self.rng.shuffle(choices)
            self.packs.append(t.normal_pack() + choices[:8])
            self.reserves.append(choices[8:])
        self.finished = False

    def next_wall(self, *, dice=None, order=None):
        if self.finished:
            raise ValueError('待选赖子不足，不能再开始新副')
        deck = self.round_number % 2
        pack = list(self.packs[deck])
        if order is None:
            self.rng.shuffle(pack)
        elif Counter(order) == Counter(pack):
            pack = list(order)
        else:
            raise ValueError('预置牌山与当前牌组实体不一致')
        dice = list(dice or (self.rng.randint(1, 6), self.rng.randint(1, 6)))
        if len(dice) != 2 or any(type(v) is not int or not 1 <= v <= 6 for v in dice):
            raise ValueError('骰点无效')
        opening = ((sum(dice) - 1) % 4 * 36 + min(dice) * 2) % 144
        self.active = deck
        self.round_number += 1
        return Wall(pack, opening, deck, dice)

    def replace_revealed(self, revealed):
        removed = sorted(v for v in revealed if t.joker(v))
        if len(removed) != len(set(removed)) or not set(removed) <= set(self.packs[self.active]):
            raise ValueError('赖子置换实体重复或不属于当前牌组')
        reserve = self.reserves[self.active]
        if len(reserve) < len(removed):
            self.finished = True
            return dict(deck=self.active, removed=removed, replaced=0, reserve_count=len(reserve), exhausted=True)
        replacements = reserve[:len(removed)]
        del reserve[:len(removed)]
        self.retired[self.active].extend(removed)
        for old, new in zip(removed, replacements):
            self.packs[self.active][self.packs[self.active].index(old)] = new
        return dict(deck=self.active, removed=removed, replaced=len(replacements), reserve_count=len(reserve), exhausted=False)


class Wall:
    def __init__(self, tiles, opening=0, deck=0, dice=(1, 1), riichi=False):
        if len(tiles) != 144 or Counter(tiles) - Counter(t.normal_pack()) != Counter(v for v in tiles if t.joker(v)):
            raise ValueError('夺彩牌山必须包含136张常规牌与8张不同赖子')
        if sum(t.joker(v) for v in tiles) != 8 or not t.valid_physical(tiles):
            raise ValueError('牌山实体不合法')
        self.tiles, self.opening, self.deck, self.dice = list(tiles), opening % 144, deck, list(dice)
        self.order = [(self.opening + i) % 144 for i in range(144)]
        self.live = list(self.order[:-14] if riichi else self.order)
        self.dead = list(self.order[-14:] if riichi else ())
        self.removed, self.revealed = set(), set()
        self.rinshan_used, self.dora_count = 0, 1 if riichi else 0

    def set_riichi(self):
        if self.removed:
            raise ValueError('配牌后不能改规则')
        self.live, self.dead = list(self.order[:-14]), list(self.order[-14:])
        self.dora_count = 1

    def draw(self, *, fourth_kong=False):
        if not self.live:
            raise ValueError('牌山已空')
        if fourth_kong and self.dead:
            if self.rinshan_used >= 4:
                raise ValueError('岭上牌已尽')
            position = self.dead[self.rinshan_used]
            self.rinshan_used += 1
            self.dead.append(self.live.pop())
            self.dora_count = min(5, self.dora_count + 1)
        else:
            position = self.live.pop(0)
        self.removed.add(position)
        return self.tiles[position], position

    def indicators(self, ura=False):
        if not self.dead:
            return []
        return [self.tiles[self.dead[4 + 2 * i + int(ura)]] for i in range(self.dora_count)]

    def public(self, *, ura=False, reveal=False):
        known = {self.dead[4 + 2 * i]: self.tiles[self.dead[4 + 2 * i]] for i in range(self.dora_count)} if self.dead else {}
        if ura:
            known.update({self.dead[5 + 2 * i]: self.tiles[self.dead[5 + 2 * i]] for i in range(self.dora_count)})
        return [dict(position=i, side=i // 36, stack=i % 36 // 2, layer=i % 2,
                     back=(self.deck + int(t.unusual_back(value))) % 2,
                     removed=i in self.removed, dead=i in self.dead,
                     tile=value if reveal or i in known else None)
                for i, value in enumerate(self.tiles)]

    def revealed_jokers(self):
        return [self.tiles[i] for i in self.removed | self.revealed if t.joker(self.tiles[i])]
