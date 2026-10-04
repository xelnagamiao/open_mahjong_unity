"""Async action queues and finite seeded matches; no real network claims."""

import asyncio
from collections import Counter
from copy import deepcopy
from unittest.mock import AsyncMock

import pytest

from .bot import play_bot, choose_action
from .test_flow import state, turn, act, start, physical, PLAIN
from .state_machine import Phase as P
from ...game_calculation.yixing.rules import TILES,FLOWERS


def test_early_replies_survive_and_later_replies_wake_waiter():
    async def run():
        for early in (False,True):
            s=turn()
            s.player_list[0].remaining_time=5
            if early:
                await s.submit_action(0,"cut",TileId=28,cutClass=True,cutIndex=13)
            task=asyncio.create_task(s.wait_action())
            await asyncio.sleep(0)
            if not early:
                await s.submit_action(0,"cut",TileId=28,cutClass=True,cutIndex=13)
            responses=await asyncio.wait_for(task,1)
            assert responses[0]["TileId"]==28
            assert not s.action_events[0].is_set() and not s.waiting_players_list
            s.apply_action_results(s.live_pending_window,responses)
            assert s.player_list[0].discard_tiles[-1]==28
        with pytest.raises(ValueError):
            await s.submit_action(True,"pass")
    asyncio.run(run())


def test_timeout_discards_legally_replaces_flowers_and_passes_sea_choice():
    async def run():
        s=turn()
        s.player_list[0].forbidden_discards={28}
        s.open_action_window(s.begin_turn(0))
        data=await s.wait_action(0)
        assert data[0]["TileId"]!=28 and data[0]["is_timeout_action"]
        s.apply_action_results(s.live_pending_window,data)
        s=turn()
        s.player_list[0].hand_tiles[0]=51
        s.open_action_window(s.begin_turn(0))
        data=await s.wait_action(0)
        assert data[0]["action_type"]=="buhua"
        s.apply_action_results(s.live_pending_window,data)
        assert s.player_list[0].huapai_list==[51]
        s.machine.transition(P.RESPONSE)
        s.tiles_list=[28]
        s.open_action_window(s.draw_for(1))
        data=await s.wait_action(0)
        assert data[1]["action_type"]=="pass"
    asyncio.run(run())


def test_ready_timeout_and_repeated_dealer_restore():
    async def run():
        s=turn()
        s.end_draw()
        s._send_claim_protection_payload=AsyncMock()
        await s.run_round_ready_phase(0)
        assert s.machine.phase==P.READY and not s.waiting_players_list
        assert all(s.build_ready_status_payload(0)["ready_status_info"]["player_to_ready"].values())
        assert s.build_final_settlement_payload(0)["show_result_info"]["score_changes"]==dict.fromkeys(range(4),0)
        s.advance_round_after_ready()
        assert s.dealer_streak==1 and s.current_round==1 and s.round_index==2
    asyncio.run(run())


def test_bot_cannot_send_to_stale_or_already_answered_window():
    async def run():
        s=turn()
        await play_bot(s,0,s.server_action_tick-1)
        assert s.action_queues[0].empty()
        s.submit_action=AsyncMock(side_effect=ValueError("closed"))
        await play_bot(s,0,s.server_action_tick)
        s.submit_action.assert_awaited_once()
    asyncio.run(run())


def test_last_draw_bot_uses_own_hand_not_hidden_wall():
    s=turn()
    s.player_list[1].hand_tiles=PLAIN[:-1]
    for tile in (28,19,51):
        s.tiles_list=[tile]
        assert choose_action(s,1,["yixing_last_draw","pass"])["action_type"]=="yixing_last_draw"
    s.player_list[1].hand_tiles=[11,13,15,17,19,21,24,27,29,32,35,38,42]
    assert choose_action(s,1,["yixing_last_draw","pass"])["action_type"]=="pass"


def test_seeded_async_complete_match_conserves_tiles_and_finalizes_once(monkeypatch):
    async def run():
        s=state(19)
        for i,p in enumerate(s.player_list): p.user_id=i+1
        s.bot_speed="instant"
        sent=[]
        async def capture(index,payload): sent.append(deepcopy(payload))
        s._send_claim_protection_payload=capture
        s.complete_game_lifecycle=AsyncMock()
        from ..game_guobiao import buhua_broadcast
        monkeypatch.setattr(buhua_broadcast,"HAND_SETTLE_GAP_SEC",0)
        await asyncio.wait_for(s.run_game_loop(),60)
        assert s.machine.phase==P.FINISHED and s.is_match_end()
        assert s.current_round==4 and len(s.game_record["game_round"])>=4
        assert all(r["action_ticks"][-1]==["end"] for r in s.game_record["game_round"].values())
        assert sum(p.score for p in s.player_list)==0
        assert physical(s)==Counter({t:4 for t in TILES})+Counter(FLOWERS)
        assert any(p["type"]=="gamestate/yixing/show_result" for p in sent)
        s.complete_game_lifecycle.assert_awaited_once()
        await asyncio.gather(*s.bot_tasks,return_exceptions=True)
    asyncio.run(run())
