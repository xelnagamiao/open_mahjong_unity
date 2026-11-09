from types import SimpleNamespace

import pytest

from server.gamestate.public.match_result import attach_match_result, build_match_result


def fixture(rule='guobiao', sub_rule='guobiao/standard', count=4, room_type='custom'):
    players = [dict(user_id=100+i, username=f'P{i}', original_player_index=i, rank=i+1, score=20-i*10)
               for i in range(count)]
    title = dict(rule=rule, sub_rule=sub_rule, room_type=room_type, end_time='2026-11-05 22:00:00')
    title.update({f'p{i}_uid': p['user_id'] for i, p in enumerate(players)})
    return dict(game_title=title, game_round={}), players


def test_custom_zero_and_seat_identity_survive_rotations_and_duplicate_bots():
    record, players = fixture()
    for p in players:
        p['user_id'] = 1
        record['game_title'][f"p{p['original_player_index']}_uid"] = 1
    result = build_match_result(record, list(reversed(players)))
    assert [p['original_player_index'] for p in result['players']] == [0, 1, 2, 3]
    assert all(p['rating_pt'] == 0 and p['rating_status'] == 'not_applicable' for p in result['players'])


def test_ranked_writer_rejects_missing_rating():
    record, players = fixture(room_type='match')
    with pytest.raises(ValueError, match='缺少玩家'):
        build_match_result(record, players)


def test_three_player_match_and_team_imp_are_independent_metrics():
    record, players = fixture('riichi', 'riichi/sanma', 3)
    assert build_match_result(record, players)['player_count'] == 3
    record, players = fixture('sichuan', 'sichuan/doubles_duplicate')
    record['game_title'].update(team_by_player_index=[0,1,0,1],
        doubles_result=dict(reason='imp', scores={0:8,1:-8}, imp_scores={0:8,1:-8},
                            small_scores={0:42,1:-42}, match_points={0:1,1:0}))
    result = build_match_result(record, players)
    assert [p['team_imp_score'] for p in result['players']] == [8,-8,8,-8]
    assert all(p['rating_pt'] == 0 for p in result['players'])


def test_live_snapshot_uses_final_player_rank_and_score():
    record, players = fixture()
    live = [SimpleNamespace(**{k:v for k,v in p.items() if k != 'rank'},
                            record_counter=SimpleNamespace(rank_result=p['rank'])) for p in players]
    attach_match_result(record, live)
    assert record['match_result']['players'][0]['score'] == 20
    live[0].score = -999
    assert record['match_result']['players'][0]['score'] == 20
