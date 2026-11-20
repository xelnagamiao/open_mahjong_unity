from .client_context import private_context
from .test_flow import game
from .state_machine import Phase as P


def context(g, index):
    return private_context(g, index, for_record=True)['local_request']['context']


def test_first_draw_uses_authoritative_source_and_never_exposes_ura():
    g = game('riichi')
    own = context(g, 0)
    assert own['win_source'] == 'self_draw'
    assert own['current_win_tile'] == g.player_list[0].hand_tiles[-1]
    assert own['tenhou'] and not own['chiihou']
    assert context(g, 1)['current_win_tile'] == 0
    assert 'ura_indicators' not in own
    assert own['pending_riichi_value'] == 2


def test_claim_snapshot_carries_only_the_actual_rob_kong_tile():
    g = game('riichi')
    g.pending_kong = object()
    g.domain_events.append(dict(action='declare_kan', player=0, tile=5026))
    g.machine.transition(P.ROB_KONG)
    g.live_pending_window = dict(status=P.ROB_KONG.value, player=0, tile=5026)
    row = context(g, 1)
    assert row['win_source'] == 'rob_kong' and row['current_win_tile'] == 5026
    assert row['chankan'] and not row['tenhou'] and not row['chiihou']
    assert context(g, 0)['current_win_tile'] == 0
    g.machine.transition(P.TURN)
    g.current_player_index = 1
    p = g.player_list[1]
    p.hand_tiles.append(38)
    p.has_draw_slot, p.draw_kind = True, 'normal'
    g.domain_events.append(dict(action='deal_tile', player=1, tile=38))
    row = context(g, 1)
    assert row['win_source'] == 'self_draw' and row['current_win_tile'] == 38
    assert not row['chankan']
    assert context(g, 2)['current_win_tile'] == 0


def test_no_stale_discard_timing_after_meld_and_no_inferred_future_last_tile():
    g = game('riichi')
    g.domain_events.append(dict(action='cut', player=0, tile=38))
    g.player_list[0].has_draw_slot = False
    g.machine.transition(P.RESPONSE)
    g.live_pending_window = dict(status=P.RESPONSE.value, player=0, tile=38)
    assert context(g, 1)['win_source'] == 'discard'
    g.domain_events.append(dict(action='peng', player=1, tile=38))
    assert context(g, 2)['current_win_tile'] == 0
    g.machine.transition(P.AFTER_MELD)
    assert context(g, 1)['current_win_tile'] == 0


def test_joker_only_wait_receives_real_exhaustive_payment_and_dealer_continuation():
    g = game('riichi')
    g.player_list[0].hand_tiles = [11, 11, 11, 11, 22, 22, 22, 33, 1033, 2033, 44, 44, 44]
    for p in g.player_list[1:]:
        p.hand_tiles = [11, 14, 17, 22, 25, 28, 32, 35, 38, 41, 42, 45, 46]
    g.end_draw()
    assert g.round_settlement['tenpai'] == [0]
    assert g.round_changes == [3000, -1000, -1000, -1000]
    assert g.next_dealer == 0 and g.honba == 1
