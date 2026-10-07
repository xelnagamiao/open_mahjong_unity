"""Guizhou hint context is authoritative, viewer-local and independent of clocks."""
import pytest

from .test_flow import PLAIN, configured_turn, act
from .state_machine import Phase


@pytest.mark.parametrize('ready', ['', 'soft_ready', 'hard_ready'])
@pytest.mark.parametrize('pending', [False, True])
def test_hint_ready_context_survives_private_snapshot_and_reconnect(ready, pending):
    state = configured_turn(PLAIN)
    player = state.player_list[0]
    player.ready_kind = ready
    player.ready_pending = pending
    player.tag_list = ['declared_ready'] if ready else []
    first = state.guizhou_info(0)
    reconnect = state.build_game_start_payload(0)['game_info']['guizhou_info']
    for info in (first, reconnect):
        assert info['self_ready_qualification'] == (ready or 'none')
        assert info['self_ready_pending'] is pending
    for viewer in (None, -1, 4, 1, 2, 3):
        info = state.guizhou_info(viewer)
        assert info['self_ready_qualification'] == 'none'
        assert info['self_ready_pending'] is False


def test_next_round_clears_ready_context_and_does_not_change_clock_contract():
    state = configured_turn(PLAIN)
    player = state.player_list[0]
    player.ready_kind = 'hard_ready'
    player.ready_pending = True
    round_time, step_time = state.round_time, state.step_time
    state.machine.transition(Phase.END)
    state.machine.transition(Phase.READY)
    state.machine.transition(Phase.START)
    state.initialize_round()
    info = state.guizhou_info(0)
    assert info['self_ready_qualification'] == 'none'
    assert info['self_ready_pending'] is False
    assert state.player_list[0].remaining_time == state.round_time == round_time
    assert state.step_time == step_time


@pytest.mark.parametrize('commit', [False, True])
def test_soft_ready_hint_context_follows_real_selection_cancel_and_commit(commit):
    state = configured_turn(PLAIN)
    state.step_time = 7
    player = state.player_list[0]
    player.remaining_time = 9
    player.discard_count = 0
    state.open_action_window(state.begin_turn(0))
    clock = state.live_pending_window['_clocks'][0]
    act(state, 0, 'guizhou_ready')
    selecting = state.build_pending_action_payload(0)['game_info']['guizhou_info']
    assert selecting['self_ready_qualification'] == 'none'
    assert selecting['self_ready_pending'] is True
    assert state.live_pending_window['_clocks'][0] is clock
    if commit:
        act(state, 0, 'cut', TileId=28, cutClass=True, cutIndex=13)
    else:
        act(state, 0, 'guizhou_ready_cancel')
        assert state.live_pending_window['_clocks'][0] is clock
    final = state.build_game_start_payload(0)['game_info']['guizhou_info']
    assert final['self_ready_qualification'] == ('soft_ready' if commit else 'none')
    assert final['self_ready_pending'] is False
    assert player.remaining_time == 9
