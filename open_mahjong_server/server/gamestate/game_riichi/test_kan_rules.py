import asyncio
from unittest.mock import AsyncMock

import pytest

from .test_rule_branches import game
from .action_check import refresh_waiting_tiles
from .kan_actions import begin_kan, commit_kan
from .wait_action import _broadcast_hu_and_end
from .boardcast import broadcast_ask_other_action
from .rule_logic import ankan_allowed


def setup_kakan():
    g=game('majsoul');p=g.player_list[0]
    p.hand_tiles=[11,12,13,21,22,23,31,32,33,45,305]
    p.combination_tiles=['k35'];p.combination_mask=[[0,35,1,35,0,35]]
    p.has_draw_slot=True
    ron=g.player_list[1]
    ron.hand_tiles=[12,13,14,22,23,24,33,34,36,37,38,28,28]
    ron.tag_list=['riichi','ippatsu']
    refresh_waiting_tiles(g,1)
    return g


def test_robbing_added_red_five_preserves_pon_and_ippatsu():
    async def run():
        g=setup_kakan();p=g.player_list[0]
        await begin_kan(g,'jiagang',305)
        assert g.game_status=='waiting_action_qianggang'
        assert p.combination_tiles==['k35'] and 305 in p.hand_tiles
        assert g.jiagang_tile==305
        result=next(r for r in g.result_dict.values() if r.get('is_valid'))
        assert result['aka_count']==1 and '一发' in result['yaku'] and '枪杠' in result['yaku']
        # The declaration may be the caller's first action; an empty river is valid.
        await broadcast_ask_other_action(g)
        await _broadcast_hu_and_end(g,1,g.action_dict[1][0],305)
        assert p.combination_tiles==['k35'] and 305 not in p.hand_tiles
        assert g.player_list[1].hand_tiles[-1]==305
        assert 'ippatsu' in g.player_list[1].tag_list
        assert g.total_kans==0 and not g.kan_dora_indicators
        assert g.game_record['game_round']['round_index_1']['action_ticks'][-1][:3]==['rk',0,305]
    asyncio.run(run())


def test_successful_kakan_cancels_ippatsu_and_removes_exact_red_tile():
    async def run():
        g=setup_kakan();await begin_kan(g,'jiagang',305)
        await commit_kan(g)
        assert g.player_list[0].combination_tiles==['g35']
        assert 305 in g.player_list[0].combination_mask[0]
        assert 305 not in g.player_list[0].hand_tiles
        assert 'ippatsu' not in g.player_list[1].tag_list
        assert g.game_status=='deal_card_after_gang' and g._pending_kan is None
    asyncio.run(run())


@pytest.mark.parametrize('preset,can_rob',[('majsoul',True),('tenhou',False),('mleague',False),('jpml_a',False)])
def test_only_configured_kokushi_can_rob_closed_kan(preset,can_rob):
    async def run():
        g=game(preset);p=g.player_list[0]
        p.hand_tiles=[12,13,14,22,23,24,31,32,33,41]+[47]*4
        p.combination_tiles=[];p.combination_mask=[];p.has_draw_slot=True
        g.player_list[1].hand_tiles=[11,19,21,29,31,39,41,42,43,44,45,46,46]
        await begin_kan(g,'angang',47)
        assert (g.game_status=='waiting_action_qianggang') is can_rob
        if can_rob:
            assert p.combination_tiles==[]
            await _broadcast_hu_and_end(g,1,g.action_dict[1][0],47)
            assert p.hand_tiles.count(47)==3 and p.combination_tiles==[]
        else:
            assert p.combination_tiles==['G47'] and p.hand_tiles.count(47)==0
    asyncio.run(run())


def test_delayed_previous_indicator_flips_before_next_kan():
    async def run():
        g=setup_kakan();g._pending_kan_dora_count=1
        g._reveal_kan_dora=AsyncMock()
        await begin_kan(g,'jiagang',305)
        g._reveal_kan_dora.assert_awaited_once()
        assert g._pending_kan_dora_count==0
    asyncio.run(run())


def test_riichi_kan_requires_drawn_tile_and_retains_waits():
    g=game();p=g.player_list[0]
    p.hand_tiles=[11]*3+[22,23,24,33,34,35,36,37,28,28]+[11]
    p.tag_list=['riichi'];p.combination_mask=[]
    assert ankan_allowed(g,p,11)
    g.detailed_config['riichi_kan_rule']='shape'
    assert ankan_allowed(g,p,11)
    p.hand_tiles[-1],p.hand_tiles[-2]=p.hand_tiles[-2],p.hand_tiles[-1]
    assert not ankan_allowed(g,p,11)


@pytest.mark.parametrize('restriction',['wait','shape','shape_yaku'])
@pytest.mark.parametrize('hand,expected',[
    ([11]*3+[22,23,24,33,34,35,36,37,28,28]+[11], {'wait':True,'shape':True,'shape_yaku':True}),
    # The original concealed 111/222/333 may also be three 123 sequences.
    ([11]*3+[12]*3+[13]*3+[27,28,29,29]+[11], {'wait':True,'shape':False,'shape_yaku':False}),
    # Nine gates' nine-sided wait cannot survive removing the 111 triplet.
    ([11]*3+[12,13,14,15,16,17,18]+[19]*3+[11], {'wait':False,'shape':False,'shape_yaku':False}),
])
def test_three_riichi_kan_restrictions_preserve_waits_and_required_decompositions(restriction,hand,expected):
    g=game();p=g.player_list[0]
    p.hand_tiles=list(hand);p.tag_list=['riichi'];p.combination_tiles=[];p.combination_mask=[]
    g.detailed_config['riichi_kan_rule']=restriction
    assert ankan_allowed(g,p,11) is expected[restriction]
    assert p.hand_tiles==hand and p.combination_tiles==[]
