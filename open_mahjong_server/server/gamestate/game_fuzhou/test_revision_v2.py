"""Executable boundaries in the revised user rulebook (2026-10-15)."""
import pytest
import asyncio
from collections import Counter

from .FuzhouGameState import FuzhouGameState
from .state_machine import Phase as P
from .test_state import WIN, state_with, act
from ...game_calculation.fuzhou import rules as book


def passes(state):
    return act(state, {i: dict(action_type='pass') for i,a in state.action_dict.items() if a})


def test_unopened_tiles_can_be_validated_before_an_indicator_exists():
    assert book.valid_tiles(WIN,jin=None)


def test_invalid_three_circle_duration_is_rejected_by_the_game_authority():
    data=FuzhouGameState._default_room_data();data['game_round']=3
    with pytest.raises(ValueError,match='时长'):
        FuzhouGameState(room_data=data,calculation_service=object())


def test_twenty_opening_flowers_can_force_win_on_the_owners_first_sea_draw():
    s=state_with(WIN[:-1]);p=s.player_list[0]
    p.huapai_list=[t for t in range(41,46) for _ in range(4)]
    s.update_reserve();s.tiles_list=[29,21,22,23]+[11]*s.dead_wall_count
    s.machine.phase=P.RESPONSE;s.open_action_window(s.draw_for(0))
    assert s.machine.phase==P.FLOWER_WIN and s.round_settlement is None
    act(s,{0:dict(action_type='fuzhou_flower_win')})
    assert s.machine.phase==P.END and s.round_settlement['score']['special']=='flower'
    assert len(p.hand_tiles)==17 and p.hand_tiles[-1]==29


@pytest.mark.parametrize('claim,hand',[
    ('chi_left',[11,12]+[21,22,23]*4+[28,28]),
    ('peng',[13,13]+[21,22,23]*4+[28,28]),
    ('gang',[13,13,13]+[21,22,23]*4+[28]),
    ('hu',[11,12]+[21,22,23]*4+[28,28]),
    ('pass',[11,12]+[21,22,23]*4+[28,28]),
])
def test_first_discard_is_claimable_after_both_qiangjin_windows(claim,hand):
    s=state_with(WIN[:-1]+[13]);s.dealer_first_discard_pending=True
    s.player_list[1].hand_tiles=hand
    s.machine.phase=P.REPLACEMENT
    s.open_action_window(s.after_gold_window())
    if s.machine.phase==P.HEAVEN:passes(s)
    assert s.machine.phase==P.OPENING
    passes(s)
    assert s.machine.phase==P.TURN and not s.discard_log
    act(s,{0:dict(action_type='cut',TileId=13,cutClass=True)})
    assert s.machine.phase==P.DEALER_GOLD and s.discard_log==[(0,13)]
    passes(s)
    assert s.machine.phase==P.RESPONSE and s.live_pending_window['tile']==13
    assert claim in s.action_dict[1]
    act(s,{i:dict(action_type=claim if i==1 else 'pass') for i,a in s.action_dict.items() if a})
    if claim=='hu':
        assert s.round_settlement['winner']==1 and s.round_settlement['payer']==0
    elif claim=='pass':
        assert s.current_player_index==1 and s.player_list[0].discard_tiles==[13]
    else:
        assert s.current_player_index==1 and not s.player_list[0].discard_tiles
        assert len(s.player_list[1].combination_tiles)==1


@pytest.mark.parametrize('held_gold',[2,3])
def test_actual_indicator_can_complete_a_standard_hand_with_multiple_held_gold(held_gold):
    # Start from one physically valid table and exchange its seats. Exactly
    # four copies exist even when the indicator is the fourth held gold.
    hand=WIN[:12]+[39]*held_gold+([29,29] if held_gold==2 else [29])
    s=state_with(hand)
    s.player_list[0].hand_tiles,s.player_list[1].hand_tiles=s.player_list[1].hand_tiles,s.player_list[0].hand_tiles
    for p in s.player_list:p.has_draw_slot=False
    extra=next(t for t in s.tiles_list if t in book.NUMBERS and t!=39)
    s.tiles_list.remove(extra);s.player_list[0].hand_tiles.append(extra);s.player_list[0].has_draw_slot=True
    s.machine.phase=P.REPLACEMENT
    s.open_action_window(s.opening_claim_window())
    assert s.physical_inventory()==Counter(book.PHYSICAL)
    assert 'qiangjin' in s.action_dict[1]
    act(s,{i:dict(action_type='qiangjin' if i==1 else 'pass') for i,a in s.action_dict.items() if a})
    assert s.indicator_claimed and s.round_settlement['winner']==1
    assert s.player_list[1].hand_tiles.count(39)==held_gold+1
    assert s.round_settlement['score']['special'] is None
    assert s.physical_inventory()==Counter(book.PHYSICAL)


@pytest.mark.parametrize('last_tile,win',[(39,True),(28,False),(41,False)])
def test_double_youjin_gets_only_one_final_draw(last_tile,win):
    s=state_with(WIN[:-2]+[39]);p=s.player_list[0]
    p.youjin=2;p.locked_waits={39};p.youjin_tile=39
    s.youjin_owner=s.double_youjin_owner=0;s.double_youjin_remaining=[]
    s.tiles_list=[last_tile,21,22,23]+[11]*18
    s.machine.phase=P.RESPONSE;s.open_action_window(s.draw_for(0))
    assert s.machine.phase==P.SEA and s.sea_queue==[]
    assert all(len(q.hand_tiles)==16 for q in s.player_list[1:])
    assert 'cut' not in s.action_dict[0] and 'buhua' not in s.action_dict[0]
    act(s,{0:dict(action_type='hu_self' if win else 'pass')})
    assert s.machine.phase==P.END and len(s.tiles_list)==21
    assert s.round_settlement['winner']==(0 if win else None)
    assert len([e for e in s.domain_events if e['action']=='deal_tile'])==1
    if win:
        assert {'youjin2','gold_pair'}<={r['id'] for r in s.round_settlement['score']['items']}


def test_opening_three_gold_and_twenty_flowers_wait_for_a_later_completed_draw():
    s=state_with(WIN[:12]+[39]*3+[28,29]);p=s.player_list[0]
    flowers=[t for t in range(41,46) for _ in range(4)]
    for tile in flowers:s.tiles_list.remove(tile)
    p.huapai_list=flowers;p.draw_kind='opening';s.update_reserve()
    s.machine.phase=P.REPLACEMENT;s.open_action_window(s.after_gold_window())
    if s.machine.phase==P.HEAVEN:passes(s)
    passes(s)
    assert s.machine.phase==P.TURN and s.round_settlement is None
    assert 'sanjin' not in s.action_dict[0]
    act(s,{0:dict(action_type='cut',TileId=29,cutClass=True)})
    assert s.round_settlement is None
    tile=next(t for t in s.tiles_list if t in book.NUMBERS and t!=39)
    s.tiles_list.remove(tile);s.tiles_list.insert(0,tile)
    s.open_action_window(s.draw_for(0))
    assert s.machine.phase==P.FLOWER_WIN and s.action_dict[0]==['fuzhou_flower_win','sanjin']
    act(s,{0:dict(action_type='fuzhou_flower_win')})
    assert s.round_settlement['score']['special']=='flower' and len(p.hand_tiles)==17
    assert s.physical_inventory()==Counter(book.PHYSICAL)


def test_four_dealer_changes_rotate_identity_scores_and_reconnect_seat_together():
    async def run():
        s=state_with();s.max_round=2;s.dealer_changes=3;s.current_round=4
        identities=[id(p) for p in s.player_list]
        for i,p in enumerate(s.player_list):p.user_id=100+i;p.score=100*i
        s.settle_win(0,'self_draw',29)  # Dealer repeat does not rotate or count.
        s.apply_deferred_score_changes();s.machine.transition(P.READY);s.advance_round_after_ready()
        assert s.dealer_changes==3 and [id(p) for p in s.player_list]==identities
        assert s.current_round==4 and s.dealer_streak==1
        s.initialize_round();s.end_draw();s.machine.transition(P.READY);s.advance_round_after_ready()
        assert s.dealer_changes==3 and [id(p) for p in s.player_list]==identities
        s.initialize_round();s._finish_round(1)
        expected_scores={p.user_id:p.score for p in s.player_list}
        s.machine.transition(P.READY);s.advance_round_after_ready();s.initialize_round()
        assert s.dealer_changes==4 and s.current_round==5 and s.dealer_index==2
        assert [id(p) for p in s.player_list]==identities[-1:]+identities[:-1]
        assert s.fuzhou_info()['seat_to_original']==[3,0,1,2]
        sent=[]
        async def capture(index,payload):sent.append((index,payload))
        s.send_payload_to_player=capture;s.open_action_window(s.opening_window())
        for user in range(100,104):
            sent.clear();await s.player_reconnect(user)
            seat=next(i for i,p in enumerate(s.player_list) if p.user_id==user)
            assert all(i==seat for i,_ in sent)
            info=sent[0][1]['game_info']
            assert info['self_hand_tiles']==s.player_list[seat].hand_tiles
            assert s.player_list[seat].score==expected_scores[user]
            assert all(row['hand_tiles'] is None for i,row in enumerate(info['players_info']) if i!=seat)
    asyncio.run(run())


def test_first_dealer_is_seeded_and_all_seats_are_possible():
    seats = set()
    for seed in range(1,33):
        data = dict(FuzhouGameState._default_room_data(),random_seed=seed)
        a,b = (FuzhouGameState(room_data=data,calculation_service=object()) for _ in range(2))
        assert a.dealer_index == b.dealer_index == a.initial_dealer
        a.initialize_round();b.initialize_round()
        assert a.dice == b.dice and a.tiles_list == b.tiles_list
        assert [p.hand_tiles for p in a.player_list] == [p.hand_tiles for p in b.player_list]
        assert [len(p.hand_tiles) for p in a.player_list] == [17 if i==a.dealer_index else 16 for i in range(4)]
        seats.add(a.dealer_index)
    assert seats == {0,1,2,3}


def test_twentieth_flower_waits_for_the_first_nonflower_and_keeps_the_whole_chain():
    state = state_with(WIN[:-1])
    p = state.player_list[0]
    p.huapai_list = [t for t in range(41,45) for _ in range(4)]+[45]*3
    state.update_reserve()
    state.tiles_list = [45]+[11]*50+[29,46]
    state.machine.transition(P.RESPONSE)
    state.open_action_window(state.draw_for(0))
    assert state.machine.phase == P.REPLACEMENT and state.round_settlement is None
    act(state,{0:dict(action_type='buhua')})
    assert len(p.huapai_list) == 20
    assert state.machine.phase == P.REPLACEMENT and p.hand_tiles[-1] == 46
    assert state.round_settlement is None  # The twentieth flower is not a win yet.
    act(state,{0:dict(action_type='buhua')})
    assert state.machine.phase == P.FLOWER_WIN and state.round_settlement is None
    act(state,{0:dict(action_type='fuzhou_flower_win')})
    assert state.machine.phase == P.END and len(p.hand_tiles) == 17
    assert p.hand_tiles[-1] == 29 and len(p.huapai_list) == 21
    quote = state.round_settlement['score']
    assert quote['special'] == 'flower' and quote['pattern_points'] == 60
    assert quote['points'] == 10+21+10+60
    assert [e['action'] for e in state.domain_events[-4:]] == ['deal_buhua_tile','buhua','deal_buhua_tile','hu_self']


def test_three_gold_requires_own_completed_draw_not_an_idle_sixteen_tile_hand():
    state = state_with(WIN[:12]+[39]*3+[29])
    p = state.player_list[0]
    assert not state.can_three_gold(0)
    state.tiles_list = [18]+[11]*50
    state.machine.transition(P.RESPONSE)
    window = state.open_action_window(state.draw_for(0))
    assert 'sanjin' in window['actions'][0]
    assert state.round_settlement is None  # It is a choice, never forced.
    act(state,{0:dict(action_type='sanjin')})
    assert state.round_settlement['score']['special'] == 'three_gold'
    assert len(p.hand_tiles) == 17


@pytest.mark.parametrize('source',['discard','rob_kong','opening'])
@pytest.mark.parametrize('special',['three_gold','flower'])
def test_special_shape_wins_are_never_claimed_from_someone_elses_draw(source,special):
    hand = WIN[:12]+[39]*3+WIN[15:]
    flowers = [t for t in range(41,46) for _ in range(4)]
    assert book.score_hand(hand,flowers=flowers,jin=39,special=special,
        context=book.WinContext(source=source,winning_tile=29)) is None
