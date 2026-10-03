"""Deferred, zero-sum chicken/kong/hand accounting (MIL 2023, pages 6, 11–13)."""

from collections import Counter
from dataclasses import asdict, dataclass

from .rules import NORMAL_CHICKENS, TILES, meld_tiles


@dataclass(frozen=True)
class Chicken:
    tile: int
    supplier: int
    claimant: int | None = None
    won: bool = False


@dataclass(frozen=True)
class Kong:
    owner: int
    tile: int
    kind: str  # concealed / added / direct
    supplier: int | None = None
    hanbao: bool = False


@dataclass(frozen=True)
class Transfer:
    payer: int
    payee: int
    points: int
    category: str
    reason: str
    tile: int | None = None


@dataclass(frozen=True)
class Settlement:
    transfers: tuple[Transfer, ...]
    indicator: int | None
    chicken_tile: int | None
    ready: tuple[bool, ...]

    @property
    def changes(self):
        result = [0] * 4
        for item in self.transfers:
            result[item.payer] -= item.points
            result[item.payee] += item.points
        return result

    def as_dict(self):
        return {**asdict(self), "transfers": [asdict(t) for t in self.transfers],
                "score_changes": self.changes}


def settle(players, *, winners=(), source="draw", payer=None, scores=None,
           ready_values=None, indicator=None, chickens=(), kongs=(), hot=False):
    """Players contain physical hand/meld/river lists; virtual ron tiles are added
    exactly once to the last winner, as required by VIII.3. A ready direct-kong
    owner collects only from its supplier; IX.2's not-ready penalty is paid
    by the owner to every ready/winning opponent.
    """
    winners = tuple(winners)
    scores = scores or {}
    ready_values = ready_values or [0] * 4
    if len(players) != 4 or len(ready_values) != 4 or len(set(winners)) != len(winners):
        raise ValueError("结算需要四家且和家不能重复")
    if any(type(i) is not int or i not in range(4) for i in winners):
        raise ValueError("无效和家")
    if source not in ("draw", "self_draw", "discard", "rob_kong"):
        raise ValueError("无效和牌来源")
    if (source == "draw") != (not winners) or (source == "self_draw" and len(winners) != 1):
        raise ValueError("和家与结算类型不匹配")
    if source in ("discard", "rob_kong") and (type(payer) is not int or payer not in range(4) or payer in winners):
        raise ValueError("无效放铳者")
    if any(type(scores.get(i)) is not int or not 1 <= scores[i] <= 39 for i in winners):
        raise ValueError("基本分无效")
    if indicator is not None and indicator not in TILES:
        raise ValueError("和牌鸡指示牌无效")
    ready = tuple(i in winners or ready_values[i] > 0 for i in range(4))
    transfers = []

    def transfer(a, b, points, category, reason, tile=None):
        if points and a != b:
            transfers.append(Transfer(a, b, points, category, reason, tile))

    if source == "draw":
        for a in range(4):
            for b in range(4):
                if not ready[a] and ready[b]:
                    transfer(a, b, ready_values[b], "ready", "流局查叫")
        return Settlement(tuple(transfers), None, None, ready)

    chicken_tile = indicator // 10 * 10 + indicator % 10 % 9 + 1 if indicator else None
    chicken_types = set(NORMAL_CHICKENS) | ({chicken_tile} if chicken_tile else set())
    offender = payer if source == "rob_kong" or hot else None

    def account(owner, other, points, category, reason, tile):
        if ready[owner]:
            if owner != offender:
                transfer(other, owner, points, category, reason, tile)
        elif ready[other]:
            transfer(owner, other, points, category, "包鸡" if category == "chicken" else "包杠", tile)

    for owner, player in enumerate(players):
        physical = list(player["discard_tiles"])
        physical += [t for code in player["combination_tiles"] for t in meld_tiles(code)]
        if ready[owner]:
            physical += list(player["hand_tiles"])
            physical += list(player.get("won_tiles", ()))
        counts = Counter(physical)
        for tile in sorted(chicken_types):
            multiplier = 2 if tile == chicken_tile and tile in NORMAL_CHICKENS else 1
            for other in range(4):
                if other != owner:
                    account(owner, other, counts[tile] * multiplier, "chicken", "金鸡" if multiplier == 2 else "鸡牌", tile)
    for chicken in chickens:
        if chicken.won:
            continue
        multiplier = 2 if chicken.tile == chicken_tile else 1
        owner = chicken.supplier if chicken.claimant is None else chicken.claimant
        for other in range(4):
            if other != owner and (chicken.claimant is None or other == chicken.supplier):
                account(owner, other, 2 * multiplier, "chicken",
                        "冲锋鸡加分" if chicken.claimant is None else "责任鸡加分", chicken.tile)
    for kong in kongs:
        if kong.hanbao:
            continue
        if kong.kind not in ("concealed", "added", "direct"):
            raise ValueError("未知杠类型")
        if kong.kind == "direct" and (type(kong.supplier) is not int or kong.supplier not in range(4) or kong.supplier == kong.owner):
            raise ValueError("无效点杠责任家")
        opponents = [kong.supplier] if kong.kind == "direct" and ready[kong.owner] else [i for i in range(4) if i != kong.owner]
        for other in opponents:
            if type(other) is not int or other not in range(4) or other == kong.owner:
                raise ValueError("无效点杠责任家")
            account(kong.owner, other, 3, "kong", {"concealed": "暗杠", "added": "加杠", "direct": "点杠"}[kong.kind], kong.tile)
    for winner in winners:
        if source == "self_draw":
            for other in range(4):
                if other != winner:
                    transfer(other, winner, scores[winner], "hand", "自摸")
        else:
            transfer(payer, winner, scores[winner] * (3 if source == "rob_kong" else 1),
                     "hand", "抢杠包三家" if source == "rob_kong" else "热炮" if hot else "点和")
    return Settlement(tuple(transfers), indicator, chicken_tile, ready)
