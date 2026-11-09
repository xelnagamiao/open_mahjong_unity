"""川麻点和按出牌者起算的座次再问，保留所有已确认和牌者。"""
import asyncio

import pytest

from .action_check import check_action_after_cut, check_action_jiagang, refresh_waiting_tiles
from .test_action_window import make_state, submit
from .test_win_responses import CLAIM_STATUSES, settle_without_animation
from .wait_action import wait_action
from ..public.lifecycle import cancel_auxiliary_tasks


MODES = ("standard", "doubles", "xueliu_doubles")
WINNING_HAND = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 25]


def claim_state(mode, status, discarder, *, distances=(1, 2), tactical_call=True):
    state, messages = make_state(mode, tactical_call=tactical_call,
                                 tactical_pre_grace_delay=0, tactical_grace_seconds=.08)
    state.room_id = 900008
    state.current_player_index = discarder
    state.game_status = status
    winners = [(discarder + distance) % 4 for distance in distances]
    for seat, player in enumerate(state.player_list):
        if seat in winners:
            player.hand_tiles = WINNING_HAND.copy()
        refresh_waiting_tiles(state, seat)
    state.player_list[discarder].discard_tiles = [25]
    if status == "waiting_action_qianggang":
        state.jiagang_tile = 25
        state.player_list[discarder].combination_tiles = ["g25"]
        state.player_list[discarder].combination_mask = [[1, 25, 3, 25, 0, 25, 0, 25]]
    refresh_claims(state)
    assert set(state.sichuan_hu_results) == set(winners)
    choices = ["hu", "pass", "force_pass"] if tactical_call else ["hu", "pass"]
    assert all(state.action_dict[seat] == choices for seat in winners)
    return state, messages, winners


def refresh_claims(state):
    state.action_dict = (check_action_jiagang(state, 25)
                         if state.game_status == "waiting_action_qianggang"
                         else check_action_after_cut(state, 25))


def reply_to_rechecks(state, respond):
    for seat, player in enumerate(state.player_list):
        socket = state.game_server.user_id_to_connection[player.user_id].websocket
        original = socket.send_json

        async def send(payload, seat=seat, original=original):
            await original(payload)
            info = payload.get("ask_other_action_info")
            if info and info.get("is_tactical_recheck"):
                await respond(seat, info)

        socket.send_json = send


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", CLAIM_STATUSES)
@pytest.mark.parametrize("discarder", range(4))
@pytest.mark.parametrize("response", ["hu", "pass", "force_pass", "timeout"])
def test_passed_nearer_winner_is_asked_again_after_farther_hu(mode, status, discarder, response):
    async def run():
        state, messages, (near, far) = claim_state(mode, status, discarder)
        rechecks = []
        task = None

        async def respond(seat, info):
            rechecks.append((seat, info))
            assert seat == near
            assert info["action_list"] == ["hu", "pass", "force_pass"]
            # 旧轮和牌、非法碰牌及重复回复不能替代本次独立选择。
            for action, tick in (("hu", opening_tick), ("peng", info["action_tick"])):
                state.action_queues[seat].put_nowait({"action_type": action, "_action_tick": tick})
            state.action_events[seat].set()
            if response != "timeout":
                await submit(state, seat, response, tick=info["action_tick"])
                state.action_queues[seat].put_nowait({"action_type": "pass", "_action_tick": info["action_tick"]})

        try:
            reply_to_rechecks(state, respond)
            await state.broadcast_ask_other_action()
            opening_tick = state.server_action_tick
            await submit(state, near, "pass")
            task = asyncio.create_task(wait_action(state))

            async def wait_for_pass():
                while near in state.waiting_players_list:
                    await asyncio.sleep(0)

            await asyncio.wait_for(wait_for_pass(), 1)
            assert not state.action_dict[near], "先消费较近座位的过，再由较远座位报和"
            assert not task.done()
            banks = [player.remaining_time for player in state.player_list]
            await submit(state, far, "hu")
            await asyncio.wait_for(task, 1)
            assert [(seat, info["action_tick"]) for seat, info in rechecks] == [(near, opening_tick + 1)]
            assert rechecks[0][1]["action_list"] == ["hu", "pass", "force_pass"]
            assert state._waiting_action_tick == state.server_action_tick
            assert [player.remaining_time for player in state.player_list] == banks
            expected = [near, far] if response == "hu" else [far]
            assert set(state.sichuan_hu_results) == set(expected)
            assert state.game_status == "settle_win"
            assert state.pending_win["discarder"] == discarder
            if status == "waiting_action_qianggang":
                assert state.player_list[discarder].combination_tiles == ["k25"]
            await settle_without_animation(state)
            assert state.first_win_event["winners"] == expected
            results = [entry["message"]["show_result_info"] for entry in messages
                       if entry["type"].endswith("/show_result")
                       and entry["user_id"] == state.player_list[discarder].user_id]
            assert [result["hepai_player_index"] for result in results] == expected
            assert all(result.get("silent") for result in results), "已播报和牌申请的玩家不能重复播音效"
            ticks = state.game_record["game_round"][f"round_index_{state.round_index}"]["action_ticks"]
            assert [tick[1] for tick in ticks if tick[0].startswith("hu_")] == expected
        finally:
            if task is not None and not task.done():
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", CLAIM_STATUSES)
@pytest.mark.parametrize("discarder", range(4))
@pytest.mark.parametrize("scenario", ["force_pass", "tactical_off", "farther_passed", "both_hu"])
def test_only_unconfirmed_higher_seat_pass_can_trigger_hu_recheck(mode, status, discarder, scenario):
    async def run():
        state, messages, (near, far) = claim_state(mode, status, discarder,
                                                 tactical_call=scenario != "tactical_off")
        rechecks = []

        async def respond(seat, info):
            rechecks.append(seat)
            await submit(state, seat, "pass", tick=info["action_tick"])

        try:
            reply_to_rechecks(state, respond)
            await state.broadcast_ask_other_action()
            opening_tick = state.server_action_tick
            if scenario == "farther_passed":
                replies, expected = [(far, "pass"), (near, "hu")], {near}
            elif scenario == "both_hu":
                replies, expected = [(far, "hu"), (near, "hu")], {near, far}
            else:
                replies = [(near, "force_pass" if scenario == "force_pass" else "pass"), (far, "hu")]
                expected = {far}
            for seat, action in replies:
                await submit(state, seat, action)
            await asyncio.wait_for(wait_action(state), 1)
            assert not rechecks
            assert state.server_action_tick == opening_tick
            assert set(state.sichuan_hu_results) == expected
            if scenario == "both_hu":
                await settle_without_animation(state)
                audio = {entry["message"]["show_result_info"]["hepai_player_index"]:
                         bool(entry["message"]["show_result_info"].get("silent"))
                         for entry in messages if entry["type"].endswith("/show_result")
                         and entry["user_id"] == state.player_list[discarder].user_id}
                assert audio == {near: False, far: True}, "仅已播过申请的和牌者静音结算"
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", CLAIM_STATUSES)
@pytest.mark.parametrize("first_recheck", ["pass", "force_pass"])
def test_hu_upgrade_rechecks_nearer_pass_but_keeps_all_confirmed_winners(mode, status, first_recheck):
    async def run():
        state, _, (near, middle, far) = claim_state(mode, status, 3, distances=(1, 2, 3))
        rechecks = []

        async def respond(seat, info):
            tick = info["action_tick"]
            rechecks.append((seat, tick))
            assert seat != far, "已报和者不可在后续再问中被取消和牌"
            action = first_recheck if seat == near and tick == opening_tick + 1 else "hu"
            await submit(state, seat, action, tick=tick)

        try:
            reply_to_rechecks(state, respond)
            await state.broadcast_ask_other_action()
            opening_tick = state.server_action_tick
            for seat, action in ((near, "pass"), (middle, "pass"), (far, "hu")):
                await submit(state, seat, action)
            await asyncio.wait_for(wait_action(state), 1)
            expected_rechecks = [(near, opening_tick + 1), (middle, opening_tick + 1)]
            expected_winners = {middle, far}
            if first_recheck == "pass":
                expected_rechecks.append((near, opening_tick + 2))
                expected_winners.add(near)
            assert rechecks == expected_rechecks
            assert set(state.sichuan_hu_results) == expected_winners
            assert state.game_status == "settle_win"
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", CLAIM_STATUSES)
@pytest.mark.parametrize("far_first", [False, True])
def test_farther_hu_rechecks_middle_pass_even_if_nearer_player_already_won(mode, status, far_first):
    async def run():
        state, _, (near, middle, far) = claim_state(mode, status, 3, distances=(1, 2, 3))
        rechecks = []

        async def respond(seat, info):
            rechecks.append(seat)
            await submit(state, seat, "hu", tick=info["action_tick"])

        try:
            reply_to_rechecks(state, respond)
            await state.broadcast_ask_other_action()
            first, last = (far, near) if far_first else (near, far)
            for seat, action in ((first, "hu"), (middle, "pass"), (last, "hu")):
                await submit(state, seat, action)
            await asyncio.wait_for(wait_action(state), 1)
            assert rechecks == [middle]
            assert set(state.sichuan_hu_results) == {near, middle, far}
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("status", CLAIM_STATUSES)
@pytest.mark.parametrize("required", [False, True])
def test_xueliu_doubles_post_hu_required_and_teammate_choices(status, required):
    async def run():
        distances = (1, 3) if required else (2, 3)
        state, _, (near, far) = claim_state("xueliu_doubles", status, 0, distances=distances)
        player = state.player_list[near]
        player.has_won = player.post_hu_lock = True
        player.huapai_list = [25]
        refresh_claims(state)
        assert state.action_dict[near] == (["hu"] if required else ["hu", "pass", "force_pass"])
        rechecks = []

        async def respond(seat, info):
            rechecks.append(seat)
            await submit(state, seat, "hu", tick=info["action_tick"])

        try:
            reply_to_rechecks(state, respond)
            await state.broadcast_ask_other_action()
            if not required:
                await submit(state, near, "pass")
            await submit(state, far, "hu")
            await asyncio.wait_for(wait_action(state), 1)
            assert rechecks == ([] if required else [near])
            assert set(state.sichuan_hu_results) == {near, far}
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
