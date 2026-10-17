"""Outcome tests for captain victory, ordinary payments, deposits and privacy."""
import asyncio
import random
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_protocol import connected
from . import doubles
from .init_tiles import init_riichi_tiles
from .boardcast import broadcast_game_start, broadcast_game_end
from .rule_logic import finalize_scores
from .wait_action import _commit_pending_riichi, wait_action, _is_valid_cut_action
from .action_check import _normalize, check_hepai
from ..public.ai.get_action import get_ai_action
from ..public.ai.smart_bot_logic import find_best_cut, count_visible_tiles
from ...game_calculation.riichi.washizu_doubles import SUB_RULE
from ...game_calculation.riichi.washizu_tiles import make_tiles


def test_shared_web_profile_cannot_drift_from_server_rules():
    import json
    from pathlib import Path
    from ...game_calculation.riichi.washizu_doubles import FIXED_OPTIONS, ROLE_LABELS
    root = Path(__file__).resolve().parents[4]
    profile = json.loads((root / 'open_mahjong_web/shared/washizuDoubles.json').read_text(encoding='utf-8'))
    assert profile['sub_rule'] == SUB_RULE
    assert profile['detailed_config'] == FIXED_OPTIONS
    assert profile['roles'] == list(ROLE_LABELS)


def test_invalid_roster_and_deposit_ledger_fail_before_silent_wrong_settlement():
    g, _ = captain_game()
    seats = [p.user_id for p in g.player_list]
    for roles in ([0, 0, 2, 3], [0, 1, 2], None):
        with pytest.raises(ValueError):
            doubles.configure(g, dict(seat_list=seats, player_list=seats, sub_rule=SUB_RULE, team_role_by_seat=roles))
    with pytest.raises(ValueError, match='各一人'):
        doubles.configure(g, dict(seat_list=seats[:3] + [-1], sub_rule=SUB_RULE,
                                 team_role_by_seat=[0, 1, 2, -1]))
    wrong = dict(seat_list=[900, 901, 902, 903], sub_rule=SUB_RULE, team_role_by_seat=[0, 1, 2, 3])
    with pytest.raises(ValueError, match='玩家与房间座位不一致'):
        doubles.configure(g, wrong)
    g.player_list.pop()
    with pytest.raises(ValueError, match='四名玩家'):
        doubles.configure(g, dict(seat_list=seats, sub_rule=SUB_RULE, team_role_by_seat=[0, 1, 2, 3]))
    g, _ = captain_game()
    g.riichi_sticks = 1
    before = [p.score for p in g.player_list]
    with pytest.raises(ValueError, match='供托账目不一致'):
        doubles.finalize(g)
    assert [p.score for p in g.player_list] == before and not getattr(g, '_riichi_finalized', False)


def test_captain_hooks_preserve_an_ordinary_riichi_game():
    g, _ = connected('majsoul')
    before = list(g.player_list)
    doubles.configure(g, {})
    rng = random.Random(123)
    doubles.shuffle_players(g, rng)
    expected = list(before); random.Random(123).shuffle(expected)
    assert g.player_list == expected
    doubles.record_profile(g); doubles.record_round(g)
    doubles.accepted_deposit(g, before[0]); doubles.collected_deposits(g); doubles.refund_current_deposit(g, before[0])
    assert doubles.public_state(g) is None
    assert 'washizu_doubles_profile' not in g.game_record.get('game_title', {})


def captain_game(roles=None):
    g, sockets = connected('majsoul')
    g.sub_rule = SUB_RULE
    room = {'player_list': [p.user_id for p in g.player_list], 'sub_rule': SUB_RULE,
            'seat_list': [p.user_id for p in g.player_list], 'team_role_by_seat': roles or [0, 1, 2, 3]}
    doubles.configure(g, room)
    g.red_dora = False; g.hepai_way = 'head_bump'; g.game_record.setdefault('game_title', {})
    for p in g.player_list: p.hand_tiles = []; p.has_draw_slot = False
    init_riichi_tiles(g)
    doubles.record_profile(g)
    return g, sockets


@pytest.mark.parametrize('seed', range(16))
def test_random_dealer_and_rotations_keep_captains_opposite_and_assistant_next(seed):
    g, _ = captain_game([3, 0, 2, 1])
    doubles.shuffle_players(g, random.Random(seed))
    for i, p in enumerate(g.player_list): p.player_index = p.original_player_index = i
    ids = {p.user_id: p.team_role_id for p in g.player_list}
    for _ in range(9):
        for i, p in enumerate(g.player_list):
            if p.team_role == 'captain':
                assert g.player_list[(i+1)%4].team_role == 'assistant'
                assert g.player_list[(i+1)%4].team_id == p.team_id
                assert g.player_list[(i+2)%4].team_role == 'captain'
        assert {p.user_id: p.team_role_id for p in g.player_list} == ids
        g._rotate_seats()


@pytest.mark.parametrize('scores,ending,winner', [
    ([25000,-50000,25000,100000], False, None),
    ([0,25000,50000,25000], False, 1),
    ([-100,25100,50000,25000], True, 1),
    ([50000,25000,-100,25100], True, 0),
    ([-100,60000,-200,40300], True, 0),
    ([-100,50100,-100,50100], True, None),
    ([30000,0,20000,50000], False, 0),
])
def test_only_captain_scores_decide_bust_and_match_result(scores, ending, winner):
    g, _ = captain_game()
    for p, score in zip(g.player_list, scores): p.score = score
    assert g._riichi_match_should_end(False, 2) is ending
    assert g._riichi_match_should_end(False, 9)
    finalize_scores(g); g._assign_final_ranks()
    assert g.team_result['winner_team_id'] == winner
    assert sum(g.team_result['match_points'].values()) == 1
    assert all(p.riichi_points == g.team_result['match_points'][p.team_id] for p in g.player_list)


@pytest.mark.parametrize('score,allowed', [(-1000,False),(0,False),(999,False),(1000,True)])
def test_assistants_may_be_negative_but_cannot_borrow_riichi(score, allowed):
    g, _ = captain_game()
    assert g._can_declare_riichi_by_score(score) is allowed


def deposit(g, seat):
    p = g.player_list[seat]
    p.pending_riichi = True; p.riichi_paid_this_round = False
    _commit_pending_riichi(g)


def test_deposit_ledger_survives_rotation_and_refunds_once_to_contributors():
    g, _ = captain_game()
    owners = {p.user_id: p.score for p in g.player_list}
    deposit(g, 0); deposit(g, 1)
    _commit_pending_riichi(g)  # Accepted declarations must be idempotent.
    g._rotate_seats()
    deposit(g, 3)  # Same original captain contributes in another hand.
    assert g.riichi_sticks == 3 and len(g.riichi_contributors) == 3
    finalize_scores(g); finalize_scores(g)
    assert {p.user_id: p.score for p in g.player_list} == owners
    assert g.team_result['deposit_refunds'] == {0:2000,1:1000,2:0,3:0}
    assert g.riichi_sticks == 0 and g.riichi_contributors == []


def test_refund_does_not_reverse_a_settled_captain_bust():
    g, _ = captain_game(); deposit(g, 0)
    g.player_list[0].score = -100
    assert g._riichi_match_should_end(False, 2)
    finalize_scores(g)
    assert g.player_list[0].score == 900
    assert g.team_result['winner_team_id'] == 1 and g.team_result['reason'] == 'captain_bust'


@pytest.mark.parametrize('tsumo', [False, True])
def test_teammate_ron_and_tsumo_pay_normally_and_collect_deposits(tsumo):
    async def run():
        g, _ = captain_game()
        deposit(g, 3)
        before = [p.score for p in g.player_list]
        g.current_player_index = 0 if tsumo else 1
        g.ron_player_index = 0; g.hu_class = 'hu_self' if tsumo else 'hu_third'
        g.result_dict[g.hu_class] = {'han':3,'fu':40,'yaku':['混一色'],'aka_count':0,
            'score':12000,'cost':{'main':4000 if tsumo else 8000,'additional':4000}}
        # Use production calculator result shape; UI waits are outside settlement.
        g.result_dict[g.hu_class]['cost'] = {'main':4000 if tsumo else 8000,'additional':4000}
        with patch('server.gamestate.game_riichi.RiichiGameState.broadcast_result', new=AsyncMock()):
            await g._settle_hu()
        changes = [p.score - old for p, old in zip(g.player_list, before)]
        assert changes == ([13000,-4000,-4000,-4000] if tsumo else [9000,-8000,0,0])
        assert g.riichi_sticks == 0 and not g.riichi_contributors
        final_before = [p.score for p in g.player_list]
        finalize_scores(g)
        assert [p.score for p in g.player_list] == final_before
        assert sum(final_before) == 100000
    asyncio.run(run())


def test_start_reconnect_spectator_and_final_protocol_keep_roles_and_hide_ally_opaque_tiles():
    async def run():
        g, sockets = captain_game()
        for p in g.player_list: p.hand_tiles = [2011+p.player_index,41+p.player_index]
        await broadcast_game_start(g)
        for uid, socket in sockets.items():
            info = socket.websocket.send_json.call_args_list[-1].args[0]['game_info']
            assert info['washizu_doubles']['team_role_by_player_index'] == ['captain','assistant','captain','assistant']
            assert [(p['team_id'],p['team_role']) for p in info['players_info']] == [(0,'captain'),(0,'assistant'),(1,'captain'),(1,'assistant')]
            assert sum('hand_tiles' in p for p in info['players_info']) == 1
            assert all(hand[-1] == 0 for hand in info['washizu_hands'].values())
        g.game_status = 'waiting_hand_action'; g.action_dict = {i:[] for i in range(4)}
        await g.player_reconnect(101)
        spectator = SimpleNamespace(websocket=AsyncMock())
        g.game_server.user_id_to_connection[500] = spectator
        await g.send_realtime_spectator_snapshot(500, 1)
        info = spectator.websocket.send_json.call_args_list[0].args[0]['game_info']
        assert info['washizu_doubles']['captain_scores'] == {0:25000,1:25000}
        assert sum('hand_tiles' in p for p in info['players_info']) == 1
        finalize_scores(g); g._assign_final_ranks(); await broadcast_game_end(g)
        for uid in range(100, 104):
            socket = sockets[uid]
            final = socket.websocket.send_json.call_args_list[-1].args[0]['game_end_info']
            assert final['team_result']['winner_team_id'] is None
            assert len(final['player_final_data']) == 4
            assert all(p['team_match_points'] == .5 for p in final['player_final_data'].values())
    asyncio.run(run())


def test_assistant_head_bumps_enemy_captain_and_furiten_still_blocks_teammate_ron():
    from .ron_resolution import should_interrupt_wait_for_action
    from .wait_action import _execute_cut
    async def run():
        g, _ = captain_game(); g.current_player_index = 0
        # Captain A discards: their assistant acts before captain B under head-bump.
        g.result_dict = {'hu_first':dict(han=1,no_yaku=False), 'hu_second':dict(han=1,no_yaku=False)}
        assert should_interrupt_wait_for_action(g, 'hu_first', [2], {2:['hu_second','pass']})
        assert not should_interrupt_wait_for_action(g, 'hu_second', [1], {1:['hu_first','pass']})
        p = g.player_list[1]
        p.hand_tiles = [12,13,14,22,23,24,33,34,35,36,37,38,28]
        p.waiting_tiles = {28}; p.tag_list = ['riichi']; p.temp_furiten = True
        g.player_list[0].hand_tiles = [2028]; g.player_list[0].has_draw_slot = True
        assert await _execute_cut(g, 0, 2028, True, None, False)
        assert not any(a.startswith('hu_') for a in g.action_dict[1])
    asyncio.run(run())


def test_chombo_refunds_current_deposit_and_captain_bust_ends_match():
    async def run():
        g, _ = captain_game(); deposit(g, 1)
        g.player_list[0].score = 1000
        g.hu_class = 'hu_self'; g.current_player_index = 0; g.open_cuohe = True
        g.result_dict = {'hu_self':dict(han=0,fu=30,no_yaku=True,yaku=[])}
        with patch('server.gamestate.game_riichi.RiichiGameState.broadcast_result', new=AsyncMock()):
            await g._settle_cuohe()
        assert g.player_list[0].score == -8000 and g.next_status == 'match_end'
        assert g.riichi_sticks == 0 and not g.riichi_contributors
        finalize_scores(g)
        assert g.team_result['winner_team_id'] == 1
    asyncio.run(run())


@pytest.mark.parametrize('kind', ['jiuzhongjiupai','four_wind_abort','four_kan_abort','four_riichi_abort'])
def test_abortive_last_hand_is_replayed_without_ending_match(kind):
    async def run():
        g, _ = captain_game(); g.current_round = 8; g.hu_class = kind
        with patch('server.gamestate.game_riichi.RiichiGameState.broadcast_result', new=AsyncMock()):
            await g._settle_round()
        assert g.next_status == 'round_end_by_ready' and g._compute_last_renchan()
    asyncio.run(run())


@pytest.mark.parametrize('seed', [20261017, 12345, 9876])
def test_full_half_match_conserves_tiles_scores_roles_and_replays(seed, caplog):
    from ..verifier.record_sim.goto_action import RecordSim, stringify_tick
    async def run():
        g, _ = captain_game(); g.max_round = 2; g.room_random_seed = seed
        for p in g.player_list: p.hand_tiles = []; p.has_draw_slot = False
        g.game_server.gamestate_manager = SimpleNamespace(cleanup_game_state_complete=AsyncMock())
        g.game_server.room_manager = SimpleNamespace(finish_custom_game_room=AsyncMock())
        g.game_server.user_id_to_connection = {}
        original_sleep = asyncio.sleep; actions = Counter()
        async def instant_sleep(_): await original_sleep(0)
        async def automated_wait():
            task = asyncio.create_task(wait_action(g)); await original_sleep(0)
            try:
                for seat in list(g.waiting_players_list):
                    p = g.player_list[seat]; choices = g.action_dict.get(seat, [])
                    if not choices: continue
                    action = next((a for a in choices if a.startswith('hu_') and not g.result_dict.get(a, {}).get('no_yaku')), None)
                    tile = target = None
                    if action is None and 'riichi_cut' in choices:
                        action = 'riichi_cut'; tile = next(iter(p.riichi_candidate_cuts))
                    if action is None:
                        for candidate in ('angang','jiagang'):
                            if candidate not in choices: continue
                            for value in p.hand_tiles:
                                if _is_valid_cut_action(g, seat, dict(action_type=candidate, target_tile=_normalize(value))):
                                    action = candidate; target = _normalize(value); break
                            if action: break
                    if action is None and 'cut' in choices:
                        action = 'cut'
                        tile = p.hand_tiles[-1] if 'riichi' in p.tag_list else find_best_cut(
                            p.hand_tiles, len(p.combination_tiles), count_visible_tiles(g, seat), set(map(_normalize, p.kuikae_forbidden_tiles)))[0]
                    if action is None:
                        action = next((a for a in ('gang','peng','chi_left','chi_mid','chi_right','pass','ready') if a in choices), choices[0])
                    actions[action] += 1
                    await get_ai_action(g, seat, action, bool(p.has_draw_slot and tile == p.hand_tiles[-1]), tile, None, target)
                await task
            finally:
                if not task.done(): task.cancel()
            if g.game_status not in ('END','waiting_ready'):
                physical = list(g.tiles_list) + g.dora_indicators + g.kan_dora_indicators
                for p in g.player_list:
                    physical += p.hand_tiles + p.discard_tiles
                    for mask in p.combination_mask: physical += mask[1::2]
                assert Counter(physical) == Counter(make_tiles())
            assert sum(p.score for p in g.player_list) + g.riichi_sticks * 1000 == 100000
            assert len(g.riichi_contributors) == g.riichi_sticks
        g.wait_action = automated_wait
        with patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep', instant_sleep), \
             patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase', new=AsyncMock()), \
             patch('server.gamestate.game_riichi.RiichiGameState.vote_checkpoint', new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_riichi(), 90)
        assert actions['cut'] > 30 and g.team_result is not None
        assert g.game_record['game_title']['washizu_doubles_profile']['version'] == 'washizu-captain-v1'
        assert g.game_record['doubles_result'] == g.team_result
        assert len(g.game_record['doubles_rounds']) == len(g.game_record['game_round'])
        assert g.team_result['reason'] == 'captain_bust' or g.current_round == 9
        sim = RecordSim(g.game_record)
        for key, record in g.game_record['game_round'].items():
            sim.load_round(key)
            assert sim.snapshot()['remain_tiles'] == 70
            for tick in record['action_ticks']: sim.apply_tick(stringify_tick(tick))
            assert record['action_ticks'][-1] == ['end']
        assert not any(r.levelname == 'ERROR' for r in caplog.records)
    asyncio.run(run())
