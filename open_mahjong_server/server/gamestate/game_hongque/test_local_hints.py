"""Display-only hints remain local; action choices carry authoritative scores."""
from .HongqueGameState import HongqueGameState
from . import scoring


def test_broadcast_supplies_authoritative_choices_without_display_only_hints(monkeypatch):
    def forbidden(*args, **kwargs):
        raise AssertionError('broadcast must not calculate a display-only score')
    monkeypatch.setattr(scoring, 'best_win_result', forbidden)
    state = HongqueGameState(None, {'room_id':'892', 'game_round':1,
        'player_list':[101,102,103,104]}, gamestate_id='local-hints')
    player=state.players[0]
    player.hand=['AX1','AX2','AX3']
    player.drawn_tile='AX3'
    state.current_player_index=0
    state.phase='turn'
    state.wall=['GX9']
    for tips in (True,False):
        state.tips=tips
        for viewer in range(4):
            packet=state.build_state(viewer)
            assert not {'win_hint', 'waiting_tiles', 'waiting_hints'} & packet.keys()
            if viewer==0:
                wins = [candidate for candidate in packet['candidates'] if candidate['kind'] == 'win']
                assert len(wins) == 1 and wins[0]['points'] > 0
                assert wins[0]['groups'] == [['AX1', 'AX2', 'AX3']]
                assert packet['local_win_context']==dict(tile='AX3',self_draw=True,
                    before_first_discard=True,wall_empty=False)
            else:
                assert packet['local_win_context'] is None
                assert packet['candidates'] == []
    state.wall.clear()
    state.players[1].discards.append('GX8')
    context=state.build_state(0)['local_win_context']
    assert context['wall_empty'] and not context['before_first_discard']
    state.phase='round_end'
    assert state.build_state(0)['local_win_context'] is None
