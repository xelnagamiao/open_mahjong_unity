"""金锣赛和牌资格。仅决定能否和牌，不改变国标番表、支付或错和处罚。

规则来源：https://jmcr.cc.cd/ 规则标签；天气由建房者选择并在整场对局中固定。
"""
from collections import Counter
from itertools import combinations

from .guobiao_hepai_check import Chinese_Hepai_Check

WEATHERS = {
    "dragon_pairs": "箭牌对子×2",
    "seat_round_winds": "门风+场风",
    "terminal_pairs": "数牌幺九对×3",
    "all_simples": "断幺",
    "missing_suit": "无字+缺门",
    "consecutive_pairs": "连对：两个及以上同花色相邻数牌对子",
    "same_rank_pairs": "同对：两个及以上相同序数数牌对子",
    "four_of_a_kind": "四归一：有四张相同牌的七对子",
}
DEFAULT_WEATHER = "dragon_pairs"
REDUCED_FANS = {"海底捞月", "妙手回春", "杠上开花", "双暗杠", "明暗杠"}
UNRESTRICTED_FANS = {"天和", "地和", "人和", "人和I", "人和II", "人和Ⅰ", "人和Ⅱ", "三色双龙会", "三同刻", "全带五"}
LOW_FANS = {
    name for key, name in Chinese_Hepai_Check.eng_to_chinese_dict.items()
    if Chinese_Hepai_Check.count_model_dict.get(key, 99) <= 2
}


def enabled(state):
    return getattr(state, "sub_rule", "guobiao/standard") == "guobiao/standard" and bool(getattr(state, "jinluo_mode", False))


def validate_room_config(sub_rule, mode, weather):
    if mode and sub_rule != "guobiao/standard":
        raise ValueError("金锣赛仅适用于标准国标")
    if weather not in WEATHERS:
        raise ValueError("请选择有效的金锣赛七对天气")


def fan_counts(fans):
    counts = Counter()
    for fan in fans:
        name, separator, count = fan.partition("*")
        counts[name] += int(count) if separator else 1
    return counts


def pair_tiles(hand, melds):
    counts = Counter(hand)
    if melds or len(hand) != 14 or any(count % 2 for count in counts.values()):
        return []
    return sorted(tile for tile, count in counts.items() for _ in range(count // 2))


def matches_weather(pairs, weather, seat_wind=41, round_wind=41):
    if len(pairs) != 7:
        return False
    numbered = [tile for tile in pairs if tile < 40]
    if weather == "dragon_pairs":
        return sum(tile in (45, 46, 47) for tile in pairs) >= 2
    if weather == "seat_round_winds":
        # 连风时同一对同时满足门风和场风。
        return seat_wind in pairs and round_wind in pairs
    if weather == "terminal_pairs":
        return sum(tile % 10 in (1, 9) for tile in numbered) >= 3
    if weather == "all_simples":
        return len(numbered) == 7 and all(2 <= tile % 10 <= 8 for tile in numbered)
    if weather == "missing_suit":
        return len(numbered) == 7 and len({tile // 10 for tile in numbered}) <= 2
    if weather == "consecutive_pairs":
        return any(tile % 10 < 9 and tile + 1 in numbered for tile in numbered)
    if weather == "same_rank_pairs":
        return any(a != b and a % 10 == b % 10 for a, b in combinations(numbered, 2))
    if weather == "four_of_a_kind":
        return len(set(pairs)) < 7
    return False


def _run(pairs):
    return pairs[0] < 40 and pairs[0] // 10 == pairs[-1] // 10 and list(pairs) == list(range(pairs[0], pairs[0] + 3))


def special_seven_pairs(pairs):
    if len(pairs) != 7:
        return False
    numbered = all(tile < 40 for tile in pairs)
    ranks = {tile % 10 for tile in pairs}
    if numbered and (len(ranks) in (2, 3) or ranks <= {2, 4, 6, 8}):
        return True
    if numbered and len(ranks) == 7 and max(ranks) - min(ranks) == 6:
        return True  # 杂连七对：七种连续序数各一对，花色不限。
    if {45, 46, 47} <= set(pairs) or {41, 42, 43, 44} <= set(pairs):
        return True
    for indices in combinations(range(7), 3):
        if _run([pairs[index] for index in indices]):
            rest = [tile for index, tile in enumerate(pairs) if index not in indices]
            if any(_run(second) for second in combinations(rest, 3)):
                return True  # 两杯口：两组同花色连续三对，余一对作将。
    return False


def is_allowed(result, hand, melds, weather=DEFAULT_WEATHER, *, seat_wind=41, round_wind=41):
    """唯一的金锣赛和牌限制判定；传入标准国标计算结果和完整和牌手牌。"""
    score, fans = result
    counts = fan_counts(fans)
    core = score - counts["花牌"]
    if not fans or core < 8:
        return False
    if counts["花牌"] >= 6 or UNRESTRICTED_FANS.intersection(counts):
        return True
    if counts["碰碰和"]:
        tiles = list(hand) + [int(meld[1:]) for meld in melds]
        if all(tile < 40 for tile in tiles) and len({tile % 10 for tile in tiles}) == 2:
            return True  # 清二数。
    if counts["七对"] or counts["连七对"]:
        pairs = pair_tiles(hand, melds)
        return core >= 28 or special_seven_pairs(pairs) or matches_weather(pairs, weather, seat_wind, round_wind)
    if core >= 24 or (core >= 16 and REDUCED_FANS.intersection(counts)):
        return True
    return set(counts) <= LOW_FANS and not (counts["门前清"] and counts["平和"])


def allows_state_win(state, result, hand, melds, player_index):
    return is_allowed(result, hand, melds, state.jinluo_weather,
                      seat_wind=41 + player_index, round_wind=41 + (state.current_round - 1) // 4)


def hepai_check(state, hand, melds, way, tile, player_index):
    candidates = Chinese_Hepai_Check().hepai_decompose(hand, melds, way, tile)
    for item in candidates:
        result = (item["score"], item["fan_list"])
        if allows_state_win(state, result, hand, melds, player_index):
            return result
    # 没有合规拆分时仍保留最高番结果，让已有错和流程展示真实牌型。
    return (candidates[0]["score"], candidates[0]["fan_list"]) if candidates else (0, [])
