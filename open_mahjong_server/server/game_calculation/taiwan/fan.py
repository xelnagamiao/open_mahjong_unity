"""台湾麻将预设台表。"""

from types import MappingProxyType
from typing import Dict, Mapping, Tuple


FAN_DEFINITIONS: Mapping[str, Tuple[str, int]] = MappingProxyType({
    "concealed_hand": ("门清", 1),
    "fully_concealed_hand": ("不求人", 1),
    "self_draw": ("自摸", 1),
    "seat_wind_pung": ("门风刻", 1),
    "prevalent_wind_pung": ("圈风刻", 1),
    "flower_tile": ("正花", 1),
    "dragon_pung": ("三元牌", 1),
    "single_wait": ("独听", 1),
    "robbing_kong": ("抢杠", 1),
    "out_with_replacement_tile": ("杠上开花", 1),
    "last_tile_draw": ("海底捞月", 1),
    "flower_kong": ("花杠", 1),
    "no_flowers": ("无花", 1),
    "declared_ready": ("报听", 1),
    "half_begging": ("半求人", 1),
    "last_tile_claim": ("河底捞鱼", 1),
    "wind_pung": ("见字", 1),
    "melded_kong": ("明杠", 1),
    "all_chows": ("平胡", 2),
    "three_concealed_pungs": ("三暗刻", 2),
    "all_begging": ("全求人", 2),
    "no_flowers_or_honors": ("无字无花", 2),
    "concealed_kong": ("暗杠", 2),
    "all_pungs": ("碰碰胡", 4),
    "little_three_dragons": ("小三元", 4),
    "half_flush": ("混一色", 4),
    "initial_flower_bonus": ("配牌花胡", 4),
    "four_concealed_pungs": ("四暗刻", 5),
    "earthly_ready": ("地听", 8),
    "five_concealed_pungs": ("五暗刻", 8),
    "big_three_dragons": ("大三元", 8),
    "little_four_winds": ("小四喜", 8),
    "full_flush": ("清一色", 8),
    "eight_flowers_and_seasons": ("八仙过海", 8),
    "seven_flowers_steal_eighth": ("七抢一", 8),
    "eight_and_a_half_pairs": ("八对半", 8),
    "heavenly_ready": ("天听", 16),
    "earthly_win": ("地胡", 16),
    "human_win": ("人胡", 16),
    "all_honors": ("字一色", 16),
    "big_four_winds": ("大四喜", 16),
    "heavenly_win": ("天胡", 24),
})


def _preset_table(**changes: int) -> Dict[str, int]:
    table = {
        fan_id: default_tai
        for fan_id, (_, default_tai) in FAN_DEFINITIONS.items()
    }
    table.update(changes)
    return table


SCORING_PRESET_TABLES: Mapping[str, Mapping[str, int]] = MappingProxyType({
    "sml": MappingProxyType(_preset_table()),
    "cml": MappingProxyType(_preset_table()),
    "star31": MappingProxyType(_preset_table(
        flower_kong=2,
        heavenly_ready=8,
        earthly_ready=4,
        all_honors=8,
    )),
    "shenlaiye": MappingProxyType(_preset_table(
        flower_kong=2,
        human_win=8,
        earthly_ready=4,
        all_honors=8,
    )),
})


def preset_fan_tai(scoring_preset: str, fan_id: str) -> int:
    return SCORING_PRESET_TABLES[scoring_preset][fan_id]


def resolved_fan_tai(scoring_preset: str, fan_tai_overrides: Mapping[str, int], fan_id: str) -> int:
    return fan_tai_overrides.get(fan_id, preset_fan_tai(scoring_preset, fan_id))
