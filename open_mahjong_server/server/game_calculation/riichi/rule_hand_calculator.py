"""Adapted from mahjong 2.0.0 hand.py (MIT; see MAHJONG_LICENSE.txt).

Changes: configurable double-wind pair fu, yakuman combination/limit, and
selection by actual payment rather than han alone. No global monkey-patching:
simultaneous rooms may safely use different rules. Upstream yaku/divider/fu APIs
remain pinned in requirements.txt.
"""
from collections.abc import Collection
from typing import TypedDict
from mahjong.constants import AKA_DORAS, CHUN, HAKU, HATSU
from mahjong.hand_calculating.divider import HandDivider
from mahjong.hand_calculating.fu import FuCalculator, FuDetail
from mahjong.hand_calculating.hand_config import HandConfig
from mahjong.hand_calculating.hand_response import HandResponse
from mahjong.hand_calculating.scores import Aotenjou, ScoresCalculator, ScoresResult
from mahjong.hand_calculating.yaku import Yaku
from mahjong.meld import Meld
from mahjong.tile import TilesConverter
from mahjong.utils import build_dora_count_map, classify_hand_suits, count_dora_for_hand, plus_dora

class _CalculatedHand(TypedDict):
    cost: ScoresResult | None
    error: str | None
    hand_yaku: list[Yaku]
    han: int
    fu: int
    fu_details: list[FuDetail]
_ALL_SUITS_MASK = 7
_DEFAULT_CONFIG = HandConfig()

class HandCalculator:
    ERR_NO_WINNING_TILE = 'winning_tile_not_in_hand'
    ERR_HAND_NOT_WINNING = 'hand_not_winning'
    ERR_NO_YAKU = 'no_yaku'
    ERR_OPEN_HAND_RIICHI = 'open_hand_riichi_not_allowed'
    ERR_OPEN_HAND_DABURI = 'open_hand_daburi_not_allowed'
    ERR_IPPATSU_WITHOUT_RIICHI = 'ippatsu_without_riichi_not_allowed'
    ERR_CHANKAN_WITH_TSUMO = 'chankan_with_tsumo_not_allowed'
    ERR_RINSHAN_WITHOUT_TSUMO = 'rinshan_without_tsumo_not_allowed'
    ERR_HAITEI_WITHOUT_TSUMO = 'haitei_without_tsumo_not_allowed'
    ERR_HOUTEI_WITH_TSUMO = 'houtei_with_tsumo_not_allowed'
    ERR_HAITEI_WITH_RINSHAN = 'haitei_with_rinshan_not_allowed'
    ERR_HOUTEI_WITH_CHANKAN = 'houtei_with_chankan_not_allowed'
    ERR_TENHOU_NOT_AS_DEALER = 'tenhou_not_as_dealer_not_allowed'
    ERR_TENHOU_WITHOUT_TSUMO = 'tenhou_without_tsumo_not_allowed'
    ERR_TENHOU_WITH_MELD = 'tenhou_with_meld_not_allowed'
    ERR_CHIIHOU_AS_DEALER = 'chiihou_as_dealer_not_allowed'
    ERR_CHIIHOU_WITHOUT_TSUMO = 'chiihou_without_tsumo_not_allowed'
    ERR_CHIIHOU_WITH_MELD = 'chiihou_with_meld_not_allowed'
    ERR_RENHOU_AS_DEALER = 'renhou_as_dealer_not_allowed'
    ERR_RENHOU_WITH_TSUMO = 'renhou_with_tsumo_not_allowed'
    ERR_RENHOU_WITH_MELD = 'renhou_with_meld_not_allowed'

    @staticmethod
    def estimate_hand_value(tiles: Collection[int], win_tile: int, melds: Collection[Meld] | None=None, dora_indicators: Collection[int] | None=None, config: HandConfig | None=None, scores_calculator_factory: type[ScoresCalculator]=ScoresCalculator, ura_dora_indicators: Collection[int] | None=None) -> HandResponse:
        if not melds:
            melds = []
        if not dora_indicators:
            dora_indicators = []
        if not ura_dora_indicators:
            ura_dora_indicators = []
        config = config or _DEFAULT_CONFIG
        hand_yaku = []
        scores_calculator = scores_calculator_factory()
        tiles_34 = TilesConverter.to_34_array(tiles)
        is_aotenjou = isinstance(scores_calculator, Aotenjou)
        opened_melds = [x.tiles_34 for x in melds if x.opened]
        is_open_hand = len(opened_melds) > 0
        if config.is_nagashi_mangan:
            hand_yaku.append(config.yaku.nagashi_mangan)
            fu = 30
            han = config.yaku.nagashi_mangan.han_closed
            cost = scores_calculator.calculate_scores(han, fu, config, False)
            return HandResponse(cost, han, fu, hand_yaku)
        if win_tile not in tiles:
            return HandResponse(error=HandCalculator.ERR_NO_WINNING_TILE)
        if config.is_riichi and (not config.is_daburu_riichi) and is_open_hand:
            return HandResponse(error=HandCalculator.ERR_OPEN_HAND_RIICHI)
        if config.is_daburu_riichi and is_open_hand:
            return HandResponse(error=HandCalculator.ERR_OPEN_HAND_DABURI)
        if config.is_ippatsu and (not config.is_riichi) and (not config.is_daburu_riichi):
            return HandResponse(error=HandCalculator.ERR_IPPATSU_WITHOUT_RIICHI)
        if config.is_chankan and config.is_tsumo:
            return HandResponse(error=HandCalculator.ERR_CHANKAN_WITH_TSUMO)
        if config.is_rinshan and (not config.is_tsumo):
            return HandResponse(error=HandCalculator.ERR_RINSHAN_WITHOUT_TSUMO)
        if config.is_haitei and (not config.is_tsumo):
            return HandResponse(error=HandCalculator.ERR_HAITEI_WITHOUT_TSUMO)
        if config.is_houtei and config.is_tsumo:
            return HandResponse(error=HandCalculator.ERR_HOUTEI_WITH_TSUMO)
        if config.is_haitei and config.is_rinshan:
            return HandResponse(error=HandCalculator.ERR_HAITEI_WITH_RINSHAN)
        if config.is_houtei and config.is_chankan:
            return HandResponse(error=HandCalculator.ERR_HOUTEI_WITH_CHANKAN)
        if config.is_tenhou and config.player_wind and (not config.is_dealer):
            return HandResponse(error=HandCalculator.ERR_TENHOU_NOT_AS_DEALER)
        if config.is_tenhou and (not config.is_tsumo):
            return HandResponse(error=HandCalculator.ERR_TENHOU_WITHOUT_TSUMO)
        if config.is_tenhou and melds:
            return HandResponse(error=HandCalculator.ERR_TENHOU_WITH_MELD)
        if config.is_chiihou and config.player_wind and config.is_dealer:
            return HandResponse(error=HandCalculator.ERR_CHIIHOU_AS_DEALER)
        if config.is_chiihou and (not config.is_tsumo):
            return HandResponse(error=HandCalculator.ERR_CHIIHOU_WITHOUT_TSUMO)
        if config.is_chiihou and melds:
            return HandResponse(error=HandCalculator.ERR_CHIIHOU_WITH_MELD)
        if config.is_renhou and config.player_wind and config.is_dealer:
            return HandResponse(error=HandCalculator.ERR_RENHOU_AS_DEALER)
        if config.is_renhou and config.is_tsumo:
            return HandResponse(error=HandCalculator.ERR_RENHOU_WITH_TSUMO)
        if config.is_renhou and melds:
            return HandResponse(error=HandCalculator.ERR_RENHOU_WITH_MELD)
        if not config.options.has_double_yakuman:
            config.yaku.daburu_kokushi.han_closed = 13
            config.yaku.suuankou_tanki.han_closed = 13
            config.yaku.daburu_chuuren_poutou.han_closed = 13
            config.yaku.daisuushi.han_closed = 13
            config.yaku.daisuushi.han_open = 13
        hand_options = HandDivider.divide_hand(tiles_34, melds)
        dora_count_map = build_dora_count_map(dora_indicators)
        precomputed_dora = count_dora_for_hand(tiles_34, dora_count_map)
        precomputed_aka_dora = 0
        if config.options.has_aka_dora:
            precomputed_aka_dora = sum((t in AKA_DORAS for t in tiles))
        precomputed_ura_dora = 0
        if config.is_riichi or config.is_daburu_riichi:
            ura_count_map = build_dora_count_map(ura_dora_indicators)
            precomputed_ura_dora = count_dora_for_hand(tiles_34, ura_count_map)
        yakuhai_seat_wind_yaku = (config.yaku.seat_wind_east, config.yaku.seat_wind_south, config.yaku.seat_wind_west, config.yaku.seat_wind_north)
        yakuhai_round_wind_yaku = (config.yaku.round_wind_east, config.yaku.round_wind_south, config.yaku.round_wind_west, config.yaku.round_wind_north)
        calculated_hands: list[_CalculatedHand] = []
        for hand in hand_options:
            is_chiitoitsu = config.yaku.chiitoitsu.is_condition_met(hand)
            valued_tiles = [HAKU, HATSU, CHUN, config.player_wind, config.round_wind]
            chi_sets: list[list[int]] = []
            pon_sets: list[list[int]] = []
            kan_sets: list[list[int]] = []
            for x in hand:
                length = len(x)
                if length == 4:
                    kan_sets.append(x)
                elif length == 3:
                    if x[0] == x[1]:
                        pon_sets.append(x)
                    else:
                        chi_sets.append(x)
            is_tanyao_hand = config.yaku.tanyao.is_condition_met(hand)
            suit_mask, honor_count = classify_hand_suits(hand)
            has_honors = honor_count > 0
            win_groups = HandCalculator._find_win_groups(win_tile, hand, opened_melds)
            for win_group in win_groups:
                cost = None
                error = None
                hand_yaku = []
                han = 0
                fu_details, fu = FuCalculator.calculate_fu(hand, win_tile, win_group, config, list(dict.fromkeys(valued_tiles)) if getattr(config.options, 'double_wind_pair_fu', 4) == 2 else valued_tiles, melds)
                is_pinfu = len(fu_details) == 1 and (not is_chiitoitsu) and (not is_open_hand)
                if config.is_tsumo and (not is_open_hand):
                    hand_yaku.append(config.yaku.tsumo)
                if is_pinfu:
                    hand_yaku.append(config.yaku.pinfu)
                if is_chiitoitsu:
                    hand_yaku.append(config.yaku.chiitoitsu)
                if config.options.has_daisharin:
                    is_daisharin = config.yaku.daisharin.is_condition_met(hand, config.options.has_daisharin_other_suits)
                    if is_daisharin:
                        config.yaku.daisharin.rename(hand)
                        hand_yaku.append(config.yaku.daisharin)
                if config.options.has_daichisei and config.yaku.daichisei.is_condition_met(hand):
                    hand_yaku.append(config.yaku.daichisei)
                if (not is_open_hand or config.options.has_open_tanyao) and is_tanyao_hand:
                    hand_yaku.append(config.yaku.tanyao)
                if config.is_riichi and (not config.is_daburu_riichi):
                    if config.is_open_riichi:
                        hand_yaku.append(config.yaku.open_riichi)
                    else:
                        hand_yaku.append(config.yaku.riichi)
                if config.is_daburu_riichi:
                    if config.is_open_riichi:
                        hand_yaku.append(config.yaku.daburu_open_riichi)
                    else:
                        hand_yaku.append(config.yaku.daburu_riichi)
                if not config.is_tsumo and config.options.has_sashikomi_yakuman and (config.yaku.daburu_open_riichi in hand_yaku or config.yaku.open_riichi in hand_yaku):
                    hand_yaku.append(config.yaku.sashikomi)
                if config.is_ippatsu:
                    hand_yaku.append(config.yaku.ippatsu)
                if config.is_rinshan:
                    hand_yaku.append(config.yaku.rinshan)
                if config.is_chankan:
                    hand_yaku.append(config.yaku.chankan)
                if config.is_haitei:
                    hand_yaku.append(config.yaku.haitei)
                if config.is_houtei:
                    hand_yaku.append(config.yaku.houtei)
                if config.is_renhou:
                    if config.options.renhou_as_yakuman:
                        hand_yaku.append(config.yaku.renhou_yakuman)
                    else:
                        hand_yaku.append(config.yaku.renhou)
                if config.is_tenhou:
                    hand_yaku.append(config.yaku.tenhou)
                if config.is_chiihou:
                    hand_yaku.append(config.yaku.chiihou)
                if config.yaku.chinitsu.is_condition_met(hand):
                    hand_yaku.append(config.yaku.chinitsu)
                elif config.yaku.honitsu.is_condition_met(hand):
                    hand_yaku.append(config.yaku.honitsu)
                if not chi_sets:
                    if has_honors and config.yaku.tsuisou.is_condition_met(hand):
                        hand_yaku.append(config.yaku.tsuisou)
                    if not is_tanyao_hand:
                        if config.yaku.honroto.is_condition_met(hand):
                            hand_yaku.append(config.yaku.honroto)
                        if not has_honors and config.yaku.chinroto.is_condition_met(hand):
                            hand_yaku.append(config.yaku.chinroto)
                if config.yaku.ryuisou.is_condition_met(hand):
                    hand_yaku.append(config.yaku.ryuisou)
                if config.paarenchan > 0 and (not config.options.paarenchan_needs_yaku):
                    config.yaku.paarenchan.set_paarenchan_count(config.paarenchan)
                    hand_yaku.append(config.yaku.paarenchan)
                if chi_sets:
                    if not is_tanyao_hand:
                        if config.yaku.chantai.is_condition_met(hand):
                            hand_yaku.append(config.yaku.chantai)
                        if config.yaku.junchan.is_condition_met(hand):
                            hand_yaku.append(config.yaku.junchan)
                        if config.yaku.ittsu.is_condition_met(hand):
                            hand_yaku.append(config.yaku.ittsu)
                    if not is_open_hand:
                        if config.yaku.ryanpeiko.is_condition_met(hand):
                            hand_yaku.append(config.yaku.ryanpeiko)
                        elif config.yaku.iipeiko.is_condition_met(hand):
                            hand_yaku.append(config.yaku.iipeiko)
                    if suit_mask == _ALL_SUITS_MASK and config.yaku.sanshoku.is_condition_met(hand):
                        hand_yaku.append(config.yaku.sanshoku)
                if pon_sets or kan_sets:
                    if config.yaku.toitoi.is_condition_met(hand):
                        hand_yaku.append(config.yaku.toitoi)
                    if config.yaku.sanankou.is_condition_met(hand, win_tile, melds, config.is_tsumo):
                        hand_yaku.append(config.yaku.sanankou)
                    if suit_mask == _ALL_SUITS_MASK and config.yaku.sanshoku_douko.is_condition_met(hand):
                        hand_yaku.append(config.yaku.sanshoku_douko)
                    if has_honors:
                        if config.yaku.shosangen.is_condition_met(hand):
                            hand_yaku.append(config.yaku.shosangen)
                        if config.yaku.haku.is_condition_met(hand):
                            hand_yaku.append(config.yaku.haku)
                        if config.yaku.hatsu.is_condition_met(hand):
                            hand_yaku.append(config.yaku.hatsu)
                        if config.yaku.chun.is_condition_met(hand):
                            hand_yaku.append(config.yaku.chun)
                        for yaku in yakuhai_seat_wind_yaku:
                            if yaku.is_condition_met(hand, config.player_wind):
                                hand_yaku.append(yaku)
                        for yaku in yakuhai_round_wind_yaku:
                            if yaku.is_condition_met(hand, config.round_wind):
                                hand_yaku.append(yaku)
                        if config.yaku.daisangen.is_condition_met(hand):
                            hand_yaku.append(config.yaku.daisangen)
                        if config.yaku.shosuushi.is_condition_met(hand):
                            hand_yaku.append(config.yaku.shosuushi)
                        if config.yaku.daisuushi.is_condition_met(hand):
                            hand_yaku.append(config.yaku.daisuushi)
                    if not melds and (not is_tanyao_hand) and config.yaku.chuuren_poutou.is_condition_met(hand):
                        if tiles_34[win_tile // 4] == 2 or tiles_34[win_tile // 4] == 4:
                            hand_yaku.append(config.yaku.daburu_chuuren_poutou)
                        else:
                            hand_yaku.append(config.yaku.chuuren_poutou)
                    if not is_open_hand and config.yaku.suuankou.is_condition_met(hand, win_tile, config.is_tsumo):
                        if tiles_34[win_tile // 4] == 2:
                            hand_yaku.append(config.yaku.suuankou_tanki)
                        else:
                            hand_yaku.append(config.yaku.suuankou)
                    if config.yaku.sankantsu.is_condition_met(hand, melds):
                        hand_yaku.append(config.yaku.sankantsu)
                    if config.yaku.suukantsu.is_condition_met(hand, melds):
                        hand_yaku.append(config.yaku.suukantsu)
                if config.paarenchan > 0 and config.options.paarenchan_needs_yaku and (len(hand_yaku) > 0):
                    config.yaku.paarenchan.set_paarenchan_count(config.paarenchan)
                    hand_yaku.append(config.yaku.paarenchan)
                yakuman_list = [x for x in hand_yaku if x.is_yakuman]
                if yakuman_list:
                    if not is_aotenjou:
                        hand_yaku = yakuman_list
                    else:
                        scores_calculator.aotenjou_filter_yaku(hand_yaku, config)
                        yakuman_list = []
                for item in hand_yaku:
                    if is_open_hand and item.han_open:
                        han += item.han_open
                    else:
                        han += item.han_closed
                if yakuman_list:
                    han = _limited_yakuman_han(hand_yaku, is_open_hand, config)
                if han == 0:
                    error = HandCalculator.ERR_NO_YAKU
                    cost = None
                if not yakuman_list and (not error):
                    if precomputed_dora:
                        config.yaku.dora.han_open = precomputed_dora
                        config.yaku.dora.han_closed = precomputed_dora
                        hand_yaku.append(config.yaku.dora)
                        han += precomputed_dora
                    if precomputed_aka_dora:
                        config.yaku.aka_dora.han_open = precomputed_aka_dora
                        config.yaku.aka_dora.han_closed = precomputed_aka_dora
                        hand_yaku.append(config.yaku.aka_dora)
                        han += precomputed_aka_dora
                    if precomputed_ura_dora:
                        config.yaku.ura_dora.han_closed = precomputed_ura_dora
                        hand_yaku.append(config.yaku.ura_dora)
                        han += precomputed_ura_dora
                if not is_aotenjou and (config.options.limit_to_sextuple_yakuman and han > 78):
                    han = 78
                if not error:
                    cost = scores_calculator.calculate_scores(han, fu, config, len(yakuman_list) > 0)
                calculated_hand = _CalculatedHand(cost=cost, error=error, hand_yaku=hand_yaku, han=han, fu=fu, fu_details=fu_details)
                calculated_hands.append(calculated_hand)
        if not is_open_hand and config.yaku.kokushi.is_condition_met(None, tiles_34):
            if tiles_34[win_tile // 4] == 2:
                hand_yaku.append(config.yaku.daburu_kokushi)
            else:
                hand_yaku.append(config.yaku.kokushi)
            if not config.is_tsumo and config.options.has_sashikomi_yakuman and config.is_open_riichi and (config.is_daburu_riichi or config.is_riichi):
                hand_yaku.append(config.yaku.sashikomi)
            if config.is_renhou and config.options.renhou_as_yakuman:
                hand_yaku.append(config.yaku.renhou_yakuman)
            if config.is_tenhou:
                hand_yaku.append(config.yaku.tenhou)
            if config.is_chiihou:
                hand_yaku.append(config.yaku.chiihou)
            if config.paarenchan > 0:
                config.yaku.paarenchan.set_paarenchan_count(config.paarenchan)
                hand_yaku.append(config.yaku.paarenchan)
            han = 0
            for item in hand_yaku:
                han += item.han_closed
            fu = 0
            if is_aotenjou:
                fu = 30 if config.is_tsumo else 40
                tiles_for_dora = list(tiles)
                count_of_dora = 0
                for tile in tiles_for_dora:
                    count_of_dora += plus_dora(tile, dora_indicators)
                if count_of_dora:
                    config.yaku.dora.han_open = count_of_dora
                    config.yaku.dora.han_closed = count_of_dora
                    hand_yaku.append(config.yaku.dora)
                    han += count_of_dora
                if config.is_riichi or config.is_daburu_riichi:
                    count_of_ura_dora = 0
                    for tile in tiles_for_dora:
                        count_of_ura_dora += plus_dora(tile, ura_dora_indicators)
                    if count_of_ura_dora:
                        config.yaku.ura_dora.han_closed = count_of_ura_dora
                        hand_yaku.append(config.yaku.ura_dora)
                        han += count_of_ura_dora
            if not is_aotenjou:
                han = _limited_yakuman_han(hand_yaku, False, config)
            cost = scores_calculator.calculate_scores(han, fu, config, len(hand_yaku) > 0)
            calculated_hands.append(_CalculatedHand(cost=cost, error=None, hand_yaku=hand_yaku, han=han, fu=fu, fu_details=[]))
        if not calculated_hands:
            return HandResponse(error=HandCalculator.ERR_HAND_NOT_WINNING)
        calculated_hand = max(calculated_hands, key=lambda x: ((x['cost'] or {}).get('total', -1), x['han'], x['fu'], sum((y['fu'] for y in x['fu_details']))))
        error = calculated_hand['error']
        if error:
            return HandResponse(error=error)
        return HandResponse(calculated_hand['cost'], calculated_hand['han'], calculated_hand['fu'], calculated_hand['hand_yaku'], error, calculated_hand['fu_details'], is_open_hand)

    @staticmethod
    def _find_win_groups(win_tile: int, hand: list[list[int]], opened_melds: list[list[int]]) -> list[list[int]]:
        win_tile_34 = (win_tile or 0) // 4
        consumed = [False] * len(opened_melds)
        seen: set[tuple[int, ...]] = set()
        win_groups: list[list[int]] = []
        for x in hand:
            is_opened_meld = False
            for i, meld in enumerate(opened_melds):
                if not consumed[i] and x == meld:
                    consumed[i] = True
                    is_opened_meld = True
                    break
            if is_opened_meld:
                continue
            if win_tile_34 in x:
                key = tuple(x)
                if key not in seen:
                    seen.add(key)
                    win_groups.append(x)
        return win_groups

def _limited_yakuman_han(yaku, is_open, config):
    values = [(y.han_open if is_open and y.han_open else y.han_closed) for y in yaku if y.is_yakuman]
    if not values:
        return 0
    han = sum(values) if getattr(config.options, "multiple_yakuman", True) else max(values)
    return min(han, 13 * getattr(config.options, "yakuman_limit", 6))

