"""New MCR clause fixtures plus an independent structural oracle for 17 tiles."""
from collections import Counter
from functools import lru_cache
from itertools import combinations
import random

import pytest

from . import guobiao_sixteen as rule
from .guobiao_hepai_check import Chinese_Hepai_Check


def tiles(text):
    """Fixture notation: 123m456p789s123z (中白发 are 567z)."""
    result, digits = [], ""
    for char in text.replace(" ", ""):
        if char.isdigit():
            digits += char
        else:
            result.extend(10 * ("mpsz".index(char) + 1) + int(n) for n in digits)
            digits = ""
    assert not digits
    return result


# Hand-written witnesses from the book's definitions, with a fifth group.
# Presence and exclusion assertions do not reproduce the implementation.
FAN_CASES = [
    ("111222333444z123m55z", [], 45, ["点和"], "大四喜 四暗刻 混一色 全带幺 门前清 单钓将", "圈风刻 门风刻 幺九刻 碰碰和", 165),
    ("111222555666777z33z", [], 43, ["点和"], "大三元 字一色 四暗刻 门前清 单钓将", "幺九刻 碰碰和 全带幺", 219),
    ("22233344466688s777z", [], 38, ["自摸"], "绿一色", "混一色", None),
    ("234m55z", ["G11", "G21", "G31", "G41"], 45, ["自摸"], "四杠 四暗刻", "碰碰和 单钓将 三杠 暗杠 双暗杠", None),
    ("66z", ["G11", "G21", "G31", "G41", "G45"], 46, ["自摸"], "四杠 四暗刻", "三杠 暗杠 双暗杠", None),
    ("111999m111999p11199s", [], 39, ["点和"], "清幺九 四暗刻 三同刻", "碰碰和 全带幺 幺九刻 无字 双同刻", 147),
    ("111222333z123m456p44z", [], 44, ["点和"], "小四喜 三暗刻", "三风刻 幺九刻", None),
    ("555666z123m456p789s77z", [], 47, ["点和"], "小三元 双暗刻", "双箭刻 幺九刻", None),
    ("222m444p666s111z55z", ["s12"], 45, ["点和"], "四暗刻", "门前清 不求人 碰碰和", None),
    ("123123123123m789p55z", [], 45, ["自摸"], "一色四同顺", "四归一 一般高 连六 喜相逢 老少副", None),
    ("111222333444m789p55z", [], 45, ["点和"], "一色四节高", "一色三节高 碰碰和", None),
    ("123234345456m678p55s", [], 35, ["点和"], "一色四步高", "一色三步高 连六 一般高", None),
    ("234m456p55z", ["G11", "g21", "G31"], 45, ["点和"], "三杠", "四杠 明杠 暗杠 双明杠 双暗杠 明暗杠", None),
    ("111999m111p111s111z66z", [], 46, ["点和"], "混幺九 四暗刻 三同刻 五门齐", "碰碰和 全带幺 幺九刻", None),
    ("222444m222444p66688s", [], 38, ["自摸"], "全双刻 四暗刻", "断幺 无字 碰碰和", None),
    ("123456789111444m55m", [], 15, ["点和"], "清一色 清龙", "无字", None),
    ("123123m456p789s55z", ["s12"], 45, ["点和"], "一色三同顺", "一色四同顺", None),
    ("111222333m456p789s55z", [], 45, ["点和"], "一色三节高", "一色四节高", None),
    ("777888m777888p99977s", [], 37, ["自摸"], "全大", "大于五 无字", None),
    ("444555m444555p66644s", [], 34, ["自摸"], "全中", "断幺 无字", None),
    ("111222m111222p33311s", [], 31, ["自摸"], "全小", "小于五 无字", None),
    ("123345567m234p678s55z", [], 45, ["点和"], "一色三步高", "一色四步高", None),
    ("345m456m345p456p555s55p", [], 25, ["点和"], "全带五", "断幺 无字", None),
    ("666m666p666s123m789p55z", [], 45, ["点和"], "三同刻 三暗刻", "双同刻", None),
    ("147m258p369s111z555z66z", [], 46, ["自摸"], "组合龙 双暗刻 五门齐 箭刻 幺九刻 不求人 单钓将", "平和", 28),
    ("147m258p369s123m456p77s", [], 37, ["点和"], "组合龙 平和", "无字", None),
    ("666777m666p777s678p99s", [], 39, ["点和"], "大于五", "全大 无字", None),
    ("111222m111p222s234p44s", [], 34, ["点和"], "小于五", "全小 无字", None),
    ("111222333z456m678p55s", [], 35, ["点和"], "三风刻", "小四喜 幺九刻", None),
    ("123m456p789s234p678m55z", [], 45, ["点和"], "花龙", "清龙", None),
    ("123p456s888p999s666z88s", [], 38, ["点和"], "推不倒", "缺一门", None),
    ("234m234p234s678m789p55z", [], 45, ["点和"], "三色三同顺", "一色三同顺", None),
    ("222m333p444s789m123p55z", [], 45, ["点和"], "三色三节高", "一色三节高", None),
    ("234m345p456s789p111m55z", [], 45, ["点和"], "三色三步高", "三色三同顺", None),
    ("22p", ["k11", "k23", "k35", "k47", "k44"], 22, ["点和"], "碰碰和 全求人", "单钓将", None),
    ("234m456p789s55z", ["G11", "G21"], 45, ["自摸"], "双暗杠", "双暗刻 暗杠", None),
    ("555666z234m456p789s11m", [], 11, ["点和"], "双箭刻", "小三元 幺九刻", None),
    ("234m456p789s55z", ["g11", "g21"], 45, ["点和"], "双明杠", "明杠", None),
    ("234m456p789s55z", ["g11", "G21"], 45, ["点和"], "明暗杠", "双明杠 双暗杠 暗杠 明杠", None),
    ("234m456p789s222p55z", ["G11"], 45, ["点和"], "暗杠 双暗刻", "明杠", None),
    ("234m456p789s222p55z", ["g11"], 45, ["点和"], "明杠", "暗杠", None),
    ("123456m234567p345s66s", [], 36, ["自摸"], "平和 连六 不求人", "无字 自摸", None),
    ("111123m444p678s789p55z", [], 45, ["点和"], "四归一 双暗刻", "四杠", None),
    ("222m222p444s123m789s55z", [], 45, ["点和"], "双同刻", "三同刻", None),
    ("234345m456567p678s55s", [], 35, ["点和"], "断幺", "无字", None),
    ("123123m456p678p789s55z", [], 45, ["点和"], "一般高", "一色三同顺", None),
    ("123m123p456p678s789m55z", [], 45, ["点和"], "喜相逢", "三色三同顺", None),
    ("123789m345p678s555z44z", [], 44, ["点和"], "老少副", "清龙", None),
    ("123234m456567m789p55p", [], 25, ["点和"], "缺一门", "清一色 混一色", None),
]


@pytest.mark.parametrize("text,melds,win,way,present,absent,total", FAN_CASES)
def test_source_fan_witnesses(text, melds, win, way, present, absent, total):
    hand = tiles(text)
    assert len(hand) + 3 * len(melds) == 17
    score, labels = rule.hepai_check(hand, melds, way, win)
    names = {s.split("*")[0] for s in labels}
    assert set(present.split()) <= names, labels
    assert not set(absent.split()) & names, labels
    if total is not None:
        assert score == total, labels


@lru_cache(maxsize=None)
def oracle_melds(hand):
    if not hand:
        return True
    tile = hand[0]
    counts = Counter(hand)
    choices = []
    if counts[tile] >= 3:
        choices.append((tile,) * 3)
    if tile < 40 and tile % 10 <= 7 and counts[tile + 1] and counts[tile + 2]:
        choices.append((tile, tile + 1, tile + 2))
    return any(oracle_melds(tuple(sorted((counts - Counter(group)).elements()))) for group in choices)


def oracle(hand, melds=()):
    counts = Counter(hand)
    for code in melds:
        face = int(code[1:])
        counts.update((face - 1, face, face + 1) if code[0] == "s" else [face] * (4 if code[0] in "gG" else 3))
    if len(hand) + 3 * len(melds) != 17 or any(t not in rule.TILES or n > 4 for t, n in counts.items()):
        return False
    hand = Counter(hand)
    remainders = [hand]
    if len(melds) <= 2:
        remainders += [hand - Counter(pattern) for pattern in rule.KNITTED if all(hand[t] for t in pattern)]
    for counts in remainders:
        for tile, count in counts.items():
            if count >= 2 and oracle_melds(tuple(sorted((counts - Counter({tile: 2})).elements()))):
                return True
    return False


def test_independent_oracle_and_all_draw_branches():
    rng = random.Random(20261014)
    shapes = [tuple([t] * 3) for t in rule.TILES] + [tuple(range(s * 10 + n, s * 10 + n + 3)) for s in (1, 2, 3) for n in range(1, 8)]
    samples = 0
    for _ in range(240):
        hand = sum((list(rng.choice(shapes)) for _ in range(5)), []) + [rng.choice(rule.TILES)] * 2
        if max(Counter(hand).values()) > 4:
            continue
        assert rule.can_win(hand) and oracle(hand)
        before = list(hand)
        before.pop(rng.randrange(17))
        expected = {t for t in rule.TILES if oracle(before + [t])}
        assert rule.waiting_tiles(before) == expected
        rng.shuffle(hand)
        score, _ = rule.hepai_check(hand, [], ["点和"], hand[-1])
        assert score > 0
        mutated = hand[:]
        mutated[rng.randrange(17)] = rng.choice(rule.TILES)
        assert rule.can_win(mutated) == oracle(mutated)
        samples += 1
    assert samples >= 130


@pytest.mark.parametrize("size", [0, 2, 5, 8, 11, 13, 14, 16, 18, 20])
def test_wrong_size_is_not_a_win(size):
    hand = (tiles("123m456p789s111z555z66z") * 2)[:size]
    assert not rule.can_win(hand)
    assert rule.hepai_check(hand, [], ["自摸"], 46) == (0, [])


@pytest.mark.parametrize("melds", [["s19"], ["k51"], ["K11"], ["bad"], ["g11"] * 6])
def test_malformed_external_melds(melds):
    assert not rule.can_win([11] * max(0, 17 - 3 * len(melds)), melds)


def test_all_six_knitted_patterns_and_two_exposed_melds():
    for pattern in rule.KNITTED:
        hand = list(pattern) + [41, 41]
        melds = ["k45", "k46"]
        assert rule.waiting_tiles(hand[:-1], melds) == {41}
        result = rule.hepai_decompose(hand, melds, ["点和"], 41)
        assert result and all(r["shape"] == "knitted" for r in result)
        assert "组合龙" in result[0]["fan_list"]
        assert "单钓将" in result[0]["fan_list"]
        result = rule.hepai_decompose(hand, melds, ["点和"], pattern[0])
        assert all("单钓将" not in r["fan_list"] for r in result)


def test_unique_wait_formal_fifth_copy_and_winning_assignment():
    # Same formal-copy condition as source p.20, with five-meld structure.
    hand = tiles("1111222s111m222p555z")
    assert rule.waiting_tiles(hand) == {33}
    assert rule._waiting(tuple(sorted(hand)), (), True) == {31, 33}
    assert "边张" not in rule.hepai_check(hand + [33], [], ["点和", "和单张"], 33)[1]
    # The winning 3m can close a pung or a chow; only the chow placement
    # preserves the third concealed pung. The best coherent placement wins.
    hand = tiles("111123m444p555s789p66z")
    choices = rule.hepai_decompose(hand, [], ["点和"], 11)
    assert len({r["score"] for r in choices}) > 1
    assert "三暗刻" in choices[0]["fan_list"]
    assert rule.hepai_check(hand, [], ["点和"], 11)[0] == choices[0]["score"]


@pytest.mark.parametrize("win,fragment,expected", [(13, "12m", "边张"), (12, "13m", "嵌张"), (45, "5z", "单钓将")])
def test_unique_wait_positive_and_opening_suppression(win, fragment, expected):
    hand = tiles(fragment + ("555z" if win != 45 else "123m") + ("222p444s111z66z" if win != 45 else "222p444s111z666z")) + [win]
    assert expected in rule.hepai_check(hand, [], ["点和"], win)[1]
    assert expected not in rule.hepai_check(hand, [], ["点和"], win, allow_single_wait=False)[1]
    assert expected not in rule.hepai_check(hand, [], ["自摸", "天和"], win)[1]


@pytest.mark.parametrize("way,present,absent", [
    (["自摸", "杠上开花"], "杠上开花", "自摸"),
    (["自摸", "last_deal"], "妙手回春", "自摸"),
    (["点和", "last_cut"], "海底捞月", "妙手回春"),
    (["点和", "抢杠和", "和绝张"], "抢杠和", "和绝张"),
    (["点和", "和绝张"], "和绝张", "抢杠和"),
    (["自摸", "天和"], "天和", "地和"),
    (["点和", "地和"], "地和", "人和"),
    (["自摸", "人和"], "人和", "天和"),
])
def test_context_fans(way, present, absent):
    hand = tiles("123m456p789s222m55z")
    score, labels = rule.hepai_check(hand, ["k44"], way + ["花牌"] * 8, 45)
    assert present in labels and absent not in labels and "花牌*8" in labels
    assert score == sum(Chinese_Hepai_Check.count_model_dict[k] * int(count or 1)
                        for label in labels for name, _, count in [label.partition("*")]
                        for k, v in Chinese_Hepai_Check.eng_to_chinese_dict.items() if name == v)


def test_two_chow_fans_use_groups_without_cycles():
    # Four related chows plus an unrelated fifth cannot form a four-edge cycle.
    result = rule._double_chows(tuple(enumerate((12, 22, 15, 25, 38))))
    assert sum(result.values()) == 3
    # A fifth related chow can contribute a fourth independent relation.
    result = rule._double_chows(tuple(enumerate((12, 22, 15, 25, 18))))
    assert sum(result.values()) == 4
    # A fixed triple can accept two further relations, never an internal edge.
    result = rule._double_chows(tuple(enumerate((12, 15, 18, 22, 28))), (0, 1, 2))
    assert sum(result.values()) == 2 and result["xixiangfeng"] == 2
    assert rule._double_chows(tuple(enumerate((12, 18, 22, 28))))["laoshaofu"] <= 1


def test_no_fourteen_tile_specials_or_subset_scoring():
    for text in ("11223344556677m", "19m19p19s1234567z1m", "1112345678999m5m"):
        hand = tiles(text)
        assert rule.hepai_check(hand, [], ["点和"], hand[-1]) == (0, [])
        grown = hand + tiles("222p")
        for entry in rule.hepai_decompose(grown, [], ["点和"], grown[-1]):
            assert not rule.FIXED_FOURTEEN & set(entry["fan_keys"])


def test_hints_are_structural_and_results_are_not_mutable_cache_values():
    hand = tiles("123m456p789s111z555z66z")
    before = hand[:-1]
    snapshot = rule.hint_snapshot(hand, [])
    assert snapshot["discard_waits"][46] == [46]
    assert rule.hint_snapshot(before, [])["waits"] == [46]
    assert rule.hint_snapshot([11], [])["waits"] == []
    assert rule.calculate_hint(hand, [], ["点和"], ["自摸"], 46, 8)["kind"] == "dianhe"
    assert rule.calculate_hint(hand, [], ["点和"], ["自摸"], 46, 64)["kind"] == "wuyi"
    labels = rule.hepai_check(hand, [], ["点和"], 46)[1]
    labels.clear()
    assert rule.hepai_check(hand, [], ["点和"], 46)[1]
    assert hand == tiles("123m456p789s111z555z66z")


def test_chicken_hand_and_flower_threshold():
    # Five complete groups, three suits and a wind pair, but no other fan.
    hand, melds = tiles("333p44z"), ["s13", "s25", "s38", "k17"]
    assert rule.hepai_check(hand, melds, ["点和"], 23) == (8, ["无番和"])
    assert rule.hepai_check(hand, melds, ["点和", "花牌"], 23) == (9, ["无番和", "花牌*1"])
    assert rule.calculate_hint(hand, melds, ["点和", "花牌"], ["自摸", "花牌"], 23, 9)["kind"] == "wuyi"


def test_tsumo_only_hint_and_wind_exclusion_overlap():
    hand = tiles("123m456p789s111z555z66z")
    ron = rule.hepai_check(hand, [], ["点和"], 46)[0]
    assert rule.calculate_hint(hand, [], ["点和"], ["自摸"], 46, ron + 1)["kind"] == "zimo"
    _, labels = rule.hepai_check(hand, [], ["点和", "场风东", "自风东"], 46)
    assert "圈风刻" in labels and "门风刻" in labels
    assert not any(f.startswith("幺九刻") for f in labels)
    # Only one actual pung is excluded, even when both wind fans name it.
    hand = tiles("111m456p789s111z555z66z")
    _, labels = rule.hepai_check(hand, [], ["点和", "场风东", "自风东"], 46)
    assert "幺九刻*1" in labels


def test_every_supported_fan_has_a_real_scored_witness():
    witnesses = [(tiles(t), m, way, win) for t, m, win, way, *_ in FAN_CASES]
    witnesses += [
        (tiles("123m456p789s222m55z"), ["k44"], ["自摸", "花牌"], 45),
        (tiles("123m456p789s222m55p"), ["k44"], ["自摸"], 25),
        (tiles("123m456p789s222m55p"), ["k44"], ["点和", "抢杠和", "和绝张"], 25),
        (tiles("333p44z"), ["s13", "s25", "s38", "k17"], ["点和"], 23),
        (tiles("123m456p789s222m55p"), ["k34"], ["点和"], 25),
        (tiles("123m555z222p444s111z66z"), [], ["点和", "场风东", "自风东"], 13),
        (tiles("123m555z222p444s111z66z"), [], ["点和"], 12),
    ]
    for context in ("天和", "地和", "人和", "last_deal", "last_cut", "杠上开花", "和绝张"):
        witnesses.append((tiles("123m456p789s111z555z66z"), [], ["自摸" if context in ("天和", "人和", "last_deal", "杠上开花") else "点和", context], 46))
    seen = {key for hand, melds, way, win in witnesses
            for row in rule.hepai_decompose(hand, melds, way, win) for key in row["fan_keys"]}
    assert seen == set(Chinese_Hepai_Check.count_model_dict) - rule.FIXED_FOURTEEN
