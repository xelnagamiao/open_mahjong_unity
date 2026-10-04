import asyncio
from unittest.mock import AsyncMock, patch
import pytest
from . import bot
from .test_state import make_state, BASE, ready


@pytest.mark.parametrize("actions,choice", [(["hu_self","cut"],"hu_self"),(["hu_first"],"hu_first"),
    (["gang","peng","pass"],"gang"),(["peng","pass"],"peng"),(["pass"],"pass")])
def test_bot_obeys_server_priorities(actions,choice):
    state=make_state()
    with patch.object(bot,"_wait_until_actionable",AsyncMock(return_value=True)), \
         patch.object(bot,"bot_action_is_current",return_value=True), \
         patch.object(bot,"submit_bot_action",new_callable=AsyncMock) as submit:
        asyncio.run(bot.shanxi_smart_bot_action.__wrapped__(state,0,actions,"waiting_hand_action"))
    assert submit.await_args.args[3]==choice


def test_bot_uses_only_known_tiles_in_34_index_format_and_declares_legal_ready():
    state=make_state()
    p=state.player_list[0]
    p.hand_tiles=BASE+[47,16]
    p.riichi_candidate_cuts={16:[47]}
    p.concealed_discards={0:19}
    state.player_list[1].hand_tiles=[47]*3
    async def cpu(gs,fn,hand,melds,visible,forbidden):
        assert len(visible)==34 and visible[8]==1 and sum(visible)==1
        assert forbidden==set(hand)-{16}
        return fn(hand,melds,visible,forbidden)
    with patch.object(bot,"_wait_until_actionable",AsyncMock(return_value=True)), \
         patch.object(bot,"bot_action_is_current",return_value=True), \
         patch.object(bot,"run_room_bot_cpu",cpu), \
         patch.object(bot,"submit_bot_action",new_callable=AsyncMock) as submit:
        asyncio.run(bot.shanxi_smart_bot_action.__wrapped__(state,0,["riichi_cut","cut"],"waiting_hand_action"))
    args=submit.await_args.args
    assert args[3]=="riichi_cut" and args[5]==16 and args[6]==13


def test_bot_can_take_authorized_concealed_kong():
    state=make_state()
    p=state.player_list[0]
    ready(p,[11]*4+[12,13,14,15,16,17,18]+[19]*3)
    p.has_draw_slot=True
    p.last_drawn_tile=11
    with patch.object(bot,"_wait_until_actionable",AsyncMock(return_value=True)), \
         patch.object(bot,"bot_action_is_current",return_value=True), \
         patch.object(bot,"submit_bot_action",new_callable=AsyncMock) as submit:
        asyncio.run(bot.shanxi_smart_bot_action.__wrapped__(state,0,["angang","cut"],"waiting_hand_action"))
    assert submit.await_args.args[3]=="angang" and submit.await_args.args[-1]==11


def test_stale_bot_turn_does_not_submit():
    state=make_state()
    with patch.object(bot,"_wait_until_actionable",AsyncMock(return_value=True)), \
         patch.object(bot,"bot_action_is_current",return_value=False), \
         patch.object(bot,"submit_bot_action",new_callable=AsyncMock) as submit:
        asyncio.run(bot.shanxi_smart_bot_action.__wrapped__(state,0,["cut"],"waiting_hand_action"))
    submit.assert_not_awaited()
