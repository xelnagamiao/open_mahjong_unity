"""Exercise the same draw-slot contract used by Unity's common action player."""
import pytest

from .test_flow import turn, act
from .state_machine import Phase as P


@pytest.mark.parametrize('drawn_kong', [False, True])
def test_secret_broadcast_preserves_an_unrelated_draw_slot(drawn_kong):
    hand = [11]*4 + [12,13,14,21,22,23,31,32,33,25,25,29,28]
    if drawn_kong:
        hand[0], hand[-1] = hand[-1], hand[0]
    s = turn(hand)
    act(s, {0: dict(action_type='angang', target_tile=11)})
    event = next(e for e in s.domain_events if e['action'] == 'angang')
    assert event['is_mo_gang'] is drawn_kong
    assert s.player_list[0].hand_tiles.count(28) == 1
    assert 11 not in s.player_list[0].hand_tiles


@pytest.mark.parametrize('dealer_mode', ['winner_roll', 'winner', 'rotate'])
def test_wall_draw_uses_documented_classic16_dealer_policy(dealer_mode):
    s = turn([11,12,13,14,15,16,21,22,23,31,32,33,25,25,26,27,28], dealer_mode=dealer_mode)
    s.end_hand()
    assert s.next_dealer == (1 if dealer_mode == 'rotate' else 0)
    s.machine.transition(P.READY)
    previous = [p.user_id for p in s.player_list]
    s.advance_round_after_ready()
    expected = previous[1:] + previous[:1] if dealer_mode == 'rotate' else previous
    assert [p.user_id for p in s.player_list] == expected


@pytest.mark.parametrize('bonus', [41,42,43,44,45,46,47,51,52,53,54,55,56,57,58])
def test_classic16_replaces_honors_and_flowers_but_preserves_suited_tiles(bonus):
    hand = [11,12,13,14,15,16,21,22,23,31,32,33,25,25,26,27,bonus]
    s = turn(hand)
    s.tiles_list[-1] = 29
    assert s.action_dict[0] == ['buhua']
    act(s, {0: dict(action_type='buhua')})
    assert s.player_list[0].huapai_list == [bonus]
    assert sorted(s.player_list[0].hand_tiles) == sorted(hand[:-1] + [29])
    assert 'buhua' not in s.action_dict[0]
    assert s.player_list[0].has_draw_slot
