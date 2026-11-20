import pytest

from .test_flow import WAIT, apply, game, turn
from .state_machine import Phase as P
from ...game_calculation.duocai.solver import Meld


@pytest.mark.parametrize('variant,outcome', [('guobiao','pass'), ('riichi','pass'), ('riichi','hu')])
@pytest.mark.parametrize('drawn', [False, True])
def test_pending_added_kong_snapshot_and_record_preserve_physical_source_until_resolution(variant,outcome,drawn):
    g = game(variant)
    p = g.player_list[0]
    p.melds = [Meld((38,38,38),False,'peng',2)]
    g.update_melds(0)
    g.player_list[1].hand_tiles = WAIT.copy()
    g.player_list[1].discard_origin_tiles = []
    hand = [11,12,13,22,23,24,31,32,33,41,38]
    if not drawn:
        hand[-2:] = reversed(hand[-2:])
    turn(g,0,hand)
    g.start_round_recording()
    choice = next(c for c in g.meld_choices[0] if c.kind=='jiagang' and c.take==(38,))
    before = (list(p.hand_tiles), tuple(p.melds), set(g.wall.removed))
    apply(g,0,'jiagang',choice_id=choice.key)
    expected = dict(player=0,meld_index=0,tile=38,is_mo_gang=drawn)
    assert g.machine.phase == P.ROB_KONG
    assert (list(p.hand_tiles),tuple(p.melds),set(g.wall.removed)) == before
    declarations = [packet for packet in g.outbound_payloads
                    if packet.get('do_action_info',{}).get('action_list')==['declare_kan']]
    assert len(declarations)==4
    for packet in declarations:
        assert packet['do_action_info']['cut_tile']==38
        assert packet['do_action_info']['is_mo_gang'] is drawn
        assert packet['game_info']['duocai_info']['pending_kong']==expected
    for viewer in range(4):
        packets = g.restore_payloads(viewer)
        assert packets[0]['game_info']['duocai_info']['pending_kong']==expected
        assert packets[0]['game_info']['players_info'][0]['hand_tiles_count']==11
        assert packets[0]['game_info']['players_info'][0]['hand_tiles']==(hand if viewer==0 else None)
    ticks = g.game_record['game_round']['round_index_1']['action_ticks']
    declaration_index = next(i for i,tick in enumerate(ticks)
        if tick[:2]==['duocai','action'] and tick[2]['action']=='declare_kan')
    assert ticks[declaration_index][2]['is_mo_gang'] is drawn
    assert ticks[declaration_index+1][0:2]==['duocai','state']
    assert ticks[declaration_index+1][2]['pending_kong']==expected
    if outcome=='hu':
        apply(g,1,'hu')
        assert g.machine.phase==P.END and len(p.melds[0].physical)==3
        assert p.hand_tiles==[v for i,v in enumerate(hand) if i!=hand.index(38)]
        resolved_action='rob_kan'
    else:
        g.apply_action_results(g.live_pending_window,{i:dict(action_type='pass') for i in g.waiting_players_list})
        assert g.machine.phase==P.TURN and len(p.melds[0].physical)==4
        assert len(g.wall.removed)==len(before[2])+1
        resolved_action='jiagang'
    assert g.pending_kong is None
    for viewer in range(4):
        assert g.build_game_start_payload(viewer)['game_info']['duocai_info']['pending_kong'] is None
    resolved_packets = [packet for packet in g.outbound_payloads
        if packet.get('do_action_info',{}).get('action_list')==[resolved_action]]
    assert len(resolved_packets)==4
    assert all(packet['game_info']['duocai_info']['pending_kong'] is None for packet in resolved_packets)
