"""文档逐条计分矩阵；预期值直接来自规则表，不调用被测公式生成。"""
import pytest
from . import lajihe as rule

PLAIN = [11,12,13,21,22,23,31,32,33,17,18,19,41,41]
PUNGS = [11]*3 + [21]*3 + [31]*3 + [19]*3 + [41]*2
MIXED = [11,12,13,14,15,16,17,18,19,41,41,41,42,42]
PURE = [11,12,13,14,15,16,17,18,19,12,13,14,18,18]
PAIRS = [11]*4 + [14]*2 + [21]*2 + [25]*2 + [31]*2 + [47]*2
HONORS = [41]*2+[42]*2+[43]*2+[44]*2+[45]*2+[46]*2+[47]*2


@pytest.mark.parametrize("hand,name,ron,tsumo,extra_tsumo", [
    (PLAIN,"垃圾和",5,10,15), (PUNGS,"碰碰和",20,30,30),
    (MIXED,"混一色",25,30,30), (PURE,"清一色",25,30,30),
    (PAIRS,"七对子",20,30,30),
    ([11]*3+[14]*3+[41]*3+[45]*3+[19]*2,"混碰",30,30,30),
    ([11]*3+[14]*3+[17]*3+[19]*3+[18]*2,"清碰",40,30,30),
    (HONORS,"全风向",40,30,30),
    ([41]*3+[42]*3+[45]*3+[46]*3+[47]*2,"全风向碰碰和",120,30,30),
])
@pytest.mark.parametrize("extra_base", [False, True])
def test_all_nine_basic_patterns(hand,name,ron,tsumo,extra_tsumo,extra_base):
    for self_draw, rob_kong, expected in ((False,False,ron),
            (True,False,extra_tsumo if extra_base else tsumo),
            (False,True,extra_tsumo if extra_base else tsumo)):
        result = rule.score(hand, flowers=[51,52], self_draw=self_draw, rob_kong=rob_kong,
                            self_draw_extra_base=extra_base)
        assert result["pattern"] == name
        assert result["base_score"] == expected


@pytest.mark.parametrize("count,points", [(0,0),(1,0),(2,0),(3,5),(4,5),(5,10),(6,10),(7,15),(8,20),(9,25),(10,30),(15,55),(20,80)])
def test_flower_staircase(count,points):
    assert rule.flower_points(count) == points


@pytest.mark.parametrize("tile,kind,flowers", [(11,"k",0),(41,"k",1),(45,"k",1),(11,"g",1),(47,"g",2),(11,"G",2),(45,"G",3)])
def test_meld_flowers_count_all_seven_honors_equally(tile,kind,flowers):
    assert rule.physical_flowers([], [f"{kind}{tile}"], []) == flowers
    assert rule.physical_flowers([51], [], [f"{kind}{tile}"]) == flowers+1


@pytest.mark.parametrize("flowers", [0,1,2,3,4,5,6,7,8])
def test_garbage_ron_gate_and_rob_kong_self_draw(flowers):
    f = list(range(51,51+flowers))
    ron = rule.score(PLAIN, flowers=f)
    assert (ron is not None) == (flowers >= 2)
    tsumo = rule.score(PLAIN, flowers=f, self_draw=True)
    robbed = rule.score(PLAIN, flowers=f, rob_kong=True)
    assert tsumo["base_score"] == robbed["base_score"]
    assert robbed["extra_points"] == 5
    assert "抢杠（5分）" in robbed["fan_names"]
    assert "自摸（5分）" not in robbed["fan_names"]


@pytest.mark.parametrize("self_draw,rob,expected", [(False,False,20),(True,False,30),(False,True,30),(True,True,30)])
def test_seven_pairs_is_not_flush_or_pungs(self_draw,rob,expected):
    pure_pairs = [11]*4+[13]*2+[15]*2+[17]*2+[18]*2+[19]*2
    result=rule.score(pure_pairs,self_draw=self_draw,rob_kong=rob)
    assert result["pattern"] == "七对子" and result["base_score"] == expected
    assert result["physical_flowers"] == 0


def test_ambiguous_standard_and_pairs_take_highest_and_honors_need_no_shape():
    assert rule.score([11,11,12,12,13,13,14,14,15,15,16,16,17,17])["base_score"] == 25
    hand=[41]*4+[42]*3+[43]*3+[44]+[45]+[46]+[47]
    value=rule.score(hand)
    assert value["pattern"] == "全风向" and value["base_score"] == 50
    assert rule.waits(HONORS[:-1]) == set(rule.HONORS)


@pytest.mark.parametrize("kong", ["k","g","G"])
@pytest.mark.parametrize("self_draw,replacement,rob", [(False,False,False),(True,False,False),(True,True,False),(False,False,True)])
@pytest.mark.parametrize("extra_base", [False, True])
def test_big_single_wait_includes_concealed_kong_and_extras_stack(kong,self_draw,replacement,rob,extra_base):
    result=rule.score([41,41],["s12","s22","s32",f"{kong}19"], [51,52],
                      self_draw=self_draw,replacement=replacement,rob_kong=rob,self_draw_extra_base=extra_base)
    extra=5+((10 if extra_base else 5) if self_draw or rob else 0)+(5 if self_draw and replacement else 0)
    assert result["extra_points"] == extra
    assert "大吊车" in result["fan_names"]
    assert ("杠头开花" in result["fan_names"]) == replacement
    assert result["base_score"] == 5+extra+(0 if kong=="k" else 5)


def test_extra_base_does_not_relax_ron_gate_or_double_count_rob_kong():
    assert rule.score(PLAIN, self_draw_extra_base=True) is None
    assert rule.score(PLAIN, self_draw=True)["base_score"] == 10
    result = rule.score(PLAIN, self_draw=True, rob_kong=True, replacement=True, self_draw_extra_base=True)
    assert result["base_score"] == 15
    assert "抢杠（10分）" in result["fan_names"]
    assert not any(name.startswith("自摸") or name == "杠头开花" for name in result["fan_names"])


@pytest.mark.parametrize("hand,melds", [(PLAIN[:-1],()),([11]*5+PLAIN[5:],()),(PLAIN[:-1]+[51],()),
                                        (PLAIN,("k11",)),([11,11],["s19"]*4),([11,11],["k41","k42","k43","bad"])])
def test_invalid_tiles_counts_and_melds_cannot_win(hand,melds):
    assert rule.score(hand,melds,self_draw=True) is None


@pytest.mark.parametrize("winner",range(4))
@pytest.mark.parametrize("source",["self_draw","discard","robbing_kong"])
def test_payments_are_zero_sum_and_payer_specific(winner,source):
    payer=(winner+1)%4
    changes=rule.payments(winner,15,discarder=None if source=="self_draw" else payer,rob_kong=source=="robbing_kong")
    assert sum(changes.values()) == 0
    assert changes[winner] == (15 if source=="discard" else 45)
    assert changes[payer] == (-45 if source=="robbing_kong" else -15)
