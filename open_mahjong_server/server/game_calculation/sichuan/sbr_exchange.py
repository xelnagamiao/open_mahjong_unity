from .sichuan_hepai_check import Sichuan_Hepai_Check

OPENING_FANS = {"自摸": 1, "地和": 4, "天和": 5}


def add_opening_fans(fan, names, way):
    extra = [name for name in OPENING_FANS if name in way]
    if not extra:
        return fan, names
    return fan + sum(OPENING_FANS[name] for name in extra), [name for name in names if name != "平和"] + extra


class ExchangeChajiao(Sichuan_Hepai_Check):
    def _gen_count(self, hand, melds):
        from collections import Counter
        counts = Counter(self._expand_all(hand, melds))
        kongs = {int(meld[1:]) for meld in melds if meld[0] in ("g", "G")}
        return len(kongs), sum(count >= 4 and tile not in kongs for tile, count in counts.items())


def basic_points(fan):
    return 1 << min(5, max(0, fan))
