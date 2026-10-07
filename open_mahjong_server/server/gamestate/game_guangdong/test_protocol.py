"""广东私有协议使用实际求解器；只替换进程桥和网络传输。"""

import asyncio
from copy import deepcopy
from dataclasses import replace
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from . import tips
from .test_state import HAND, draw, hand, make_state, ticks
from ...game_calculation.guangdong import rules
from ...game_calculation.guangdong.scoring import Context
from ...game_calculation.guangdong.settlement import kong_payments
from ..game_taiwan import boardcast


TIP_MODULE = "server.gamestate.game_guangdong.tips"
TIME_MODULE = "server.gamestate.public.spectator_manager.time.time"
MELDS = ("k11", "k22", "k33")


class Socket:
    def __init__(self):
        self.messages = []

    async def send_json(self, payload):
        self.messages.append(deepcopy(payload))


def connection():
    return SimpleNamespace(websocket=Socket())


async def actual_cpu(_state, function, *args):
    return function(*args)


@pytest.fixture
def cpu():
    with patch(f"{TIP_MODULE}.run_room_bot_cpu", new=AsyncMock(side_effect=actual_cpu)) as bridge:
        yield bridge


def protocol_state(**options):
    state = make_state(**({"tips": True, "allow_spectator": True} | options))
    hands = ([41, 41, 41, 45], [42, 42, 42, 46], [43, 43, 43, 47], [44, 44, 44, 19])
    melds = (("k11", "k12", "k13"), ("k21", "k22", "k23"),
             ("k31", "k32", "k33"), ("k17", "k27", "k37"))
    for index, player in enumerate(state.player_list):
        hand(state, index, hands[index], melds[index])
        player.discard_count = 1
        state.game_server.user_id_to_connection[player.user_id] = connection()
    state.game_status = "waiting_hand_action"
    state.action_dict = {i: ["cut"] if i == 0 else [] for i in range(4)}
    state.waiting_players_list = [0]
    return state


def inbox(state, index):
    return state.game_server.user_id_to_connection[state.player_list[index].user_id].websocket.messages


def assert_private_hand(game_info, state, view_index):
    assert game_info["room_rule"] == "guangdong"
    assert game_info["sub_rule"] == "guangdong/mil2023"
    assert game_info["guangdong_tips"]["hand"] == sorted(state.player_list[view_index].hand_tiles)
    assert game_info["guangdong_tips"]["melds"] == state.player_list[view_index].combination_tiles
    for index, player in enumerate(game_info["players_info"]):
        if index == view_index:
            assert player["hand_tiles"] == state.player_list[index].hand_tiles
        else:
            assert "hand_tiles" not in player


def tip_ticks(state):
    return [tick for tick in ticks(state) if tick[:2] == ["guangdong", "tips"]]


def test_real_batch_13_14_and_invalid_nominal_sizes():
    context = Context()
    complete = (41, 41, 41, 45, 45)
    waiting = complete[:-1]
    batch = tips.calculate_tip_batch([
        (waiting, MELDS, context, -1),
        (complete, MELDS, context, -1),
        ((11,), (), context, -1),
    ])
    assert batch[0]["waits"] == rules.waiting_scores(waiting, MELDS, context=context)
    assert {entry["tile"] for entry in batch[0]["waits"]} == {45, 55, 56, 57, 58}
    assert batch[1]["waits"] == [] and batch[1]["discard_waits"]
    for tile, waits in batch[1]["discard_waits"].items():
        remainder = list(complete)
        remainder.remove(int(tile))
        assert waits == rules.waiting_scores(remainder, MELDS, context=context)
    assert batch[2] == {"hand": [11], "melds": [], "waits": [], "discard_waits": {}}


@pytest.mark.parametrize("ghost", (55, 56, 57, 58))
def test_discard_ghost_tips_include_new_coefficient_without_passing_win(ghost):
    complete = (41, 41, 41, 45, ghost)
    context = Context(heavenly=True, earthly=True, kong_flower=True, discarded_ghosts=1)
    payload = tips.calculate_tip_batch([(complete, MELDS, context, -1)])[0]
    after = replace(context, heavenly=False, earthly=False, kong_flower=False, discarded_ghosts=2)
    expected = rules.waiting_scores(complete[:-1], MELDS, context=after)
    assert payload["discard_waits"][str(ghost)] == expected
    ordinary = next(item for item in expected if item["tile"] == 45)
    assert ordinary["ron"] and ordinary["self_draw"]
    assert ordinary["score"] >= 4 * 8  # 出二鬼 ×4、无鬼再 ×2。


def test_nonwinning_14_hand_excludes_discards_without_legal_waits():
    # 任何切牌均未形成基本听牌结构。
    complete = (11, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31, 33, 35, 47)
    payload = tips.calculate_tip_batch([(complete, (), Context(), -1)])[0]
    assert payload["discard_waits"] == {}


@pytest.mark.parametrize("fan,count", [(False, False), (False, True), (True, False), (True, True)])
def test_hint_switches_supply_structure_for_count_only_rooms(cpu, fan, count):
    state = protocol_state(tips=fan, count_tips=count)
    player = hand(state, 0, [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 55])
    asyncio.run(boardcast.broadcast_game_start(state))
    fields = inbox(state, 0)[0]["game_info"]
    if not fan and not count:
        assert "guangdong_tips" not in fields and cpu.await_count == 0
        return
    payload = fields["guangdong_tips"]
    assert {item["tile"] for item in payload["waits"]} == set(rules.structural_waits(player.hand_tiles))
    assert len(payload["waits"]) == 36
    asyncio.run(boardcast.broadcast_do_action(state, ["cut"], 1, cut_tile=46))
    own = inbox(state, 0)[-1]["do_action_info"]["guangdong_tips"]
    assert own["hand"] == sorted(player.hand_tiles)
    assert any(tick[2] == 0 and tick[3]["waits"] == payload["waits"] for tick in tip_ticks(state))


def test_prepare_cache_skips_invalid_seats_reuses_snapshot_and_getters_are_read_only(cpu):
    state = protocol_state()
    fallback = state.guangdong_tips(0)
    assert not fallback["waits"]
    asyncio.run(state.prepare_private_fields((-1, 0, 4)))
    assert cpu.await_count == 1 and set(state._tips_cache) == {0}
    expected = state.guangdong_tips(0)
    assert expected["waits"]
    for _ in range(2):
        assert state.build_private_game_info_fields(0)["guangdong_tips"] == expected
        assert state.build_private_game_info_fields(-1) == {}
        assert state.build_private_game_info_fields(4) == {}
        asyncio.run(state.prepare_private_fields((0,)))
    assert cpu.await_count == 1
    state.player_list[0].hand_tiles.reverse()
    asyncio.run(state.prepare_private_fields((0,)))
    assert cpu.await_count == 1  # 排序与 UI 槽位改变不使相同的牌值缓存失效。
    state.player_list[0].hand_tiles[-1] = 46
    assert state.guangdong_tips(0)["waits"] == []
    asyncio.run(state.prepare_private_fields((0,)))
    assert cpu.await_count == 2


@pytest.mark.parametrize("change", ("hand", "meld", "passed", "ghost_count", "last_wall", "configuration"))
def test_cpu_result_for_stale_snapshot_is_discarded(change):
    state = protocol_state()

    async def compute_then_change(_state, function, *args):
        payload = function(*args)
        player = state.player_list[0]
        if change == "hand":
            player.hand_tiles[-1] = 46
        elif change == "meld":
            player.combination_tiles[0] = "k18"
        elif change == "passed":
            player.passed_base_score = 100
        elif change == "ghost_count":
            player.discarded_ghosts = 1
        elif change == "last_wall":
            state.tiles_list.clear()
        else:
            state.rules_dict["require_minimum_score"] = False
        return payload

    with patch(f"{TIP_MODULE}.run_room_bot_cpu", new=AsyncMock(side_effect=compute_then_change)):
        asyncio.run(state.prepare_private_fields((0,)))
    assert 0 not in state._tips_cache
    assert state.guangdong_tips(0)["waits"] == []


def test_out_of_order_cpu_results_keep_newer_snapshot():
    state = protocol_state()

    async def scenario():
        started, resume = asyncio.Event(), asyncio.Event()
        calls = 0

        async def reordered_cpu(_state, function, *args):
            nonlocal calls
            calls += 1
            result = function(*args)
            if calls == 1:
                started.set()
                await resume.wait()
            return result

        with patch(f"{TIP_MODULE}.run_room_bot_cpu", new=AsyncMock(side_effect=reordered_cpu)):
            old_task = asyncio.create_task(state.prepare_private_fields((0,)))
            await started.wait()
            state.player_list[0].hand_tiles[-1] = 46
            await state.prepare_private_fields((0,))
            newer = deepcopy(state.guangdong_tips(0))
            resume.set()
            await old_task
        assert state.guangdong_tips(0) == newer
        assert newer["hand"][-1] == 46 and newer["waits"]

    asyncio.run(scenario())


@pytest.mark.parametrize("event", ("start", "ask", "do", "reconnect"))
def test_tips_disabled_never_computes_transmits_or_records_private_tips(cpu, event):
    state = protocol_state(tips=False)
    if event == "start":
        asyncio.run(boardcast.broadcast_game_start(state))
    elif event == "ask":
        asyncio.run(boardcast.broadcast_ask_hand_action(state))
    elif event == "do":
        asyncio.run(boardcast.broadcast_do_action(state, ["cut"], 0, cut_tile=45))
    else:
        asyncio.run(state.player_reconnect(101))
    cpu.assert_not_awaited()
    messages = [message for i in range(4) for message in inbox(state, i)]
    assert messages
    for message in messages:
        for field in ("game_info", "ask_hand_action_info", "do_action_info"):
            assert "guangdong_tips" not in message.get(field, {})
    assert not tip_ticks(state)


def test_all_four_start_and_reconnect_messages_have_only_their_own_hand_and_tips(cpu):
    state = protocol_state()
    asyncio.run(boardcast.broadcast_game_start(state))
    assert cpu.await_count == 1
    for index in range(4):
        assert_private_hand(inbox(state, index)[0]["game_info"], state, index)
        asyncio.run(state.player_reconnect(state.player_list[index].user_id))
        snapshots = [m for m in inbox(state, index) if "game_info" in m]
        assert len(snapshots) == 2
        assert_private_hand(snapshots[-1]["game_info"], state, index)
    assert cpu.await_count == 1


@pytest.mark.parametrize("active", range(4))
def test_ask_only_contains_active_seat_private_tips(cpu, active):
    state = protocol_state()
    state.current_player_index = active
    state.action_dict = {i: ["cut"] if i == active else [] for i in range(4)}
    asyncio.run(boardcast.broadcast_ask_hand_action(state))
    for index in range(4):
        info = inbox(state, index)[-1]["ask_hand_action_info"]
        assert info["player_index"] == active
        if index == active:
            assert info["guangdong_tips"]["hand"] == sorted(state.player_list[index].hand_tiles)
            assert info["action_list"] == ["cut"]
        else:
            assert "guangdong_tips" not in info and info["action_list"] == []
    assert [tick[2] for tick in tip_ticks(state)] == [active]


@pytest.mark.parametrize("actor", range(4))
def test_do_action_uses_viewer_tips_and_hides_other_players_draw(cpu, actor):
    state = protocol_state()
    draw(state.player_list[actor], 55)
    asyncio.run(boardcast.broadcast_do_action(state, ["deal_tile"], actor, deal_tile=55))
    for index in range(4):
        info = inbox(state, index)[-1]["do_action_info"]
        assert info["action_player"] == actor
        assert info["guangdong_tips"]["hand"] == sorted(state.player_list[index].hand_tiles)
        if index == actor:
            assert info["deal_tile"] == 55
        else:
            assert info.get("deal_tile") in (None, 0)
    assert sorted(tick[2] for tick in tip_ticks(state)) == list(range(4))


def test_authorized_realtime_spectators_follow_host_identity_after_seat_rotation(cpu):
    state = protocol_state()
    ordinary = connection()
    state.game_server.user_id_to_connection[900] = ordinary
    state.spectator_manager.spectator_connections[900] = ordinary
    spectators = []
    for index in range(4):
        uid = 901 + index
        state.game_server.user_id_to_connection[uid] = connection()
        spectators.append(SimpleNamespace(user_id=uid, host_user_id=state.player_list[index].user_id))
    state.realtime_spectators = spectators
    asyncio.run(boardcast.broadcast_game_start(state))
    for index, spectator in enumerate(spectators):
        messages = state.game_server.user_id_to_connection[spectator.user_id].websocket.messages
        assert len(messages) == 1
        assert_private_hand(messages[0]["game_info"], state, index)
        assert messages[0]["game_info"]["view_player_index"] == index
    assert ordinary.websocket.messages == []
    state.player_list = state.player_list[1:] + state.player_list[:1]
    for index, player in enumerate(state.player_list):
        player.player_index = index
    asyncio.run(boardcast.broadcast_do_action(state, ["cut"], 1, cut_tile=46))
    for original, spectator in enumerate(spectators):
        view = (original - 1) % 4
        info = state.game_server.user_id_to_connection[spectator.user_id].websocket.messages[-1]["do_action_info"]
        assert info["guangdong_tips"]["hand"] == sorted(state.player_list[view].hand_tiles)
    assert ordinary.websocket.messages == []


@pytest.mark.parametrize("view", range(4))
def test_realtime_initial_snapshot_and_pending_ask_keep_authorized_view(cpu, view):
    state = protocol_state()
    uid = 910
    state.game_server.user_id_to_connection[uid] = connection()
    state.realtime_spectators = [SimpleNamespace(user_id=uid, host_user_id=state.player_list[view].user_id)]
    state.current_player_index = view
    state.action_dict = {i: ["cut"] if i == view else [] for i in range(4)}
    asyncio.run(boardcast.send_realtime_spectator_snapshot(state, uid, view))
    messages = state.game_server.user_id_to_connection[uid].websocket.messages
    assert len(messages) == 2
    assert_private_hand(messages[0]["game_info"], state, view)
    assert messages[1]["ask_hand_action_info"]["guangdong_tips"]["hand"] == sorted(state.player_list[view].hand_tiles)


def test_spectator_removal_and_match_block_prevent_future_private_delivery(cpu):
    state = protocol_state()
    state.game_server.user_id_to_connection[901] = connection()
    spectator = SimpleNamespace(user_id=901, host_user_id=101)
    state.realtime_spectators = [spectator]
    state.game_server.match_manager = SimpleNamespace(blocks_spectator=lambda uid: uid == 901)
    asyncio.run(boardcast.broadcast_game_start(state))
    messages = state.game_server.user_id_to_connection[901].websocket.messages
    assert messages == []
    state.game_server.match_manager.blocks_spectator = lambda uid: False
    state.realtime_spectators.clear()
    asyncio.run(boardcast.broadcast_do_action(state, ["cut"], 0, cut_tile=45))
    assert messages == []


def test_concealed_kong_has_public_middle_tiles_and_ledger_for_all_viewers(cpu):
    state = protocol_state()
    state.kong_ledger = [dict(player=0, kind="concealed", payer=None, tile=41,
                              changes=kong_payments(0, "concealed")),
                         dict(player=1, kind="direct", payer=2, tile=42,
                              changes=kong_payments(1, "direct", payer=2)),
                         dict(player=3, kind="added", payer=None, tile=44,
                              changes=kong_payments(3, "added"))]
    state._score_event = {"gang_score_changes": {0: 6, 1: -2, 2: -2, 3: -2}, "gang_score_type": "concealed"}
    asyncio.run(boardcast.broadcast_do_action(state, ["angang"], 0,
        combination_mask=[2, 41, 0, 41, 0, 41, 2, 41], combination_target="G41"))
    for index in range(4):
        info = inbox(state, index)[-1]["do_action_info"]
        ledger = info["guangdong_state"]["kong_ledger"]
        assert ledger[0]["tile"] == 41
        assert [row["tile"] for row in ledger[1:]] == [42, 44]
        assert info["gang_score_type"] == "concealed"
        assert sum(info["gang_score_changes"].values()) == 0
        assert info["combination_mask"] == [2, 41, 0, 41, 0, 41, 2, 41]
        assert info["combination_target"] == "G41"
    assert state.kong_ledger[0]["tile"] == 41


def test_real_concealed_kong_snapshot_reconnect_and_record_keep_public_faces(cpu):
    async def run():
        state = protocol_state(tips=False)
        player = hand(state, 0, HAND); draw(player, 11)
        await state.execute_angang(0, 11)
        expected = [2, 11, 0, 11, 0, 11, 2, 11]
        assert player.combination_mask == [expected]
        assert ["ag", 11, "T", 11, 11, 11, 11] in ticks(state)
        await boardcast.broadcast_game_start(state)
        for viewer in range(4):
            await boardcast.send_reconnect_game_state(state, state.player_list[viewer])
            info = next(m["game_info"] for m in reversed(inbox(state, viewer)) if "game_info" in m)
            assert info["players_info"][0]["combination_tiles"] == ["G11"]
            assert info["players_info"][0]["combination_mask"] == [expected]
            if viewer != 0:
                assert "hand_tiles" not in info["players_info"][0]
        state.game_server.user_id_to_connection[501] = connection()
        await boardcast.send_realtime_spectator_snapshot(state, 501, 1)
        info = state.game_server.user_id_to_connection[501].websocket.messages[0]["game_info"]
        assert info["players_info"][0]["combination_mask"] == [expected]
        assert "hand_tiles" not in info["players_info"][0]
    asyncio.run(run())


def test_record_deduplicates_tips_and_ordinary_spectator_waits_full_delay(cpu):
    state = protocol_state()
    manager = state.spectator_manager
    with patch(TIME_MODULE, return_value=1000):
        asyncio.run(boardcast.broadcast_game_start(state))
    with patch(TIME_MODULE, return_value=1010):
        state.record_guangdong_tips(0)
        state.record_guangdong_tips(0)
    assert len(tip_ticks(state)) == 1
    assert manager._build_spectator_record(999) is None
    before = manager._build_spectator_record(1009)
    assert before is not None
    assert before["game_round"]["round_index_1"]["action_ticks"] == []
    after = manager._build_spectator_record(1010)
    assert after["game_round"]["round_index_1"]["action_ticks"] == tip_ticks(state)
    # delivery_loop 使用 now - delay；精确跨过 180 秒时才可下发该提示。
    manager.spectator_progress[900] = {"round_index": 1, "tick_index": 0}
    assert manager._get_new_updates(900, (1010 + manager.spectator_delay - 0.001) - manager.spectator_delay) is None
    update = manager._get_new_updates(900, (1010 + manager.spectator_delay) - manager.spectator_delay)
    assert update is not None
    assert manager._get_new_updates(900, 2000) is None
    state.game_record["game_round"].clear()
    state.record_guangdong_tips(1)
    assert 1 not in state._recorded_tips


def test_private_kong_choices_are_actual_legal_actions_and_do_not_need_tips(cpu):
    state = protocol_state(tips=False)
    p = hand(state, 0, [41, 41, 41, 41, 11], ("k11", "k22", "k33"))
    p.last_drawn_tile = 11
    fields = state.build_private_hand_action_info(0)
    assert fields["kong_candidates"] == {"angang": [41], "jiagang": [11]}
    assert fields["riichi_candidate_cuts"] == {} and fields["forbidden_cut_tiles"] == []
    assert "guangdong_tips" not in fields
    cpu.assert_not_awaited()


def test_terminal_reconnect_resends_snapshot_without_repaying_or_rebuying_horses(cpu):
    state = protocol_state()
    player = hand(state, 0, HAND)
    draw(player, 45)
    state.current_round = state.max_round * 4
    state.opening_dealer_action = False
    state.tiles_list = [11, 12, 13, 14, 55, 56]
    before = {p.original_player_index: p.score for p in state.player_list}
    state.accept_self_draw(0)
    assert asyncio.run(state._settle_hand(before))
    snapshot = deepcopy(state._terminal_result)
    assert snapshot["show_result_info"]["guangdong_result"]["horses"]["tiles"] == [11, 12, 13, 14]
    stable = ([p.score for p in state.player_list], [deepcopy(p.score_history) for p in state.player_list],
              state.server_action_tick, deepcopy(ticks(state)), deepcopy(state.tiles_list), player.record_counter.zimo_times)
    for index in range(4):
        asyncio.run(state.player_reconnect(state.player_list[index].user_id))
        assert inbox(state, index)[-1] == snapshot
    assert stable == ([p.score for p in state.player_list], [deepcopy(p.score_history) for p in state.player_list],
                      state.server_action_tick, ticks(state), state.tiles_list, player.record_counter.zimo_times)
