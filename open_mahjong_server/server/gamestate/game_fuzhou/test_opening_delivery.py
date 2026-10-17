"""Opening batching changes pacing, never seat order, privacy or replay steps."""
import asyncio
from collections import Counter
import time
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .FuzhouGameState import FuzhouGameState
from .state_machine import Phase as P
from .test_state import act, state_with, WIN
from ...game_calculation.fuzhou import rules as book


def opening_fixture(actor=0, replacement=25, *, drawn=False):
    s = FuzhouGameState(calculation_service=object())
    s.initialize_round()
    s.dealer_index = actor
    for i, p in enumerate(s.player_list):
        p.hand_tiles = [11,12,13]*5+[29]
        p.has_draw_slot = i == actor
    s.player_list[actor].hand_tiles.insert(16 if drawn else 0, 41)
    s.player_list[(actor+1)%4].hand_tiles[0] = 42
    s.tiles_list = [21]*45+[replacement]
    s.start_game_recording(); s.start_round_recording()
    s.open_action_window(s.opening_window())
    s.outbound_payloads.clear()
    return s


@pytest.mark.parametrize('actor', range(4))
@pytest.mark.parametrize('replacement', [25, 43])
@pytest.mark.parametrize('drawn', [False, True])
def test_combined_opening_draw_is_private_and_keeps_two_record_steps(actor, replacement, drawn):
    s = opening_fixture(actor, replacement, drawn=drawn)
    act(s, {actor: dict(action_type='buhua')})
    packets = [p for p in s.outbound_payloads if 'do_action_info' in p]
    assert len(packets) == 4
    assert s.outbound_payloads[4] == {'_pause': .3}
    assert sum('_pause' in p for p in s.outbound_payloads) == 1
    assert all('do_action_info' in p for p in s.outbound_payloads[:4])
    assert all('do_action_info' not in p for p in s.outbound_payloads[5:])
    for viewer, packet in enumerate(packets):
        info = packet['do_action_info']
        assert info['action_list'] == ['buhua', 'deal_buhua_tile']
        assert info['buhua_tile'] == 41 and info['is_mo_buhua'] == drawn
        assert info['deal_tile'] == (replacement if viewer == actor else None)
        players = packet['game_info']['players_info']
        assert players[actor]['hand_tiles_count'] == 17
        assert players[actor]['huapai_list'] == [41]
        for i, player in enumerate(players):
            assert player['hand_tiles'] == (s.player_list[i].hand_tiles if i == viewer else None)
    assert s.live_pending_window['player'] == (actor+1)%4
    assert [e['action'] for e in s.domain_events[-2:]] == ['buhua','deal_buhua_tile']
    ticks = s.game_record['game_round']['round_index_1']['action_ticks']
    assert len([t for t in ticks if t[0] == 'bh']) == 1
    assert len([t for t in ticks if t[0] == 'bd']) == 1
    assert s.player_list[actor].hand_tiles[-1] == replacement


def test_opening_outbox_delivers_combined_pair_then_paces_the_next_seat():
    from ..public.outbound_pipe import close_outbound_pipes, drain_viewer

    async def run():
        s = opening_fixture()
        received = {i: [] for i in range(4)}
        connections = {}
        for i, p in enumerate(s.player_list):
            async def send(payload, viewer=i):
                received[viewer].append((time.monotonic(), payload))
            connections[p.user_id] = NS(websocket=NS(send_json=send))
        s.game_server = NS(user_id_to_connection=connections)
        s.send_to_realtime_spectators = AsyncMock()
        act(s, {0: dict(action_type='buhua')})
        try:
            await s.flush_outbound_payloads()
            await asyncio.gather(*(drain_viewer(s, i) for i in range(4)))
            for messages in received.values():
                pair_time, pair = messages[0]
                assert pair['do_action_info']['action_list'] == ['buhua','deal_buhua_tile']
                ask_time, ask = messages[1]
                assert 'ask_hand_action_info' in ask
                assert ask_time-pair_time >= .29
                assert len(messages) == 2
            # The next player's clock begins after the server's pacing gap.
            assert s.action_clocks[1].start_time >= received[1][0][0]+.29
        finally:
            close_outbound_pipes(s)
    asyncio.run(run())


@pytest.mark.parametrize('seed', range(24))
def test_every_opening_replacement_preserves_tail_order_seat_cycle_and_inventory(seed):
    data = FuzhouGameState._default_room_data(); data['random_seed'] = seed
    s = FuzhouGameState(room_data=data, calculation_service=object())
    s.initialize_round(); s.open_action_window(s.opening_window())
    replacements = 0
    while s.opening_replacement:
        actor, flower = s.replacement_pending
        tail = s.tiles_list[-1]
        old_count = len(s.player_list[actor].hand_tiles)
        s.outbound_payloads.clear()
        act(s, {actor: dict(action_type='buhua')})
        replacements += 1
        assert replacements <= len(book.PHYSICAL)
        assert s.physical_inventory() == Counter(book.PHYSICAL)
        assert len(s.player_list[actor].hand_tiles) == old_count
        pairs = [p['do_action_info'] for p in s.outbound_payloads
                 if p.get('do_action_info', {}).get('action_list') == ['buhua','deal_buhua_tile']]
        assert len(pairs) == 4 and pairs[actor]['deal_tile'] == tail
        if s.opening_replacement:
            expected = next((actor+step)%4 for step in range(1,5)
                            if any(t in book.FLOWERS for t in s.player_list[(actor+step)%4].hand_tiles))
            assert s.replacement_pending[0] == expected
    assert s.jin in book.NUMBERS
    assert all(not any(t in book.FLOWERS for t in p.hand_tiles) for p in s.player_list)
    assert s.round_settlement is None


@pytest.mark.parametrize('previous_flowers', [0, 3])
def test_opening_reserve_boundary_does_not_fabricate_a_replacement(previous_flowers):
    s = opening_fixture()
    s.player_list[0].huapai_list = [41]*previous_flowers
    # The fourth same flower increases reserve before testing drawable tiles.
    s.tiles_list = [21]*(s.dead_wall_count+4+(2 if previous_flowers else 0))
    before = list(s.tiles_list)
    act(s, {0: dict(action_type='buhua')})
    assert s.machine.phase == P.END and s.tiles_list == before
    assert not any(e['action'] == 'deal_buhua_tile' for e in s.domain_events)
    packets = [p for p in s.outbound_payloads if 'do_action_info' in p]
    assert len(packets) == 4
    assert all(p['do_action_info']['action_list'] == ['buhua'] for p in packets)


def test_normal_play_keeps_separate_packets_and_hand_settle_pause():
    s = state_with(WIN[:-1]+[41])
    s.open_action_window(s.begin_turn(0)); s.outbound_payloads.clear()
    assert not s.opening_replacement
    act(s, {0: dict(action_type='buhua')})
    packets = [p for p in s.outbound_payloads if 'do_action_info' in p]
    assert [p['do_action_info']['action_list'] for p in packets] == [['buhua']]*4+[['deal_buhua_tile']]*4
    assert [p['_pause'] for p in s.outbound_payloads if '_pause' in p] == [.3]


def test_normal_flower_timeout_keeps_the_players_time_bank():
    async def run():
        s = state_with(WIN[:-1]+[41])
        s.player_list[0].remaining_time = 17
        s.open_action_window(s.begin_turn(0))
        s.action_clocks[0].start(100)
        s._timing_now = lambda: 104
        response = await s.wait_action()
        assert response[0]['action_type'] == 'buhua'
        assert s.player_list[0].remaining_time == 17
        assert s.action_clocks[0].deadline == 103
        act(s, response)
        assert s.player_list[0].huapai_list == [41]
    asyncio.run(run())


@pytest.mark.parametrize('opening, action, delayed', [(True,'buhua',False),(False,'buhua',True),(True,'ready',True),(False,'pass',False)])
@pytest.mark.parametrize('stale', [False, True])
def test_only_automatic_opening_flowers_skip_bot_pacing(monkeypatch, opening, action, delayed, stale):
    from . import bot
    async def run():
        s = opening_fixture()
        s.opening_replacement = opening
        wait = AsyncMock(); submit = AsyncMock()
        monkeypatch.setattr(bot, 'wait_bot_delay', wait)
        monkeypatch.setattr(bot, 'choose_action', lambda *args: dict(action_type=action))
        s.submit_action = submit
        await bot.play_bot(s, 0, s.server_action_tick-int(stale))
        assert wait.await_count == int(delayed)
        assert submit.await_count == int(not stale)
    asyncio.run(run())
