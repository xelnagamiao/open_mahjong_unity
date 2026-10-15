"""Client protocol and timeout contracts shared by the other platform rules."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_flow import state, arrange, apply
from .get_action import handle_action

NORMAL = [21,22,23,24,25,26,27,28,29,41,41,41,45,45]


def turn():
    s = state()
    arrange(s, [NORMAL, [21], [22]], [[51], [], []])
    s.open_action_window(s.begin_turn(0))
    return s


@pytest.mark.parametrize('visual_index', [0, 4, 13])
def test_draw_discard_accepts_independent_visual_index(visual_index):
    s = turn()
    apply(s, **{'0': dict(action_type='cut', TileId=45, cutClass=True, cutIndex=visual_index)})
    assert s.player_list[0].discard_tiles == [45]
    assert s.player_list[0].hand_tiles.count(45) == 1


def test_rejected_request_restores_the_still_pending_action_window():
    async def run():
        s = turn()
        ws = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(players={'me': SimpleNamespace(user_id=s.player_list[0].user_id)},
            gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: s))
        await handle_action(server, 'me', dict(type='gamestate/malaysia/cut_tile',
            gamestate_id=s.gamestate_id, action_tick=s.server_action_tick,
            TileId=81, cutClass=True, cutIndex=0), ws)
        packets = [c.args[0] for c in ws.send_json.call_args_list]
        assert packets[0]['type'] == 'tips'
        assert packets[-1]['type'] == 'gamestate/malaysia/broadcast_hand_action'
        assert packets[-1]['ask_hand_action_info']['action_list'] == s.action_dict[0]
        assert s.action_queues[0].empty()
    asyncio.run(run())


@pytest.mark.parametrize('offered,expected', [(['hu','pass'],'pass'), (['hu_self','cut'],'cut')])
def test_human_timeout_does_not_enable_automatic_win(offered, expected):
    s = turn()
    s.action_dict[0] = offered
    assert s.automatic_action(0)['action_type'] == expected
    assert s.automatic_action(0, bot=True)['action_type'] == offered[0]


def test_deadline_is_checked_at_ingress_before_the_waiter_runs():
    async def run():
        s = turn()
        s._active_deadlines = {0: asyncio.get_running_loop().time() - 1}
        with pytest.raises(ValueError):
            await s.submit_action(0, 'cut', TileId=45, cutClass=True, cutIndex=13)
        assert s.action_queues[0].empty()
    asyncio.run(run())


@pytest.mark.parametrize('flower', [51,52,53,54,55,56,57,58,61,62,63,64,71,72,73,74])
def test_automatic_flower_replacement_keeps_held_and_replacement_flies(flower):
    s = state()
    arrange(s, [NORMAL[:-2] + [81, flower], [21], [22]])
    s.tiles_list[-1] = 81
    s.open_action_window(s.begin_turn(0))
    assert s.automatic_action(0)['action_type'] == 'buhua'
    apply(s, **{'0': s.automatic_action(0)})
    assert s.player_list[0].hand_tiles.count(81) == 2
    assert s.player_list[0].huapai_list == [flower]
    assert 'buhua' not in s.action_dict[0]
    assert 'malaysia_fly' in s.action_dict[0]
    assert s.automatic_action(0)['action_type'] == 'cut'


@pytest.mark.parametrize('honor', [41,42,43,44,45,46,47])
def test_honors_are_playable_tiles_not_automatic_flowers(honor):
    s = state()
    arrange(s, [NORMAL[:-1] + [honor], [21], [22]], [[51],[],[]])
    s.open_action_window(s.begin_turn(0))
    assert 'buhua' not in s.action_dict[0]
    apply(s, **{'0': dict(action_type='cut', TileId=honor, cutClass=True, cutIndex=0)})
    assert s.player_list[0].discard_tiles == [honor]


def test_server_scheduling_delay_does_not_charge_the_players_time_bank(monkeypatch):
    async def run():
        s = turn()
        for p in s.player_list:
            assert not p.is_bot
            p.remaining_time = 2
        s.step_time = 0
        charged = []
        monkeypatch.setattr(s, '_consume_time_bank', lambda i, elapsed: charged.append(elapsed))
        original_wait = asyncio.wait
        async def delayed_wait(*args, **kwargs):
            result = await original_wait(*args, **kwargs)
            await asyncio.sleep(.12)  # the accepted request waits for server work
            return result
        monkeypatch.setattr(asyncio, 'wait', delayed_wait)
        task = asyncio.create_task(s.wait_action())
        await asyncio.sleep(.01)
        await s.submit_action(0, 'cut', TileId=45, cutClass=True, cutIndex=0)
        result = await task
        assert result[0]['action_type'] == 'cut'
        assert len(charged) == 1 and charged[0] < .1
    asyncio.run(run())
