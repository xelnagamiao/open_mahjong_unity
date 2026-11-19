import asyncio
from unittest.mock import AsyncMock, patch

from ..public.ai.auto_cut_ai import auto_cut_action
from .bot import doubles_claim
from .test_doubles import CHICKEN, state
from .test_state import hand


def test_efficiency_bot_keeps_closed_tenpai_and_improves_open_hand():
    assert doubles_claim([11,11,12,13,14,22,23,24,31,32,33,41,41], (), 11, ("peng","pass"))=="pass"
    assert doubles_claim([11,11,12,13,14,15,16,17,42,46], ("k41",), 11, ("peng","pass"))=="peng"


def test_auto_bot_only_discards_draw_slot_and_declines_legal_win_and_claims():
    async def run():
        gs=state()
        p=hand(gs,0,CHICKEN+[41])
        p.has_draw_slot=True
        p.last_drawn_tile=41
        gs.waiting_players_list=[0]
        with patch("server.gamestate.public.ai.auto_cut_ai._wait_until_actionable",new=AsyncMock(return_value=True)), \
             patch("server.gamestate.public.ai.auto_cut_ai.submit_bot_action",new=AsyncMock()) as send:
            await auto_cut_action.__wrapped__(gs,0,["hu_self","angang","cut"],"waiting_hand_action")
            assert send.await_args.args[3:7]==("cut",True,41,13)
            await auto_cut_action.__wrapped__(gs,0,["hu_first","gang","peng","pass"],"waiting_action_after_cut")
            assert send.await_args.args[3]=="pass"
    asyncio.run(run())
