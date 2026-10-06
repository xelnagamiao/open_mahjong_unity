"""Real-library three-player scoring, including bonus-only and limit hands."""
import pytest

from .riichi_hepai_check import Riichi_Hepai_Check
from .sanma import dora_indicator, player_count, starting_score, tsumo_payments

checker = Riichi_Hepai_Check()
HAND = [21,22,23,24,25,26,31,32,33,36,37,38,29,29]
KOKUSHI = [11,19,21,29,31,39,41,42,43,44,45,46,47,47]


def calc(hand=HAND, win=38, **options):
    ctx = dict(is_sanma=True, player_wind=1, round_wind=0, is_riichi=True)
    ctx.update(options)
    return checker.hepai_check(hand, [], [], win, ctx)


@pytest.mark.parametrize('sub,count,score', [('riichi/sanma',3,35000),('riichi/standard',4,25000),('riichi/langyong',4,50000)])
def test_subrule_constants(sub,count,score):
    assert player_count(sub)==count and starting_score(sub)==score


@pytest.mark.parametrize('indicator,tile', [(11,19),(19,11)])
def test_one_and_nine_manzu_indicator_cycle(indicator,tile):
    hand = [tile]*3 + [21,22,23,31,32,33,36,37,38,45,45]
    base = calc(hand,38)
    bonus = calc(hand,38,dora_indicators=[indicator])
    assert bonus['han']==base['han']+3 and '宝牌*3' in bonus['yaku']
    assert dora_indicator(indicator,False)==indicator


@pytest.mark.parametrize('nuki', range(5))
@pytest.mark.parametrize('riichi', [False,True])
@pytest.mark.parametrize('ura', [False,True])
def test_north_bonus_and_indicator_dora_stack_without_opening_hand(nuki,riichi,ura):
    base = calc(is_riichi=riichi,is_tsumo=True)
    result = calc(is_riichi=riichi,is_tsumo=True,nuki_count=nuki,dora_indicators=[43],ura_dora_indicators=[43],ura_dora=ura)
    assert result['is_valid'] and result['han']==base['han']+nuki*(2+int(riichi and ura))
    assert ('拔北宝牌*'+str(nuki) in result['yaku']) is bool(nuki)
    assert '门前清自摸和' in result['yaku']


def test_north_kept_in_hand_is_a_guest_wind_with_no_extraction_bonus():
    hand=[44]*3+[21,22,23,31,32,33,36,37,38,45,45]
    result=calc(hand,38,is_riichi=False)
    assert not any(y.startswith('役牌') or y.startswith('拔北') for y in result['yaku'])
    assert result.get('no_yaku')


def test_nuki_dora_never_grants_a_yaku():
    result=checker.hepai_check([21,22,23,31,32,33,36,37,38,29,29],['k44'],[],38,
        dict(is_sanma=True,player_wind=1,nuki_count=3,dora_indicators=[43]))
    assert result['is_valid'] and result['no_yaku'] and result['han']==result['score']==0


@pytest.mark.parametrize('removed', [12,13,14,15,16,17,18,105])
def test_removed_manzu_is_rejected(removed):
    assert not calc([removed]+HAND[1:])['is_valid']


@pytest.mark.parametrize('seat', range(3))
@pytest.mark.parametrize('mode', ['loss','split'])
@pytest.mark.parametrize('tsumo', [False,True])
def test_scoring_received_points_match_actual_payers(seat,mode,tsumo):
    result=calc(player_wind=seat,is_tsumo=tsumo,sanma_tsumo=mode)
    expected=sum(tsumo_payments(result['cost'],seat,3,mode).values()) if tsumo else result['cost']['main']
    assert result['score']==expected


@pytest.mark.parametrize('mode', ['loss','split'])
def test_true_yakuman_ignores_north_and_all_bonus_han(mode):
    result=calc(KOKUSHI,47,nuki_count=4,dora_indicators=[11,43],ura_dora_indicators=[43],
        double_yakuman=True,is_tsumo=True,sanma_tsumo=mode)
    assert result['han']==26 and result['yakuman_multiplier']==2
    assert not any('宝牌' in y for y in result['yaku'])
    assert result['score']==(48000 if mode=='loss' else 64000)


@pytest.mark.parametrize('limit,expected', [('yakuman',32000),('sanbaiman',24000),('unlimited',64000)])
def test_counted_yakuman_uses_north_bonus_before_limit(limit,expected):
    result=calc(nuki_count=4,dora_indicators=[37]*21,kazoe_limit=limit)
    assert result['han']==27 and result['score']==expected and result['yakuman_multiplier']==0


def test_split_rounding_and_four_player_payments():
    assert tsumo_payments(dict(main=700,additional=400),1,3,'split')=={0:900,2:600}
    assert tsumo_payments(dict(main=1300,additional=1300),0,3,'split')=={1:2000,2:2000}
    assert tsumo_payments(dict(main=700,additional=400),1,4)=={0:700,2:400,3:400}
