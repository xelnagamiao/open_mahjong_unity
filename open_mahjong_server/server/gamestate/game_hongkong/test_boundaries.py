"""Invalid input atomicity, human result windows, and reconnection boundaries."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_flow import make_state, open_round
from .test_branches import table, act
from .state_machine import HongKongPhase as P
from .get_action import handle_action
from .bot import play_bot, choose_action
from .action_check import claim_actions, ready_discards
from ...game_calculation.hongkong.models import PROFILES
from ...game_calculation.hongkong.ledger import Debt


def mutation_snapshot(state):
    return deepcopy((state.game_status, state.tiles_list, state.pending_kong,
        state.ledger.snapshot(), state.deferred_hu_settlements,
        [(p.hand_tiles, p.combination_tiles, p.discard_tiles, p.score) for p in state.player_list]))


@pytest.mark.parametrize('winners,source,tile,payer,error', [
    ([], 'self_draw', 11, None, 'winner seats'),
    ([0, 0], 'self_draw', 11, None, 'winner seats'),
    ([4], 'self_draw', 11, None, 'winner seats'),
    ([0, 1], 'self_draw', 11, None, 'seat-order'),
    ([0], 'invented', 11, None, 'source'),
    ([0], 'discard', 11, None, 'payer'),
    ([0], 'discard', 11, 0, 'payer'),
    ([0], 'discard', 11, 1, 'current discard'),
    ([0], 'rob_kong', 11, 1, 'pending kong'),
    ([0], 'self_draw', 47, None, 'cannot win'),
])
def test_invalid_settlement_is_atomic(winners, source, tile, payer, error):
    state, _ = table()
    before = mutation_snapshot(state)
    with pytest.raises(ValueError, match=error):
        state.settle_winners(winners, source, tile, payer=payer)
    assert mutation_snapshot(state) == before


def test_robbed_kong_requires_one_actual_tile_before_settlement():
    state, _ = table()
    state.pending_kong = dict(actor=1, tile=47)
    state.player_list[1].hand_tiles = [t for t in state.player_list[1].hand_tiles if t != 47]
    before = mutation_snapshot(state)
    with pytest.raises(ValueError, match='missing'):
        state.settle_winners([0], 'rob_kong', 47, payer=1)
    assert mutation_snapshot(state) == before


@pytest.mark.parametrize('changes', [[1, -1], [1, 0, 0, 0], [True, -1, 0, 0], [1.0, -1, 0, 0]])
def test_invalid_immediate_payment_does_not_partially_change_scores(changes):
    state, _ = table(PROFILES[2])
    before = mutation_snapshot(state)
    with pytest.raises(ValueError, match='zero-sum'):
        state.apply_immediate_points(changes, 'test')
    assert mutation_snapshot(state) == before


@pytest.mark.parametrize('action,data,error', [
    ('invented', {}, 'Illegal action'),
    ('angang', {'target_tile': True}, 'not in hand'),
    ('angang', {'target_tile': 999}, 'not in hand'),
    ('angang', {'target_tile': 11}, 'four physical'),
    ('jiagang', {'target_tile': 11}, 'exposed pung'),
])
def test_rejected_kong_never_mutates_table(action, data, error):
    state, _ = table(hands={0: [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 45, 45, 47]})
    before = mutation_snapshot(state)
    with pytest.raises(ValueError, match=error):
        state.validate_response(0, dict(action_type=action, **data), ['angang', 'jiagang'])
    assert mutation_snapshot(state) == before


@pytest.mark.parametrize('data,error', [
    ({'TileId': 11, 'cutClass': True}, 'Draw discard'),
    ({'TileId': 47, 'cutClass': False}, 'Held discard'),
    ({'TileId': 47, 'cutClass': True, 'cutIndex': 0}, 'Draw discard'),
    ({'TileId': 11, 'cutIndex': 2}, 'index does not identify'),
])
def test_held_and_drawn_physical_copy_rejections_are_atomic(data, error):
    state, window = table(hands={0: [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 45, 45, 47]})
    before = mutation_snapshot(state)
    with pytest.raises(ValueError, match=error):
        state.apply_action_results(window, {0: dict(action_type='cut', **data)})
    assert mutation_snapshot(state) == before


def test_untrusted_precomputed_settlement_and_non_action_phase_are_rejected():
    state, window = table()
    with pytest.raises(ValueError, match='authoritative'):
        state.apply_action_results(window, {}, settlements=[dict(points=100)])
    window = state.open_action_window(state.end_draw())
    with pytest.raises(ValueError, match='No action resolution'):
        state.apply_action_results(window, {})


def test_round_and_ledger_transitions_reject_wrong_phase():
    state, _ = table(PROFILES[2])
    with pytest.raises(RuntimeError, match='round-ready'):
        state.initialize_round()
    with pytest.raises(RuntimeError, match='result phase'):
        state.advance_round_after_ready()
    with pytest.raises(ValueError, match='between hands'):
        state.cut_pulls(1)
    state.end_draw()
    state.machine.transition(P.READY)
    with pytest.raises(ValueError, match='three-mouth'):
        state.cut_pulls(1)


def test_human_result_window_cuts_debt_then_requires_a_new_ready_tick():
    async def run():
        state, _ = table(PROFILES[2])
        state.ledger.debts[(1, 2)] = Debt(1, 2, 48, 3)
        state.end_draw()
        state.apply_deferred_score_changes()
        state.outbound_payloads.clear()
        sent = []
        async def flush():
            sent.extend(deepcopy(state.outbound_payloads))
            state.outbound_payloads.clear()
            for i in list(state.waiting_players_list):
                action = 'pull_cut' if 'pull_cut' in state.action_dict[i] else 'ready'
                await state.submit_action(i, action)
        state.flush_outbound_payloads = flush
        first_tick = state.server_action_tick
        await state.run_round_ready_phase(timeout=0.1)
        assert state.server_action_tick == first_tick + 2
        assert state.waiting_players_list == [] and not state.ledger.debts
        assert [p.score for p in state.player_list] == [0, 48, -48, 0]
        assert state.player_list[2].score_history[-1] == '-48'
        assert any(p.get('ready_status_info', {}).get('can_cut_pull') for p in sent)
        last = [p for p in sent if 'ready_status_info' in p][-4:]
        assert all(not p['ready_status_info']['can_cut_pull'] for p in last)
    asyncio.run(run())


def test_final_settlement_panels_cover_flower_hu_and_draw_restoration():
    state, _ = table(PROFILES[2])
    p = state.player_list[1]
    p.huapai_list = list(range(51, 59))
    state.settle_winners([1], 'self_draw', 0, flower='eight')
    payload = state.build_final_settlement_payload(2)
    info = payload['show_result_info']
    assert info['hongkong_flower_win'] == 'eight' and info['hepai_tile'] is None
    assert info['hepai_player_hand'] == p.hand_tiles
    assert info['hepai_player_huapai'] == p.huapai_list
    assert info['hongkong_fan_details'] == [dict(id='flower_win', name='两台花', value=40)]
    assert len(state.restore_payloads(2)) == 2
    assert state.build_pending_action_payload(2) is None
    other, _ = table()
    other.end_draw()
    info = other.build_final_settlement_payload(1)['show_result_info']
    assert info['score_changes'] == dict.fromkeys(range(4), 0)


def test_simultaneous_winner_panels_have_one_aggregate_payment_and_ordered_delivery():
    async def run():
        first = [11,12,13,21,22,23,31,32,33,45,45,45,41,17,18,19]
        second = [14,15,16,24,25,26,34,35,36,46,46,46,41,27,28,29]
        actor = [17,18,19,27,28,29,37,38,39,42,42,43,43,31,32,33,41]
        state, window = table(PROFILES[2], hands={0:actor, 1:first, 2:second})
        window = act(state, window, **{'0':dict(action_type='cut', TileId=41)})
        act(state, window, **{'1':'hu', '2':'hu'})
        send = state._send_claim_protection_payload = AsyncMock()
        with patch('asyncio.sleep', new_callable=AsyncMock) as sleep:
            await state.present_final_settlements()
        assert send.await_count == 8 and sleep.await_count == 1
        payloads = [call.args[1]['show_result_info'] for call in send.await_args_list]
        assert all(p['multi_ron'] for p in payloads)
        assert all(p['next_status'] == 'round_continue' for p in payloads[:4])
        assert all(p['next_status'] == 'round_end_by_ready' for p in payloads[4:])
    asyncio.run(run())


def test_missing_connections_stale_actions_and_late_spectators_are_safe():
    async def run():
        state, window = table()
        socket = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:state),
            players={'owner':SimpleNamespace(user_id=state.player_list[0].user_id)})
        await handle_action(server, 'absent', {}, socket)
        state.game_server = SimpleNamespace(user_id_to_connection={1:SimpleNamespace(websocket=None)})
        for user, index in ((1,-1), (1,4), (1,0), (2,0)):
            await state.send_realtime_spectator_snapshot(user, index)
        state.game_server = None
        await state.send_realtime_spectator_snapshot(1, 0)
        await state.player_reconnect(9999)
        state.send_payload_to_player = AsyncMock()
        await state.player_reconnect(state.player_list[0].user_id)
        assert state.send_payload_to_player.await_count == 2
        state.end_draw()
        await handle_action(server, 'owner', {'action_tick':-1}, socket)
        socket.send_json.assert_not_awaited()
        assert len(state.restore_payloads(1)) == 2
        state.machine.transition(P.READY)
        await handle_action(server, 'owner', {'action_tick':-1}, socket)
        assert socket.send_json.await_args.args[0]['type'].endswith('/ready_status')
    asyncio.run(run())


def test_duplicate_queued_input_is_drained_when_new_window_opens():
    async def run():
        state, window = table()
        await state.action_queues[0].put(dict(action_type='cut', TileId=999))
        state.open_action_window(state.begin_turn(0))
        assert state.action_queues[0].empty()
        state.end_draw()
        state.schedule_bot_actions()
        assert not state.bot_tasks
    asyncio.run(run())


def test_delayed_bot_cannot_submit_after_window_was_replaced():
    async def run():
        state, window = table()
        state.player_list[0].user_id = 1
        state.bot_speed = 'fast'
        task = asyncio.create_task(play_bot(state, 0, ['cut'], state.game_status, state.server_action_tick))
        await asyncio.sleep(0)
        state.open_action_window(state.begin_turn(0))
        await task
        assert state.action_queues[0].empty()
    asyncio.run(run())


def test_recording_outside_active_round_and_duplicate_finalization_are_noops():
    state = make_state()
    state.record_opening_complete()
    state.record_visible_action(dict(action='cut', player=0, tile=11))
    state.finalize_round_recording()
    open_round(state)
    state.end_draw()
    state.finalize_round_recording()
    before = deepcopy(state.game_record)
    state.finalize_round_recording()
    assert state.game_record == before


@pytest.mark.parametrize('profile',PROFILES[:2])
def test_fourth_called_meld_assigns_liability_and_exposed_thirteen_cannot_declare_ready(profile):
    state,window=table(profile,melds={1:['s12','s25','s38']},hands={1:[11,11,45,45]})
    actor=state.player_list[0]
    assert 11 in actor.hand_tiles
    window=act(state,window,**{'0':dict(action_type='cut',TileId=11)})
    assert 'peng' in window['actions'][1]
    window=act(state,window,**{'1':'peng'})
    assert state.player_list[1].liability_payer==state.player_list[0].original_player_index
    assert not ready_discards(state,1)


@pytest.mark.parametrize('lock',['passed_claim_tiles','discarded_since_draw'])
def test_old_liability_claim_is_blocked_by_same_turn_pass_or_own_discard(lock):
    state,_=table(melds={1:['s12','s25','s38']},hands={1:[11,11,45,45]})
    assert 'peng' in claim_actions(state,0,11)[1]
    getattr(state.player_list[1],lock).add(11)
    assert 'peng' not in claim_actions(state,0,11)[1]
    state.begin_new_draw(1,normal=True)
    assert 'peng' in claim_actions(state,0,11)[1]


def test_claim_that_leaves_only_swap_forbidden_tiles_is_not_offered():
    state,_=table(melds={1:['k25','k35','k45']},hands={1:[12,13,14,14]})
    assert 'chi_right' not in claim_actions(state,0,11)[1]


def test_old_third_dragon_pung_assigns_the_latest_payer():
    state,window=table(melds={1:['k45','k46']},
        hands={0:[11,12,13,21,22,23,31,32,33,41,41,42,42,47],1:[47,47,22,23,24,32,32]})
    state.player_list[1].liability_payer=3
    window=act(state,window,**{'0':dict(action_type='cut',TileId=47)})
    act(state,window,**{'1':'peng'})
    assert state.player_list[1].liability_payer==state.player_list[0].original_player_index


def test_sixteen_self_draw_collects_extra_dealer_streak_only_from_east():
    hand=[11,12,13,24,25,26,37,38,39,22,22,22,45,45,45,47,47]
    state,_=table(PROFILES[2],hands={1:hand},actor=1)
    state.dealer_streak=2
    state.settle_winners([1],'self_draw',47)
    payments=state.deferred_hu_settlements[-1]['pull_payments']
    by_payer={p['payer']:p['points'] for p in payments}
    assert by_payer[0]==by_payer[2]+5==by_payer[3]+5
    assert state.deferred_hu_settlements[0]['score_changes']==[0,0,0,0]


def test_multi_ron_with_east_keeps_dealer_but_resets_streak():
    state,_=table(PROFILES[2])
    state.dealer_streak=4
    state.end_draw()
    state.machine.transition(P.READY)
    state.deferred_hu_settlements=[{'winner':0},{'winner':2}]
    state.advance_round_after_ready()
    assert state.dealer_streak==0 and state.current_round==1 and state.round_index==2


def test_sixteen_draw_keeps_dealer_streak_and_human_result_timeout_advances():
    async def run():
        state,_=table(PROFILES[2])
        state.dealer_streak=2
        state.end_draw()
        state.flush_outbound_payloads=AsyncMock()
        await state.run_round_ready_phase(timeout=0)
        assert not state.waiting_players_list
        state.advance_round_after_ready()
        assert state.current_round==1 and state.round_index==2 and state.dealer_streak==2
    asyncio.run(run())


def test_protocol_restore_before_first_window_and_payload_without_game_info():
    state=make_state()
    state.initialize_round()
    assert len(state.restore_payloads(1))==1
    assert state._adapt({'type':'noop'},1)=={'type':'noop','player_index':1}
    window=state.begin_turn(0)
    state.open_action_window(window)
    payload=state._ask_payload(0,{**window,'player':None})
    assert payload['ask_hand_action_info']['player_index']==0


def test_old_bot_declines_chows_when_preserving_concealed_fan():
    state,window=table(hands={1:[14,15,21,23,25,27,29,31,33,35,37,39,41]})
    state.live_pending_window={'tile':13}
    assert choose_action(state,1,['chi_right','pass'])=={'action_type':'pass'}
    assert choose_action(state,1,['pass'])=={'action_type':'pass'}


@pytest.mark.parametrize('hu',[False,True])
def test_legacy_empty_river_flags_still_support_claim_or_win(hu):
    state,window=table(PROFILES[1],hands={0:[11,12,13,21,22,23,31,32,33,41,41,42,42,45],
        1:[14,15,16,24,25,26,34,35,36,45,45,46,46]})
    window=act(state,window,**{'0':dict(action_type='cut',TileId=45)})
    state.player_list[0].discard_riichi_flags.clear()
    act(state,window,**{'1':'hu' if hu else 'peng'})
    assert not state.player_list[0].discard_tiles
    assert state.machine.phase==(P.END if hu else P.DISCARD_ONLY)


def test_sixteen_rob_added_kong_and_recorded_hu_event_is_idempotent():
    state,window=table(PROFILES[2],melds={0:['k15']},
        hands={0:[12,13,14,21,22,23,31,32,33,41,24,25,26,15],
            1:[13,14,21,22,23,31,32,33,46,46,46,41,41,34,35,36]})
    window=act(state,window,**{'0':dict(action_type='jiagang',target_tile=15)})
    act(state,window,**{'1':'hu'})
    assert state.player_list[0].combination_tiles==['k15']
    assert state.deferred_hu_settlements[0]['source']=='rob_kong'
    before=deepcopy(state.game_record)
    event=next(e for e in state.domain_events if e['action'].startswith('hu'))
    state.record_visible_action(event)
    assert state.game_record==before
