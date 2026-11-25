"""Qingque 13-tile winning hand (hepai) checker.

Pure Python, stateless port of Qingque13Hepai.cs.
"""

from __future__ import annotations

from typing import List, Sequence, Tuple

from .criteria import check_fan_tile_1p, evaluate_fans, is_thirteen_orphans
from .fans import CHINESE_NAMES, QingqueFan, derepellenise
from .scoring import get_max_fan
from .core import (
    HONOUR_E,
    HONOUR_N,
    HONOUR_S,
    HONOUR_W,
    QingqueDecomposition,
    QingqueHand,
    QingqueMeld,
    QingqueWinType,
    SuitType,
    make_tile,
)


def _convert_tile(tile_id: int) -> int:
    if tile_id < 0:
        return 0
    suit = tile_id // 10
    num = tile_id % 10
    if suit == 1:
        return make_tile(SuitType.M, num)
    if suit == 2:
        return make_tile(SuitType.P, num)
    if suit == 3:
        return make_tile(SuitType.S, num)
    if suit == 4:
        return make_tile(SuitType.Z, num)
    return 0


def _convert_melds(meld_strings: Sequence[str]) -> List[QingqueMeld]:
    melds: List[QingqueMeld] = []
    for s in meld_strings:
        if len(s) < 2:
            continue
        mtype = s[0]
        tile = _convert_tile(int(s[1:]))
        is_concealed = mtype.isupper()
        lower = mtype.lower()
        if lower == "s":
            melds.append(QingqueMeld.sequence(tile, is_concealed, not is_concealed))
        elif lower == "k":
            melds.append(QingqueMeld.triplet(tile, is_concealed, not is_concealed))
        elif lower == "g":
            melds.append(QingqueMeld.kong(tile, is_concealed, not is_concealed))
    return melds


def _convert_win_type(way_to_hepai: Sequence[str]) -> QingqueWinType:
    self_drawn = any(
        w in ("自摸", "海底捞月", "岭上开花", "天和") for w in way_to_hepai
    )
    final_tile = any(w in ("海底捞月", "河底捞鱼") for w in way_to_hepai)
    kong_related = any(w in ("岭上开花", "抢杠") for w in way_to_hepai)
    heavenly_or_earthly = any(w in ("天和", "地和") for w in way_to_hepai)

    seat_wind = 0
    for w in way_to_hepai:
        if "自风东" in w:
            seat_wind = HONOUR_E
        elif "自风南" in w:
            seat_wind = HONOUR_S
        elif "自风西" in w:
            seat_wind = HONOUR_W
        elif "自风北" in w:
            seat_wind = HONOUR_N

    prevalent_wind = 0
    for w in way_to_hepai:
        if "场风东" in w or "圈风东" in w:
            prevalent_wind = HONOUR_E
        elif "场风南" in w or "圈风南" in w:
            prevalent_wind = HONOUR_S
        elif "场风西" in w or "圈风西" in w:
            prevalent_wind = HONOUR_W
        elif "场风北" in w or "圈风北" in w:
            prevalent_wind = HONOUR_N

    return QingqueWinType(
        self_drawn=self_drawn,
        final_tile=final_tile,
        kong_related=kong_related,
        heavenly_or_earthly_hand=heavenly_or_earthly,
        seat_wind=seat_wind,
        prevalent_wind=prevalent_wind,
    )


def _add_occasional_fans(fans: List[QingqueFan], win_type: QingqueWinType) -> None:
    if win_type.has_flags(
        QingqueWinType.HEAVENLY_OR_EARTHLY_HAND | QingqueWinType.SELF_DRAWN
    ):
        fans.append(QingqueFan.HeavenlyHand)
    if win_type.has_flags(
        QingqueWinType.HEAVENLY_OR_EARTHLY_HAND, QingqueWinType.SELF_DRAWN
    ):
        fans.append(QingqueFan.EarthlyHand)
    if win_type.has_flags(
        QingqueWinType.KONG_RELATED | QingqueWinType.SELF_DRAWN
    ):
        fans.append(QingqueFan.OutWithReplacementTile)
    if win_type.has_flags(
        QingqueWinType.FINAL_TILE | QingqueWinType.SELF_DRAWN
    ):
        fans.append(QingqueFan.LastTileDraw)
    if not win_type.is_self_drawn and win_type.is_final_tile:
        fans.append(QingqueFan.LastTileClaim)
    if not win_type.is_self_drawn and win_type.has_flags(
        QingqueWinType.KONG_RELATED
    ):
        fans.append(QingqueFan.RobbingTheKong)


def qingque_hepai_check(
    hand_list: Sequence[int],
    tiles_combination: Sequence[str],
    way_to_hepai: Sequence[str],
    get_tile: int,
    debug: bool = False,
) -> Tuple[float, List[str]]:
    """Check if a Qingque 13-tile hand wins and calculate its fan score.

    Matches `Qingque13Hepai.HepaiCheck` and `call_hepai_check` semantics:
    `hand_list` must NOT include `get_tile` (`winningTileIncluded=False`);
    `get_tile` is added internally.

    Args:
        hand_list: Concealed hand tiles WITHOUT `get_tile` (IDs 11-47).
        tiles_combination: Declared melds (e.g. "k11", "s12", "g45", "G11").
        way_to_hepai: Win conditions and winds (e.g. "自摸", "自风东").
        get_tile: Winning tile ID (11-47).
        debug: Enable debug printing.

    Returns:
        (fan_score, fan_names) tuple.
    """
    try:
        tiles = [_convert_tile(tid) for tid in hand_list]
        melds = _convert_melds(tiles_combination)
        win_type = _convert_win_type(way_to_hepai)
        win_tile = _convert_tile(get_tile)

        if debug:
            print(f"[Qingque13] Hand: {','.join(map(str, hand_list))}")
            print(f"[Qingque13] Melds: {','.join(tiles_combination)}")
            print(f"[Qingque13] Win type: {win_type}")
            print(f"[Qingque13] Win tile: {win_tile}")

        hand = QingqueHand(
            tiles, melds, win_tile, win_type, winning_tile_included=False
        )

        if not hand.is_valid():
            return 0.0, ["Invalid hand"]

        all_fans = evaluate_fans(hand, ignore_occasional=False)

        if not all_fans and is_thirteen_orphans(hand):
            fans: List[QingqueFan] = [
                QingqueFan.Trivial,
                QingqueFan.ThirteenOrphans,
                QingqueFan.ConcealedHand,
                QingqueFan.AllTerminalsAndHonours,
                QingqueFan.MixedOutsideHand,
            ]
            if check_fan_tile_1p(QingqueDecomposition(hand)):
                fans.append(QingqueFan.FanTile1P)
            _add_occasional_fans(fans, win_type)
            all_fans.append(fans)

        fan_score, best_fans = get_max_fan(all_fans)
        display_fans = derepellenise(best_fans)
        fan_names = [CHINESE_NAMES.get(f, f.name) for f in display_fans]

        return float(fan_score), fan_names
    except Exception as exc:
        if debug:
            print(f"[Qingque13] Error: {exc}")
        return 0.0, [f"Error: {exc}"]
