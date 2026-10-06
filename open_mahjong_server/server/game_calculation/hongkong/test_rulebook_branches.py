"""Rare public-table patterns and independent broadcast ledger examples."""
from dataclasses import replace

import pytest

from .models import HandContext, HongKongRules, PROFILES
from .scoring import score_hand
from .ledger import Debt, PullLedger
from .old_style import old_payments
from .solver import _key, _partitions
from .test_hongkong_calculation import tiles

OLD=HongKongRules()
NEW13=HongKongRules(PROFILES[1])
NEW16=HongKongRules(PROFILES[2])


@pytest.mark.parametrize('notation,fan,value',[
    ('123234345456m 789s 55p','pure_steps_4',120),
    ('123234345456567m 99p','pure_steps_5',400),
    ('123345567789m 456p 11z','pure_jumps_4',100),
    ('123m 234p 345s 456m 789p 11z','mixed_steps_4',30),
    ('123m 234p 345s 456m 567p 11z','mixed_steps_5',100),
    ('111222333444555m 22p','linked_pungs_5',240),
    ('111m 222p 333s 444m 555p 22s','mixed_linked_pungs_5',180),
    ('11122233344455566m','little_linked_pungs_6',480),
    ('111m 222p 333s 444m 555p 66s','little_mixed_pungs_6',260),
    ('111999m 111999p 234s 55z','double_terminal_pungs',30),
    ('111222m 555666p 789s 33z','double_linked_pungs',20),
    ('111m 111p 888p 888s 234m 55z','double_same_pungs',20),
    ('111m 111p 11s 234m 456p 678s','little_same_pungs_3',20),
])
def test_sixteen_rare_local_table_patterns(notation,fan,value):
    hand=tiles(notation)
    result=score_hand(HandContext(hand,winning_tile=hand[-1],self_draw=True),NEW16)
    assert result.is_win
    assert any(f.id==fan and f.value==value for f in result.fans), result.fans
    if fan in ('pure_steps_5','mixed_steps_5'):
        assert 'all_chows' not in result.fan_ids
    if fan in ('linked_pungs_5','mixed_linked_pungs_5','little_linked_pungs_6','little_mixed_pungs_6'):
        assert 'all_pungs' not in result.fan_ids
    if fan=='little_linked_pungs_6':
        assert 'full_flush' not in result.fan_ids


@pytest.mark.parametrize('ready,fan,value',[('heaven','heaven_ready',8),('earth','earth_ready',4)])
def test_thirteen_special_early_ready(ready,fan,value):
    result=score_hand(HandContext(tiles('123m 234p 456789s 22z'),winning_tile=42,ready=ready),NEW13)
    assert any(f.id==fan and f.value==value for f in result.fans)
    assert 'ready' not in result.fan_ids


@pytest.mark.parametrize('notation,fan,value',[
    ('111222333m 456p 77z','linked_1',2),
    ('112233m 445566p 77z','double_identical',10),
])
def test_thirteen_local_table_patterns(notation,fan,value):
    hand=tiles(notation)
    result=score_hand(HandContext(hand,winning_tile=hand[-1],self_draw=True),NEW13)
    assert any(f.id==fan and f.value==value for f in result.fans)


def test_classical_flower_input_validation_and_last_live_tile():
    ctx=HandContext(tiles('123456789m 123p 22z'),winning_tile=42,self_draw=True,last_tile=True)
    for flowers in ((51,51),(50,),(59,)):
        assert not score_hand(replace(ctx,flowers=flowers),HongKongRules(flowers=True)).is_win
    assert not score_hand(replace(ctx,flowers=(51,)),OLD).is_win
    assert not score_hand(replace(ctx,flower_win='eight',flowers=tuple(range(51,58))),HongKongRules(flowers=True)).is_win
    assert 'last_tile' in score_hand(ctx,OLD).fan_ids


def test_hkma_2026_final_observed_third_pull_77_plus_28_is_144():
    # Official HKMA broadcast PuMshceSMrE: 11:37 ledger 77; 14:43 win 28;
    # 15:00 third mouth shown as +144/-144. This tests the publicly observed
    # ledger transition, not uncertain tile/flower transcription from video.
    ledger=PullLedger()
    ledger.debts[(0,1)]=Debt(0,1,77,2)
    assert ledger.apply_win({(0,1):28},winners=[0])==[0,0,0,0]
    assert ledger.snapshot()==[dict(creditor=0,debtor=1,points=144,mouths=3)]
    assert ledger.cut(1,0)==[144,-144,0,0]
    assert ledger.snapshot()==[]


def test_hkma_2026_classical_final_first_hand_observed_self_draw_payment():
    # HKMA e6kma2VH0PM, first win 12:26; next-hand HUD 13:09:
    # three seats 1952, winner 2144, from 2000 each. This independently
    # checks the five-fan self-draw payment, without guessing obscured tiles.
    assert old_payments(5, 3) == [-48, -48, -48, 144]
    assert [2000 + n for n in old_payments(5, 3)] == [1952, 1952, 1952, 2144]


@pytest.mark.parametrize('self_draw,fan,value', [(False,'begging',3), (True,'begging',2)])
def test_thirteen_four_called_pungs_and_begging(self_draw,fan,value):
    result=score_hand(HandContext(tiles('77z'),('k11','k24','k35','k46'),47,self_draw),NEW13)
    assert any(f.id==fan and f.value==value for f in result.fans)
    assert 'four_called_pungs' in result.fan_ids


@pytest.mark.parametrize('melds,fan_value',[(('G11','g24'),1),(('G11','g24','G35'),10),(('G11','g24','G35','g46'),40)])
def test_thirteen_two_three_four_kongs(melds,fan_value):
    missing=[(31,32,33),(45,45,45)][max(0,len(melds)-2):]
    hand=tuple(t for group in missing for t in group)+(47,47)
    result=score_hand(HandContext(hand,melds,47,True),NEW13)
    assert result.is_win
    assert any(f.id=='kongs' and f.value==fan_value for f in result.fans)


@pytest.mark.parametrize('context,fan,value,excluded',[
    ({'replacement':'flower','tail_draws':1},'replacement',2,{'self_draw'}),
    ({'replacement':'kong','tail_draws':1},'replacement',5,{'self_draw'}),
    ({'replacement':'flower','tail_draws':2},'tail_chain',10,{'self_draw','replacement'}),
    ({'replacement':'kong','tail_draws':3},'tail_chain',20,{'self_draw','replacement'}),
    ({'heavenly':True},'heavenly',120,{'self_draw','concealed_self_draw','concealed'}),
    ({'earthly':True},'earthly',120,{'concealed'}),
    ({'humanly':True},'humanly',80,{'concealed'}),
])
def test_sixteen_win_circumstance_values_and_explicit_exclusions(context,fan,value,excluded):
    ctx=HandContext(tiles('123m 456p 789s 222m 555p 77z'),winning_tile=47,self_draw=True,**context)
    result=score_hand(ctx,NEW16)
    assert any(f.id==fan and f.value==value for f in result.fans)
    assert not excluded & set(result.fan_ids)


@pytest.mark.parametrize('self_draw,fan,value',[(False,'all_begging',40),(True,'half_begging',20)])
def test_sixteen_five_called_melds_begging_excludes_single_and_self(self_draw,fan,value):
    ctx=HandContext(tiles('77z'),('s12','s25','s38','k22','k35'),47,self_draw)
    result=score_hand(ctx,NEW16)
    assert any(f.id==fan and f.value==value for f in result.fans)
    assert not {'single_wait','self_draw'} & set(result.fan_ids)


@pytest.mark.parametrize('hand,melds,fan,value',[
    ('111222333z 44z 123m 456p',(),'little_winds',120),
    ('222m 333p 444s 555z 66z',('G11',),'concealed_pungs',180),
    ('112233m 112233p 123s 77z',(),'same_rank_chows',60),
    ('123m 123p 789p 789s 567m 22z',(),'double_mixed_chows',10),
    ('123789m 123789p 555s 22z',(),'double_terminal_chows',15),
    ('111m 111p 111s 456m 789p 77z',(),'same_pungs_3',40),
    ('123789m 111p 999s 123p 99m',(),'pure_outside',80),
])
def test_sixteen_remaining_public_patterns(hand,melds,fan,value):
    parsed=tiles(hand)
    result=score_hand(HandContext(parsed,melds,parsed[-1],True),NEW16)
    assert result.is_win
    assert any(f.id==fan and f.value==value for f in result.fans),result.fans
    if fan=='concealed_pungs': assert 'all_pungs' not in result.fan_ids
    if fan=='same_rank_chows': assert 'all_chows' not in result.fan_ids
    if fan=='pure_outside': assert not {'missing_five','no_honors'} & set(result.fan_ids)


def test_eight_flowers_can_continue_and_score_on_later_ordinary_hu():
    ctx=HandContext(tiles('123m 456p 789s 222m 555p 77z'),winning_tile=47,self_draw=True,flowers=tuple(range(51,59)))
    result=score_hand(ctx,NEW16)
    assert any(f.id=='eight_flowers' and f.value==40 for f in result.fans)
    assert not any(f.id.startswith(('flower_set_','flower_','seat_flower_')) for f in result.fans)


def test_classical_single_kong_replacement_does_not_award_consecutive_kongs():
    ctx=HandContext(tiles('123456789m 123p 22z'),winning_tile=42,self_draw=True,replacement='kong',consecutive_kongs=1)
    result=score_hand(ctx,OLD)
    assert 'kong_draw' in result.fan_ids and 'consecutive_kongs' not in result.fan_ids


def test_partition_primitive_rejects_inconsistent_remaining_meld_count():
    assert _partitions(_key({11: 1}),1)==()
    assert _partitions(_key({11: 3}),0)==()
