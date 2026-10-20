from collections import Counter
import pytest
from .ShangraoGameState import ShangraoGameState
from .state_machine import Phase as P
from .bot import choose_action
from ...game_calculation.shangrao import rules as book
from ...database.shared_record_metrics import analyze_shared_record_for_player


@pytest.mark.parametrize('seed',[1,29,452])
@pytest.mark.parametrize('options',[False,True])
def test_four_hand_match_conserves_physical_tiles_and_recorded_net_scores(seed,options):
    room=ShangraoGameState._default_room_data()
    room.update(random_seed=seed,detailed_config={k:options for k in book.CONFIG if k!='rule_version'})
    s=ShangraoGameState(calculation_service=object(),room_data=room)
    s.start_game_recording()
    for number in range(1,5):
        s.initialize_round();s.start_round_recording();s.open_action_window(s.opening_window())
        for step in range(600):
            physical=Counter(s.tiles_list)
            physical.update(t for t in s.burnt_tiles if t==s.bao)
            for player in s.player_list:
                physical.update(player.hand_tiles);physical.update(player.discard_tiles)
                for meld in player.combination_tiles:physical.update(book.meld_tiles(meld))
            assert physical==Counter({t:4 for t in book.TILES})
            assert sum(p.score for p in s.player_list)==0
            if s.machine.phase==P.END:break
            window=s.live_pending_window
            responses={index:choose_action(s,index,actions) for index,actions in window['actions'].items() if actions}
            s.apply_action_results(window,responses)
            s.outbound_payloads.clear()
        else:pytest.fail('Hand exceeded a complete physical wall')
        s.finalize_round_recording()
        if number<4:
            s.machine.transition(P.READY);s.advance_round_after_ready()
    assert s.match_finishing and s.current_round==4
    for player in s.player_list:
        metrics=analyze_shared_record_for_player(s.game_record,player.original_player_index)
        assert metrics['total_round_score']==player.score
        assert metrics['total_rounds']==4
        assert metrics['deal_in_count']==0
        assert metrics['self_draw_count']==metrics['win_count']==player.record_counter.zimo_times
        assert metrics['total_win_turn']==player.record_counter.win_turn
        assert metrics['total_fan_score']==player.record_counter.win_score
        assert len(player.score_history)==4
