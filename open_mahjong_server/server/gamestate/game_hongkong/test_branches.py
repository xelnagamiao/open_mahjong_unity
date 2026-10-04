"""Physical-table scenarios exercising decisions through real action windows."""
import asyncio
from collections import Counter
from copy import deepcopy
from unittest.mock import AsyncMock

import pytest

from .test_flow import make_state, assert_conservation, open_round
from .state_machine import HongKongPhase as P
from .action_check import claim_actions, turn_actions, empty_actions
from ...game_calculation.hongkong.models import PROFILES,TILES,FLOWERS,ORPHANS
from ...game_calculation.hongkong.solver import parse_meld


def table(profile=PROFILES[0], *, hands=None, melds=None, flowers=None, actor=0, tail=None, opening=False,detailed_config=None):
    """A legal partial table; unspecified seats/wall are filled from remaining copies."""
    hands,melds,flowers = hands or {},melds or {},flowers or {}
    state = make_state(profile,flowers=bool(flowers),detailed_config=detailed_config)
    state.initialize_round()
    available = Counter({t:4 for t in TILES})
    if state.rules.flowers:
        available.update(FLOWERS)
    reserved = [] if tail is None else [tail]
    available.subtract(reserved)
    for i,p in enumerate(state.player_list):
        p.hand_tiles = list(hands.get(i,[]))
        p.combination_tiles = list(melds.get(i,[]))
        p.combination_mask = [[x for j,t in enumerate(parse_meld(code).tiles) for x in
                               (2 if code[0]=='G' else 1 if j==0 else 0,t)] for code in p.combination_tiles]
        p.huapai_list = list(flowers.get(i,[]))
        p.has_draw_slot = i==actor
        available.subtract(p.hand_tiles)
        available.subtract(p.huapai_list)
        for code in p.combination_tiles:
            available.subtract(parse_meld(code).tiles)
    assert all(n>=0 for n in available.values()),available
    for i,p in enumerate(state.player_list):
        expected = state.rules.structure.concealed_tile_count(len(p.combination_tiles),complete=i==actor)
        if i not in hands:
            for t in TILES:
                while available[t]>0 and len(p.hand_tiles)<expected:
                    p.hand_tiles.append(t)
                    available[t]-=1
        assert len(p.hand_tiles)==expected,(i,p.hand_tiles,expected)
    state.tiles_list = sorted(available.elements())+reserved
    state.opening_flow_interrupted = not opening
    state.opening_flower_stage = False
    state.current_player_index = actor
    state.start_game_recording()
    state.start_round_recording()
    window = state.open_action_window(state.begin_turn(actor))
    assert_conservation(state)
    return state,window


def act(state,window,**choices):
    data = {i:dict(action_type='pass') for i,actions in window['actions'].items() if actions}
    for key,value in choices.items():
        data[int(key)] = value if isinstance(value,dict) else dict(action_type=value)
    result = state.apply_action_results(window,data)
    assert_conservation(state)
    return result


def replace_all(state,window):
    """Advance explicit one-flower decisions, stopping at any optional win."""
    while state.machine.phase==P.REPLACEMENT:
        window=act(state,window,**{str(window['player']):'buhua'})
    return window


@pytest.mark.parametrize('action,own,forbidden,code',[
    ('chi_left',[11,12],{13},'s12'),('chi_mid',[12,14],{13},'s13'),
    ('chi_right',[14,15],{13,16},'s14'),('peng',[13,13],{13},'k13'),
    ('gang',[13,13,13],set(),'g13'),
])
def test_each_claim_consumes_correct_tiles_priority_and_forbids_swap(action,own,forbidden,code):
    tail=47
    rest=[21,23,25,27,29,31,33,35,37,39,41][:13-len(own)]
    hand0=[11,12,14,16,18,21,22,24,26,28,31,32,34,13]
    state,window=table(hands={0:hand0,1:own+rest},tail=tail)
    window=act(state,window,**{'0':dict(action_type='cut',TileId=13)})
    assert action in window['actions'][1]
    window=act(state,window,**{'1':action})
    assert state.player_list[1].combination_tiles==[code]
    if action=='gang':
        assert state.player_list[1].hand_tiles[-1]==tail
        assert state.player_list[1].replacement_kind=='kong'
    else:
        assert state.machine.phase==P.DISCARD_ONLY
        assert state.player_list[1].forbidden_discards==forbidden
        assert not {'angang','jiagang'} & set(window['actions'][1])


def test_pung_beats_chow_and_river_marker_is_carried_to_next_discard():
    state,window=table(hands={0:[11,12,14,16,18,21,22,24,26,28,31,32,34,13],
                              1:[14,15,21,23,25,27,29,31,33,35,37,39,41],
                              2:[13,13,21,23,25,27,29,31,33,35,37,39,41]})
    state.player_list[0].pending_ready_marker=True
    window=act(state,window,**{'0':dict(action_type='cut',TileId=13)})
    window=act(state,window,**{'1':'chi_right','2':'peng'})
    assert state.current_player_index==2
    assert not state.player_list[0].discard_tiles
    assert state.player_list[0].pending_ready_marker


@pytest.mark.parametrize('profile',PROFILES)
def test_concealed_kong_commits_after_window_and_correct_visibility_payment(profile):
    hand=[11]*4+[21,22,23,31,32,33,41,41,45,45]
    if profile==PROFILES[2]: hand += [24,25,26]
    state,window=table(profile,hands={0:hand},tail=47)
    before=list(state.player_list[0].hand_tiles)
    window=act(state,window,**{'0':dict(action_type='angang',target_tile=11)})
    assert state.machine.phase==P.KONG_RESPONSE and state.player_list[0].hand_tiles==before
    window=act(state,window)
    assert state.player_list[0].combination_tiles==['G11']
    assert state.player_list[0].hand_tiles[-1]==47
    view=state.build_game_start_payload(1)['game_info']['players_info'][0]
    assert view['combination_tiles']==(['G11'] if profile==PROFILES[0] else ['G0'])
    assert [p.score for p in state.player_list]==([15,-5,-5,-5] if profile==PROFILES[2] else [0]*4)


def test_only_old_orphans_can_rob_concealed_kong_and_only_one_tile_leaves_actor():
    hand=[47]*4+[12,13,14,21,22,23,31,32,33,45]
    orphan=sorted(ORPHANS-{47})+[11]
    state,window=table(hands={0:hand,1:orphan})
    window=act(state,window,**{'0':dict(action_type='angang',target_tile=47)})
    assert window['actions'][1]==['hu','pass']
    window=act(state,window,**{'1':'hu'})
    assert state.machine.phase==P.END
    assert state.player_list[0].hand_tiles.count(47)==3
    assert state.player_list[0].combination_tiles==[]
    assert state.won_tiles==[47]
    state.apply_deferred_score_changes()
    assert [p.score for p in state.player_list]==[-768,768,0,0]


@pytest.mark.parametrize('rob',[False,True])
def test_added_kong_commit_or_rob_preserves_original_pung(rob):
    state,window=table(PROFILES[1],melds={0:['k15']},
        hands={0:[12,13,14,21,22,23,31,32,33,41,15],1:[13,14,21,22,23,31,32,33,46,46,46,41,41]},tail=47)
    window=act(state,window,**{'0':dict(action_type='jiagang',target_tile=15)})
    assert state.player_list[0].combination_tiles==['k15']
    window=act(state,window,**{'1':'hu' if rob else 'pass'})
    assert state.player_list[0].combination_tiles==(['k15'] if rob else ['g15'])
    assert 15 not in state.player_list[0].hand_tiles


@pytest.mark.parametrize('profile',PROFILES)
def test_multiple_ron_head_bump_or_simultaneous_from_same_discard(profile):
    first=[11,12,13,21,22,23,31,32,33,45,45,45,41]
    second=[14,15,16,24,25,26,34,35,36,46,46,46,41]
    actor=[17,18,19,27,28,29,37,38,39,42,42,43,43,41]
    if profile==PROFILES[2]: first += [17,18,19]; second += [27,28,29]; actor += [31,32,33]
    state,window=table(profile,hands={0:actor,1:first,2:second})
    if profile==PROFILES[0]:
        # Half-flush source hand yields a valid >=3-fan ron without synthetic flags.
        state,window=table(profile,hands={0:actor,1:[11,12,13,14,15,16,17,18,19,45,45,45,41],
                                        2:[21,22,23,24,25,26,27,28,29,46,46,46,41]})
    window=act(state,window,**{'0':dict(action_type='cut',TileId=41)})
    assert 'hu' in window['actions'][1] and 'hu' in window['actions'][2]
    window=act(state,window,**{'1':'hu','2':'hu'})
    assert [r['winner'] for r in state.deferred_hu_settlements]==([1,2] if profile==PROFILES[2] else [1])
    assert state.won_tiles==[41]
    state.apply_deferred_score_changes()
    assert_conservation(state)


@pytest.mark.parametrize('profile',PROFILES)
def test_pass_win_reset_policy(profile):
    state,_=table(profile)
    p=state.player_list[1]
    state.remember_pass(1,45)
    if profile==PROFILES[2]:
        assert p.water
        state.begin_new_draw(1,normal=False)
        assert p.water
        state.begin_new_draw(1,normal=True)
        assert not p.water
        p.declared_ready=True
        state.remember_pass(1,45)
        state.begin_new_draw(1,normal=True)
        assert p.permanent_water and not state.can_win(1,'self_draw',45)
    else:
        assert 45 in p.discard_win_lockout_tiles
        assert not state.can_win(1,'discard',45,payer=0)
        state.begin_new_draw(1,normal=False)
        assert not p.discard_win_lockout_tiles


@pytest.mark.parametrize('profile',PROFILES[1:])
def test_ready_selection_cancel_lock_and_physical_copy_validation(profile):
    hand=[11,12,13,21,22,23,31,32,33,45,45,45,41,41]
    if profile==PROFILES[2]: hand += [24,25,26]
    state,window=table(profile,hands={0:hand})
    window=act(state,window,**{'0':'riichi'})
    assert state.player_list[0].ready_pending
    assert state.build_pending_action_payload(0)['ask_hand_action_info']['riichi_candidate_cuts']
    window=act(state,window,**{'0':'riichi_cancel'})
    assert not state.player_list[0].ready_pending
    window=act(state,window,**{'0':'riichi'})
    window=act(state,window,**{'0':dict(action_type='cut',TileId=41)})
    p=state.player_list[0]
    assert p.declared_ready and p.discard_riichi_flags[-1]
    assert not {'angang','jiagang'} & set(turn_actions(state,0)[0])
    p.has_draw_slot=True
    p.hand_tiles.append(p.hand_tiles[0])
    with pytest.raises(ValueError):
        state._validated_cut(0,dict(TileId=p.hand_tiles[0],cutIndex=0))


def test_timeouts_cancel_all_wait_tasks_and_keep_legal_discard():
    async def run():
        state,window=table()
        result=await state.wait_action(timeout=0)
        assert result[0]['action_type']=='cut' and result[0]['is_timeout_action']
        state.apply_action_results(window,result)
        assert_conservation(state)
        state,window=table()
        state.step_time=100
        task=asyncio.create_task(state.wait_action())
        await asyncio.sleep(0)
        task.cancel()
        with pytest.raises(asyncio.CancelledError): await task
        assert not [t for t in asyncio.all_tasks() if t is not asyncio.current_task() and not t.done()]
    asyncio.run(run())


def opening_flow(profile,initial_hands,*,tail=(),detailed_config=None):
    state=make_state(profile,flowers=True,detailed_config=detailed_config)
    available=Counter({t:4 for t in TILES}); available.update(FLOWERS)
    for hand in initial_hands.values(): available.subtract(hand)
    available.subtract(tail)
    assert all(n>=0 for n in available.values())
    count=16 if profile==PROFILES[2] else 13
    hands={i:list(initial_hands.get(i,[])) for i in range(4)}
    for i in range(4):
        n=count+int(i==0 and count==13)
        if i not in initial_hands:
            for tile in TILES:
                while available[tile]>0 and len(hands[i])<n:
                    hands[i].append(tile);available[tile]-=1
        assert len(hands[i])==n
    wall=[]
    for packet in range(4 if count==16 else 3):
        for i in range(4): wall.extend(hands[i][packet*4:packet*4+4])
    if count==13:
        wall.extend([hands[0][12],hands[1][12],hands[2][12],hands[3][12],hands[0][13]])
    wall.extend(sorted(available.elements()));wall.extend(tail)
    state.start_game_recording();state.initialize_round(wall=wall);state.start_round_recording()
    window=state.open_action_window(state.opening_window())
    assert_conservation(state)
    return state,window


@pytest.mark.parametrize('winner',[0,2])
@pytest.mark.parametrize('count',[7,8])
def test_old_initial_flower_win_timing_and_payment(winner,count):
    n=14 if winner==0 else 13
    hand=list(range(51,51+count))+[11,12,13,21,22,23,31][:n-count]
    state,window=opening_flow(PROFILES[0],{winner:hand},tail=[45,44,43,42,41,39,38])
    window=replace_all(state,window)
    if count==7 and winner!=0:
        assert state.machine.phase==P.TURN and state.player_list[winner].initial_seven_pending
        for actor in range(winner):
            assert state.current_player_index==actor
            p=state.player_list[actor]
            window=act(state,window,**{str(actor):dict(action_type='cut',TileId=p.hand_tiles[-1])})
            window=act(state,window)
    assert state.machine.phase==P.FLOWER
    assert window['player']==winner
    window=act(state,window,**{str(winner):'hu_self'})
    state.apply_deferred_score_changes()
    assert state.machine.phase==P.END
    expected=16 if count==7 else 256
    assert state.player_list[winner].score==expected*3
    assert all(p.score==-expected for i,p in enumerate(state.player_list) if i!=winner)
    assert_conservation(state)


@pytest.mark.parametrize('decline',[False,True])
@pytest.mark.parametrize('kind',['eight','seven_steal'])
def test_sixteen_flower_choice_optional_hu_or_immediate_points(kind,decline):
    count=8 if kind=='eight' else 7
    initial={1:list(range(51,51+count))+[11,12,13,21,22,23,31,32,33][:16-count]}
    if kind=='seven_steal': initial[2]=[58,14,15,16,24,25,26,34,35,36,41,41,42,42,43,43]
    state,window=opening_flow(PROFILES[2],initial)
    assert state.machine.phase==(P.FLOWER if kind=='eight' else P.FLOWER_RESPONSE)
    if kind=='seven_steal':
        assert state.build_pending_action_payload(1)['ask_other_action_info']['action_list']==['hu','pass']
    choice='pass' if decline else 'hu_self' if kind=='eight' else 'hu'
    window=act(state,window,**{'1':choice})
    if decline:
        window=replace_all(state,window)
        assert state.machine.phase==P.INITIAL_READY
        assert state.player_list[1].score==(120 if kind=='eight' else 30)
        assert not state.ledger.debts
        assert state.player_list[1].water
    else:
        assert state.machine.phase==P.END
        assert state.deferred_hu_settlements[0]['points']==(40 if kind=='eight' else 30)
        assert len(state.ledger.debts)==(3 if kind=='eight' else 1)
    assert_conservation(state)


def test_initial_sixteen_ding_is_offered_to_each_waiting_seat_and_locks_hand():
    hand=[11,12,13,14,15,16,21,22,23,31,32,33,45,45,45,41]
    state,window=opening_flow(PROFILES[2],{2:hand})
    assert state.machine.phase==P.INITIAL_READY
    payload=state.build_pending_action_payload(2)['ask_hand_action_info']
    assert payload['player_index']==2 and 'ding_initial' in payload['action_list']
    window=act(state,window,**{'2':'ding_initial'})
    assert state.player_list[2].declared_ready and state.player_list[2].ready_kind=='heaven'
    assert state.current_player_index==0 and state.player_list[0].has_draw_slot


def test_flowers_replaced_from_tail_and_new_flower_waits_next_opening_cycle():
    # The first replacement is another flower. East's initial slot is replaced
    # before the new flower is replaced in the following flower cycle.
    state,window=opening_flow(PROFILES[0],{0:[51,52,11,12,13,21,22,23,31,32,33,41,41,45]},tail=[46,47,53])
    window=replace_all(state,window)
    events=[(e['action'],e.get('tile')) for e in state.domain_events]
    assert events[:4]==[('buhua',52),('deal_buhua_tile',53),('buhua',51),('deal_buhua_tile',47)]
    replacements=[tile for action,tile in events if action=='deal_buhua_tile']
    assert replacements[:3]==[53,47,46]
    assert state.player_list[0].huapai_list==[52,51,53]
    assert state.player_list[0].hand_tiles[-1]==46
    assert_conservation(state)


@pytest.mark.parametrize('profile',PROFILES)
def test_full_async_game_loop_completes_records_and_releases_action_tasks(profile):
    async def run():
        state=make_state(profile,seed=241)
        for i,p in enumerate(state.player_list):
            p.user_id=i+1
        state.bot_speed='instant'
        state.current_round=4
        state.present_final_settlements=AsyncMock()
        state.complete_game_lifecycle=AsyncMock()
        # Branch instrumentation and busy CI hosts can triple scorer time.
        # Still bound the complete match so a deadlocked action task fails.
        await asyncio.wait_for(state.run_game_loop(),60)
        assert state.machine.phase==P.FINISHED
        assert state.game_record['game_title']['rule_version']==state.rules.version
        state.complete_game_lifecycle.assert_awaited_once()
        assert all(v['action_ticks'][-1]==['end'] for v in state.game_record['game_round'].values())
        assert_conservation(state)
        await asyncio.gather(*state.bot_tasks,return_exceptions=False)
        await asyncio.sleep(0)
        assert not state.bot_tasks
    asyncio.run(run())


def test_three_mouth_cut_and_rotation_preserve_identity_and_score_history():
    state,window=table(PROFILES[2])
    for _ in range(3): state.ledger.apply_win({(1,2):10},winners=[1],self_draw=False)
    before=state.ledger.snapshot()[0]
    assert before['mouths']==3
    state.end_draw();state.apply_deferred_score_changes();state.machine.transition(P.READY)
    state.cut_pulls(2)
    assert not state.ledger.debts
    assert state.player_list[1].score==before['points']
    assert state.player_list[2].score==-before['points']
    assert state.player_list[1].score_history[-1]==f"+{before['points']}"
    state.ledger.apply_win({(1,2):20},winners=[1],self_draw=False)
    snapshot=deepcopy(state.ledger.snapshot())
    # Non-East win advances seat order; the debt remains between original 1/2.
    state.deferred_hu_settlements=[{'winner':1}]
    state.advance_round_after_ready()
    assert state.ledger.snapshot()==snapshot
    assert state.player_list[0].original_player_index==1
    assert state.round_index==2 and state.current_round==2
