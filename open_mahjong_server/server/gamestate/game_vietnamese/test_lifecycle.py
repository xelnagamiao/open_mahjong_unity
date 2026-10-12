"""Exercise the actual async match loop, bot queue, ready barrier and close path."""
import asyncio
from unittest.mock import Mock
from .VietnameseGameState import VietnameseGameState
from .state_machine import Phase


def test_complete_four_wind_match_uses_live_async_lifecycle_once():
    async def run():
        data=VietnameseGameState._default_room_data()
        data.update(player_list=[1,2,3,4],game_round=4,random_seed=76153,bot_speed='instant',step_timer=5)
        state=VietnameseGameState(room_data=data)
        state.persist_game_record=Mock(wraps=state.persist_game_record)
        errors=[]
        loop=asyncio.get_running_loop()
        prior=loop.get_exception_handler()
        loop.set_exception_handler(lambda loop,context:errors.append(context))
        try:
            await asyncio.wait_for(state.run_game_loop(),120)
            await asyncio.sleep(0)
            assert state.machine.phase==Phase.FINISHED
            assert state.lifecycle_completed and not state.waiting_players_list
            assert state.current_round==16
            assert len(state.game_record['game_round'])>=16
            assert sum(p.score for p in state.player_list)==1200
            assert all(not p.record_counter.recorded_fans for p in state.player_list)
            assert len({len(p.score_history) for p in state.player_list})==1
            assert state.outbound_send_cursor==len(state.outbound_payloads)
            assert state._local_record_detail is not None
            assert not errors
            state.persist_game_record.assert_called_once()
            await state.complete_game_lifecycle()
            state.persist_game_record.assert_called_once()
        finally:
            loop.set_exception_handler(prior)
    asyncio.run(run())
