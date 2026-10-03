import pytest
from .riichi_hepai_check import Riichi_Hepai_Check

checker=Riichi_Hepai_Check()
PINFU=[12,13,14,22,23,24,33,34,35,36,37,38,28,28]
KOKUSHI=[11,19,21,29,31,39,41,42,43,44,45,46,47,47]
def calc(hand=PINFU,win=38,**kw):
    ctx=dict(player_wind=1,round_wind=0,is_riichi=True,**kw)
    return checker.hepai_check(hand,[],[],win,ctx)

def test_four_han_thirty_fu_kiriage():
    assert calc(dora_indicators=[37])['score']==7700
    assert calc(dora_indicators=[37],kiriage_mangan=True)['score']==8000

@pytest.mark.parametrize('limit,expected',[('yakuman',32000),('sanbaiman',24000),('unlimited',64000)])
def test_counted_yakuman_is_not_true_yakuman(limit,expected):
    result=calc(dora_indicators=[37]*24,kazoe_limit=limit)
    assert result['han']==27
    assert result['score']==expected
    assert result['yakuman_multiplier']==0
    assert result['fu']==30

@pytest.mark.parametrize('enabled,multiple',[(False,1),(True,2)])
def test_kokushi_thirteen_wait_double_switch(enabled,multiple):
    result=calc(KOKUSHI,47,double_yakuman=enabled)
    assert result['score']==32000*multiple
    assert result['yakuman_multiplier']==multiple

def test_double_wind_pair_fu_changes_rounding():
    hand=[11,11,11,22,23,24,34,35,36,36,37,38,41,41]
    for pair,fu in [(2,40),(4,50)]:
        result=checker.hepai_check(hand,[],[],38,dict(player_wind=0,round_wind=0,is_riichi=True,double_wind_pair_fu=pair))
        assert result['fu']==fu

def test_true_yakuman_has_no_bonus_han_and_can_disable_combination():
    hand=[41]*3+[42]*3+[43]*3+[45]*3+[47]*2
    for combine,multiple in [(True,3),(False,2)]:
        result=calc(hand,47,double_yakuman=True,multiple_yakuman=combine,dora_indicators=[46]*5)
        assert result['yakuman_multiplier']==multiple
        assert result['han']==13*multiple
        assert result['score']==32000*multiple
        assert not any('宝牌' in x for x in result['yaku'])

def test_ippatsu_ura_and_red_rules_are_consumed():
    base=calc(is_ippatsu=True,ura_dora_indicators=[27])
    disabled=calc(is_ippatsu=True,ippatsu=False,ura_dora=False,ura_dora_indicators=[27])
    assert base['han']-disabled['han']==3
    red=[305 if t==35 else t for t in PINFU]
    assert calc(red,red_dora=True)['han']==calc(red,red_dora=False)['han']+1

def test_invalid_fifth_copy_returns_invalid_not_exception():
    assert not calc([11]*5+[12,13,21,22,23,31,32,33,45],11)['is_valid']

def test_open_tanyao_cannot_win_with_dora_only():
    result=checker.hepai_check([22,23,24,33,34,35,36,37,38,28,28],['s13'],[],38,
        dict(player_wind=1,has_open_tanyao=False,dora_indicators=[37]))
    assert result['no_yaku'] and result['han']==0
