from collections import Counter

import pytest

from .DuocaiGameState import DuocaiGameState
from .actions import choices
from .bot import choose_action
from .state_machine import Phase as P, StateMachine
from ...game_calculation.duocai import tiles as t
from ...game_calculation.duocai.solver import Meld


def make(variant='riichi', desired=None):
    data = DuocaiGameState._default_room_data()
    data.update(sub_rule='duocai/'+variant, random_seed=17, game_round=2 if variant=='riichi' else 4)
    g = DuocaiGameState(room_data=data)
    g.initialize_round()
    if desired:
        available = Counter(g.wall.tiles)
        for hand in desired.values():
            available.subtract(hand)
        assert min(available.values()) >= 0
        remaining = list(available.elements())
        for i,p in enumerate(g.player_list):
            count = 14 if i == 0 else 13
            p.hand_tiles = list(desired[i]) if i in desired else remaining[:count]
            if i not in desired:
                del remaining[:count]
            p.has_draw_slot = i == 0
            p.draw_kind = 'normal'
        drawn = [v for p in g.player_list for v in p.hand_tiles]
        for pos,tile in zip(g.wall.order,drawn+remaining):
            g.wall.tiles[pos] = tile
        g.sync_wall()
    g.open_action_window(g.opening_window())
    return g


def apply(g,index,action,**fields):
    responses = {i:dict(action_type='pass') for i,a in g.action_dict.items() if a}
    responses[index] = dict(action_type=action,**fields)
    return g.apply_action_results(g.live_pending_window,responses)


def supply(g):
    values = [v for p in g.player_list for v in p.hand_tiles+p.discard_tiles]
    values += [v for p in g.player_list for m in p.melds for v in m.physical]
    values += [v for i,v in enumerate(g.wall.tiles) if i not in g.wall.removed]
    values += [e['tile'] for e in g.deferred_hu_settlements if e['source']=='rob_kong']
    return Counter(values)


@pytest.mark.parametrize('variant',['guobiao','riichi'])
@pytest.mark.parametrize('kind,illegal,hand,take,called',[
    ('peng','angang',[14,14,11,11,11,11,22,24,26,31,34,37,41],(14,14),14),
    ('peng','jiagang',[13,1013,2013,11,12,22,23,24,31,32,33,41,41],(13,1013),13),
    ('chi','angang',[11,12,14,14,14,14,22,24,26,31,34,37,41],(11,12),13),
])
def test_called_meld_allows_only_discard_and_rejects_forged_kong(variant,kind,illegal,hand,take,called):
    available = Counter(t.normal_pack())-Counter(hand)-Counter([called])
    g = make(variant,{0:list(available.elements())[:13]+[called],1:hand})
    before = supply(g)
    apply(g,0,'cut',TileId=called)
    choice = next(c for c in g.meld_choices[1] if c.kind==kind and c.take==take)
    apply(g,1,'chi_left' if kind=='chi' else kind,choice_id=choice.key)
    assert g.machine.phase == P.AFTER_MELD
    assert g.action_dict[1] == ['cut']
    assert not g.player_list[1].has_draw_slot and not choices(g,1)
    snapshot = (list(g.wall.live),list(g.player_list[1].hand_tiles),list(g.player_list[1].melds))
    with pytest.raises(ValueError):
        apply(g,1,illegal)
    with pytest.raises(ValueError,match='吃碰后'):
        g.validate_response(1,dict(action_type=illegal),[illegal])
    with pytest.raises(ValueError,match='吃碰后'):
        g._turn(g.live_pending_window,dict(action_type=illegal))
    assert snapshot == (g.wall.live,g.player_list[1].hand_tiles,g.player_list[1].melds)
    apply(g,1,'cut',TileId=g.player_list[1].hand_tiles[-1])
    assert g.machine.phase == P.RESPONSE
    assert supply(g) == before


@pytest.mark.parametrize('target',[P.TURN,P.ROB_KONG])
def test_after_meld_phase_cannot_skip_discard(target):
    state = StateMachine()
    for phase in [P.TURN,P.RESPONSE,P.AFTER_MELD]:
        state.transition(phase)
    with pytest.raises(RuntimeError):
        state.transition(target)


def test_called_riichi_marker_moves_to_next_discard_and_is_recorded():
    wait=[11,12,13,22,23,24,34,35,36,36,37,28,28]
    g=make('riichi',{0:wait+[19],1:[19,19,11,12,22,24,26,31,32,33,41,41,43]})
    g.start_game_recording()
    g.start_round_recording()
    apply(g,0,'riichi_cut',TileId=19,cutClass=True)
    c=next(c for c in g.meld_choices[1] if c.kind=='peng' and c.take==(19,19))
    apply(g,1,'peng',choice_id=c.key)
    assert g.player_list[0].riichi_marker_pending
    for _ in range(24):
        if g.machine.phase==P.TURN and g.current_player_index==0:
            break
        if g.machine.phase in (P.TURN,P.AFTER_MELD):
            i=g.current_player_index
            apply(g,i,'cut',TileId=g.legal_discard_tiles(i)[-1])
        else:
            g.apply_action_results(g.live_pending_window,{i:dict(action_type='pass') for i,a in g.action_dict.items() if a})
    assert g.current_player_index==0 and g.player_list[0].has_draw_slot
    apply(g,0,'cut',TileId=g.player_list[0].hand_tiles[-1],cutClass=True)
    assert g.player_list[0].discard_riichi_flags==[True]
    assert not g.player_list[0].riichi_marker_pending
    assert [e for e in g.domain_events if e['action']=='cut' and e['player']==0][-1]['is_riichi_horizontal']
    assert g.build_game_start_payload(0)['game_info']['players_info'][0]['discard_riichi_flags']==[True]


@pytest.mark.parametrize('variant',['guobiao','riichi'])
@pytest.mark.parametrize('source,expected',[
    (3,[3,5015,1,36,0,36,0,36]),
    (2,[0,36,3,5015,1,36,0,36]),
    (1,[0,36,0,36,3,5015,1,36]),
])
def test_fourth_added_entity_stacks_at_original_called_slot_and_fifth_is_flat(variant,source,expected):
    g=make(variant)
    p=g.player_list[0]
    p.melds=[Meld((36,36,36,5015),False,'peng',source)]
    g.update_melds(0)
    assert p.combination_mask==[expected]
    p.melds=[Meld((36,36,36,5015,5039),False,'peng',source)]
    g.update_melds(0)
    assert p.combination_mask==[([0,5039]+expected if variant=='riichi' else expected+[0,5039])]


@pytest.mark.parametrize('variant',['guobiao','riichi'])
def test_two_natural_rounds_keep_entity_supply_and_only_cut_after_calls(variant):
    g=make(variant)
    seen=[]
    for hand in range(2):
        before=Counter(g.wall.tiles)
        for step in range(600):
            assert supply(g)==before
            if g.machine.phase==P.END:
                break
            if g.machine.phase==P.AFTER_MELD:
                assert g.action_dict[g.current_player_index]==['cut']
            responses={i:choose_action(g,i,a) for i,a in g.action_dict.items() if a}
            g.apply_action_results(g.live_pending_window,responses)
        assert g.machine.phase==P.END and not g.match_finishing
        seen.append(g.wall.deck)
        if hand==0:
            g.machine.transition(P.READY)
            g.advance_round_after_ready()
            g.initialize_round()
            g.open_action_window(g.opening_window())
    assert seen==[0,1]


def test_riichi_result_and_record_keep_fu_han_ura_and_collected_counters():
    g=make('riichi')
    p=g.player_list[1]
    p.hand_tiles=[11,12,13,22,23,24,34,35,36,36,37,28,28,38]
    p.has_draw_slot=True
    p.draw_kind='normal'
    p.riichi=1
    g.opening_flow_interrupted=True
    g.honba,g.riichi_sticks=2,3
    for player in g.player_list[1:]:
        player.score-=1000
    g.open_action_window(g.begin_turn(1))
    g.start_round_recording()
    before=g.build_game_start_payload(1)['game_info']['duocai_info']
    assert before['wall_layout_version']==2 and before['ura_indicators']==[]
    assert sum(v['tile'] is not None for v in before['wall'])==1
    quote=g.score_win(1,'self_draw',38)
    assert quote.valid
    apply(g,1,'hu_self')
    assert g.honba==0 and g.riichi_sticks==0
    for viewer in range(4):
        info=g.build_final_settlement_payload(viewer)['show_result_info']
        assert (info['han'],info['fu'])==(quote.fan,quote.fu)
        assert (info['honba'],info['riichi_sticks_collected'])==(2,3)
        assert info['dora_indicators']==g.wall.indicators()
        assert info['ura_dora_indicators']==g.wall.indicators(True)
        assert sum(v['tile'] is not None for v in info['duocai_info']['wall'])==2
    ticks=g.game_record['game_round']['round_index_1']['action_ticks']
    result_index=next(i for i,v in enumerate(ticks) if v[:2]==['duocai','result'])
    assert ticks[result_index+1][0]=='hu_self'
    assert ticks[result_index][2]==g.deferred_hu_settlements[0]['riichi_result']
    assert p.record_counter.zimo_times==1


def test_kan_announcement_exposes_actual_entity_and_target_group_to_every_viewer():
    g=make('riichi')
    p=g.player_list[0]
    p.melds=[Meld((38,38,38),False,'peng',2)]
    p.hand_tiles=[11,12,13,22,23,24,31,32,33,41,5026]
    p.has_draw_slot=True
    g.update_melds(0)
    g.open_action_window(g.begin_turn(0))
    c=next(c for c in g.meld_choices[0] if c.kind=='jiagang' and c.take==(5026,))
    apply(g,0,'jiagang',choice_id=c.key)
    assert p.melds[0].physical==(38,38,38)
    events=[v['do_action_info'] for v in g.outbound_payloads
            if v.get('do_action_info',{}).get('action_list')==['declare_kan']]
    assert len(events)==4
    assert all(e['cut_tile']==5026 and e['duocai_meld_index']==0 for e in events)
