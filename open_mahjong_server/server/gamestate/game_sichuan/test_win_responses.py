"""点和、抢杠及一炮多响须分别确认，不能把可和者当作已报和者。"""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .action_check import check_action_after_cut, check_action_jiagang, refresh_waiting_tiles
from .test_action_window import MODES, make_state, submit
from .wait_action import wait_action
from ..public.lifecycle import cancel_auxiliary_tasks


CLAIM_STATUSES = ("waiting_action_after_cut", "waiting_action_qianggang")


def winning_state(mode, status, *, tactical_call=False):
    state, messages = make_state(mode, tactical_call=tactical_call, tactical_pre_grace_delay=0)
    state.room_id = 900008
    state.game_status = status
    state.player_list[0].discard_tiles = [25]
    for seat in (1, 2):
        player = state.player_list[seat]
        player.hand_tiles = [11, 12, 13, 14, 15, 16, 21, 22, 23, 25]
        if mode != "xueliu":
            player.hand_tiles[6:6] = [17, 18, 19]
        refresh_waiting_tiles(state, seat)
    if status == "waiting_action_qianggang":
        state.jiagang_tile = 25
        state.player_list[0].combination_tiles = ["g25"]
        state.player_list[0].combination_mask = [[1, 25, 3, 25, 0, 25, 0, 25]]
        state.action_dict = check_action_jiagang(state, 25)
    else:
        state.action_dict = check_action_after_cut(state, 25)
    assert set(state.sichuan_hu_results) == {1, 2}
    assert all("hu" in state.action_dict[seat] and "pass" in state.action_dict[seat] for seat in (1, 2))
    return state, messages


async def settle_without_animation(state):
    module = "SichuanGameState" if not state.is_xueliu else "XueliuGameState"
    with patch(f"{__package__}.{module}.asyncio.sleep", new_callable=AsyncMock):
        await state._settle_win()


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", CLAIM_STATUSES)
@pytest.mark.parametrize("tactical_call,response", [
    (False, "hu"), (False, "pass"),
    (True, "hu"), (True, "pass"), (True, "force_pass"),
])
@pytest.mark.parametrize("winner_first", [False, True])
def test_each_winner_must_confirm_even_after_another_player_wins(mode, status, tactical_call, response, winner_first):
    async def run():
        state, messages = winning_state(mode, status, tactical_call=tactical_call)
        task = None
        try:
            fan = state.sichuan_hu_results[2]["fan"]
            await state.broadcast_ask_other_action()
            first, second = ((1, "hu"), (2, response)) if winner_first else ((2, response), (1, "hu"))
            await submit(state, *first)
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(.02)
            assert not task.done(), "另一位可和者尚未响应时不得结束询问或代其和牌"
            assert state.pending_win is None
            await submit(state, *second)
            await asyncio.wait_for(task, 1)
            winners = {1, 2} if response == "hu" else {1}
            assert set(state.sichuan_hu_results) == winners
            assert state.player_list[2].shunhe_passed_max_fan == (None if response == "hu" else fan)
            await settle_without_animation(state)
            assert state.first_win_event["winners"] == sorted(winners)
            assert state.current_player_index == max(winners)
            assert state.game_status == "deal_card"
            for seat in (1, 2):
                player = state.player_list[seat]
                assert (player.has_won if state.is_xueliu else player.is_hu) == (seat in winners)
            published_winners = {
                entry["message"]["show_result_info"]["hepai_player_index"]
                for entry in messages if entry["type"].endswith("/show_result")
            }
            assert published_winners == winners
            ticks = state.game_record["game_round"][f"round_index_{state.round_index}"]["action_ticks"]
            assert {tick[1] for tick in ticks if tick[0].startswith("hu_")} == winners
        finally:
            if task is not None and not task.done():
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", CLAIM_STATUSES)
@pytest.mark.parametrize("tactical_call", [False, True])
def test_unanswered_win_times_out_as_pass(mode, status, tactical_call):
    async def run():
        state, _ = winning_state(mode, status, tactical_call=tactical_call)
        try:
            fan = state.sichuan_hu_results[2]["fan"]
            state.step_time = 0
            state.player_list[2].remaining_time = 0
            await state.broadcast_ask_other_action()
            await submit(state, 1, "hu")
            await asyncio.wait_for(wait_action(state), 1)
            assert state.game_status == "settle_win"
            assert set(state.sichuan_hu_results) == {1}
            assert state.player_list[2].shunhe_passed_max_fan == fan
            await settle_without_animation(state)
            assert state.first_win_event["winners"] == [1]
            player = state.player_list[2]
            assert not (player.has_won if state.is_xueliu else player.is_hu)
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", CLAIM_STATUSES)
@pytest.mark.parametrize("response", ["pass", "timeout"])
def test_every_declining_player_gets_shunhe_without_a_win(mode, status, response):
    async def run():
        state, _ = winning_state(mode, status)
        try:
            fans = {seat: result["fan"] for seat, result in state.sichuan_hu_results.items()}
            if response == "timeout":
                state.step_time = 0
                for seat in fans:
                    state.player_list[seat].remaining_time = 0
            await state.broadcast_ask_other_action()
            if response == "pass":
                for seat in fans:
                    await submit(state, seat, "pass")
            await asyncio.wait_for(wait_action(state), 1)
            assert state.pending_win is None
            assert state.game_status == ("deal_card" if status.endswith("cut") else "deal_card_after_gang")
            for seat, fan in fans.items():
                assert state.player_list[seat].shunhe_passed_max_fan == fan
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("response", ["hu", "pass", "force_pass", "timeout"])
def test_tactical_recheck_waits_for_each_win_response(mode, response):
    async def run():
        state, _ = winning_state(mode, "waiting_action_after_cut", tactical_call=True)
        state.tactical_grace_seconds = .15
        # 第2家听顺子，第3家可碰同一张25；各人的手牌均经真实规则检查。
        state.player_list[2].hand_tiles = [11, 12, 13, 14, 15, 16, 23, 24, 27, 27]
        if mode != "xueliu":
            state.player_list[2].hand_tiles[6:6] = [17, 18, 19]
        refresh_waiting_tiles(state, 2)
        state.player_list[3].hand_tiles.append(25)
        state.action_dict = check_action_after_cut(state, 25)
        recheck_offered = asyncio.Event()
        task = None
        try:
            for seat in (1, 2, 3):
                socket = state.game_server.user_id_to_connection[state.player_list[seat].user_id].websocket
                original = socket.send_json

                async def send(payload, seat=seat, original=original):
                    await original(payload)
                    info = payload.get("ask_other_action_info")
                    if not info or not info["action_list"]:
                        return
                    if info.get("is_tactical_recheck"):
                        if seat == 1:
                            await submit(state, seat, "hu", tick=info["action_tick"])
                        elif seat == 2:
                            for action, tick in (("hu", info["action_tick"] - 1), ("peng", info["action_tick"])):
                                state.action_queues[seat].put_nowait({"action_type": action, "_action_tick": tick})
                            state.action_events[seat].set()
                            recheck_offered.set()
                    else:
                        await submit(state, seat, "peng" if seat == 3 else "pass", tick=info["action_tick"])

                socket.send_json = send

            await state.broadcast_ask_other_action()
            task = asyncio.create_task(wait_action(state))
            await asyncio.wait_for(recheck_offered.wait(), 1)
            banks = [player.remaining_time for player in state.player_list]
            await asyncio.sleep(.02)
            assert not task.done(), "战术再询问也须等待其他可和者独立选择"
            if response != "timeout":
                await submit(state, 2, response)
                if response == "hu":
                    state.action_queues[2].put_nowait({"action_type": "pass", "_action_tick": state.server_action_tick})
            await asyncio.wait_for(task, 1)
            assert state.game_status == "settle_win"
            assert set(state.sichuan_hu_results) == ({1, 2} if response == "hu" else {1})
            assert not state.player_list[3].combination_tiles
            assert state.player_list[1].shunhe_passed_max_fan is None
            assert (state.player_list[2].shunhe_passed_max_fan is None) == (response == "hu")
            assert [player.remaining_time for player in state.player_list] == banks
        finally:
            if task is not None and not task.done():
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("breaks_win", [False, True])
def test_peng_only_creates_shunhe_if_the_hand_can_still_win(mode, breaks_win):
    async def run():
        state, _ = make_state(mode)
        try:
            player = state.player_list[1]
            player.hand_tiles = ([21, 21, 22, 22, 22, 23, 23, 11, 12, 13] if breaks_win
                                 else [22, 22, 24, 25, 26, 11, 12, 13, 29, 29])
            if mode != "xueliu":
                player.hand_tiles += [16, 17, 18]
            refresh_waiting_tiles(state, 1)
            state.player_list[0].discard_tiles = [22]
            state.action_dict = check_action_after_cut(state, 22)
            assert "hu" in state.action_dict[1] and "peng" in state.action_dict[1]
            passed_fan = state.sichuan_hu_results[1]["fan"]
            await state.broadcast_ask_other_action()
            await submit(state, 1, "peng")
            await asyncio.wait_for(wait_action(state), 1)
            assert state.pending_win is None
            assert player.combination_tiles == ["k22"]
            assert player.shunhe_passed_max_fan == (None if breaks_win else passed_fan)
            assert player.shunhe_skipped_fan is None
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
