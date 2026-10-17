"""Meld and rob-kong decisions share the configured per-hand time bank."""
import asyncio
import pytest

from .state_machine import Phase as P
from .test_branches import kong_state
from .test_state import WIN, act, open_turn, state_with


def claim_window(action, bank=20, step=5):
    if action == "rob_kong":
        state = kong_state()
    else:
        state = state_with(WIN[:-1]+[23])
        owned = {"chi_left":[21,22], "chi_mid":[22,24], "chi_right":[24,25],
                 "peng":[23,23], "gang":[23,23,23], "hu":[21,22]}[action]
        rest = [11,12,13,14,15,16,31,32,33,35,36,37,28,28]
        state.player_list[1].hand_tiles = owned+rest[:16-len(owned)]
    state.step_time = step
    for player in state.player_list:
        player.remaining_time = bank
    open_turn(state)
    act(state,{0:dict(action_type="jiagang",target_tile=22) if action=="rob_kong"
                    else dict(action_type="cut",TileId=23,cutClass=True)})
    assert state.machine.phase == (P.KONG if action=="rob_kong" else P.RESPONSE)
    assert ("hu" if action=="rob_kong" else action) in state.action_dict[1]
    return state


@pytest.mark.parametrize("action",["chi_left","chi_mid","chi_right","peng","gang","hu","rob_kong"])
@pytest.mark.parametrize("bank,step",[(20,5),(8,2),(0,5)])
def test_claim_deadline_and_client_payload_follow_room_clock(action,bank,step):
    state=claim_window(action,bank,step)
    clock=state.action_clocks[1]
    clock.start(100)
    state._timing_now=lambda:100
    info=state.build_pending_action_payload(1)["ask_other_action_info"]
    assert clock.deadline == 100+bank+step
    assert (info["remaining_time"],info["step_remaining"]) == (bank,step)


@pytest.mark.parametrize("action",["chi_left","peng","gang","hu","rob_kong"])
@pytest.mark.parametrize("elapsed,remaining",[(4,20),(7,18)])
def test_claim_after_three_seconds_is_accepted_and_charges_only_own_elapsed_bank(action,elapsed,remaining):
    async def run():
        state=claim_window(action)
        for clock in state.action_clocks.values():clock.start(100)
        state._timing_now=lambda:100+elapsed
        for index in list(state.waiting_players_list):
            await state.submit_action(index,("hu" if action=="rob_kong" else action) if index==1 else "pass")
        # Queued network actions retain receive time even if processing is late.
        state._timing_now=lambda:200
        responses=await state.wait_action()
        assert responses[1]["action_type"] == ("hu" if action=="rob_kong" else action)
        assert state.player_list[1].remaining_time == remaining
        assert state.player_list[0].remaining_time == 20
        act(state,responses)
        assert state.machine.phase in (P.END,P.DISCARD_ONLY,P.TURN,P.REPLACEMENT,P.SEA)
    asyncio.run(run())


@pytest.mark.parametrize("action",["peng","rob_kong"])
def test_claim_reconnect_keeps_spent_bank_and_deadline(action):
    async def run():
        state=claim_window(action)
        for clock in state.action_clocks.values():clock.start(100)
        state._timing_now=lambda:108
        sent=[]
        async def send(index,payload):sent.append(payload)
        state.send_payload_to_player=send
        await state.player_reconnect(state.player_list[1].user_id)
        info=sent[-1]["ask_other_action_info"]
        assert (info["remaining_time"],info["step_remaining"]) == (17,0)
        assert state.action_clocks[1].deadline == 125
        state._timing_now=lambda:125
        with pytest.raises(ValueError,match="超时"):
            await state.submit_action(1,"pass")
        responses=await state.wait_action()
        assert all(a["action_type"]=="pass" for a in responses.values())
        assert all(state.player_list[i].remaining_time==0 for i in responses)
        assert state.player_list[0].remaining_time==20
    asyncio.run(run())
