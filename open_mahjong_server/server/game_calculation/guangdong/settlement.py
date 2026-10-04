"""MIL 广东2023八节的零和支付，独立于手牌求解器和网络状态。"""

from .config import TILES, GHOSTS

HORSE_RANKS = ((1, 5, 9), (2, 6), (3, 7), (4, 8))
HORSE_HONORS = ((41,), (42, 45), (43, 47), (44, 46))  # 45中、46白、47发


def _seat(value):
    if type(value) is not int or value not in range(4):
        raise ValueError("无效座位")
    return value


def _zero():
    return {seat: 0 for seat in range(4)}


def _transfer(changes, receiver, payer, amount):
    changes[receiver] += amount
    changes[payer] -= amount


def winning_horses(tiles, winner, dealer=0):
    """马牌按庄家相对座位分配；花鬼明确不中马。"""
    relative = (_seat(winner) - _seat(dealer)) % 4
    if len(tiles) > 4 or any(type(tile) is not int or tile not in TILES + GHOSTS for tile in tiles):
        raise ValueError("无效马牌")
    return [tile for tile in tiles if tile in HORSE_HONORS[relative]
            or tile < 40 and tile % 10 in HORSE_RANKS[relative]]


def win_payments(winner, base_score, *, payer=None, horse_hits=0,
                 direct_kong_payer=None, rob_kong=False):
    _seat(winner)
    if type(base_score) is not int or base_score < 0 or type(horse_hits) is not int or not 0 <= horse_hits <= 4:
        raise ValueError("无效和牌分数或中马数量")
    if type(rob_kong) is not bool:
        raise ValueError("无效抢杠标记")
    for source in (payer, direct_kong_payer):
        if source is not None and (_seat(source) == winner):
            raise ValueError("和牌者不能支付自己的分数")
    if payer is not None and (horse_hits or direct_kong_payer is not None):
        raise ValueError("点和不奖马，也不使用直杠补牌责任")
    if rob_kong and payer is None:
        raise ValueError("抢杠需要加杠者")
    base, horses = _zero(), _zero()
    if payer is not None:
        _transfer(base, winner, payer, base_score * (3 if rob_kong else 1))
    elif direct_kong_payer is not None:
        _transfer(base, winner, direct_kong_payer, 3 * base_score)
        _transfer(horses, winner, direct_kong_payer, 3 * horse_hits)
    else:
        for other in range(4):
            if other != winner:
                _transfer(base, winner, other, base_score)
                _transfer(horses, winner, other, horse_hits)
    return {
        "base_changes": base,
        "horse_changes": horses,
        "changes": {seat: base[seat] + horses[seat] for seat in range(4)},
    }


def kong_payments(player, kind, *, payer=None, drawn=True):
    _seat(player)
    if kind not in ("direct", "added", "concealed") or type(drawn) is not bool:
        raise ValueError("无效杠类型")
    if kind == "direct":
        if _seat(payer) == player:
            raise ValueError("无效点杠者")
    elif payer is not None:
        raise ValueError("暗杠及加杠不指定点杠者")
    changes = _zero()
    if kind == "direct":
        _transfer(changes, player, payer, 3)
    elif kind == "concealed" or drawn:
        for other in range(4):
            if other != player:
                _transfer(changes, player, other, 2 if kind == "concealed" else 1)
    return changes


def refund_kongs(ledger):
    changes = _zero()
    for event in ledger:
        row = event["changes"]
        normalized = {seat: row.get(seat, row.get(str(seat), 0)) for seat in range(4)}
        if any(type(delta) is not int for delta in normalized.values()) or sum(normalized.values()) != 0:
            raise ValueError("杠分流水不是零和整数")
        for seat, delta in normalized.items():
            changes[seat] -= delta
    return changes
