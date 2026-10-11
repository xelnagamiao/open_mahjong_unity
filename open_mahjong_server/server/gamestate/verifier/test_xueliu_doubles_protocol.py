from .test_xueliu_protocol import sim_table, table, mid_win
from .record_sim.flags import resolve_record_flags


def test_two_stage_exchange_restores_committed_and_pending_view():
    sim=sim_table()
    sim.apply(table('sichuan/xueliu_doubles'))
    phase=dict(stage=1,active_players=[0,1],teammate_suit=0,selection_done=False)
    def ask(actions,tick):
        sim.apply(dict(type='gamestate/sichuan/broadcast_hand_action',ask_hand_action_info=dict(
            action_list=actions,player_index=0,remaining_time=8,step_remaining=0,action_tick=tick,xueliu_exchange=phase.copy())))
    ask(['xueliu_exchange_three'],11)
    assert sim.opening['three_tiles'] and not sim.action_button['visible']
    phase.update(stage=2,active_players=[2,3],selection_done=True)
    ask([],12)
    assert not sim.opening['three_tiles'] and not sim.gsm['IsSelfActionRequired']
    assert sim.opening['team_exchange']['selection_done']
    assert sim.gsm['LastAskActionTick']==12
    phase.update(active_players=[0,1],selection_done=False,teammate_suit=2)
    ask(['xueliu_exchange_three'],13)
    assert sim.opening['three_tiles'] and sim.opening['team_exchange']['teammate_suit']==2


def test_new_mode_repeated_hu_stays_active_and_does_not_infer_dingque():
    sim=sim_table()
    sim.apply(table('sichuan/xueliu_doubles'))
    sim.apply(dict(type='gamestate/sichuan/do_action',do_action_info=dict(action_list=['deal_tile'],action_player=0,deal_tile=29)))
    sim.apply(mid_win(score_changes=[6,-3,0,-3],player_to_score={0:6,1:-3,2:0,3:-3}))
    assert not sim.end_result['show_result']
    assert sim.game3d['self']['flowers']==[29]
    assert not resolve_record_flags({'rule':'sichuan','sub_rule':'sichuan/xueliu_doubles'}).infers_dingque_from_discards


def test_exchange_animation_restores_without_exposing_selection_actions():
    sim=sim_table()
    sim.apply(table('sichuan/xueliu_doubles'))
    phase=dict(stage=1,active_players=[2,3],selection_done=False,teammate_suit=1,
               outgoing_suit=0,animating=True,animation_seconds=.6)
    sim.apply(dict(type='gamestate/sichuan/broadcast_hand_action',ask_hand_action_info=dict(
        action_list=[],player_index=0,remaining_time=0,step_remaining=0,action_tick=21,xueliu_exchange=phase)))
    assert not sim.opening['three_tiles'] and not sim.gsm['IsSelfActionRequired']
    assert sim.opening['team_exchange']['animating']
    assert sim.opening['team_exchange']['animation_seconds']==.6
    phase=dict(stage=2,active_players=[0,1],selection_done=False,teammate_suit=1,
               outgoing_suit=0,animating=False,animation_seconds=0)
    sim.apply(dict(type='gamestate/sichuan/broadcast_hand_action',ask_hand_action_info=dict(
        action_list=['xueliu_exchange_three'],player_index=0,remaining_time=10,step_remaining=0,action_tick=22,xueliu_exchange=phase)))
    assert sim.opening['three_tiles'] and sim.gsm['IsSelfActionRequired']
