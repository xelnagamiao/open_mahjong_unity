"""The four-player result is one authoritative, replayable board snapshot."""
import asyncio
import json
from copy import deepcopy
from unittest.mock import AsyncMock, patch

import pytest

from .test_doubles import make_state, settle
from .test_doubles_kong_settlement import kong
from .board_settlement import match_summary
from ..public.settlement_sync import restore_result
from .test_duplicate import pair


def finish(state, messages, *, already_finished=False):
    if not already_finished:
        with patch('asyncio.sleep', new_callable=AsyncMock):
            asyncio.run(state._settle_liuju())
    packets = [m['message']['show_result_info'] for m in messages
               if m['message'].get('show_result_info', {}).get('liuju_step') == 'board_summary']
    assert len(packets) == 4
    result = packets[0]['sichuan_settlement']
    assert all(p['sichuan_settlement'] == result for p in packets)
    assert all(p['liuju_status_final'] and not p.get('sichuan_grouped_endgame') for p in packets)
    changes = {int(i): value for i, value in result['changes'].items()}
    for seat in range(4):
        assert sum(event['changes'].get(str(seat), event['changes'].get(seat, 0)) for event in result['events']) == changes[seat]
    assert result['team_changes'] == [sum(changes[p.player_index] for p in state.player_list if p.team_id==t) for t in (0,1)]
    return result


@pytest.mark.parametrize('kind,payer,expected', [
    ('xiayu2', None, [4,-2,0,-2]), ('xiayu1',None,[2,-1,0,-1]),
    ('guafeng',1,[2,-2,0,0]), ('guafeng',2,[0,0,0,0]), ('non_timely',None,[0,0,0,0]),
])
def test_one_snapshot_contains_unpaid_and_paid_kongs(kind,payer,expected):
    state,messages=make_state()
    kong(state,kind,payer)
    for i in (0,2): state.player_list[i].is_hu=True
    result=finish(state,messages)
    assert list(result['changes'].values()) == expected
    assert len(result['events']) == 1
    assert result['events'][0]['kind']=='kong'
    assert result['events'][0]['points']==max([-value for value in expected if value<0],default=0)
    assert result['events'][0]['per_payer']==(len([value for value in expected if value<0])>1)
    if not any(expected):
        assert result['events'][0]['melds']==[[0,11]*4]
    assert len(result['players'])==4
    records=state.game_record['game_round'][f'round_index_{state.round_index}']['action_ticks']
    assert records[-1][:2]==['liuju','board_summary']
    assert json.loads(records[-1][2])['team_changes']==result['team_changes']


def test_three_winners_share_one_page_and_teammate_win_has_zero_transfer():
    state,messages=make_state()
    settle(state,[0],discarder=2)
    settle(state,[1],discarder=3)
    settle(state,[2],discarder=3)
    result=finish(state,messages,already_finished=True)
    wins=[e for e in result['events'] if e['kind']=='ron']
    assert len(wins)==3
    assert not any(wins[0]['changes'].values())
    assert sum(p['status']=='hu' for p in result['players'])==3


def test_no_ting_refund_is_visible_and_never_pays_then_refunds_again():
    state,messages=make_state()
    kong(state,'xiayu2')
    action_snapshot=deepcopy(state.player_list[0].gang_score_records[0]['presentation'])
    state.player_list[0].hand_tiles=[11,14,17,21,24,27,29,11,15,19]
    result=finish(state,messages)
    row=next(e for e in result['events'] if e['kind']=='kong')
    assert '未听退税' in row['detail'] and not any(row['changes'].values())
    assert any(e['kind']=='chajiao' for e in result['events'])
    assert state.player_list[0].combination_mask==[[0,11]*4]
    assert row['melds']==[[0,11]*4]
    assert row['hand']==action_snapshot['hand']
    assert state.player_list[0].gang_score_records[0]['presentation']==action_snapshot


def test_latest_reconnect_packet_is_entire_board_and_old_steps_are_grouped():
    state,messages=make_state()
    result=finish(state,messages)
    state.game_status='waiting_ready'
    for seat in range(4):
        restored=restore_result(state,seat)
        assert restored['show_result_info']['sichuan_settlement']==result
        assert restored['settlement_sync']['restore']
    steps=[m['message']['show_result_info'] for m in messages if m['message'].get('show_result_info',{}).get('liuju_step')=='chajiao']
    assert steps and all(s['sichuan_grouped_endgame'] for s in steps)


def test_match_summary_has_individuals_team_scores_and_round_drilldown():
    state,messages=make_state()
    kong(state,'xiayu2')
    for i in (0,2):state.player_list[i].is_hu=True
    finish(state,messages)
    state.team_result={'scores':{0:4,1:-4},'match_points':{0:1,1:0},'reason':'score'}
    summary=match_summary(state)
    assert len(summary['players'])==4 and len(summary['tables']['single'])==1
    assert sum(p['score'] for p in summary['players'] if p['team_id']==0)==4
    assert summary['players'][0]['history']==[4]


def test_zero_settlement_still_has_all_players_and_confirmation():
    state,messages=make_state()
    result=finish(state,messages)
    assert len(result['events'])==4 and result['team_changes']==[0,0]
    assert [e['seat'] for e in result['events']]==[0,2,1,3]
    assert all(e['phase']=='check' and not any(e['changes'].values()) for e in result['events'])
    assert all(p['status']=='ting' for p in result['players'])


@pytest.mark.parametrize('synchronous', [False, True])
def test_duplicate_final_contains_both_tables_only_after_both_finish(synchronous):
    paired, _ = pair(count=1, sync_mode=synchronous)
    opened, open_messages = make_state()
    closed, closed_messages = make_state()
    for player in closed.player_list:
        player.team_id = 1 - player.team_id
        player.username = '闭室' + str(player.player_index)
    kong(opened, 'xiayu2')
    for i in (0, 2): opened.player_list[i].is_hu = True
    open_board = finish(opened, open_messages)
    close_board = finish(closed, closed_messages)
    opened.duplicate_match = closed.duplicate_match = paired
    assert match_summary(opened) is None

    async def complete():
        first = asyncio.create_task(paired.submit('open', 1, open_board['team_changes'], [0,0], {},
                                    {'game_record': deepcopy(opened.game_record)}))
        await asyncio.sleep(0)
        assert match_summary(closed) is None
        await paired.submit('closed', 1, close_board['team_changes'], [0,0], {},
                            {'game_record': deepcopy(closed.game_record)})
        await first
    asyncio.run(complete())
    public = paired.public_info()
    for state in (opened, closed):
        state.team_result = dict(scores=dict(enumerate(public['scores'])),
                                 match_points={0:1,1:0}, reason='imp')
    result = match_summary(opened)
    assert result == match_summary(closed)
    assert len(result['players']) == 8
    assert {p['table'] for p in result['players']} == {'open','closed'}
    assert result['scores'] == result['duplicate_info']['imp_scores']
    assert result['tables']['open'][0]['changes'] == {'0':4,'1':-2,'2':0,'3':-2}
    assert result['tables']['closed'][0]['changes'] == {'0':0,'1':0,'2':0,'3':0}
    assert len([p for p in result['players'] if p['team_id']==0]) == 4


def test_summary_keeps_nonzero_prior_boards_and_original_wind_identity():
    state, messages = make_state()
    kong(state,'xiayu2')
    for p, score in zip(state.player_list, [3,-1,4,-6]): p.score=score
    for i in (0,2):state.player_list[i].is_hu=True
    result=finish(state,messages)
    assert result['team_totals']==[11,-11] and result['team_changes']==[4,-4]
    assert result['scores_before']=={0:3,1:-1,2:4,3:-6}
    assert [p['original_seat'] for p in result['players']]==[0,1,2,3]


def test_timeline_interleaves_kongs_and_wins_and_keeps_action_hands():
    state,messages=make_state()
    state.dealer_index=3
    kong(state,'xiayu2')
    original=list(state.player_list[0].hand_tiles)
    original_meld=deepcopy(state.player_list[0].combination_mask)
    settle(state,[1],discarder=3)
    p=state.player_list[2]
    p.combination_tiles=['G21'];p.combination_mask=[[2,21,0,21,0,21,2,21]]
    state._record_gang_score(2,21,'xiayu2')
    settle(state,[0],discarder=3)
    result=finish(state,messages)
    played=[e for e in result['events'] if e['phase']=='play']
    assert [(e['kind'],e['seat']) for e in played]==[('kong',0),('ron',1),('kong',2),('ron',0)]
    assert [e['order'] for e in played]==[1,2,3,4]
    assert [e['hu_order'] for e in played if e['kind']=='ron']==[1,2]
    assert played[0]['hand']==original and played[0]['melds']==original_meld
    assert played[-1]['hand']==state.player_list[0].hand_tiles
    assert played[-1]['win_tile']==25
    assert result['dealer_seat']==3 and result['version']==2
    assert result['presentation_ms']==sum(e['duration_ms'] for e in result['events'])
    assert [e['duration_ms'] for e in played]==[2500,3000,2500,3000]
    state.player_list[0].hand_tiles.clear()
    assert played[0]['hand']==original


@pytest.mark.parametrize('teams',[[0,1,0,1],[1,0,1,0]])
def test_all_unwon_players_are_checked_in_visual_left_to_right_order(teams):
    state,messages=make_state()
    for p,t in zip(state.player_list,teams):p.team_id=t
    state.player_list[2].hand_tiles=[11,14,17,21,24,27,31,34,37,11,15,25,35]
    state.player_list[2].discard_tiles=[11,21]
    result=finish(state,messages)
    order=sorted(range(4),key=lambda i:(teams[i],i))
    assert [e['seat'] for e in result['events'] if e['phase']=='check']==order
    assert len(result['events'])==4
    assert all(e['hand'] and 'melds' in e for e in result['events'])


def test_ready_budget_includes_timeline_but_does_not_change_bloodflow():
    from .board_settlement import ready_seconds
    state,messages=make_state()
    result=finish(state,messages)
    assert result['presentation_ms'] >= 2500 * len(result['events'])
    assert ready_seconds(state)==8+result['presentation_ms']/1000
    state.is_xueliu=True
    assert ready_seconds(state)==8
    state._reset_round_state()
    assert state._sbr_event_order==0 and state._sbr_presentation_ms==0
