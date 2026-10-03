"""换三张独立番表：牌型加番后乘根数，杠分另行即时结算。"""

FAN_VALUES = {
    '基本胡': 2, '碰碰胡': 4, '清一色': 6, '七对': 6, '金钩钓': 8,
    '清碰': 10, '清七对': 12, '清金钩钓': 16, '门清': 1, '断幺九': 1,
}
ROOT_FANS = frozenset({'根', '杠上花', '杠上炮', '抢杠', '海底'})


def fan_from_names(names):
    return sum(FAN_VALUES.get(name, 0) for name in names) * (1 + sum(name in ROOT_FANS for name in names))


def evaluate_exchange(hand, melds, decompositions, physical, way):
    pure = len({tile // 10 for tile in physical}) == 1
    concealed = not any(m[0] in ('k', 'g') for m in melds)
    roots = sum(count == 4 for count in physical.values())
    root_names = ['根'] * roots + [name for name in ('杠上花', '杠上炮', '抢杠', '海底') if name in way]
    extras = ['断幺九'] if all(tile % 10 not in (1, 9) for tile in physical) else []
    candidates = []
    for groups in decompositions:
        pungs = all(group[0] in ('K', 'q') for group in groups)
        if len(melds) == 4 and pungs:
            main = '清金钩钓' if pure else '金钩钓'
        elif pungs:
            main = '清碰' if pure else '碰碰胡'
        else:
            main = '清一色' if pure else '基本胡'
        names = [main] + (['门清'] if concealed else []) + extras + root_names
        candidates.append((fan_from_names(names), names))
    if not melds and len(hand) == 14 and all(count % 2 == 0 for count in physical.values()):
        # 四张同牌可作两对，也计一根；七对不加门清。
        names = ['清七对' if pure else '七对'] + extras + root_names
        candidates.append((fan_from_names(names), names))
    return max(candidates, key=lambda item: item[0]) if candidates else (0, [])
