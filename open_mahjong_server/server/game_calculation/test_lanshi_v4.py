"""蓝十第4版原书条款/牌例回归。预期按规则书独立填写。"""
from collections import Counter
from itertools import combinations

import pytest


def test_zero_fan_complete_hand_is_distinct_from_an_invalid_shape():
    from .lanshi_v4 import evaluate, waiting_tiles
    hand = tiles('234456p678s5z')
    result = evaluate(hand + [45], ['s12'], ['点和'], 45)
    assert result and result[0]['score'] == 0 and result[0]['fan_list'] == []
    assert 45 in waiting_tiles(hand, ['s12'])


@pytest.mark.parametrize("open_mask", range(16))
@pytest.mark.parametrize("self_draw", [False, True])
def test_reported_sequences_survive_every_open_closed_arrangement(open_mask, self_draw):
    # The user's spaces may mark exposed groups. Sequence relations are the
    # same in all arrangements; only 门前清/全求人/自摸 change the total.
    tokens=[("s" if open_mask & (1 << i) else "S")+str(tile)
            for i,tile in enumerate((14,24,24,33))]
    result=evaluate(**fixture(" ".join(tokens)+" q11", ["自摸" if self_draw else "点和"], win=11))[0]
    expected=Counter(yibangao=1,xixiangfeng=1)
    if open_mask==0:expected['menqianqing']=1
    if open_mask==15 and not self_draw:expected['quanqiuren']=1
    if self_draw:expected['zimo']=1
    assert keys(result)==expected
    assert result['score']==3+int(open_mask==0)+int(self_draw)+2*int(open_mask==15 and not self_draw)

from .lanshi_v4 import FAN_VALUES, FAN_NAMES, evaluate, valid_input, relation_options, waiting_tiles, Meld


def tiles(text):
    values, digits = [], []
    for char in text.replace(" ", ""):
        if char.isdigit():
            digits.append(int(char))
        else:
            suit = {"m": 10, "p": 20, "s": 30, "z": 40}[char]
            values.extend(suit + rank for rank in digits)
            digits = []
    assert not digits
    return values


def fixture(tokens, way=("点和", "自风东", "场风西"), win=None):
    """用牌例面子表示测试输入，大小写分别表示闭手/副露。"""
    hand, fixed = [], []
    for token in tokens.split():
        kind, value = token[0], int(token[1:])
        if kind in "skgG":
            fixed.append(token)
        else:
            hand.extend([value - 1, value, value + 1] if kind == "S" else [value] * (2 if kind == "q" else 3))
    return dict(hand=hand, meld_tokens=fixed, way=list(way), win_tile=win if win is not None else hand[-1])


def keys(result):
    return Counter({key.split("*")[0]: int(key.split("*")[1]) if "*" in key else 1 for key in result["fan_keys"]})


# 每一个四人原生番种至少有一组合法输入；特殊牌形与偶然番另列。
# 页码定位番种原文（PDF页码），未标明副露/和张的牌例补上明确上下文。
FAN_CASES = [
    ("sitongshun", "s34 S34 S34 S34 q46", 26),
    ("sigang", "g22 g33 G39 g41 q46", 26),
    ("dasixi", "k41 k42 k43 k44 q47", 27),
    ("qingyaojiu", "k19 k21 k29 k31 q39", 27),
    ("sianke", "K12 K13 K14 K32 q31", 28),
    ("ziyise", "k43 k44 k45 k47 q41", 28),
    ("silianshun", "s22 S23 S24 S25 q46", 28),
    ("silianke", "k15 k16 k17 k18 q19", 28),
    ("xiaosixi", "s38 k41 k42 k43 q44", 29),
    ("sangang", "g22 g28 G34 S15 q36", 29),
    ("dasanyuan", "s17 k45 k46 k47 q29", 29),
    ("shunwang", "s12 S12 S22 S22 q33", 29),
    ("santongshun", "s34 S34 S34 k44 q31", 30),
    ("shunlian", "s12 S14 S16 S18 q43", 30),
    ("hunyaojiu", "k19 k21 k29 k42 q31", 30),
    ("quanda", "s18 S28 K27 K37 q39", 30),
    ("quanzhong", "k14 k15 k16 S25 q34", 31),
    ("quanxiao", "g23 K11 K12 S12 q33", 31),
    ("quandaiwu", "s14 S36 S26 S25 q35", 31),
    ("santongke", "k17 k27 k37 k44 q43", 31),
    ("xiaosanyuan", "s17 S34 k45 k47 q46", 31),
    ("sanfengke", "k11 k41 k42 k43 q45", 32),
    ("sananke", "g38 K28 K35 K44 q47", 32),
    ("sanlianke", "k24 k25 k26 k41 q46", 32),
    ("qingyise", "s23 S24 K25 S27 q26", 32),
    ("sanlianshun", "s23 S24 S25 k42 q45", 33),
    ("sanselianke", "s18 K38 k29 K17 q47", 33),
    ("qingquandaiyao", "k11 k29 k31 S38 q39", 33),
    ("shuanggang", "g22 G38 S12 K45 q46", 33),
    ("dayuwu", "s17 K17 K27 K38 q39", 33),
    ("xiaoyuwu", "s12 K21 S22 S12 q24", 33),
    ("shunhuan", "s12 S15 S22 S25 q35", 34),
    ("shuangjianke", "s17 K19 k45 k46 q43", 34),
    ("qinglong", "s12 S15 S18 K34 q39", 34),
    ("hualong", "s12 S12 S25 S38 q45", 35),
    ("sansetongshun", "s15 S25 S35 S38 q35", 36),
    ("pengpenghe", "k12 k24 k35 K41 q46", 36),
    ("hunquandaiyao", "k19 k21 k29 S38 q46", 36),
    ("hunyise", "s12 S15 K18 k41 q45", 36),
    ("sanselianshun", "s14 S36 S26 S25 q34", 36),
    ("angang", "G12 s24 S36 K45 q46", 36),
    ("shuanganke", "K12 k24 K35 s32 q46", 36),
    ("wumenqi", "s14 S28 K31 k47 q44", 37),
    ("shuangtongke", "K12 k22 s36 S15 q46", 36),
    ("quanqiuren", "s12 k23 s35 g46 q41", 36),
    ("siguiyi", "s23 S24 K25 S27 q26", 37),
    ("yibangao", "S14 S24 S24 S33 q11", 37),
    ("jianke", "s12 S24 S37 k45 q41", 37),
    ("quanfengke", "s12 S24 S37 k43 q45", 37),
    ("menfengke", "s12 S24 S37 k41 q45", 37),
    ("menqianqing", "S14 S24 S24 S33 q11", 37),
    ("minggang", "g12 s24 S36 K45 q46", 37),
    ("duanyao", "s14 S36 S26 S25 q34", 37),
    ("xixiangfeng", "S14 S24 S24 S33 q11", 38),
    ("lianliu", "s12 S15 S28 K31 q45", 38),
    ("laoshaofu", "s12 S18 S25 K32 q45", 38),
    ("yaojiuke", "s14 S28 K31 k47 q44", 38),
]


@pytest.mark.parametrize("fan,tokens,page", FAN_CASES, ids=[case[0] for case in FAN_CASES])
def test_each_regular_fan(fan, tokens, page):
    case = fixture(tokens)
    result = evaluate(**case)
    assert result, (fan, page, case)
    assert keys(result[0])[fan], (fan, page, result[0])


@pytest.mark.parametrize("text,fan,score,win", [
    ("11223344556677z", "qixingdui", 100, 47),
    ("11123456789995m", "jiulianbaodeng", 100, 15),
    ("19m19p19s12345677z", "shisanyao", 48, 47),
    ("147m258p36s123456z", "quanbukao", 16, 46),
    ("557788m2233p1199s", "qiduizi", 8, 39),
])
def test_special_shapes(text, fan, score, win):
    result = evaluate(tiles(text), [], ["点和"], win)
    assert result[0]["score"] == score
    assert result[0]["fan_list"] == [FAN_NAMES[fan]]


@pytest.mark.parametrize("fan", ("miaoshouhuichun", "haidilaoyue", "gangshangkaihua", "qiangganghe", "tianhe", "dihe"))
def test_occasional_floor_and_normal_score(fan):
    # A.12：副露的低分手牌，仅偶然番5分；高分手牌不叠加偶然番。
    weak = fixture("s12 s15 s28 K31 q45", way=[FAN_NAMES[fan]])
    strong = fixture("k38 S12 S15 S18 q28", way=[FAN_NAMES[fan]])
    assert evaluate(**weak)[0]["fan_list"] == [FAN_NAMES[fan]]
    higher = evaluate(**strong)[0]
    assert higher["score"] >= 6
    assert FAN_NAMES[fan] not in higher["fan_list"]


def test_wall_last_draw_book_examples():
    # PDF35：图中有明刻888s，不能把全部14张当门清再改低预期。
    assert evaluate(**fixture("k38 S12 S15 S18 q28", ["妙手回春"]))[0]["fan_list"] == ["清龙", "自摸"]
    assert evaluate(**fixture("k38 S12 S15 S34 q28", ["妙手回春"]))[0]["fan_list"] == ["妙手回春"]


def test_book_p32_flush_figure_has_a_five_dot_triplet():
    # PDF32高清验图：234p、345p、555p、678p、66p；不是456p。
    result = evaluate(**fixture("s23 S24 K25 S27 q26"))[0]
    assert result["score"] == 16
    assert result["fan_list"] == ["清一色", "四归一*1", "断幺", "连六*1"]


def test_book_p31_all_fives_exact_sequences():
    result = evaluate(**fixture("s14 S35 S26 S24 q35"))[0]
    assert result["score"] == 20
    assert result["fan_list"] == ["全带五", "三色连顺", "喜相逢*1"]


@pytest.mark.parametrize("way,score", [("点和", 4), ("自摸", 5), ("天和", 5), ("抢杠和", 6)])
def test_original_reported_hand(way, score):
    result = evaluate(tiles("11345m334455p234s"), [], [way], 34)[0]
    assert result["score"] == score
    assert keys(result)["yibangao"] == 1
    assert keys(result)["xixiangfeng"] == 1
    assert not keys(result)["sansetongshun"]


def test_three_colour_same_chows_can_add_identical_chow():
    result = evaluate(**fixture("s14 S24 S24 S34 q11"))[0]
    assert keys(result)["sansetongshun"] == 1
    assert keys(result)["yibangao"] == 1
    assert not keys(result)["xixiangfeng"]


@pytest.mark.parametrize("tokens,required,excluded", [
    ("s14 S36 S26 S25 q35", {"quandaiwu", "sanselianshun", "xixiangfeng"}, {"duanyao"}),
    ("s12 S12 S25 S38 q45", {"hualong", "yibangao"}, set()),
    ("s15 S25 S35 S38 q35", {"sansetongshun", "lianliu"}, {"xixiangfeng"}),
    ("s22 S22 S25 S25 q28", {"shunwang", "qingyise"}, {"qiduizi", "yibangao", "xixiangfeng", "lianliu", "laoshaofu"}),
    ("s12 S15 S22 S25 q35", {"shunhuan"}, {"xixiangfeng", "lianliu", "laoshaofu"}),
    ("s12 S14 S16 S18 q43", {"shunlian"}, {"laoshaofu"}),
    ("s12 S15 S18 K34 q39", {"qinglong"}, {"lianliu", "laoshaofu"}),
])
def test_book_sequence_combinations(tokens, required, excluded):
    result = evaluate(**fixture(tokens))[0]
    found = set(keys(result))
    assert required <= found
    assert not excluded & found


@pytest.mark.parametrize("text,fixed,win", [
    ("11111222333444m", [], 11),  # 五张同牌
    ("123456789m11122p", ["k41"], 22),  # 副露后闭手张数错误
    ("123456789m22p", ["s19"], 22),  # 跨色顺子
    ("123456789m22p", ["K41"], 22),  # 伪造已声明暗刻
    ("123456789m22p", ["q41"], 22),
    ("123456789m22p", ["bad"], 22),
    ("123456789m22p", ["s42"], 22),
    ("123456789m22p", ["g11"], 22),  # 杠的第四张仍须计入实体上限
    ("123456789m22p", ["G11"], 22),
    ("123456789m11122p", [], 47),  # 和张不在闭手
    ("123456789m11122p", [], 0),
    ("123456789m22p", [], 22),
    ("", [], 22),
])
def test_invalid_physical_hands(text, fixed, win):
    assert evaluate(tiles(text), fixed, ["自摸"], win) == []


def test_pair_specials_need_seven_distinct_pairs():
    for text in ("11112233445566m", "1122334455666z7m"):
        result = evaluate(tiles(text), [], ["点和"], tiles(text)[-1])
        assert all(not keys(r)["qiduizi"] and not keys(r)["qixingdui"] for r in result)


def test_win_component_and_declared_concealed_kong():
    ron_trip = evaluate(**fixture("G11 K22 K33 K44 q46", win=22))[0]
    ron_pair = evaluate(**fixture("G11 K22 K33 K44 q46", win=46))[0]
    assert keys(ron_trip)["sananke"] and not keys(ron_trip)["sianke"]
    assert keys(ron_pair)["sianke"] and not keys(ron_pair)["menqianqing"]
    assert "G11" in ron_trip["combinations"]
    assert "k22" in ron_trip["combinations"]
    assert "K22" in ron_pair["combinations"]


def test_all_wait_components_are_considered():
    # 222p+234p 对2p点和既可完成刻子，也可完成顺子，后者保留第三暗刻。
    results = evaluate(**fixture("K22 S23 K35 K47 q11", win=22))
    assert any("K22" in r["combinations"] for r in results)
    assert any("k22" in r["combinations"] for r in results)
    assert keys(results[0])["sananke"]


def test_nine_gates_requires_original_thirteen_unless_dealer_opening():
    hand = tiles("11123455678999m")
    assert not keys(evaluate(hand, [], ["自摸"], 11)[0])["jiulianbaodeng"]
    assert not keys(evaluate(hand, [], ["天和"], 11)[0])["jiulianbaodeng"]
    assert keys(evaluate(hand, [], ["自摸", "天和", "庄家起手"], 11)[0])["jiulianbaodeng"]


def test_double_wind_suppresses_only_its_own_terminal_triplet():
    result = evaluate(**fixture("k41 K31 S25 S16 q46", ["点和", "自风东", "场风东"]))[0]
    assert keys(result)["quanfengke"] == keys(result)["menfengke"] == keys(result)["yaojiuke"] == 1


def test_all_claimed_can_win_without_single_wait_hint():
    result = evaluate(**fixture("s12 k23 s35 g46 q41", ["点和"]))[0]
    assert keys(result)["quanqiuren"] == 1
    result = evaluate(**fixture("s12 k23 s35 G46 q41", ["点和"]))[0]
    assert keys(result)["quanqiuren"] == 0


def test_inputs_remain_unchanged_and_best_decompositions_unique():
    case = fixture("K22 S23 K35 K47 q11", win=22)
    before = (list(case["hand"]), list(case["meld_tokens"]), list(case["way"]))
    results = evaluate(**case)
    assert before == (case["hand"], case["meld_tokens"], case["way"])
    identity = [(r["shape"], tuple(r["combinations"]), tuple(r["fan_keys"])) for r in results]
    assert len(identity) == len(set(identity))
    assert [r["score"] for r in results] == sorted((r["score"] for r in results), reverse=True)


def test_relations_preserve_node_identity_and_never_cycle():
    groups = tuple(Meld("S", tile) for tile in (12, 22, 15, 35))
    options = relation_options(groups)
    best = max(options, key=lambda option: sum(FAN_VALUES[r.fan] for r in option))
    assert Counter(r.fan for r in best) == {"xixiangfeng": 2, "lianliu": 1}
    assert all(sum(len(r.nodes) - 1 for r in option) <= 3 for option in options)


def test_minimum_boundary_at_exactly_five_does_not_add_occasional():
    case = fixture("S14 S24 S24 S33 q11", ["自摸", "妙手回春"])
    result = evaluate(**case)[0]
    assert result["score"] == 5
    assert "妙手回春" not in result["fan_list"]


def test_all_four_player_fans_have_explicit_positive_cases():
    covered = {name for name, _, _ in FAN_CASES} | {
        "qixingdui", "jiulianbaodeng", "shisanyao", "quanbukao", "qiduizi",
        "miaoshouhuichun", "haidilaoyue", "gangshangkaihua", "qiangganghe", "tianhe", "dihe", "hejuezhang", "zimo",
    }
    assert covered == set(FAN_VALUES)


@pytest.mark.parametrize("tokens,excluded", [
    ("s12 S12 S24 S24 q46", {"shunwang", "shunhuan"}),
    ("s12 S15 S22 S26 q46", {"shunwang", "shunhuan"}),
    ("s12 S12 S12 K43 q45", {"sitongshun"}),
    ("s12 S14 S16 K43 q45", {"sanlianshun", "silianshun"}),
    ("k12 K14 K16 S25 q46", {"sanlianke", "silianke"}),
])
def test_similar_patterns_do_not_create_higher_relations(tokens, excluded):
    result = evaluate(**fixture(tokens))[0]
    assert not excluded & set(keys(result))


def test_full_straight_and_duplicated_sequence_use_global_maximum():
    # 清龙的789条与第四副789条构成一般高，不应贪心取连六。
    result = evaluate(**fixture("s32 S35 S38 S38 q24", ["自摸"]))[0]
    assert result["score"] == 9
    assert result["fan_list"] == ["清龙", "一般高*1", "自摸"]


@pytest.mark.parametrize("text,fixed,expected", [
    ("1112345678999m", [], set(range(11, 20))),
    ("19m19p19s1234567z", [], {11,19,21,29,31,39,41,42,43,44,45,46,47}),
    ("1122m3344p5566s7z", [], {47}),
    ("147m258p369s1234z", [], {45,46,47}),
    ("147m258p369s1112z", [], set()),  # 组合龙不属于蓝十牌形
    ("1111223344556m", [], {12,13,15,16}),  # 四张不能当七对中的两对
    ("1m", ["g11", "k22", "s35", "k43"], set()),  # 不能听第五张
    ("1m", ["G11", "k22", "s35", "k43"], set()),
    ("1m", ["g19", "k22", "s35", "k43"], {11}),
    ("11123456789999m", [], set()),
    ("123456789m1234p", ["bad"], set()),
])
def test_native_waiting_shapes(text, fixed, expected):
    assert waiting_tiles(tiles(text), fixed) == expected


def test_waiting_null_and_invalid_input():
    assert waiting_tiles(None, []) == set()
    assert waiting_tiles([], None) == set()
    assert waiting_tiles([10] * 13, []) == set()
    assert valid_input(tiles("123456789m11122p"), [], 22.0) is None
