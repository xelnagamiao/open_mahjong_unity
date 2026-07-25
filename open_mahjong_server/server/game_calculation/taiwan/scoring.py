"""台湾麻将台种判断、最高解释与逐笔支付。"""

from collections import Counter
from dataclasses import replace
from typing import Dict, FrozenSet, Iterable, List, Optional, Sequence, Set, Tuple

from ..hand_structure import SIXTEEN_TILE_MAHJONG
from .rules import (
    DRAGON_TILES,
    FLOWER_SETS,
    SEAT_FLOWERS,
    STRUCTURE_TILES,
    WIND_TILES,
    Decomposition,
    Fan,
    HandContext,
    Meld,
    Payment,
    ScoreResult,
    Settlement,
    TaiwanRules,
)
from .solver import (
    decomposition_is_all_sequences,
    decomposition_is_all_triplets,
    derive_pre_win_tiles,
    enumerate_decompositions,
    is_eight_pairs_half,
    parse_melds,
    structural_waits,
    winning_use_is_single_wait,
    winning_uses_only_two_sided,
)


FAN = {
    "menqing": ("门清", 1),
    "self_reliant": ("不求人", 1),
    "self_draw": ("自摸", 1),
    "seat_wind": ("门风刻", 1),
    "round_wind": ("圈风刻", 1),
    "seat_flower": ("正花", 1),
    "dragon": ("三元牌", 1),
    "single_wait": ("独听", 1),
    "rob_kong": ("抢杠", 1),
    "after_kong": ("杠上开花", 1),
    "last_draw": ("海底捞月", 1),
    "flower_kong": ("花杠", 1),
    "pinfu": ("平胡", 2),
    "three_concealed": ("三暗刻", 2),
    "fully_exposed": ("全求人", 2),
    "all_triplets": ("碰碰胡", 4),
    "small_dragons": ("小三元", 4),
    "half_flush": ("混一色", 4),
    "four_concealed": ("四暗刻", 5),
    "earthly_ready": ("地听", 8),
    "five_concealed": ("五暗刻", 8),
    "big_dragons": ("大三元", 8),
    "small_winds": ("小四喜", 8),
    "full_flush": ("清一色", 8),
    "eight_immortals": ("八仙过海", 8),
    "seven_robs_one": ("七抢一", 8),
    "eight_pairs_half": ("八对半", 8),
    "heavenly_ready": ("天听", 16),
    "earthly_win": ("地胡", 16),
    "human_win": ("人胡", 16),
    "all_honors": ("字一色", 16),
    "big_winds": ("大四喜", 16),
    "heavenly_win": ("天胡", 24),
}


PRESET_FAN_TAI = {
    "star31": {
        "heavenly_win": 24,
        "earthly_win": 16,
        "human_win": 0,
        "heavenly_ready": 8,
        "earthly_ready": 4,
        "fully_exposed": 2,
        "all_honors": 8,
        "pinfu": 2,
    },
    "shenlaiye": {
        "heavenly_win": 24,
        "earthly_win": 16,
        "human_win": 8,
        "heavenly_ready": 0,
        "earthly_ready": 4,
        "fully_exposed": 2,
        "all_honors": 8,
        "pinfu": 2,
    },
}


def _fan(fan_id: str, count: int = 1, tai: Optional[int] = None) -> Fan:
    name, default_tai = FAN[fan_id]
    return Fan(fan_id, name, default_tai if tai is None else tai, count)


def _apply_scoring_preset(fans: Iterable[Fan], rules: TaiwanRules) -> Tuple[Fan, ...]:
    """套用预设台表；独立馆规（例如花杠台数）仍以字段值为准。"""

    overrides = PRESET_FAN_TAI.get(rules.scoring_preset, {})
    resolved = [
        replace(fan, tai=overrides[fan.fan_id])
        if fan.fan_id in overrides
        else fan
        for fan in fans
    ]
    resolved = [fan for fan in resolved if fan.tai > 0]
    if rules.scoring_preset == "shenlaiye" and any(
        fan.fan_id == "all_honors" for fan in resolved
    ):
        resolved = [fan for fan in resolved if fan.fan_id != "all_triplets"]
    return tuple(resolved)


def _score_tai(context: HandContext, fans: Sequence[Fan]) -> int:
    tai = sum(fan.total for fan in fans)
    has_flower_win = any(
        fan.fan_id in ("eight_immortals", "seven_robs_one")
        for fan in fans
    )
    if (
        has_flower_win
        and (
            context.heavenly_win
            or context.earthly_win
        )
    ):
        tai += context.rules.heavenly_earthly_flower_tai
    return tai


def _is_self_draw(context: HandContext) -> bool:
    return context.win_source == "self_draw"


def _is_rob_kong(context: HandContext) -> bool:
    return context.win_source == "rob_kong"


def _all_structure_tiles(decomposition: Decomposition) -> List[int]:
    tiles = [decomposition.pair, decomposition.pair]
    for meld in decomposition.melds:
        tiles.extend(meld.tiles[:3])
    return tiles


def _triplet_tiles(decomposition: Decomposition) -> Set[int]:
    return {
        meld.tile
        for meld in decomposition.melds
        if meld.kind in ("triplet", "kong")
    }


def _concealed_triplet_count(
    decomposition: Decomposition,
    context: HandContext,
) -> int:
    winning_kind, winning_index = decomposition.winning_component
    count = 0
    for index, meld in enumerate(decomposition.melds):
        if meld.kind not in ("triplet", "kong") or not meld.concealed:
            continue
        completed_by_discard = (
            not _is_self_draw(context)
            and winning_kind == "triplet"
            and winning_index == index
            and meld.kind == "triplet"
        )
        if not completed_by_discard:
            count += 1
    return count


def _is_menqing(melds: Iterable[Meld]) -> bool:
    return all(meld.concealed for meld in melds)


def _flower_fans(context: HandContext, *, suppress: bool = False) -> List[Fan]:
    if suppress:
        return []
    rules = context.rules
    flowers = set(context.flowers)
    completed_flower_sets = tuple(
        group for group in FLOWER_SETS if group.issubset(flowers)
    )
    flower_kong_tiles = {
        tile for group in completed_flower_sets for tile in group
    }
    fans: List[Fan] = []
    if rules.flower_scoring == "any":
        if context.flowers:
            fans.append(Fan("seat_flower", "见花", 1, len(context.flowers)))
    else:
        correct = sum(
            1
            for tile in context.flowers
            if tile in SEAT_FLOWERS.get(context.seat_wind, ())
            and tile not in flower_kong_tiles
        )
        if correct:
            fans.append(_fan("seat_flower", correct))
    flower_kong_count = len(completed_flower_sets)
    if flower_kong_count:
        fans.append(_fan("flower_kong", count=flower_kong_count, tai=rules.flower_kong_tai))
    if not context.flowers and rules.no_flower_tai:
        fans.append(Fan("no_flower", "无花", rules.no_flower_tai))
    return fans


def _append_extension_fans(
    fans: List[Fan],
    context: HandContext,
    decomposition: Decomposition,
    *,
    all_exposed: bool,
) -> None:
    rules = context.rules
    structure = (
        list(context.hand_tiles)
        if decomposition.special == "eight_pairs_half"
        else _all_structure_tiles(decomposition)
    )
    triplets = _triplet_tiles(decomposition)

    if rules.half_exposed_tai and all_exposed and _is_self_draw(context):
        fans.append(Fan("half_exposed", "半求人", rules.half_exposed_tai))
    if rules.river_bottom_tai and context.river_bottom and not _is_self_draw(context):
        fans.append(Fan("river_bottom", "河底捞鱼", rules.river_bottom_tai))
    if rules.all_winds_tai:
        wind_count = sum(1 for tile in WIND_TILES if tile in triplets)
        if wind_count:
            fans.append(Fan("all_winds", "见字", rules.all_winds_tai, wind_count))
    if rules.no_honor_no_flower_tai and not context.flowers and all(tile < 40 for tile in structure):
        fans[:] = [fan for fan in fans if fan.fan_id != "no_flower"]
        fans.append(Fan("no_honor_no_flower", "无字无花", rules.no_honor_no_flower_tai))
    if rules.open_kong_tai:
        count = sum(1 for meld in decomposition.melds if meld.kind == "kong" and not meld.concealed)
        if count:
            fans.append(Fan("open_kong", "明杠", rules.open_kong_tai, count))
    if rules.concealed_kong_tai:
        count = sum(1 for meld in decomposition.melds if meld.kind == "kong" and meld.concealed)
        if count:
            fans.append(Fan("concealed_kong", "暗杠", rules.concealed_kong_tai, count))


def _starting_win(context: HandContext) -> Optional[str]:
    if context.heavenly_win:
        return "heavenly_win"
    if context.earthly_win:
        return "earthly_win"
    if context.human_win:
        return "human_win"
    return None


def _qualification_fan(context: HandContext) -> Optional[str]:
    # 开放公开听牌后，天地听与普通公开听牌使用同一套公开声明；
    # 未开放时天地听由服务器秘密登记，不要求玩家额外报听。
    if context.rules.public_ready_tai and not context.declared_ready:
        return None
    if context.rules.heavenly_earthly_ready_enabled:
        if context.heavenly_ready:
            return "heavenly_ready"
        if context.earthly_ready:
            return "earthly_ready"
    if context.declared_ready and context.rules.public_ready_tai:
        return "declared_ready"
    return None


def _score_decomposition(
    context: HandContext,
    decomposition: Decomposition,
    waits: FrozenSet[int],
) -> Tuple[Fan, ...]:
    rules = context.rules
    winning_tile = context.winning_tile
    triplets = _triplet_tiles(decomposition)
    # 八对半只借一个伪刻子复用风牌刻判断；一色类必须检查完整手牌，
    # 否则伪刻子恰在单一花色时会把混合牌手误判成清一色。
    structure = (
        list(context.hand_tiles)
        if decomposition.special == "eight_pairs_half"
        else _all_structure_tiles(decomposition)
    )
    menqing = _is_menqing(decomposition.melds)
    self_draw = _is_self_draw(context)
    all_exposed = (
        len(decomposition.melds) == SIXTEEN_TILE_MAHJONG.meld_count
        and all(meld.external and not meld.concealed for meld in decomposition.melds)
    )
    starting = _starting_win(context)
    if starting and PRESET_FAN_TAI.get(rules.scoring_preset, {}).get(starting) == 0:
        # 该台表不设此起手胡时，按普通胡牌计分，而不是先套用起手胡排除项再删掉 0 台项。
        starting = None

    single_wait = bool(
        winning_tile is not None
        and len(waits) == 1
        and winning_use_is_single_wait(decomposition, winning_tile)
        and not all_exposed
    )
    if rules.scoring_preset in ("star31", "shenlaiye"):
        pinfu = (
            decomposition_is_all_sequences(decomposition)
            and not self_draw
            and not context.flowers
            and decomposition.pair < 40
            and all(tile < 40 for tile in structure)
            and len(waits) >= 2
            and winning_uses_only_two_sided(decomposition, winning_tile)
        )
    else:
        pinfu = (
            decomposition_is_all_sequences(decomposition)
            and any(meld.external for meld in decomposition.melds)
            and len(waits) >= 2
            and decomposition.winning_component[0] != "pair"
        )

    big_dragons = all(tile in triplets for tile in DRAGON_TILES)
    dragon_triplet_count = sum(1 for tile in DRAGON_TILES if tile in triplets)
    dragon_pair_count = int(decomposition.pair in DRAGON_TILES)
    small_dragons = not big_dragons and dragon_triplet_count == 2 and dragon_pair_count == 1

    wind_triplet_count = sum(1 for tile in WIND_TILES if tile in triplets)
    big_winds = wind_triplet_count == 4
    small_winds = not big_winds and wind_triplet_count == 3 and decomposition.pair in WIND_TILES

    suits = {tile // 10 for tile in structure if tile < 40}
    has_honor = any(tile >= 40 for tile in structure)
    has_number = any(tile < 40 for tile in structure)
    full_flush = len(suits) == 1 and not has_honor
    half_flush = len(suits) == 1 and has_honor and has_number
    all_honors = not has_number

    fans: List[Fan] = []

    # 起手胡的排除项在加入基础台时直接应用；天胡、地胡仍另计自摸。
    pinfu_excludes_menqing = pinfu and rules.scoring_preset not in ("star31", "shenlaiye")
    if menqing and starting not in ("heavenly_win", "earthly_win", "human_win") and not pinfu_excludes_menqing:
        fans.append(_fan("menqing"))
    if menqing and self_draw and starting not in ("heavenly_win", "earthly_win"):
        fans.append(_fan("self_reliant"))
    if self_draw:
        fans.append(_fan("self_draw"))

    small_winds_keeps_wind_fans = (
        small_winds and rules.scoring_preset in ("shenlaiye", "cml")
    )
    if (
        not big_winds
        and (not small_winds or small_winds_keeps_wind_fans)
        and not rules.all_winds_tai
    ):
        if context.seat_wind in triplets:
            fans.append(_fan("seat_wind"))
        if context.round_wind in triplets:
            fans.append(_fan("round_wind"))

    fans.extend(_flower_fans(context))

    if not (big_dragons or small_dragons) and dragon_triplet_count:
        fans.append(_fan("dragon", dragon_triplet_count))

    if single_wait and not pinfu and starting != "heavenly_win":
        fans.append(_fan("single_wait"))
    if _is_rob_kong(context):
        fans.append(_fan("rob_kong"))
    if context.after_kong and self_draw and starting != "heavenly_win":
        fans.append(_fan("after_kong"))
    if context.last_tile and self_draw:
        fans.append(_fan("last_draw"))

    if pinfu:
        fans.append(_fan("pinfu"))

    concealed_count = _concealed_triplet_count(decomposition, context)
    if concealed_count >= SIXTEEN_TILE_MAHJONG.meld_count:
        fans.append(_fan("five_concealed"))
    elif concealed_count >= 4:
        fans.append(_fan("four_concealed"))
    elif concealed_count >= 3:
        fans.append(_fan("three_concealed"))

    if all_exposed and not self_draw:
        fans.append(_fan("fully_exposed"))
    if decomposition_is_all_triplets(decomposition):
        fans.append(_fan("all_triplets"))
    if big_dragons:
        fans.append(_fan("big_dragons"))
    elif small_dragons:
        fans.append(_fan("small_dragons"))
    if half_flush:
        fans.append(_fan("half_flush"))
    if big_winds:
        fans.append(_fan("big_winds"))
    elif small_winds:
        fans.append(_fan("small_winds"))
    if full_flush:
        fans.append(_fan("full_flush"))
    if all_honors:
        fans.append(_fan("all_honors"))

    qualification = _qualification_fan(context)
    if qualification and not starting:
        if qualification == "declared_ready":
            fans.append(Fan("declared_ready", "公开听牌", rules.public_ready_tai))
        else:
            fans.append(_fan(qualification))
        if rules.scoring_preset == "shenlaiye" and qualification == "earthly_ready":
            fans = [fan for fan in fans if fan.fan_id not in ("menqing", "declared_ready")]

    if starting:
        fans.append(_fan(starting))

    _append_extension_fans(fans, context, decomposition, all_exposed=all_exposed)

    # 八花普通胡牌加计/复合模式替代正花、花杠后再加 8 台。
    if len(set(context.flowers)) == 8 and rules.eight_immortals_mode in ("add_to_normal", "compound"):
        fans = [fan for fan in fans if fan.fan_id not in ("seat_flower", "flower_kong")]
        fans.append(_fan("eight_immortals"))

    return _apply_scoring_preset(fans, rules)


def _score_eight_pairs_half(context: HandContext, waits: FrozenSet[int]) -> Tuple[Fan, ...]:
    counter = Counter(context.hand_tiles)
    triplet_tile = next(tile for tile, count in counter.items() if count == 3)
    pseudo_meld = Meld("triplet", triplet_tile, True, f"K{triplet_tile}")
    # 用特殊拆分复用花色、风牌和起手胡等通用判断，再移除规则明确禁止的暗刻/碰碰胡。
    decomposition = Decomposition(
        pair=min(tile for tile, count in counter.items() if count >= 2 and tile != triplet_tile),
        melds=(pseudo_meld,),
        winning_component=("special", -1),
        special="eight_pairs_half",
    )
    fans = list(_score_decomposition(context, decomposition, waits))
    forbidden = {"three_concealed", "four_concealed", "five_concealed", "all_triplets", "fully_exposed"}
    fans = [fan for fan in fans if fan.fan_id not in forbidden]
    fans.append(_fan("eight_pairs_half"))
    return _apply_scoring_preset(fans, context.rules)


class TaiwanScorer:
    """线程安全、无内部可变状态的台湾麻将计算器。"""

    def score_hand(self, context: HandContext) -> ScoreResult:
        context.rules.validate()

        if context.seven_robs_one:
            if not context.rules.seven_robs_one:
                return ScoreResult(False, reason="房间未启用七抢一")
            if len(set(context.flowers)) != 8:
                return ScoreResult(False, reason="七抢一结算必须集齐八张花牌")
            return self._special_result(context, "seven_robs_one")
        compound_eight_immortals = False
        if context.eight_immortals:
            if len(set(context.flowers)) != 8:
                return ScoreResult(False, reason="八仙过海必须取得全部八张花牌")
            if context.rules.eight_immortals_mode in ("optional_separate", "forced_separate"):
                return self._special_result(context, "eight_immortals")
            compound_eight_immortals = context.rules.eight_immortals_mode == "compound"

        winning_tile = context.winning_tile
        if winning_tile not in STRUCTURE_TILES:
            return ScoreResult(False, reason="胡牌张不是有效结构牌")
        if winning_tile not in context.hand_tiles:
            return ScoreResult(False, reason="最终手牌中缺少胡牌张")

        pre_win = (
            list(context.pre_win_tiles)
            if context.pre_win_tiles is not None
            else derive_pre_win_tiles(context.hand_tiles, winning_tile)
        )
        waits = frozenset(structural_waits(pre_win, context.meld_codes, context.rules))
        decompositions = enumerate_decompositions(
            context.hand_tiles,
            context.meld_codes,
            winning_tile,
        )

        candidates: List[Tuple[int, int, tuple, Tuple[Fan, ...], Decomposition]] = []
        for decomposition in decompositions:
            fans = _score_decomposition(context, decomposition, waits)
            tai = _score_tai(context, fans)
            candidates.append((tai, len(fans), decomposition.stable_key(), fans, decomposition))

        if context.rules.eight_pairs_half and is_eight_pairs_half(context.hand_tiles, context.meld_codes):
            fans = _score_eight_pairs_half(context, waits)
            decomposition = Decomposition(
                pair=0,
                melds=(),
                winning_component=("special", -1),
                special="eight_pairs_half",
            )
            candidates.append((_score_tai(context, fans), len(fans), decomposition.stable_key(), fans, decomposition))

        if not candidates and compound_eight_immortals:
            # 复合模式在补齐八花时强制结束；当时没有普通牌形也仍按固定花胡结算。
            return self._special_result(context, "eight_immortals")
        if not candidates:
            return ScoreResult(False, waits=waits, reason="不满足标准面子加一将结构")

        selection_pool = candidates
        if (
            context.rules.scoring_preset == "star31"
            and context.win_source != "self_draw"
        ):
            triplet_wins = [
                item
                for item in candidates
                if item[4].winning_component[0] == "triplet"
            ]
            if triplet_wins:
                selection_pool = triplet_wins

        # 平台指定的解释顺序优先；其后依台数、台种数与稳定拆分顺序择一。
        max_tai = max(item[0] for item in selection_pool)
        max_count = max(item[1] for item in selection_pool if item[0] == max_tai)
        best = min(
            (
                item
                for item in selection_pool
                if item[0] == max_tai and item[1] == max_count
            ),
            key=lambda item: item[2],
        )
        tai, _, _, fans, decomposition = best
        if tai < context.rules.minimum_tai:
            return ScoreResult(
                False,
                tai=tai,
                capped_tai=tai,
                fans=fans,
                decomposition=decomposition,
                waits=waits,
                reason=f"未达到最低 {context.rules.minimum_tai} 台",
                below_minimum=True,
            )
        capped = min(tai, context.rules.tai_cap) if context.rules.tai_cap else tai
        return ScoreResult(True, tai, capped, fans, decomposition, waits)

    def _special_result(self, context: HandContext, fan_id: str) -> ScoreResult:
        resolved = _apply_scoring_preset((_fan(fan_id),), context.rules)
        tai = _score_tai(context, resolved)
        if tai < context.rules.minimum_tai:
            return ScoreResult(
                False,
                tai=tai,
                capped_tai=tai,
                fans=resolved,
                reason="未达到最低台",
                below_minimum=True,
            )
        capped = min(tai, context.rules.tai_cap) if context.rules.tai_cap else tai
        decomposition = Decomposition(0, (), ("special", -1), special=fan_id)
        return ScoreResult(True, tai, capped, resolved, decomposition)


def combine_settlements(*settlements: Settlement) -> Settlement:
    """合并同一次和牌中由不同付款规则产生的零和账本。"""
    changes: Dict[int, int] = {}
    payments: List[Payment] = []
    for settlement in settlements:
        for index, change in settlement.score_changes.items():
            changes[index] = changes.get(index, 0) + change
        payments.extend(settlement.payments)
    if sum(changes.values()) != 0:
        raise AssertionError("台湾麻将复合结算账本必须为零和")
    return Settlement(changes, tuple(payments))


def settlement_display_fan_names(
    hand_fan_names: Sequence[str],
    settlement: Settlement,
) -> List[str]:
    """为结算 UI 附加实际参与本次支付的连庄拉庄，不改变手牌台数。"""

    fan_names = list(hand_fan_names)
    dealer_tai = max((payment.dealer_tai for payment in settlement.payments), default=0)
    if dealer_tai > 0:
        fan_names.append("连庄拉庄" if dealer_tai == 1 else f"连庄拉庄*{dealer_tai}")
    return fan_names


def settle_win(
    *,
    winner: int,
    hand_tai: int,
    win_source: str,
    dealer: int,
    dealer_streak: int,
    rules: Optional[TaiwanRules] = None,
    discarder: Optional[int] = None,
    liable_payer: Optional[int] = None,
    player_indices: Sequence[int] = (0, 1, 2, 3),
) -> Settlement:
    """按“一底若干台”及连庄拉庄逐笔生成零和账本。"""

    rules = rules or TaiwanRules()
    rules.validate()
    if winner not in player_indices or dealer not in player_indices:
        raise ValueError("赢家或庄家座位非法")
    capped_tai = min(hand_tai, rules.tai_cap) if rules.tai_cap else hand_tai
    if liable_payer is not None:
        if liable_payer == winner or liable_payer not in player_indices:
            raise ValueError("包赔责任人座位非法")
        # 包牌者承担原本三名输家的全部逐笔支付；每笔仍按原付款人与庄家的
        # 关系计算连庄拉庄，最后合并记到责任人名下。
        payers = [index for index in player_indices if index != winner]
    elif win_source == "self_draw":
        payers = [index for index in player_indices if index != winner]
    elif win_source in ("discard", "rob_kong", "seven_robs_one"):
        if discarder is None or discarder == winner or discarder not in player_indices:
            raise ValueError("点胡、抢杠或七抢一必须指定责任付款人")
        payers = [discarder]
    else:
        raise ValueError(f"未知胡牌来源: {win_source}")

    dealer_relation = 1 + 2 * max(0, dealer_streak)
    changes: Dict[int, int] = {index: 0 for index in player_indices}
    payments: List[Payment] = []
    for original_payer in payers:
        payer = liable_payer if liable_payer is not None else original_payer
        relation_tai = dealer_relation if dealer in (winner, original_payer) else 0
        amount = rules.base_points + (capped_tai + relation_tai) * rules.points_per_tai
        changes[payer] -= amount
        changes[winner] += amount
        payments.append(Payment(payer, winner, capped_tai, relation_tai, amount))

    if sum(changes.values()) != 0:
        raise AssertionError("台湾麻将结算账本必须为零和")
    return Settlement(changes, tuple(payments))
