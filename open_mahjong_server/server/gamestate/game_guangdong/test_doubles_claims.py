import asyncio
from unittest.mock import AsyncMock

import pytest

from .test_doubles import CHICKEN, presentation, state
from .test_state import HAND, hand


@pytest.mark.parametrize("second,expected", [("pass",[1,2]), ("force_pass",[1])])
def test_tactical_equal_priority_wins_and_permanent_pass(second, expected):
    async def run():
        gs=state(tactical_call=True)
        hand(gs,1,CHICKEN)
        hand(gs,2,CHICKEN)
        gs.current_player_index=0
        gs.player_list[0].discard_tiles=[41]
        allowed=gs.check_discard_actions(41)
        for i in (1,2):
            allowed[i].append("force_pass")
        async def recheck(st, **kwargs):
            assert st.action_dict[2]==["hu_second","pass","force_pass"]
            assert kwargs["remaining_time_override"]==5 and kwargs["is_tactical_recheck"]
        gs._broadcast_claim=AsyncMock()
        gs.broadcast_tactical_recheck=recheck
        gs.collect_action_responses=AsyncMock(return_value=({2:{"action_type":"hu_second"}},{}))
        await gs.resolve_discard_responses({1:{"action_type":"hu_first"},2:{"action_type":second}},allowed)
        await gs.confirm_wins()
        assert [i["index"] for i in gs.win_events]==expected
        assert gs.game_status=="deal_card" and gs.player_list[0].discard_origin_tiles==[41]
        assert gs.next_dealer()==1
        assert gs.collect_action_responses.await_count==int(second=="pass")
        if len(expected)==2:
            assert gs.player_list[1].blood_win_multi and gs.player_list[2].blood_win_multi
            p=hand(gs,3,HAND+[45])
            p.last_drawn_tile=45
            gs.current_player_index=3
            gs.accept_self_draw(3)
            await gs.confirm_wins()
            assert gs.game_status=="END" and gs.draw_reason=="three_winners"
    asyncio.run(run())
