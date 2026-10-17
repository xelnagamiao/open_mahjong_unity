"""Exercise the real game loop, claim queues, replacement draw and dora timing."""
import asyncio
import copy
from types import SimpleNamespace
from unittest.mock import patch

import pytest

from .test_sanma import sanma
from .test_double_riichi import make_game
from .wait_action import wait_action
from .boardcast import broadcast_game_start
from ..public.ai.get_action import get_ai_action
from ...game_calculation.riichi.washizu_tiles import SUB_RULES, make_tiles, normalize


class AuditFinished(Exception):
    pass


async def run_kan_case(rule, kind, timing='after_discard', enabled=True):
    transparent = rule in SUB_RULES
    if rule == 'riichi/washizu_doubles':
        from .test_washizu_doubles import captain_game
        g = captain_game()[0]
    elif transparent or rule == 'four':
        g = make_game(0)[0]
        if transparent:
            g.sub_rule = rule
            g.red_dora = False
    else:
        g = sanma(rule)
    g.room_id = 825014
    g.room_random_seed = 20261014
    g.gamestate_id = f'{rule}-{kind}-{timing}-{enabled}'
    g.detailed_config.update(kan_dora_timing=timing, kan_dora=enabled)
    count = len(g.player_list)
    messages = []
    phase = 'start'
    class Socket:
        def __init__(self, seat): self.seat = seat
        async def send_json(self, payload):
            messages.append(dict(seat=self.seat, phase=phase, payload=copy.deepcopy(payload)))
    g.game_server.user_id_to_connection = {}
    for i, p in enumerate(g.player_list):
        p.user_id = 11000001 + i
        p.username = f'Kan QA {i+1}'
        g.game_server.user_id_to_connection[p.user_id] = SimpleNamespace(websocket=Socket(i))
    def initial(game):
        ids = [11,19,*range(21,30),*range(31,40),*range(41,48)] if count == 3 else [*range(11,20),*range(21,30),*range(31,40),*range(41,48)]
        pool = make_tiles() if transparent else ids * 4
        caller = 0 if kind == 'angang' else 1
        held = 2 if kind == 'jiagang' else 3
        held_tiles = [2031 if transparent else 31] * held
        draws = [31] + ([22,23,24][:count-1] + [2031 if transparent else 31] if kind == 'jiagang' else [])
        for tile in held_tiles + draws: pool.remove(tile)
        # Keep the last physical 31 reserved for a later kakan, not in a filler hand.
        import random
        random.Random(1407).shuffle(pool)
        for i, p in enumerate(game.player_list):
            p.hand_tiles = list(held_tiles) if i == caller else []
            while len(p.hand_tiles) < 13: p.hand_tiles.append(pool.pop())
            p.has_draw_slot = False
            p.discard_tiles = []; p.discard_origin_tiles = []
        game.tiles_list = draws + pool
        if transparent:
            from .washizu import init_pool_after_deal
            init_pool_after_deal(game)
            return
        game.dead_wall_count = 14
        game._dora_slots = [game.tiles_list[-6-2*i] for i in range(5)]
        game._ura_slots = [game.tiles_list[-5-2*i] for i in range(5)]
        game.dora_indicators = game._dora_slots[:1]; game.kan_dora_indicators = []
        game.ura_dora_indicators = game._ura_slots[:1]; game.ura_kan_dora_indicators = []
        game.rinshan_count = 0
    calls = 0
    before_cut = None
    async def choose():
        nonlocal phase, calls, before_cut
        calls += 1
        assert calls < 30, (kind, g.game_status, g.action_dict)
        phase = 'live'
        done = g.total_kans == 1
        if done:
            before_cut = list(g.kan_dora_indicators)
            expected = int(enabled and (kind == 'angang' or timing == 'immediate'))
            assert len(before_cut) == expected
        choices = []
        if g.game_status == 'waiting_hand_action':
            seat = g.current_player_index
            p = g.player_list[seat]
            if kind == 'angang' and not done:
                choices = [(seat, 'angang', None, 31)]
            elif kind == 'jiagang' and seat == 1 and 'k31' in p.combination_tiles and any(normalize(t) == 31 for t in p.hand_tiles) and not done:
                choices = [(seat, 'jiagang', None, 31)]
            else:
                tile = 31 if calls == 1 else p.hand_tiles[-1]
                choices = [(seat, 'cut', tile, None)]
        else:
            wanted = 'gang' if kind == 'gang' else 'peng'
            for seat, actions in g.action_dict.items():
                if seat == 1 and wanted in actions and not g.player_list[1].combination_tiles:
                    choices.append((seat, wanted, None, None))
                elif 'pass' in actions:
                    choices.append((seat, 'pass', None, None))
        waiter = asyncio.create_task(wait_action(g))
        await asyncio.sleep(0)
        for seat, action, tile, target in choices:
            p = g.player_list[seat]
            await get_ai_action(g, seat, action, bool(tile is not None and p.has_draw_slot and tile == p.hand_tiles[-1]), tile, None, target)
        await asyncio.wait_for(waiter, 3)
        if done:
            assert len(g.kan_dora_indicators) == int(enabled)
            phase = 'reconnect'
            await broadcast_game_start(g)
            raise AuditFinished()
    g.wait_action = choose
    # Only remove initial seating randomness; all actions and drawing use production code.
    with patch('server.gamestate.game_riichi.RiichiGameState.init_riichi_tiles', initial), \
         patch('server.gamestate.game_riichi.RiichiGameState.random.Random.shuffle', lambda self, values: None):
        try:
            await g.game_loop_riichi()
        except AuditFinished:
            pass
    ticks = g.game_record['game_round']['round_index_1']['action_ticks']
    dora = [i for i,t in enumerate(ticks) if t[0] == 'dora']
    assert len(dora) == int(enabled)
    if enabled:
        assert ticks[dora[0]-1][0] == ('gd' if kind == 'angang' or timing == 'immediate' else 'c')
    return dict(id=g.gamestate_id, rule='riichi', count=count, kind=kind, timing=timing, enabled=enabled,
                initial=g.dora_indicators, indicators=g.kan_dora_indicators, before_cut=before_cut,
                record=g.game_record, messages=messages)


@pytest.mark.parametrize('rule', ['sanma_majsoul','sanma_tenhou','four'])
@pytest.mark.parametrize('kind', ['angang','gang','jiagang'])
@pytest.mark.parametrize('timing', ['after_discard','immediate'])
@pytest.mark.parametrize('enabled', [False,True])
def test_kan_indicator_timing_in_authoritative_loop(rule, kind, timing, enabled):
    asyncio.run(run_kan_case(rule, kind, timing, enabled))
