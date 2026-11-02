from dataclasses import dataclass
from functools import lru_cache

from . import tiles as t
from .scoring import Context, conditions, guobiao_shape, riichi_base_points, riichi_shape
from .solver import Meld, Shape, iter_shapes


@dataclass(frozen=True)
class Win:
    variant: str
    shape: Shape
    yaku_fan: int
    fan: int
    visible: int
    fu: int
    yakuman: int
    entries: tuple
    meets_minimum: bool
    meets_conditions: bool

    @property
    def valid(self):
        return self.meets_minimum and self.meets_conditions

    @property
    def base_points(self):
        return max(0, self.fan) if self.variant == 'guobiao' else riichi_base_points(self.fan, self.fu, self.yakuman)


def score_shape(shape, variant, ctx):
    score = (guobiao_shape if variant == 'guobiao' else riichi_shape)(shape, ctx)
    entries = list(score.entries)
    if variant == 'guobiao':
        reward = 64 * score.pure_open + 88 * score.pure_closed
        visible = score.visible + reward
        for count, name, amount in ((score.pure_open, '纯赖子明杠', 64), (score.pure_closed, '纯赖子暗杠', 88)):
            if count:
                entries.append(f'{name}×{count}（{amount * count}番）')
        if score.physical_bonus:
            entries.append(f'金银赤宝牌（{score.physical_bonus:+}番）')
        fan, yakuman = visible, 0
    else:
        extra = score.pure_open + 2 * score.pure_closed
        yakuman = score.yakuman + extra
        if extra:
            entries = list(score.entries) if score.yakuman else []
            for count, name, amount in ((score.pure_open, '纯赖子明杠', 13), (score.pure_closed, '纯赖子暗杠', 26)):
                if count:
                    entries.append((f'{name}×{count}', count * amount))
            fan = visible = 13 * yakuman
        else:
            fan, visible = score.total, score.visible
            for name, value in (('金银赤宝牌', score.physical_bonus), ('宝牌', score.dora), ('里宝牌', score.ura)):
                if value:
                    entries.append((name, value))
        entries = [f'{name}（{value}番）' for name, value in entries]
    physical = shape.physical + tuple(v for g in shape.groups if g.external for v in g.physical)
    return Win(variant, shape, score.yaku_fan, fan, visible, 25 if yakuman else score.fu, yakuman, tuple(entries),
               score.yaku_fan >= (8 if variant == 'guobiao' else 1), conditions(physical, visible, score.fu, variant))


def iter_scores(hand, melds=(), variant='guobiao', ctx=Context()):
    if variant not in ('guobiao', 'riichi'):
        raise ValueError('夺彩只开放国标与立直')
    for shape in iter_shapes(hand, melds, variant):
        yield score_shape(shape, variant, ctx)


@lru_cache(maxsize=2048)
def _evaluate(hand, melds, variant, ctx):
    best, fallback = None, None
    for result in iter_scores(hand, melds, variant, ctx):
        key = result.base_points, result.fan, result.fu
        if fallback is None or (result.meets_conditions, result.meets_minimum, *key) > (fallback.meets_conditions, fallback.meets_minimum, fallback.base_points, fallback.fan, fallback.fu):
            fallback = result
        if result.valid and (best is None or key > (best.base_points, best.fan, best.fu)):
            best = result
    return best or fallback


def evaluate(hand, melds=(), variant='guobiao', ctx=Context()):
    return _evaluate(tuple(hand), tuple(Meld.read(m) for m in melds), variant, ctx)
