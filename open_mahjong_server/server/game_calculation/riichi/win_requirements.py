"""Separate yaku-only han requirements from bonus-inclusive hand value."""
from mahjong.constants import SOUTH
from mahjong.hand_calculating.hand_config import HandConfig
from mahjong.hand_calculating.scores import ScoresCalculator


BONUS_NAMES = ('宝牌', '赤宝牌', '里宝牌', '拔北宝牌')


def standard_ron_score(han, fu, options, is_yakuman=False):
    """Non-dealer ron equivalent, with no honba, deposits or payment multipliers."""
    if han <= 0:
        return 0
    config = HandConfig(player_wind=SOUTH, is_tsumo=False, options=options)
    return int(ScoresCalculator.calculate_scores(han, fu, config, is_yakuman)['main'])


def yaku_han(result):
    if result.get('no_yaku'):
        return 0
    if 'yaku_han' in result:
        return int(result['yaku_han'])
    # Older cached results have only localized bonus entries and total han.
    bonus = 0
    for name in result.get('yaku', []):
        head, separator, count = name.partition('*')
        if head in BONUS_NAMES:
            bonus += int(count) if separator else 1
    return max(0, int(result.get('han', 0)) - bonus)


def meets_win_requirements(result, minimum_han=1, minimum_score=0):
    return (result.get('is_valid', True) and not result.get('no_yaku')
            and yaku_han(result) >= max(1, int(minimum_han))
            and int(result.get('standard_ron_score', 0)) >= int(minimum_score))
