"""台湾麻将台种判断、最高解释与逐笔支付。"""

from collections import Counter
from dataclasses import replace
from typing import Dict, FrozenSet, Iterable, List, Optional, Sequence, Set, Tuple

from ..hand_structure import SIXTEEN_TILE_MAHJONG
from .fan import FAN_DEFINITIONS, resolved_fan_tai
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
)


def _fan(fan_id: str, count: int = 1) -> Fan:
    name, default_tai = FAN_DEFINITIONS[fan_id]
    return Fan(fan_id, name, default_tai, count)


def _apply_scoring_table(fans: Iterable[Fan], rules: TaiwanRules) -> Tuple[Fan, ...]:
    """依所选完整预设台表与房间稀疏差异解析每个台种。"""

    resolved = [
        replace(
            fan,
            tai=resolved_fan_tai(
                rules.scoring_preset,
                rules.fan_tai_overrides,
                fan.fan_id,
            ),
        )
        for fan in fans
    ]
    return tuple(resolved)


def _append_opening_flower_bonus(
    fans: List[Fan],
    context: HandContext,
) -> None:
    has_flower_win = any(
        fan.fan_id in ("eight_flowers_and_seasons", "seven_flowers_steal_eighth")
        for fan in fans
    )
    if (
        has_flower_win
        and (context.heavenly_win or context.earthly_win)
        and context.rules.initial_flower_bonus_enabled
    ):
        fans.append(_fan("initial_flower_bonus"))


def _score_tai(fans: Sequence[Fan]) -> int:
    return sum(fan.total for fan in fans)


def _is_self_draw(context: HandContext) -> bool:
    return context.win_source == "self_draw"


def _is_rob_kong(context: HandContext) -> bool:
    return context.win_source == "robbing_kong"


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
    if rules.all_flower_tiles_enabled:
        if context.flowers:
            fans.append(Fan("flower_tile", "花牌", 1, len(context.flowers)))
    else:
        correct = sum(
            1
            for tile in context.flowers
            if tile in SEAT_FLOWERS.get(context.seat_wind, ())
            and (
                not rules.flower_kong_excludes_seat_flower
                or tile not in flower_kong_tiles
            )
        )
        if correct:
            fans.append(_fan("flower_tile", correct))
        flower_kong_count = len(completed_flower_sets)
        if flower_kong_count:
            fans.append(_fan("flower_kong", count=flower_kong_count))
    if not context.flowers and rules.no_flowers_enabled:
        fans.append(_fan("no_flowers"))
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
        if decomposition.special == "eight_and_a_half_pairs"
        else _all_structure_tiles(decomposition)
    )
    triplets = _triplet_tiles(decomposition)

    if rules.half_begging_enabled and all_exposed and _is_self_draw(context):
        fans.append(_fan("half_begging"))
    if rules.last_tile_claim_enabled and context.last_tile_claim and not _is_self_draw(context):
        fans.append(_fan("last_tile_claim"))
    if rules.all_wind_pungs_enabled:
        wind_count = sum(1 for tile in WIND_TILES if tile in triplets)
        if wind_count:
            fans.append(_fan("wind_pung", wind_count))
    if (
        rules.no_flowers_or_honors_enabled
        and not context.flowers
        and all(tile < 40 for tile in structure)
    ):
        fans[:] = [fan for fan in fans if fan.fan_id != "no_flowers"]
        fans.append(_fan("no_flowers_or_honors"))
    if rules.melded_kong_enabled:
        count = sum(1 for meld in decomposition.melds if meld.kind == "kong" and not meld.concealed)
        if count:
            fans.append(_fan("melded_kong", count))
    if rules.concealed_kong_enabled:
        count = sum(1 for meld in decomposition.melds if meld.kind == "kong" and meld.concealed)
        if count:
            fans.append(_fan("concealed_kong", count))


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
    if context.rules.public_ready_enabled and not context.declared_ready:
        return None
    if context.rules.ready_qualification_mode != "disabled":
        if context.heavenly_ready:
            return "heavenly_ready"
        if context.earthly_ready:
            return "earthly_ready"
    if context.declared_ready and context.rules.public_ready_enabled:
        return "declared_ready"
    return None


def _score_decomposition(
    context: HandContext,
    decomposition: Decomposition,
    *,
    single_wait: bool,
    all_chows: bool,
) -> Tuple[Fan, ...]:
    rules = context.rules
    triplets = _triplet_tiles(decomposition)
    # 八对半只借一个伪刻子复用风牌刻判断；一色类必须检查完整手牌，
    # 否则伪刻子恰在单一花色时会把混合牌手误判成清一色。
    structure = (
        list(context.hand_tiles)
        if decomposition.special == "eight_and_a_half_pairs"
        else _all_structure_tiles(decomposition)
    )
    concealed_hand = _is_menqing(decomposition.melds)
    self_draw = _is_self_draw(context)
    all_exposed = (
        len(decomposition.melds) == SIXTEEN_TILE_MAHJONG.meld_count
        and all(meld.external and not meld.concealed for meld in decomposition.melds)
    )
    starting = _starting_win(context)

    big_three_dragons = all(tile in triplets for tile in DRAGON_TILES)
    dragon_triplet_count = sum(1 for tile in DRAGON_TILES if tile in triplets)
    dragon_pair_count = int(decomposition.pair in DRAGON_TILES)
    little_three_dragons = not big_three_dragons and dragon_triplet_count == 2 and dragon_pair_count == 1

    wind_triplet_count = sum(1 for tile in WIND_TILES if tile in triplets)
    big_four_winds = wind_triplet_count == 4
    little_four_winds = not big_four_winds and wind_triplet_count == 3 and decomposition.pair in WIND_TILES

    suits = {tile // 10 for tile in structure if tile < 40}
    has_honor = any(tile >= 40 for tile in structure)
    has_number = any(tile < 40 for tile in structure)
    full_flush = len(suits) == 1 and not has_honor
    half_flush = len(suits) == 1 and has_honor and has_number
    all_honors = not has_number

    fans: List[Fan] = []

    # 起手胡的排除项在加入基础台时直接应用；天胡、地胡仍另计自摸。
    if concealed_hand and starting not in ("heavenly_win", "earthly_win", "human_win"):
        fans.append(_fan("concealed_hand"))
    if concealed_hand and self_draw and starting not in ("heavenly_win", "earthly_win"):
        fans.append(_fan("fully_concealed_hand"))
    if self_draw:
        fans.append(_fan("self_draw"))

    little_four_winds_keeps_wind_fans = little_four_winds and rules.little_four_winds_add_wind_pungs
    if (
        not big_four_winds
        and (not little_four_winds or little_four_winds_keeps_wind_fans)
        and not rules.all_wind_pungs_enabled
    ):
        if context.seat_wind in triplets:
            fans.append(_fan("seat_wind_pung"))
        if context.round_wind in triplets:
            fans.append(_fan("prevalent_wind_pung"))

    fans.extend(_flower_fans(context))

    if not (big_three_dragons or little_three_dragons) and dragon_triplet_count:
        fans.append(_fan("dragon_pung", dragon_triplet_count))

    if single_wait and starting != "heavenly_win":
        fans.append(_fan("single_wait"))
    if _is_rob_kong(context):
        fans.append(_fan("robbing_kong"))
    if context.out_with_replacement_tile and self_draw and starting != "heavenly_win":
        fans.append(_fan("out_with_replacement_tile"))
    if context.last_tile and self_draw:
        fans.append(_fan("last_tile_draw"))

    if all_chows:
        fans.append(_fan("all_chows"))

    concealed_count = _concealed_triplet_count(decomposition, context)
    if concealed_count >= SIXTEEN_TILE_MAHJONG.meld_count:
        fans.append(_fan("five_concealed_pungs"))
    elif concealed_count >= 4:
        fans.append(_fan("four_concealed_pungs"))
    elif concealed_count >= 3:
        fans.append(_fan("three_concealed_pungs"))

    kong_count = sum(meld.kind == "kong" for meld in decomposition.melds)
    if kong_count >= 5 and rules.five_kongs_enabled:
        fans.append(_fan("five_kongs"))
    elif kong_count >= 4 and rules.four_kongs_enabled:
        fans.append(_fan("four_kongs"))
    if all_exposed and not self_draw:
        fans.append(_fan("all_begging"))
    if (
        decomposition_is_all_triplets(decomposition)
        and (rules.all_honors_add_all_pungs or not all_honors)
    ):
        fans.append(_fan("all_pungs"))
    if big_three_dragons:
        fans.append(_fan("big_three_dragons"))
    elif little_three_dragons:
        fans.append(_fan("little_three_dragons"))
    if half_flush:
        fans.append(_fan("half_flush"))
    if big_four_winds:
        fans.append(_fan("big_four_winds"))
    elif little_four_winds:
        fans.append(_fan("little_four_winds"))
    if full_flush:
        fans.append(_fan("full_flush"))
    if all_honors:
        fans.append(_fan("all_honors"))

    qualification = _qualification_fan(context)
    if qualification and not starting:
        if qualification == "declared_ready":
            fans.append(_fan("declared_ready"))
        else:
            fans.append(_fan(qualification))
        if rules.earthly_ready_excludes_concealed_and_declared_ready and qualification == "earthly_ready":
            fans = [fan for fan in fans if fan.fan_id not in ("concealed_hand", "declared_ready")]

    if starting:
        fans.append(_fan(starting))

    _append_extension_fans(fans, context, decomposition, all_exposed=all_exposed)

    # 八花普通胡牌加计/复合模式替代正花、花杠后再加 8 台。
    if len(set(context.flowers)) == 8 and rules.eight_flowers_mode in ("additive", "compound"):
        fans = [fan for fan in fans if fan.fan_id not in ("flower_tile", "flower_kong")]
        fans.append(_fan("eight_flowers_and_seasons"))

    _append_opening_flower_bonus(fans, context)
    return _apply_scoring_table(fans, rules)


def _score_eight_pairs_half(context: HandContext) -> Tuple[Fan, ...]:
    counter = Counter(context.hand_tiles)
    triplet_tile = next(tile for tile, count in counter.items() if count == 3)
    pseudo_meld = Meld("triplet", triplet_tile, True, f"K{triplet_tile}")
    # 用特殊拆分复用花色、风牌和起手胡等通用判断，再移除规则明确禁止的暗刻/碰碰胡。
    decomposition = Decomposition(
        pair=min(tile for tile, count in counter.items() if count >= 2 and tile != triplet_tile),
        melds=(pseudo_meld,),
        winning_component=("special", -1),
        special="eight_and_a_half_pairs",
    )
    fans = list(
        _score_decomposition(
            context,
            decomposition,
            single_wait=False,
            all_chows=False,
        )
    )
    forbidden = {"three_concealed_pungs", "four_concealed_pungs", "five_concealed_pungs", "all_pungs", "all_begging"}
    fans = [fan for fan in fans if fan.fan_id not in forbidden]
    fans.append(_fan("eight_and_a_half_pairs"))
    return _apply_scoring_table(fans, context.rules)


def _analyze_winning_interpretations(
    context: HandContext,
    decompositions: Sequence[Decomposition],
    winning_tile: int,
    waits: FrozenSet[int],
) -> Tuple[FrozenSet[tuple], FrozenSet[tuple]]:
    single_wait_keys = set()
    all_chows_keys = set()
    all_chows_two_sided_keys = set()

    for decomposition in decompositions:
        component, index = decomposition.winning_component
        single_wait_use = component == "pair"
        two_sided_use = False
        if (
            component == "sequence"
            and 0 <= index < len(decomposition.melds)
        ):
            low, middle, high = decomposition.melds[index].tiles
            rank = winning_tile % 10
            single_wait_use = (
                winning_tile == middle
                or (winning_tile == high and rank == 3)
                or (winning_tile == low and rank == 7)
            )
            two_sided_use = not single_wait_use

        all_exposed = (
            len(decomposition.melds) == SIXTEEN_TILE_MAHJONG.meld_count
            and all(
                meld.external and not meld.concealed
                for meld in decomposition.melds
            )
        )
        if len(waits) == 1 and single_wait_use and not all_exposed:
            single_wait_keys.add(decomposition.stable_key())

        all_chows = (
            decomposition_is_all_sequences(decomposition)
            and (
                context.rules.all_chows_concealed_allowed
                or not _is_menqing(decomposition.melds)
            )
            and (
                context.rules.all_chows_self_draw_allowed
                or not _is_self_draw(context)
            )
            and (
                context.rules.all_chows_honors_and_flowers_allowed
                or (
                    not context.flowers
                    and all(
                        tile < 40
                        for tile in _all_structure_tiles(decomposition)
                    )
                )
            )
        )
        if all_chows:
            key = decomposition.stable_key()
            all_chows_keys.add(key)
            if two_sided_use:
                all_chows_two_sided_keys.add(key)

    single_wait_eligible = frozenset(single_wait_keys)
    all_chows_eligible = frozenset(all_chows_keys)
    if context.rules.all_chows_wait_mode == "unrestricted":
        return single_wait_eligible, all_chows_eligible

    two_sided = frozenset(all_chows_two_sided_keys)
    if context.rules.all_chows_wait_mode == "any_two_sided":
        return single_wait_eligible, two_sided
    if (
        context.rules.all_chows_wait_mode == "only_two_sided"
        and len(two_sided) == len(all_chows_eligible)
    ):
        return single_wait_eligible, two_sided
    return single_wait_eligible, frozenset()


class TaiwanScorer:
    """台湾麻将计算器。"""

    def score_hand(self, context: HandContext) -> ScoreResult:
        context.rules.validate()

        if context.seven_flowers_steal_eighth:
            if not context.rules.seven_flowers_steal_eighth_enabled:
                return ScoreResult(False, reason="房间未启用七抢一")
            if len(set(context.flowers)) != 8:
                return ScoreResult(False, reason="七抢一结算必须集齐八张花牌")
            return self._special_result(context, "seven_flowers_steal_eighth")
        compound_eight_flowers = False
        if context.eight_flowers_and_seasons:
            if len(set(context.flowers)) != 8:
                return ScoreResult(False, reason="八仙过海必须取得全部八张花牌")
            if context.rules.eight_flowers_mode in ("optional_standalone", "forced_standalone"):
                return self._special_result(context, "eight_flowers_and_seasons")
            compound_eight_flowers = context.rules.eight_flowers_mode == "compound"

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
        (
            single_wait_eligible_keys,
            all_chows_eligible_keys,
        ) = _analyze_winning_interpretations(
            context,
            decompositions,
            winning_tile,
            waits,
        )

        candidates: List[Tuple[int, int, tuple, Tuple[Fan, ...], Decomposition]] = []
        for decomposition in decompositions:
            fans = _score_decomposition(
                context,
                decomposition,
                single_wait=(
                    decomposition.stable_key()
                    in single_wait_eligible_keys
                ),
                all_chows=(
                    decomposition.stable_key()
                    in all_chows_eligible_keys
                ),
            )
            tai = _score_tai(fans)
            candidates.append((tai, len(fans), decomposition.stable_key(), fans, decomposition))

        if context.rules.eight_and_a_half_pairs_enabled and is_eight_pairs_half(context.hand_tiles, context.meld_codes):
            fans = _score_eight_pairs_half(context)
            decomposition = Decomposition(
                pair=0,
                melds=(),
                winning_component=("special", -1),
                special="eight_and_a_half_pairs",
            )
            candidates.append((_score_tai(fans), len(fans), decomposition.stable_key(), fans, decomposition))

        if not candidates and compound_eight_flowers:
            # 复合模式在补齐八花时强制结束；当时没有普通牌形也仍按固定花胡结算。
            return self._special_result(context, "eight_flowers_and_seasons")
        if not candidates:
            return ScoreResult(False, waits=waits, reason="不满足标准面子加一将结构")

        selection_pool = candidates
        if (
            context.rules.prefer_triplet_decomposition_on_discard_win
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
        fans = [_fan(fan_id)]
        _append_opening_flower_bonus(fans, context)
        resolved = _apply_scoring_table(fans, context.rules)
        tai = _score_tai(resolved)
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
    if type(hand_tai) is not int or hand_tai < 0:
        raise ValueError("和牌台数必须是非负整数")
    if type(dealer_streak) is not int or dealer_streak < 0:
        raise ValueError("连庄数必须是非负整数")
    if (
        type(winner) is not int
        or type(dealer) is not int
        or winner < 0
        or dealer < 0
    ):
        raise ValueError("赢家与庄家座位必须是非负整数")
    if discarder is not None and (
        type(discarder) is not int or discarder < 0
    ):
        raise ValueError("放铳座位必须是非负整数或空值")
    if liable_payer is not None and (
        type(liable_payer) is not int or liable_payer < 0
    ):
        raise ValueError("包赔责任人座位必须是非负整数或空值")
    if (
        not player_indices
        or any(type(index) is not int or index < 0 for index in player_indices)
        or len(set(player_indices)) != len(player_indices)
    ):
        raise ValueError("结算座位必须是非负且互不重复的整数")
    if winner not in player_indices or dealer not in player_indices:
        raise ValueError("赢家或庄家座位非法")
    capped_tai = min(hand_tai, rules.tai_cap) if rules.tai_cap else hand_tai
    if win_source == "self_draw":
        payers = [index for index in player_indices if index != winner]
    elif win_source in ("discard", "robbing_kong", "seven_flowers_steal_eighth"):
        if discarder is None or discarder == winner or discarder not in player_indices:
            raise ValueError("点胡、抢杠或七抢一必须指定责任付款人")
        payers = [discarder]
    else:
        raise ValueError(f"未知胡牌来源: {win_source}")
    if liable_payer is not None:
        if liable_payer == winner or liable_payer not in player_indices:
            raise ValueError("包赔责任人座位非法")
        if win_source not in ("self_draw", "discard", "robbing_kong"):
            raise ValueError("包牌责任只适用于自摸、点胡或抢杠")

    dealer_relation = 1 + 2 * max(0, dealer_streak)
    changes: Dict[int, int] = {index: 0 for index in player_indices}
    payments: List[Payment] = []
    for original_payer in payers:
        relation_tai = dealer_relation if dealer in (winner, original_payer) else 0
        amount = rules.base_points + (capped_tai + relation_tai) * rules.points_per_tai
        if (
            liable_payer is not None
            and win_source in ("discard", "robbing_kong")
            and liable_payer != original_payer
            and rules.liability_ron_split_enabled
        ):
            shared_amount = (amount + 1) // 2
            payment_parts = (
                (liable_payer, shared_amount),
                (original_payer, shared_amount),
            )
        else:
            payer = liable_payer if liable_payer is not None else original_payer
            payment_parts = ((payer, amount),)
        for payer, payment_amount in payment_parts:
            if payment_amount <= 0:
                continue
            changes[payer] -= payment_amount
            changes[winner] += payment_amount
            payments.append(Payment(payer, winner, capped_tai, relation_tai, payment_amount))

    if sum(changes.values()) != 0:
        raise AssertionError("台湾麻将结算账本必须为零和")
    return Settlement(changes, tuple(payments))
