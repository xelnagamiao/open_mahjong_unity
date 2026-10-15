import asyncio
from collections import Counter
from types import SimpleNamespace
import unittest
from unittest.mock import patch, AsyncMock

from server.game_calculation.changsha.exchange_six import (
    evaluate, waiting_tiles, big_weight, base_score, group_kong_wins,
)
from server.gamestate.game_changsha.ChangshaGameState import ChangshaGameState
from server.gamestate.game_changsha.exchange_six import EXCHANGE_ACTION, ChangshaExchangeSixResponse
from server.gamestate.game_changsha.exchange_six_config import SUB_RULE, normalize_config
from server.gamestate.game_changsha.init_tiles import init_changsha_tiles
from server.gamestate.game_changsha.boardcast import reconnected_send_pending_ask
from server.gamestate.public.ai.get_action import get_action
from server.gamestate.public.ai.pacing import configure_bot_pacing


class FakeSocket:
    def __init__(self):
        self.messages = []

    async def send_json(self, message):
        self.messages.append(message)


def make_state(bot_seats=()):
    ids = [seat if seat in bot_seats else 100 + seat for seat in range(4)]
    connections = {uid: SimpleNamespace(user_id=uid, websocket=FakeSocket()) for uid in ids if uid > 10}
    server = SimpleNamespace(user_id_to_connection=connections,
                             players={f"conn-{seat}": connections[uid] for seat, uid in enumerate(ids) if uid > 10})
    config = {"player_list": ids, "round_timer": 10, "step_timer": 2, "room_id": "test-room",
              "tips": False, "game_round": 1, "room_rule": "changsha", "room_type": "custom",
              "sub_rule": SUB_RULE, "allow_spectator": False, "claim_protection": False, "bot_speed": "instant"}
    state = ChangshaGameState(server, config, None, None, "test-game")
    configure_bot_pacing(state, config)
    for seat, player in enumerate(state.player_list):
        player.player_index = seat
        player.original_player_index = seat
    state.game_record = {"game_round": {"round_index_1": {"action_ticks": []}}}
    state.round_random_seed = 2345
    init_changsha_tiles(state)
    # 固定手牌回归不依赖随机禁手座位；禁手专项另从真实抓牌骰子入口验证。
    state.exchange_six.small_hu_forbidden_seat = None
    state.exchange_six.round_scores_before = {player.original_player_index: player.score for player in state.player_list}
    return state


class ExchangeSixCalculationTests(unittest.TestCase):
    def test_context_alone_cannot_allow_non_jiang_pair(self):
        hand = [11, 11, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32, 33]
        for way in ("地胡", "海底", "杠上开花", "杠上炮", "天胡", "抢杠胡"):
            with self.subTest(way=way):
                self.assertEqual(evaluate(hand, [], [way]), (0, []))
        self.assertGreater(evaluate(hand, [], reported_ready=True)[0], 0)

    def test_valid_jiang_pair_and_flush_still_win(self):
        mixed = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32, 33]
        self.assertEqual(evaluate(mixed, []), (2, ["小胡"]))
        self.assertEqual(evaluate(mixed, [], ["海底"]), (8, ["海底"]))
        pure = [11, 11, 12, 13, 14, 12, 13, 14, 15, 16, 17, 17, 18, 19]
        self.assertIn("清一色", evaluate(pure, [])[1])

    def test_thirteen_jiang_with_and_without_pair(self):
        terminals = [11, 19, 21, 29, 31, 39]
        with_pair = terminals + [12, 15, 18, 22, 25, 28, 32, 32]
        without_pair = terminals + [12, 15, 18, 22, 25, 28, 32, 35]
        self.assertEqual(evaluate(with_pair, []), (8, ["十三将"]))
        self.assertEqual(evaluate(without_pair, []), (16, ["无对十三将"]))
        self.assertIn(35, waiting_tiles(without_pair[:-1], [], reported_ready=True))

    def test_all_terminals_needs_no_normal_shape_but_valid_count(self):
        hand = [11] * 4 + [19] * 2 + [21] * 2 + [29] * 2 + [31] * 2 + [39] * 2
        fans = evaluate(hand, [])[1]
        self.assertIn("全幺九", fans)
        self.assertEqual(big_weight(["全幺九"]), 2)
        self.assertEqual(evaluate(hand[:-1], []), (0, []))
        self.assertEqual(evaluate([11] * 5 + hand[5:], []), (0, []))

    def test_luxury_pairs_replaces_normal_pairs(self):
        hand = [11] * 4 + [12] * 2 + [13] * 2 + [14] * 2 + [15] * 2 + [16] * 2
        fans = evaluate(hand, [])[1]
        self.assertIn("豪华七小对", fans)
        self.assertNotIn("七小对", fans)

    def test_actual_waits_produce_64_and_48_groups(self):
        hand = [11] * 3 + [12] * 3 + [13] * 3 + [14] * 2 + [15] * 2
        for tiles, expected in (([11, 12, 13], 64), ([11, 14], 48), ([11, 12, 14], 64)):
            results = [(tile, evaluate(hand + [tile], [], ["杠上炮"])[1]) for tile in tiles]
            groups = group_kong_wins(results, "杠上炮")
            self.assertEqual(sum(base_score(big_weight(group["fans"])) for group in groups), expected)


class ExchangeSixFlowTests(unittest.IsolatedAsyncioTestCase):
    async def test_bot_stage_respects_room_speed_without_delaying_human_ingress(self):
        state = make_state(bot_seats=(0,))
        flow = state.exchange_six
        with patch('server.gamestate.game_changsha.exchange_six.bot_delay', return_value=0.5):
            task = asyncio.create_task(flow.run_stage('claims', {0: ['pass'], 1: ['pass']}, lambda seat: {'action_type': 'pass'}))
            await self.wait_for_opening(state)
            await self.action(state, 1, 'pass')
            async def wait_for_human():
                while 1 in state.waiting_players_list:
                    await asyncio.sleep(0.005)
            await asyncio.wait_for(wait_for_human(), 0.4)
            self.assertEqual(state.waiting_players_list, [0])
            self.assertFalse(task.done())
            result = await asyncio.wait_for(task, 1)
            self.assertEqual(set(result), {0, 1})

    async def test_exchange_time_bounds_and_default_are_validated(self):
        from server.room.room_validators import ChangshaRoomValidator
        options = dict(room_name="计时测试", game_round=1, round_timer=10, step_timer=2)
        self.assertEqual(ChangshaRoomValidator(**options).exchange_selection_seconds, 30)
        for seconds in (10, 30, 60):
            self.assertEqual(ChangshaRoomValidator(**options, exchange_selection_seconds=seconds).exchange_selection_seconds, seconds)
            self.assertEqual(normalize_config({"exchange_selection_seconds": seconds}, SUB_RULE)["exchange_selection_seconds"], seconds)
        for invalid in (9, 61, 10.5, True, "30", None):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                ChangshaRoomValidator(**options, exchange_selection_seconds=invalid)

    async def test_selection_budget_reconnect_late_ingress_and_timeout_preserve_turn_bank(self):
        from server.gamestate.gamestate_router import handle_gamestate_message
        for seconds in (10, 60):
            state = make_state()
            state.exchange_selection_seconds = seconds
            flow = state.exchange_six
            state.game_server.gamestate_manager = SimpleNamespace(get_game_state_by_gamestate_id=lambda gid: state)
            before = [player.remaining_time for player in state.player_list]
            task = asyncio.create_task(flow.exchange(dice=[1, 1]))
            await self.wait_for_opening(state)
            try:
                with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=seconds - 2.2):
                    await reconnected_send_pending_ask(state, 100)
                    packet = state.game_server.user_id_to_connection[100].websocket.messages[-1]["state"]
                    self.assertEqual((packet["remaining_time"], packet["step_remaining"]), (3, 0))
                    self.assertEqual(packet["public_state"]["dice"], [])
                    self.assertEqual(packet["public_state"]["source_offset"], 0)
                request = {"type": "gamestate/GB/send_action", "gamestate_id": state.gamestate_id,
                           "action": EXCHANGE_ACTION, "action_tick": state.server_action_tick,
                           "selectedTiles": flow.default_selection(0)}
                with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=seconds):
                    await handle_gamestate_message(state.game_server, "conn-0", request, FakeSocket())
                    self.assertTrue(state.action_queues[0].empty(), "到期客户端请求必须拒绝")
                    state.action_events[0].set()
                    await asyncio.wait_for(task, 1)
                self.assertEqual([player.remaining_time for player in state.player_list], before)
                messages = state.game_server.user_id_to_connection[100].websocket.messages
                roll = next(item["state"] for item in messages if item["state"]["phase"] == "exchange_dice")
                self.assertEqual(roll["public_state"]["submitted_seats"], [0, 1, 2, 3])
                self.assertEqual(roll["public_state"]["dice"], [1, 1])
            finally:
                if not task.done():
                    task.cancel()
                    await asyncio.gather(task, return_exceptions=True)

    async def test_real_router_accepts_meld_target_and_current_tick(self):
        from server.gamestate.gamestate_router import handle_gamestate_message
        state = make_state()
        flow = state.exchange_six
        state.game_server.gamestate_manager = SimpleNamespace(get_game_state_by_gamestate_id=lambda gid: state)
        flow.targets[0] = {"chi_mid": [24]}
        task = asyncio.create_task(flow.run_stage("claims", {0: ["chi_mid", "pass"]}, lambda seat: {"action_type": "pass"}))
        await self.wait_for_opening(state)
        request = {"type": "gamestate/GB/send_action", "gamestate_id": state.gamestate_id,
                   "action": "chi_mid", "targetTile": 24, "action_tick": state.server_action_tick}
        await handle_gamestate_message(state.game_server, "conn-0", request, FakeSocket())
        answers = await asyncio.wait_for(task, 1)
        self.assertEqual((answers[0]["action_type"], answers[0]["target_tile"]), ("chi_mid", 24))

    async def action(self, state, seat, action, target=0, tile=0):
        await get_action(state, f"conn-{seat}", action, False, tile, 0, target, action_tick=state.server_action_tick)
    async def submit(self, state, seat, selected, tick=None):
        await get_action(state, f"conn-{seat}", EXCHANGE_ACTION, False, 0, 0, 0,
                         action_tick=state.server_action_tick if tick is None else tick, selected_tiles=selected)

    async def wait_for_opening(self, state):
        for _ in range(20):
            if state.waiting_players_list:
                return
            await asyncio.sleep(0)
        self.fail("生产阶段未建立等待集合")

    async def test_real_ingress_atomically_exchanges_and_preserves_wall(self):
        state = make_state()
        flow = state.exchange_six
        before = [list(player.hand_tiles) for player in state.player_list]
        wall = list(state.tiles_list)
        outgoing = [flow.default_selection(seat) for seat in range(4)]
        task = asyncio.create_task(flow.exchange())
        await self.wait_for_opening(state)
        try:
            await self.submit(state, 0, outgoing[0], tick=state.server_action_tick - 1)
            await self.submit(state, 0, [99] * 6)
            self.assertTrue(state.action_queues[0].empty())
            for seat in range(3):
                await self.submit(state, seat, outgoing[seat])
            await asyncio.sleep(0)
            self.assertEqual([player.hand_tiles for player in state.player_list], before)
            await self.submit(state, 3, outgoing[3])
            await asyncio.wait_for(task, 1)
        finally:
            if not task.done():
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
        for seat, player in enumerate(state.player_list):
            expected = Counter(before[seat]) - Counter(outgoing[seat])
            expected += Counter(outgoing[(seat + flow.source_offset) % 4])
            self.assertEqual(Counter(player.hand_tiles), expected)
        self.assertEqual(state.tiles_list, wall)
        self.assertEqual([len(p.hand_tiles) for p in state.player_list], [14, 13, 13, 13])
        self.assertEqual(sum((Counter(p.hand_tiles) for p in state.player_list), Counter()) + Counter(wall),
                         Counter({tile: 4 for tile in range(11, 40) if tile // 10 in (1, 2, 3) and 1 <= tile % 10 <= 9}))
        self.assertEqual(state.waiting_players_list, [])

    async def test_legal_three_suit_submission_is_disqualified(self):
        state = make_state()
        player = state.player_list[0]
        player.hand_tiles = [11, 12, 21, 22, 31, 32, 13, 14, 15, 16, 17, 18, 19, 19]
        task = asyncio.create_task(state.exchange_six.exchange())
        await self.wait_for_opening(state)
        await self.submit(state, 0, [11, 12, 21, 22, 31, 32])
        for seat in (1, 2, 3):
            await self.submit(state, seat, state.exchange_six.default_selection(seat))
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.exchange_six.disqualified, {0})

    async def test_second_exchange_real_ingress_new_window_private_reconnect_and_atomic_hands(self):
        state = make_state((0, 1, 2, 3))
        flow = state.exchange_six
        await flow.exchange()
        old_tick = state.server_action_tick
        old_selections = dict(flow.submissions)
        for player in state.player_list:
            player.is_bot = False
            player.user_id = 100 + player.player_index
            state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=FakeSocket())
            state.game_server.players[f"conn-{player.player_index}"] = SimpleNamespace(user_id=player.user_id)
        before = [list(player.hand_tiles) for player in state.player_list]
        wall = list(state.tiles_list)
        scores = [player.score for player in state.player_list]
        outgoing = [flow.default_selection(seat) for seat in range(4)]
        task = asyncio.create_task(flow.exchange(dice=[1, 3]))
        await self.wait_for_opening(state)
        try:
            self.assertEqual(flow.submissions, {})
            self.assertEqual(flow.packet(0).state.private_state["selected_tiles"], [])
            self.assertEqual(flow.packet(0).state.public_state["exchange_round"], 2)
            self.assertEqual(flow.packet(0).state.public_state["completed_exchange_rounds"], 1)
            await self.submit(state, 0, old_selections[0], tick=old_tick)
            self.assertTrue(state.action_queues[0].empty())
            await self.submit(state, 0, outgoing[0])
            for _ in range(20):
                if 0 in flow.submissions:
                    break
                await asyncio.sleep(0)
            await reconnected_send_pending_ask(state, 100)
            own = state.game_server.user_id_to_connection[100].websocket.messages[-1]["state"]
            self.assertEqual(own["public_state"]["exchange_round"], 2)
            self.assertEqual(own["private_state"]["selected_tiles"], outgoing[0])
            self.assertEqual(flow.packet(1).state.private_state["selected_tiles"], [])
            self.assertNotIn("selected_tiles", own["public_state"])
            for seat in (1, 2):
                await self.submit(state, seat, outgoing[seat])
            await asyncio.sleep(0)
            self.assertEqual([player.hand_tiles for player in state.player_list], before)
            await self.submit(state, 3, outgoing[3])
            await asyncio.wait_for(task, 1)
        finally:
            if not task.done():
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
        for seat, player in enumerate(state.player_list):
            self.assertEqual(Counter(player.hand_tiles), Counter(before[seat]) - Counter(outgoing[seat]) + Counter(outgoing[(seat + 3) % 4]))
        self.assertEqual(state.tiles_list, wall)
        self.assertEqual([player.score for player in state.player_list], scores)
        self.assertEqual([len(player.hand_tiles) for player in state.player_list], [14, 13, 13, 13])
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        self.assertEqual([tick[5] for tick in ticks if tick[0] == EXCHANGE_ACTION], [1, 2])
        self.assertEqual([tick[1] for tick in ticks if tick[0] == "changsha_exchange_start"], [1, 2])
        with self.assertRaises(ValueError):
            await flow.exchange(dice=[1, 1])
        self.assertEqual(flow.completed_exchange_rounds, 2)

    async def test_second_exchange_ai_and_timeout_preserve_disqualification_and_cap(self):
        for bots in ((0, 1, 2, 3), ()):
            state = make_state(bots)
            flow = state.exchange_six
            with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
                await asyncio.wait_for(flow.exchange(), 1)
                flow.disqualified.add(0)
                await asyncio.wait_for(flow.exchange(dice=[2, 3]), 1)
            self.assertEqual(flow.disqualified, {0})
            self.assertEqual(flow.completed_exchange_rounds, 2)
            self.assertEqual(flow.source_offset, 2)
            self.assertTrue(all(len(tiles) == 6 and len({tile // 10 for tile in tiles}) <= 2 for tiles in flow.submissions.values()))
            self.assertEqual(state.waiting_players_list, [])

    async def test_opening_reserved_hook_defaults_one_and_delays_four_joy_until_final_exchange(self):
        for second_dice, expected_rounds in ((None, 1), ([1, 1], 2)):
            state = make_state((0, 1, 2, 3))
            flow = state.exchange_six
            observed_rounds = []
            async def observe_four_joy(seat, tiles):
                observed_rounds.append((flow.completed_exchange_rounds, flow.exchange_closed))
            with patch.object(flow, "second_exchange_dice", return_value=second_dice), \
                 patch.object(flow, "four_joy", side_effect=observe_four_joy):
                await asyncio.wait_for(flow.opening(), 1)
            self.assertEqual(observed_rounds, [(expected_rounds, True)] * 4)
            self.assertTrue(flow.opening_done)
            self.assertEqual(flow.completed_exchange_rounds, expected_rounds)
            with self.assertRaises(ValueError):
                await flow.exchange(dice=[1, 1])
            flow.reset()
            self.assertEqual((flow.exchange_round, flow.completed_exchange_rounds, flow.exchange_closed), (0, 0, False))
            self.assertIsNone(flow.second_exchange_dice())

    async def test_second_exchange_requires_explicit_valid_server_dice_and_no_overlapping_window(self):
        state = make_state((0, 1, 2, 3))
        flow = state.exchange_six
        await flow.exchange()
        before = [list(player.hand_tiles) for player in state.player_list]
        for dice in (None, [0, 3], [7, 1], [True, 1], [1], "12"):
            with self.assertRaises(ValueError):
                await flow.exchange(dice=dice)
        self.assertEqual([player.hand_tiles for player in state.player_list], before)
        self.assertEqual(flow.completed_exchange_rounds, 1)
        for player in state.player_list:
            player.is_bot = False
        task = asyncio.create_task(flow.exchange(dice=[1, 1]))
        await self.wait_for_opening(state)
        try:
            with self.assertRaises(ValueError):
                await flow.exchange(dice=[1, 1])
        finally:
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
        self.assertEqual(state.waiting_players_list, [])
        self.assertEqual(flow.completed_exchange_rounds, 1)
        self.assertEqual([player.hand_tiles for player in state.player_list], before)

    async def test_ai_and_timeout_choose_legal_six(self):
        for bots in ((0, 1, 2, 3), ()):
            state = make_state(bots)
            state.player_list[0].hand_tiles = [11, 12, 21, 22, 31, 31, 32, 32, 33, 33, 34, 34, 35, 35]
            with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
                await asyncio.wait_for(state.exchange_six.exchange(), 1)
            self.assertEqual(state.exchange_six.disqualified, set())
            self.assertTrue(all(len(tiles) == 6 for tiles in state.exchange_six.submissions.values()))
            self.assertTrue(all(len({tile // 10 for tile in tiles}) <= 2 for tiles in state.exchange_six.submissions.values()))

    async def test_reconnect_payload_preserves_private_selection(self):
        state = make_state()
        task = asyncio.create_task(state.exchange_six.exchange())
        await self.wait_for_opening(state)
        try:
            selected = state.exchange_six.default_selection(0)
            await self.submit(state, 0, selected)
            for _ in range(20):
                if 0 in state.exchange_six.submissions:
                    break
                await asyncio.sleep(0)
            await reconnected_send_pending_ask(state, 100)
            own = state.game_server.user_id_to_connection[100].websocket.messages[-1]
            other = state.exchange_six.packet(1).dict()
            self.assertEqual(own["state"]["private_state"]["selected_tiles"], selected)
            self.assertEqual(other["state"]["private_state"]["selected_tiles"], [])
            self.assertEqual(own["state"]["action_tick"], state.server_action_tick)
            self.assertEqual(ChangshaExchangeSixResponse(**own).dict(), own)
        finally:
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
        self.assertEqual(state.waiting_players_list, [])
        self.assertTrue(all(not p for p in state.action_dict.values()))

    async def test_fixed_preset_overrides_stale_settings(self):
        config = normalize_config({"bird_count": 4, "small_hu_score": 999, "open_kong_replacement_count": 1}, SUB_RULE)
        self.assertEqual((config["bird_count"], config["small_hu_score"], config["open_kong_replacement_count"]), (2, 2, 4))
        ordinary = {"bird_count": 4}
        self.assertIs(normalize_config(ordinary, "changsha/classic_double_bird"), ordinary)
        state = make_state()
        self.assertEqual(state.open_kong_replacement_count, 4)
        self.assertFalse(state.initial_hu_enabled["板板胡"])

    async def test_room_creation_ignores_hidden_stale_rule_values(self):
        from server.room.room_manager import RoomManager
        server = SimpleNamespace(players={"connection": SimpleNamespace(user_id=100001, username="host", current_room_id=None)},
            match_manager=None, db_manager=SimpleNamespace(get_user_settings=lambda uid: {"username": "host"}),
            gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda uid: False))
        manager = RoomManager(server)
        manager._broadcast_room_info = AsyncMock()
        result = await manager.create_Changsha_room("connection", "换六张固定规则", 1, "", 20, 5, False,
            sub_rule=SUB_RULE, open_kong_replacement_count=99, bird_count=99,
            small_hu_score=9999, big_hu_score=9999, tactical_call=True,
            small_hu_self_draw_only=True, score_cap_type="stale", score_cap_value=-1,
            initial_hu_ban_ban_hu=True, dealer_bird=True, base_score_no_dealer=False)
        self.assertTrue(result.success, result.message)
        room = result.room_info
        self.assertEqual((room["open_kong_replacement_count"], room["bird_count"], room["small_hu_score"], room["big_hu_score"]), (4, 2, 2, 8))
        self.assertFalse(room["initial_hu_ban_ban_hu"])
        self.assertFalse(room["tactical_call"])
        self.assertTrue(room["base_score_no_dealer"])
        self.assertFalse(room["small_hu_self_draw_only"])
        self.assertEqual((room["score_cap_type"], room["score_cap_value"]), ("none", 0))

    async def test_open_kong_from_real_hand_action_groups_64_and_48(self):
        class Dice:
            def randint(self, lower, upper):
                return 1
        pre_kong = [12] * 3 + [14] * 3 + [15] * 2 + [16] * 2
        for batch, expected in (([13, 14, 17, 29], 64), ([13, 15, 29, 39], 48)):
            state = make_state()
            flow = state.exchange_six
            flow.actual_draw_counts = {seat: 2 for seat in range(4)}
            state.player_list[0].hand_tiles = [11] * 4 + pre_kong
            state.player_list[0].has_draw_slot = True
            for player in state.player_list[1:]:
                player.tag_list.append("peida")
            task = asyncio.create_task(flow.hand_action())
            await self.wait_for_opening(state)
            await self.action(state, 0, "angang", target=11)
            await asyncio.wait_for(task, 1)
            self.assertEqual(state.game_status, "deal_card_after_gang")
            self.assertEqual(state.player_list[0].hand_tiles, pre_kong)
            self.assertIn(15, flow.locked_patterns[0])
            # 正常骰子取牌入口：骰子1/1，取尾部第2、3墩。
            state.tiles_list = list(range(21, 30)) * 3 + [31, 32, 33, 34, 35, 36, 37]
            for position, tile in zip((len(state.tiles_list) - 4, len(state.tiles_list) - 3,
                                       len(state.tiles_list) - 6, len(state.tiles_list) - 5), batch):
                state.tiles_list[position] = tile
            state.player_list[0].is_bot = True
            with patch("server.gamestate.game_changsha.exchange_six.random.Random", return_value=Dice()):
                await asyncio.wait_for(flow.draw_after_kong(), 1)
            self.assertEqual(state.game_status, "END")
            with patch.object(state, "_draw_changsha_birds", return_value=[]):
                await flow.settle()
            self.assertEqual([player.score for player in state.player_list], [expected * 3, -expected, -expected, -expected])
            self.assertEqual(flow.next_dealer, 0)

    async def test_multiple_ron_share_birds_and_make_discarder_dealer(self):
        state = make_state(bot_seats=(1, 2))
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 12]
        state.player_list[2].hand_tiles = [24, 25, 26, 27, 28, 29, 31, 32, 33, 34, 35, 36, 12]
        state.player_list[3].tag_list.append("peida")
        state.tiles_list = [11, 19, 39]
        state.player_list[0].discard_tiles = [12]
        self.assertTrue(await asyncio.wait_for(flow.claims([12], allow_meld=False), 1))
        self.assertEqual(set(flow.wins), {1, 2})
        with patch.object(state, "_draw_changsha_birds", wraps=state._draw_changsha_birds) as draw_birds:
            await flow.settle()
            draw_birds.assert_called_once_with(2)
        self.assertEqual(flow.birds, [11, 19])
        self.assertEqual(state.tiles_list, [11, 19, 39])
        self.assertEqual(flow.next_dealer, 0)
        self.assertEqual(sum(player.score for player in state.player_list), 0)

    async def test_declaration_penalty_only_changes_its_payer(self):
        for extra, expected in ((1, [-8, -2, -2]), (2, [-16, -2, -2])):
            state = make_state(bot_seats=(0,))
            flow = state.exchange_six
            flow.actual_draw_counts = {seat: 2 for seat in range(4)}
            state.player_list[0].hand_tiles = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32, 33]
            state.player_list[0].has_draw_slot = True
            flow.reported_ready.add(1)
            if extra == 2:
                flow.open_hands.add(1)
            await asyncio.wait_for(flow.hand_action(), 1)
            with patch.object(state, "_draw_changsha_birds", return_value=[]):
                await flow.settle()
            self.assertEqual([player.score for player in state.player_list[1:]], expected)
            self.assertEqual(sum(player.score for player in state.player_list), 0)

    async def test_four_joy_after_lock_does_not_unlock_and_new_fourth_is_discarded(self):
        state = make_state(bot_seats=(0,))
        flow = state.exchange_six
        state.player_list[0].hand_tiles = [12, 12, 12, 14, 14, 14, 15, 15, 15, 16, 16, 17, 18, 12]
        state.player_list[0].open_kong_locked = True
        state.player_list[0].has_draw_slot = True
        flow.locked_patterns[0] = {19: ["清一色"]}
        flow.reported_ready.add(1)
        flow.open_hands.add(1)
        await asyncio.wait_for(flow.after_normal_draw(12), 1)
        self.assertEqual(flow.four_joy_paid[0], {12})
        self.assertTrue(state.player_list[0].open_kong_locked)
        self.assertEqual(state.game_status, "waiting_hand_action")
        before = [player.score for player in state.player_list]
        await asyncio.wait_for(flow.after_normal_draw(12), 1)
        self.assertEqual([player.score for player in state.player_list], before)
        await asyncio.wait_for(flow.hand_action(), 1)
        self.assertEqual(state.player_list[0].hand_tiles.count(12), 3)
        self.assertEqual(state.player_list[0].discard_tiles[-1], 12)
        self.assertEqual(state.game_status, "waiting_action_after_cut")

    async def test_rob_large_kong_charges_opener_and_draws_no_replacements(self):
        state = make_state(bot_seats=(2,))
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12] * 3 + [13, 14, 15, 16, 17, 18, 21, 22, 23, 25]
        state.player_list[2].hand_tiles = [21, 22, 23, 24, 25, 26, 31, 32, 33, 25, 25, 13, 14]
        state.player_list[0].tag_list.append("peida")
        state.player_list[3].tag_list.append("peida")
        state.player_list[0].discard_tiles = [12]
        wall = list(state.tiles_list)
        await asyncio.wait_for(flow.declare_kong(1, 12, formal=True, claimed_from=0), 1)
        self.assertEqual(flow.payer, 1)
        self.assertEqual(state.game_status, "END")
        self.assertEqual(state.tiles_list, wall)
        self.assertEqual(state.player_list[1].combination_tiles, [])
        with patch.object(state, "_draw_changsha_birds", return_value=[]):
            await flow.settle()
        self.assertEqual([player.score for player in state.player_list], [0, -8, 8, 0])
        self.assertEqual(flow.next_dealer, 2)

    async def test_blind_normal_and_single_replacement_hide_until_four_public(self):
        for source in ("normal", "buzhang"):
            state = make_state()
            flow = state.exchange_six
            for player in state.player_list[1:]:
                player.tag_list.append("peida")
            if source == "normal":
                state.player_list[0].hand_tiles = [12] * 4 + [21, 22, 23, 24, 25, 26, 31, 32, 33]
            else:
                state.player_list[0].hand_tiles = [11] * 4 + [12] * 4 + [21, 22, 23, 31, 32, 33]
                first = asyncio.create_task(flow.hand_action())
                await self.wait_for_opening(state)
                await self.action(state, 0, "buzhang", target=11)
                await asyncio.wait_for(first, 1)
                self.assertEqual(state.player_list[0].combination_tiles, ["G11"])
            state.tiles_list = [tile for tile in state.tiles_list if tile not in (11, 12)]
            state.tiles_list[0 if source == "normal" else -2] = 19
            pre_hidden_hand_count = len(state.player_list[0].hand_tiles) - 4
            pending = asyncio.create_task(flow.before_draw(source) if source == "normal" else flow.draw_after_kong())
            await self.wait_for_opening(state)
            self.assertEqual(flow.phase, "before_draw")
            await self.action(state, 0, "changsha_blind_kong", target=12)
            await asyncio.wait_for(pending, 1)
            snapshots = [message["state"] for message in state.game_server.user_id_to_connection[100].websocket.messages
                         if message["type"] == "gamestate/changsha/exchange_six_state"]
            public = next(snapshot for snapshot in snapshots if snapshot["phase"] == "blind_kong_public")
            revealed = next(snapshot for snapshot in snapshots if snapshot["phase"] == "blind_kong_revealed")
            self.assertTrue(public["public_state"]["hidden_draw_pending"])
            self.assertEqual(len(public["private_state"]["hand_tiles"]), pre_hidden_hand_count)
            self.assertEqual(len(public["public_state"]["kong_tiles"]), 4)
            self.assertEqual(revealed["private_state"]["hand_tiles"][-1], 19)
            self.assertEqual(len(revealed["private_state"]["hand_tiles"]), pre_hidden_hand_count + 1)
            self.assertFalse(flow.wins)
            self.assertIn("G12", state.player_list[0].combination_tiles)
            self.assertTrue(state.player_list[0].open_kong_locked)

    async def test_meld_hand_cannot_self_win_without_actual_draw(self):
        state = make_state()
        state.player_list[0].hand_tiles = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32, 33]
        state.player_list[0].has_draw_slot = False
        task = asyncio.create_task(state.exchange_six.hand_action())
        await self.wait_for_opening(state)
        self.assertNotIn("hu_self", state.action_dict[0])
        await self.action(state, 0, "cut", tile=33)
        await asyncio.wait_for(task, 1)
        self.assertFalse(state.exchange_six.wins)

    async def test_sea_self_win_has_real_confirmation_and_private_draw(self):
        for accept in (True, False):
            state = make_state()
            flow = state.exchange_six
            flow.actual_draw_counts = {seat: 2 for seat in range(4)}
            state.player_list[1].hand_tiles = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32]
            for seat in (0, 2, 3):
                state.player_list[seat].tag_list.append("peida")
                flow.disqualified.add(seat)
            state.tiles_list = [33]
            flow.four_joy_revealed[1] = [12] * 4
            task = asyncio.create_task(flow.sea_bottom(start=1))
            await self.wait_for_opening(state)
            await self.action(state, 1, "sea_bottom")
            for _ in range(100):
                if flow.phase == "sea_self_win":
                    break
                await asyncio.sleep(0)
            self.assertEqual(flow.phase, "sea_self_win")
            self.assertNotIn(1, flow.four_joy_revealed)
            self.assertFalse(task.done())
            self.assertEqual(flow.packet(1).state.private_state["hand_tiles"][-1], 33)
            await self.action(state, 1, "hu_self" if accept else "pass")
            await asyncio.wait_for(task, 1)
            self.assertEqual(bool(flow.wins), accept)
            if accept:
                self.assertEqual(len(flow.wins[1][0]["hand"]), 14)
                with patch.object(state, "_draw_changsha_birds", return_value=[]):
                    await flow.settle()
                self.assertEqual(flow.birds, [33, 33])
            else:
                self.assertEqual(state.player_list[1].discard_tiles[-1], 33)
                self.assertEqual(len(state.player_list[1].hand_tiles), 13)

    async def test_authoritative_hints_preserve_reported_pair_and_discard_context(self):
        state = make_state()
        flow = state.exchange_six
        state.player_list[0].hand_tiles = [11, 11, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32]
        self.assertNotIn(33, {item["tile"] for item in flow.hand_hints(0, [])["waits"]})
        flow.reported_ready.add(0)
        hints = flow.hand_hints(0, [])
        self.assertEqual(next(item["score"] for item in hints["waits"] if item["tile"] == 33), 8)
        state.player_list[0].hand_tiles.append(33)
        hints = flow.hand_hints(0, [33])
        self.assertEqual(hints["waits"], [])
        self.assertIn(33, {item["tile"] for item in hints["discard_waits"]["33"]})

    async def test_cancel_stage_cleans_actions_and_reset_clears_hidden_state(self):
        state = make_state()
        flow = state.exchange_six
        task = asyncio.create_task(flow.exchange())
        await self.wait_for_opening(state)
        task.cancel()
        await asyncio.gather(task, return_exceptions=True)
        self.assertEqual(state.waiting_players_list, [])
        self.assertTrue(all(not queue.qsize() for queue in state.action_queues.values()))
        self.assertTrue(all(not actions for actions in state.action_dict.values()))
        flow.hidden_draw = 11
        flow.open_hands.add(0)
        flow.public_kong_tiles = [12, 13, 14, 15]
        await state.cleanup_game_state()
        self.assertIsNone(flow.hidden_draw)
        self.assertEqual(flow.open_hands, set())
        self.assertEqual(flow.public_kong_tiles, [])

    async def test_sea_draw_fourth_tile_offers_four_joy_before_final_win(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[0].hand_tiles = [12] * 3 + [13] * 3 + [14] * 3 + [15] * 2 + [16] * 2
        state.tiles_list = [12]
        task = asyncio.create_task(flow.sea_bottom(start=0))
        try:
            await self.wait_for_opening(state)
            await self.action(state, 0, "sea_bottom")
            for _ in range(100):
                if flow.phase in ("four_joy", "sea_self_win"):
                    break
                await asyncio.sleep(.001)
            self.assertEqual(flow.phase, "four_joy")
            self.assertIn("changsha_four_joy", state.action_dict[0])
            await self.action(state, 0, "changsha_four_joy", target=12)
            for _ in range(100):
                if flow.phase == "sea_self_win":
                    break
                await asyncio.sleep(.001)
            self.assertEqual(flow.four_joy_paid[0], {12})
            self.assertGreater(state.player_list[0].score, 0)
            self.assertEqual(sum(player.score for player in state.player_list), 0)
            await self.action(state, 0, "hu_self")
            await asyncio.wait_for(task, 1)
            self.assertEqual(state.game_status, "END")
            self.assertIn(0, flow.wins)
            kinds = [tick[0] for tick in state.game_record["game_round"]["round_index_1"]["action_ticks"]]
            self.assertLess(kinds.index("changsha_sea_draw"), kinds.index("changsha_four_joy"))
        finally:
            if not task.done():
                task.cancel()
            await asyncio.gather(task, return_exceptions=True)

    async def test_sea_four_joy_is_paid_even_when_last_tile_cannot_finish_hand(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[0].hand_tiles = [12,12,12,13,14,21,22,23,24,25,26,31,32]
        for seat in (1, 2, 3):
            flow.disqualified.add(seat)
        state.tiles_list = [12]
        self.assertIn(33, flow.waits(0))
        task = asyncio.create_task(flow.sea_bottom(start=0))
        try:
            await self.wait_for_opening(state)
            await self.action(state, 0, "sea_bottom")
            for _ in range(100):
                if flow.phase == "four_joy":
                    break
                await asyncio.sleep(.001)
            self.assertEqual(flow.phase, "four_joy")
            await self.action(state, 0, "changsha_four_joy", target=12)
            await asyncio.wait_for(task, 1)
            self.assertEqual(flow.four_joy_paid[0], {12})
            self.assertGreater(state.player_list[0].score, 0)
            self.assertEqual(sum(player.score for player in state.player_list), 0)
            self.assertEqual(state.player_list[0].discard_tiles[-1], 12)
            self.assertEqual(len(state.player_list[0].hand_tiles), 13)
            self.assertFalse(flow.wins)
            self.assertEqual(state.game_status, "END")
        finally:
            if not task.done():
                task.cancel()
            await asyncio.gather(task, return_exceptions=True)

    async def test_full_four_round_loop_uses_fixed_mode_and_preserves_scores(self):
        state = make_state(bot_seats=(0, 1, 2, 3))
        state.game_server.gamestate_manager = SimpleNamespace(cleanup_game_state_complete=AsyncMock())
        state.db_manager = SimpleNamespace(store_changsha_game_record=lambda *args: "local-test-record")
        with patch("server.gamestate.game_changsha.ChangshaGameState.liuju_ready_wait_seconds", return_value=0), \
             patch("server.gamestate.game_changsha.ChangshaGameState.hu_result_ready_wait_seconds", return_value=0), \
             patch("server.gamestate.game_changsha.ChangshaGameState.setup_random_seed_system", return_value=(2345, 3456, 4567, False)):
            await asyncio.wait_for(state.game_loop_changsha(), 20)
        rounds = state.game_record["game_round"]
        self.assertEqual(len(rounds), 4)
        self.assertEqual(state.game_record["game_title"]["sub_rule"], SUB_RULE)
        self.assertEqual(state.game_record["game_title"]["changsha_exchange_six_rule_version"], 2)
        for round_data in rounds.values():
            self.assertEqual(sum(tick[0] == "changsha_deal_dice" for tick in round_data["action_ticks"]), 1)
            self.assertEqual(sum(tick[0] == "changsha_exchange_six" for tick in round_data["action_ticks"]), 1)
            self.assertEqual(round_data["action_ticks"][-1][0], "end")
        self.assertEqual(sum(player.score for player in state.player_list), 0)
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()

    async def test_concealed_continuation_retracts_only_selected_public_tile(self):
        class Dice:
            def randint(self, lower, upper):
                return 1
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[0].hand_tiles = [11] * 4 + [12] * 3 + [14] * 3 + [15] * 2 + [16] * 2
        state.player_list[0].has_draw_slot = True
        for player in state.player_list[1:]:
            player.tag_list.append("peida")
        await flow.declare_kong(0, 11, formal=True)
        frozen = dict(flow.locked_patterns[0])
        state.tiles_list = [21, 22, 23, 24, 32, 39, 12, 22, 25, 26]
        with patch("server.gamestate.game_changsha.exchange_six.random.Random", return_value=Dice()):
            task = asyncio.create_task(flow.draw_after_kong())
            await self.wait_for_opening(state)
            self.assertEqual(flow.phase, "kong_continue")
            self.assertEqual(flow.targets[0]["angang"], [12])
            await self.action(state, 0, "angang", target=12)
            await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_list[0].combination_tiles, ["G11", "G12"])
        self.assertEqual(state.player_list[0].discard_tiles, [22, 32, 39])
        self.assertEqual(state.player_list[0].hand_tiles, [14] * 3 + [15] * 2 + [16] * 2)
        self.assertEqual(flow.locked_patterns[0], frozen)
        self.assertEqual(state.game_status, "deal_card_after_gang")
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        self.assertIn(["changsha_kong_continue", 0, 12, 12], ticks)
        wire = [message["do_action_info"] for message in state.game_server.user_id_to_connection[100].websocket.messages
                if message["type"] == "gamestate/changsha/do_action" and "changsha_kong_continue" in message["do_action_info"]["action_list"]]
        self.assertEqual(len(wire), 1)
        self.assertEqual((wire[0]["cut_tile"], wire[0]["cut_from_player"]), (12, 0))

    async def test_timeout_pass_persists_through_draw_but_never_blocks_self_draw(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32]
        for seat in (2, 3):
            state.player_list[seat].tag_list.append("peida")
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            self.assertFalse(await flow.claims([33], allow_meld=False))
        self.assertEqual(state.player_passed_hu_base[1], 2)
        self.assertFalse(await flow.claims([33], allow_meld=False))
        self.assertEqual(flow.result(1, 33)[0], 2)
        state.current_player_index = 1
        state.player_list[1].hand_tiles.append(33)
        state.player_list[1].has_draw_slot = True
        await flow.after_normal_draw(33)
        self.assertEqual(state.player_passed_hu_base.get(1, 0), 2)
        task = asyncio.create_task(flow.hand_action())
        await self.wait_for_opening(state)
        self.assertIn("hu_self", state.action_dict[1])
        await self.action(state, 1, "hu_self")
        await asyncio.wait_for(task, 1)

    async def test_opening_player_chooses_ready_and_open_before_heaven(self):
        state = make_state()
        flow = state.exchange_six
        state.player_list[0].hand_tiles = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32, 33]
        # 原子换牌已另行真实入口覆盖；此处保留一副确定天胡手牌检查后续声明顺序。
        with patch.object(flow, "exchange", new=AsyncMock()):
            task = asyncio.create_task(flow.opening())
            await self.wait_for_opening(state)
            self.assertEqual(flow.phase, "declarations")
            self.assertEqual(flow.reported_ready, set())
            self.assertEqual(flow.open_hands, set())
            await self.action(state, 0, "changsha_report_ready", target=33)
            for seat in (1, 2, 3):
                await self.action(state, seat, "pass")
            first_tick = state.server_action_tick
            for _ in range(100):
                if state.server_action_tick != first_tick:
                    break
                await asyncio.sleep(0)
            await self.action(state, 0, "changsha_open_hand")
            await asyncio.wait_for(task, 1)
        pending = asyncio.create_task(flow.hand_action(first=True))
        await self.wait_for_opening(state)
        await self.action(state, 0, "hu_self")
        await asyncio.wait_for(pending, 1)
        self.assertEqual(big_weight(flow.wins[0][0]["fans"]), 3)
        with patch.object(state, "_draw_changsha_birds", return_value=[]):
            await flow.settle()
        self.assertEqual([p.score for p in state.player_list], [96, -32, -32, -32])

    async def test_real_pass_then_large_kong_batch_discard_clears_pass_before_next_draw(self):
        class Dice:
            def randint(self, lower, upper):
                return 1
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12] * 3 + [21, 22, 23, 24, 25, 26, 31, 32, 33, 25]
        for seat in (0, 2, 3):
            state.player_list[seat].tag_list.append("peida")
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            await flow.claims([25], allow_meld=False)
        self.assertEqual(state.player_passed_hu_base[1], 2)
        state.player_list[0].discard_tiles.append(12)
        task = asyncio.create_task(flow.claims([12]))
        await self.wait_for_opening(state)
        await self.action(state, 1, "gang", target=12)
        await asyncio.wait_for(task, 1)
        state.tiles_list = [21, 22, 38, 39, 28, 29, 31, 32]
        with patch("server.gamestate.game_changsha.exchange_six.random.Random", return_value=Dice()):
            await flow.draw_after_kong()
        self.assertEqual(state.player_passed_hu_base.get(1, 0), 0)
        state.current_player_index = 2
        state.player_list[2].discard_tiles.append(25)
        task = asyncio.create_task(flow.claims([25], allow_meld=False))
        await self.wait_for_opening(state)
        self.assertIn("hu_third", state.action_dict[1])
        await self.action(state, 1, "hu_third")
        await asyncio.wait_for(task, 1)
        self.assertEqual(set(flow.wins), {1})

    async def test_public_batch_meld_wire_identifies_non_last_claim_before_peng(self):
        class Dice:
            def randint(self, lower, upper):
                return 1
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[0].hand_tiles = [11] * 4 + [12] * 3 + [14] * 3 + [15] * 2 + [16] * 2
        state.player_list[1].hand_tiles = [22, 22, 11, 12, 13, 14, 15, 16, 21, 23, 31, 33, 35]
        for player in state.player_list[2:]:
            player.tag_list.append("peida")
        await flow.declare_kong(0, 11, formal=True)
        state.tiles_list = [21, 22, 38, 39, 22, 29, 31, 32]
        with patch("server.gamestate.game_changsha.exchange_six.random.Random", return_value=Dice()):
            task = asyncio.create_task(flow.draw_after_kong())
            await self.wait_for_opening(state)
            self.assertEqual(flow.phase, "claims")
            await self.action(state, 1, "peng", target=22)
            await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_list[0].discard_tiles, [29, 38, 39])
        wire = [message["do_action_info"] for message in state.game_server.user_id_to_connection[100].websocket.messages
                if message["type"] == "gamestate/changsha/do_action"]
        prepare = next(index for index, action in enumerate(wire) if "changsha_batch_claim" in action["action_list"])
        self.assertEqual((wire[prepare]["cut_tile"], wire[prepare]["cut_from_player"]), (22, 0))
        self.assertEqual(wire[prepare + 1]["action_list"], ["peng"])

    async def test_v3_dice_exact_pile_fallback_and_last_taken_lower_sea(self):
        class Dice:
            def __init__(self, values):
                self.values = iter(values)
            def randint(self, lower, upper):
                return next(self.values)
        cases = [
            ([11,12,13,14,15,16,17,18], [2,2], "double", [11,12], [13,14,15,16,17,18]),
            ([11,12,13,14,15,16], [2,2], "double", [13,14,11,12], [15,16]),
            ([11,12], [1,1], "double", [12], [11]),
            ([11,12,13], [1,1], "double", [11], [12,13]),
            ([11,12,13,14], [1,4], "double", [13,14,12], [11]),
            ([11,12], [2,2], "double", [], [11,12]),
        ]
        for wall, dice, tail, expected, rest in cases:
            state = make_state()
            state.tiles_list = list(wall)
            state.backward_tiles_list_type = tail
            with patch("server.gamestate.game_changsha.exchange_six.random.Random", return_value=Dice(dice)):
                actual = state.exchange_six.take_open_kong_tiles()
            self.assertEqual(actual, expected)
            self.assertEqual(state.tiles_list, rest)
            self.assertEqual(Counter(actual) + Counter(rest), Counter(wall))

    async def test_v3_direct_replacement_can_be_selected_without_ready(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12] * 3 + [11,14,17,19,21,24,27,29,31,34]
        for player in state.player_list[2:]:
            player.tag_list.append("peida")
        state.player_list[0].discard_tiles = [12]
        task = asyncio.create_task(flow.claims([12]))
        await self.wait_for_opening(state)
        self.assertIn("buzhang", state.action_dict[1])
        self.assertNotIn("gang", state.action_dict[1])
        await self.action(state, 1, "buzhang", target=12)
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.pending_gang_replacement_count, 1)
        self.assertFalse(state.player_list[1].open_kong_locked)
        self.assertEqual(state.player_list[1].combination_tiles, ["g12"])
        self.assertEqual(state.player_list[0].discard_tiles, [])

    async def test_v3_open_hand_same_pattern_upgrade_can_ron_after_pass(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12,12,12,13,14,21,22,23,24,25,26,31,32]
        flow.open_hands.add(1)
        for player in state.player_list[2:]:
            player.tag_list.append("peida")
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            await flow.claims([33], allow_meld=False)
        self.assertEqual(flow.greedy_pass_patterns[1], {(): 8})
        task = asyncio.create_task(flow.claims([33], kong=True, allow_meld=False))
        await self.wait_for_opening(state)
        self.assertIn("hu_first", state.action_dict[1])
        await self.action(state, 1, "hu_first")
        await asyncio.wait_for(task, 1)
        self.assertEqual(big_weight(flow.wins[1][0]["fans"]), 2)

    async def test_expired_queued_hu_is_replaced_by_server_timeout_pass(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12,12,12,13,14,21,22,23,24,25,26,31,32]
        for player in state.player_list[2:]:
            player.tag_list.append("peida")
        task = asyncio.create_task(flow.claims([33], allow_meld=False))
        await self.wait_for_opening(state)
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            await self.action(state, 1, "hu_first")
            await asyncio.wait_for(task, 1)
        self.assertFalse(flow.wins)
        self.assertEqual(state.player_passed_hu_base[1], 2)

    async def test_v3_formal_kong_beats_nearer_pong_or_replacement(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12]*3 + [11,14,17,19,21,24,27,29,31,34]
        state.player_list[2].hand_tiles = [12]*3 + [21,22,23,24,25,26,31,32,33,25]
        state.player_list[3].tag_list.append("peida")
        state.player_list[0].discard_tiles = [12]
        task = asyncio.create_task(flow.claims([12]))
        await self.wait_for_opening(state)
        await self.action(state, 1, "buzhang", target=12)
        await self.action(state, 2, "gang", target=12)
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.current_player_index, 2)
        self.assertEqual(state.pending_gang_replacement_count, 4)
        self.assertEqual(state.player_list[1].combination_tiles, [])

    async def test_v3_replacement_and_pong_share_priority_then_nearest_seat(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12,12,11,14,17,19,21,24,27,29,31,34,37]
        state.player_list[2].hand_tiles = [12]*3 + [11,14,17,19,21,24,27,29,31,34]
        state.player_list[3].tag_list.append("peida")
        state.player_list[0].discard_tiles = [12]
        task = asyncio.create_task(flow.claims([12]))
        await self.wait_for_opening(state)
        await self.action(state, 1, "peng", target=12)
        await self.action(state, 2, "buzhang", target=12)
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.current_player_index, 1)
        self.assertEqual(state.player_list[1].combination_tiles, ["k12"])
        self.assertEqual(state.player_list[2].combination_tiles, [])


    async def test_v3_deal_dice_selects_forbidden_seat_independently_of_exchange(self):
        state = make_state()
        flow = state.exchange_six
        flow.dice = [6, 6]
        for first in range(1, 7):
            for second in range(1, 7):
                with patch("server.gamestate.game_changsha.exchange_six.random.Random") as rng:
                    rng.return_value.randint.side_effect = [first, second]
                    flow.prepare_deal()
                self.assertEqual(flow.small_hu_forbidden_seat, (first + second - 1) % 4)
                self.assertEqual(flow.dice, [6, 6])
                public = flow.packet(0).state.public_state
                self.assertEqual(public["deal_dice"], [first, second])
                self.assertEqual(public["small_hu_forbidden_seat"], flow.small_hu_forbidden_seat)
        flow.small_hu_forbidden_seat = 3
        init_changsha_tiles(state)
        self.assertEqual(flow.small_hu_forbidden_seat, (sum(flow.deal_dice) - 1) % 4)

    async def test_each_hand_rerolls_small_hu_ban_and_reconnect_uses_current_dealer_seats(self):
        state = make_state()
        flow = state.exchange_six
        for round_number, dice in enumerate(([1, 1], [2, 2], [3, 4], [2, 3]), 1):
            # 模拟换庄后的真实座位重排，再从每局入口清理并重新发牌。
            state.player_list = state.player_list[1:] + state.player_list[:1]
            for seat, player in enumerate(state.player_list):
                player.player_index = seat
                player.hand_tiles = []
            state.current_round = round_number
            state.game_record["game_round"][f"round_index_{round_number}"] = {"action_ticks": []}
            state._reset_changsha_round_state()
            self.assertIsNone(flow.small_hu_forbidden_seat)
            with patch("server.gamestate.game_changsha.exchange_six.random.Random") as rng:
                rng.return_value.randint.side_effect = dice
                init_changsha_tiles(state)
            expected = (sum(dice) - 1) % 4
            self.assertEqual(flow.small_hu_forbidden_seat, expected)
            task = asyncio.create_task(flow.exchange(dice=[6, 6]))
            await self.wait_for_opening(state)
            try:
                for player in state.player_list:
                    await reconnected_send_pending_ask(state, player.user_id)
                    packet = state.game_server.user_id_to_connection[player.user_id].websocket.messages[-1]["state"]
                    self.assertEqual(packet["public_state"]["deal_dice"], dice)
                    self.assertEqual(packet["public_state"]["small_hu_forbidden_seat"], expected)
                self.assertEqual(state.player_list[expected].player_index, expected)
            finally:
                task.cancel()
                with self.assertRaises(asyncio.CancelledError):
                    await task

    async def test_v3_small_hu_ban_preserves_waits_and_allows_upgrades(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        hand = [12,12,12,13,14,21,22,23,24,25,26,31,32]
        state.player_list[1].hand_tiles = hand
        flow.small_hu_forbidden_seat = 1
        self.assertEqual(flow.result(1, 33), (0, []))
        self.assertIn(33, flow.waits(1))
        hint = next(row for row in flow.packet(1).state.private_state["hints"]["waits"] if row["tile"] == 33)
        self.assertTrue(hint["blocked_small_hu"])
        for ways in (("海底",), ("杠上炮",), ("抢杠胡",)):
            self.assertEqual(flow.result(1, 33, ways=ways)[0], 8)
        for declaration in (flow.reported_ready, flow.open_hands):
            declaration.add(1)
            self.assertEqual(flow.result(1, 33)[0], 8)
            self.assertFalse(next(row for row in flow.hand_hints(1, [])["waits"] if row["tile"] == 33)["blocked_small_hu"])
            declaration.remove(1)
        for seat in (2, 3):
            state.player_list[seat].tag_list.append("peida")
        self.assertFalse(await flow.claims([33], allow_meld=False))
        state.current_player_index = 1
        state.player_list[1].hand_tiles.append(33)
        state.player_list[1].has_draw_slot = True
        task = asyncio.create_task(flow.hand_action())
        await self.wait_for_opening(state)
        self.assertNotIn("hu_self", state.action_dict[1])
        await self.action(state, 1, "cut", tile=33)
        await asyncio.wait_for(task, 1)

    async def test_v3_forbidden_player_still_collects_each_four_joy(self):
        state = make_state(bot_seats=(1,))
        flow = state.exchange_six
        flow.small_hu_forbidden_seat = 1
        state.player_list[1].hand_tiles = [12]*4 + [27]*4 + [11,14,21,31,39]
        await flow.four_joy(1, [12,27])
        self.assertEqual(flow.four_joy_paid[1], {12,27})
        self.assertGreater(state.player_list[1].score, 0)
        self.assertEqual(sum(player.score for player in state.player_list), 0)
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        self.assertEqual(sum(tick[0] == "changsha_four_joy" for tick in ticks), 2)
        self.assertNotEqual(state.game_status, "END")

    async def test_v3_reported_ready_pass_survives_draw_and_discard_until_upgrade(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        flow.reported_ready.add(1)
        state.player_list[1].hand_tiles = [12,12,12,13,14,21,22,23,24,25,26,31,32]
        for seat in (2,3):
            state.player_list[seat].tag_list.append("peida")
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            await flow.claims([33], allow_meld=False)
        self.assertEqual(flow.reported_ready_pass_base[1], 8)
        state.current_player_index = 1
        state.player_list[1].hand_tiles.append(39)
        state.player_list[1].has_draw_slot = True
        await flow.after_normal_draw(39)
        self.assertEqual(state.player_passed_hu_base[1], 8)
        task = asyncio.create_task(flow.hand_action())
        await self.wait_for_opening(state)
        await self.action(state, 1, "cut", tile=39)
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_passed_hu_base.get(1, 0), 0)
        self.assertEqual(flow.reported_ready_pass_base[1], 8)
        state.current_player_index = 0
        self.assertFalse(await flow.claims([33], allow_meld=False))
        task = asyncio.create_task(flow.claims([33], sea=True, allow_meld=False))
        await self.wait_for_opening(state)
        self.assertIn("hu_first", state.action_dict[1])
        await self.action(state, 1, "hu_first")
        await asyncio.wait_for(task, 1)
        self.assertEqual(flow.wins[1][0]["fans"], ["海底", "报听"])

    async def test_v3_each_player_ground_win_ends_at_own_first_draw_and_discard(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts[0] = 3
        hand = [12,12,12,13,14,21,22,23,24,25,26,31,32]
        state.player_list[1].hand_tiles = list(hand)
        for seat in (2,3):
            state.player_list[seat].tag_list.append("peida")
        task = asyncio.create_task(flow.claims([33], allow_meld=False))
        await self.wait_for_opening(state)
        await self.action(state, 1, "pass")
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_passed_hu_base[1], 8)
        state.current_player_index = 1
        state.player_list[1].hand_tiles.append(33)
        state.player_list[1].has_draw_slot = True
        await flow.after_normal_draw(33)
        task = asyncio.create_task(flow.hand_action())
        await self.wait_for_opening(state)
        self.assertIn("hu_self", state.action_dict[1])
        self.assertEqual(flow.result(1,33,hand=state.player_list[1].hand_tiles,ways=flow.ways(1,self_draw=True))[1], ["地胡"])
        await self.action(state, 1, "cut", tile=33)
        await asyncio.wait_for(task, 1)
        self.assertNotIn("地胡", flow.ways(1,self_draw=True))
        state.current_player_index = 0
        task = asyncio.create_task(flow.claims([33], allow_meld=False))
        await self.wait_for_opening(state)
        await self.action(state, 1, "hu_first")
        await asyncio.wait_for(task, 1)
        self.assertEqual(flow.wins[1][0]["fans"], ["小胡"])
        self.assertIn("地胡", flow.ways(2))

    async def test_v3_locked_ready_kong_offers_single_replacement_and_keeps_lock(self):
        state = make_state()
        flow = state.exchange_six
        player = state.player_list[0]
        player.hand_tiles = [12]*3 + [21,22,23,31,32,25,25]
        player.combination_tiles = ["G11"]
        player.combination_mask = [[0,11]*4]
        flow.lock_patterns(0)
        frozen = dict(flow.locked_patterns[0])
        self.assertIn(33, frozen)
        player.open_kong_locked = True
        flow.reported_ready.add(0)
        flow.small_hu_forbidden_seat = 0
        player.hand_tiles.append(12)
        player.has_draw_slot = True
        task = asyncio.create_task(flow.after_normal_draw(12))
        await self.wait_for_opening(state)
        await self.action(state, 0, "changsha_four_joy", target=12)
        await asyncio.wait_for(task, 1)
        self.assertEqual(flow.four_joy_paid[0], {12})
        for other in state.player_list[1:]:
            other.tag_list.append("peida")
        task = asyncio.create_task(flow.hand_action())
        await self.wait_for_opening(state)
        self.assertIn(12, flow.targets[0]["buzhang"])
        self.assertIn(12, flow.targets[0]["angang"])
        await self.action(state, 0, "buzhang", target=12)
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.pending_gang_replacement_count, 1)
        self.assertFalse(state.pending_gang_forced_discard)
        state.tiles_list = [37,39,38,36]
        await flow.draw_after_kong()
        self.assertEqual(player.hand_tiles[-1], 38)
        self.assertEqual(len(state.tiles_list), 3)
        self.assertTrue(player.open_kong_locked)
        self.assertEqual(flow.locked_patterns[0], frozen)
        self.assertEqual(player.combination_tiles, ["G11", "G12"])
        task = asyncio.create_task(flow.hand_action())
        await self.wait_for_opening(state)
        self.assertEqual(flow.packet(0).state.private_state["cut_tiles"], [38])
        await self.action(state, 0, "cut", tile=38)
        await asyncio.wait_for(task, 1)
        self.assertEqual(player.hand_tiles, [21,22,23,31,32,25,25])
        self.assertEqual(flow.locked_patterns[0], frozen)

    async def test_v3_first_public_kong_batch_is_ground_win_but_next_batch_is_not(self):
        class Dice:
            def randint(self, lower, upper):
                return 1
        for prior_draws, expected_ground in ((0, True), (1, False)):
            state = make_state(bot_seats=(1,))
            flow = state.exchange_six
            flow.actual_draw_counts[1] = prior_draws
            state.current_player_index = 1
            player = state.player_list[1]
            player.hand_tiles = [21,22,23,31,32,25,25]
            player.combination_tiles = ["G11", "G12"]
            player.open_kong_locked = True
            flow.locked_patterns[1] = {33: []}
            state.prepare_gang_replacement(4, True)
            state.tiles_list = [37,39,33,36,38,29,28,27]
            with patch("server.gamestate.game_changsha.exchange_six.random.Random", return_value=Dice()):
                await flow.draw_after_kong()
            self.assertEqual(state.game_status, "END")
            self.assertEqual("地胡" in flow.wins[1][0]["fans"], expected_ground)
            self.assertEqual(flow.actual_draw_counts[1], prior_draws + 1)

    async def test_v3_locked_kong_without_preserved_waits_cannot_choose_single_replacement(self):
        state = make_state()
        flow = state.exchange_six
        player = state.player_list[0]
        player.hand_tiles = [12]*3 + [21,22,23,31,32,25,25] + [12]
        player.combination_tiles = ["G11"]
        player.open_kong_locked = True
        player.has_draw_slot = True
        flow.locked_patterns[0] = {35: []}
        task = asyncio.create_task(flow.hand_action())
        await self.wait_for_opening(state)
        self.assertNotIn("buzhang", state.action_dict[0])
        self.assertNotIn("angang", state.action_dict[0])
        await self.action(state, 0, "cut", tile=12)
        await asyncio.wait_for(task, 1)
        self.assertTrue(player.open_kong_locked)

    async def test_v3_public_fourth_tile_can_continue_with_single_replacement(self):
        class Dice:
            def randint(self, lower, upper):
                return 1
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        player = state.player_list[0]
        player.hand_tiles = [11]*4 + [12]*3 + [14]*3 + [15]*2 + [16]*2
        player.has_draw_slot = True
        for other in state.player_list[1:]:
            other.tag_list.append("peida")
        await flow.declare_kong(0, 11, formal=True)
        frozen = dict(flow.locked_patterns[0])
        state.tiles_list = [21,22,23,24,32,39,12,22,25,26]
        with patch("server.gamestate.game_changsha.exchange_six.random.Random", return_value=Dice()):
            task = asyncio.create_task(flow.draw_after_kong())
            await self.wait_for_opening(state)
            self.assertEqual(flow.phase, "kong_continue")
            self.assertEqual(flow.targets[0]["buzhang"], [12])
            await self.action(state, 0, "buzhang", target=12)
            await asyncio.wait_for(task, 1)
        self.assertEqual(state.pending_gang_replacement_count, 1)
        self.assertFalse(state.pending_gang_forced_discard)
        self.assertEqual(player.discard_tiles, [22,32,39])
        self.assertEqual(player.combination_tiles, ["G11", "G12"])
        self.assertEqual(flow.locked_patterns[0], frozen)
        before = list(state.tiles_list)
        await flow.draw_after_kong()
        self.assertEqual(len(state.tiles_list), len(before)-1)
        self.assertEqual(player.hand_tiles[-1], 25)
        self.assertTrue(player.open_kong_locked)
        self.assertEqual(flow.locked_patterns[0], frozen)


    async def test_v3_public_fourth_tile_can_upgrade_pong_by_preserved_single_replacement(self):
        class Dice:
            def randint(self, lower, upper):
                return 1
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        player = state.player_list[0]
        player.hand_tiles = [13]*3 + [14]*2 + [15]*2
        player.combination_tiles = ["G11", "k12"]
        player.combination_mask = [[0,11]*4, [0,12,1,12,0,12]]
        flow.lock_patterns(0)
        frozen = dict(flow.locked_patterns[0])
        player.open_kong_locked = True
        state.prepare_gang_replacement(4, True)
        for other in state.player_list[1:]:
            other.tag_list.append("peida")
        state.tiles_list = [21,22,23,24,32,39,12,22,25,26]
        with patch("server.gamestate.game_changsha.exchange_six.random.Random", return_value=Dice()):
            task = asyncio.create_task(flow.draw_after_kong())
            await self.wait_for_opening(state)
            self.assertEqual(flow.phase, "kong_continue")
            self.assertEqual(flow.targets[0]["jiagang"], [12])
            self.assertEqual(flow.targets[0]["buzhang"], [12])
            await self.action(state, 0, "buzhang", target=12)
            await asyncio.wait_for(task, 1)
        self.assertEqual(player.combination_tiles, ["G11", "g12"])
        self.assertEqual(state.pending_gang_replacement_count, 1)
        self.assertEqual(player.hand_tiles, [13]*3+[14]*2+[15]*2)
        self.assertEqual(flow.locked_patterns[0], frozen)


    async def test_v3_kong_pass_compares_complete_group_for_ordinary_ready_and_open(self):
        for declaration, old_base in ((None,64), ("ready",128), ("open",128)):
            state = make_state()
            flow = state.exchange_six
            flow.actual_draw_counts = {seat: 2 for seat in range(4)}
            player = state.player_list[1]
            player.hand_tiles = [12]*3+[14]*3+[15]*2+[16]*2
            player.combination_tiles = ["G11"]
            if declaration == "ready":
                flow.reported_ready.add(1)
            elif declaration == "open":
                flow.open_hands.add(1)
            for seat in (2,3):
                state.player_list[seat].tag_list.append("peida")
            task = asyncio.create_task(flow.claims([13,14,17], kong=True, allow_meld=False))
            await self.wait_for_opening(state)
            await self.action(state, 1, "pass")
            await asyncio.wait_for(task, 1)
            self.assertEqual(state.player_passed_hu_base[1], old_base)
            if declaration == "ready":
                self.assertEqual(flow.reported_ready_pass_base[1], old_base)
            if declaration == "open":
                self.assertEqual(flow.greedy_pass_patterns[1][("清一色",)], old_base)
                state.current_player_index = 1
                player.hand_tiles.append(39)
                player.has_draw_slot = True
                await flow.after_normal_draw(39)
                task = asyncio.create_task(flow.hand_action())
                await self.wait_for_opening(state)
                await self.action(state, 1, "cut", tile=39)
                await asyncio.wait_for(task, 1)
                self.assertEqual(state.player_passed_hu_base.get(1,0), 0)
                state.current_player_index = 0
            self.assertFalse(await flow.claims([13,14], kong=True, allow_meld=False))
            task = asyncio.create_task(flow.claims([13,14,17,13], kong=True, allow_meld=False))
            await self.wait_for_opening(state)
            await self.action(state, 1, "hu_first")
            await asyncio.wait_for(task, 1)
            self.assertEqual(sum(base_score(big_weight(group["fans"])) for group in flow.wins[1]), old_base*2)
            self.assertEqual(flow.wins[1][0]["tiles"], [13,14,17,13])

    async def test_v3_kong_pass_compares_mixed_group_total_48_then_64(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = [12]*3+[14]*3+[15]*2+[16]*2
        state.player_list[1].combination_tiles = ["G11"]
        for seat in (2,3):
            state.player_list[seat].tag_list.append("peida")
        task = asyncio.create_task(flow.claims([13,15], kong=True, allow_meld=False))
        await self.wait_for_opening(state)
        await self.action(state, 1, "pass")
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_passed_hu_base[1], 48)
        self.assertFalse(await flow.claims([13,14], kong=True, allow_meld=False))
        task = asyncio.create_task(flow.claims([13,14,15], kong=True, allow_meld=False))
        await self.wait_for_opening(state)
        await self.action(state, 1, "hu_first")
        await asyncio.wait_for(task, 1)
        self.assertEqual(sum(base_score(big_weight(group["fans"])) for group in flow.wins[1]), 64)
        self.assertEqual([group["tiles"] for group in flow.wins[1]], [[13,14],[15]])


    async def test_audit_four_joy_then_hu_wire_contains_whole_round_delta(self):
        state = make_state(bot_seats=(0,))
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[0].hand_tiles = [12]*4+[11,14,17,19,21,24,27,31,39,38]
        await flow.four_joy(0, [12])
        instant = [player.score for player in state.player_list]
        self.assertGreater(instant[0], 0)
        state.player_list[0].hand_tiles = [12,12,12,13,14,21,22,23,24,25,26,31,32,33]
        state.player_list[0].has_draw_slot = True
        await flow.hand_action()
        with patch.object(state, "_draw_changsha_birds", return_value=[]):
            await flow.settle()
        messages = state.game_server.user_id_to_connection[101].websocket.messages
        results = [message["show_result_info"] for message in messages if message["type"] == "gamestate/changsha/show_result"]
        final = results[-1]
        expected = {index: player.score for index, player in enumerate(state.player_list)}
        self.assertEqual(final["score_changes"], expected)
        self.assertEqual(final["player_to_score"], expected)
        self.assertEqual(state.player_list[0].score, instant[0]+6)
        self.assertTrue(any("付给" in label and "鸟乘" in label for label in final["hu_fan"]))

    async def test_audit_four_joy_draw_loop_sends_absolute_scores_without_double_count(self):
        state = make_state(bot_seats=(0,1,2,3))
        state.game_server.gamestate_manager = SimpleNamespace(cleanup_game_state_complete=AsyncMock())
        state.db_manager = SimpleNamespace(store_changsha_game_record=lambda *args: "audit-record")
        captured = []
        async def capture_result(current_state, **kwargs):
            captured.append((kwargs, {p.player_index: p.original_player_index for p in current_state.player_list}))
        async def opening_with_instant_four_joy():
            flow = state.exchange_six
            for player in state.player_list[1:]:
                player.tag_list.append("peida")
            state.player_list[0].hand_tiles = [12]*4+[11,14,17,19,21,24,27,31,39,38]
            await flow.four_joy(0, [12])
            state.tiles_list = []
        with patch.object(state.exchange_six, "opening", side_effect=opening_with_instant_four_joy), \
             patch("server.gamestate.game_changsha.ChangshaGameState.broadcast_result", new=AsyncMock(side_effect=capture_result)) as result, \
             patch("server.gamestate.game_changsha.ChangshaGameState.liuju_ready_wait_seconds", return_value=0), \
             patch("server.gamestate.game_changsha.ChangshaGameState.setup_random_seed_system", return_value=(2345,3456,4567,False)):
            await asyncio.wait_for(state.game_loop_changsha(), 20)
        self.assertEqual(result.await_count, 4)
        cumulative = {seat: 0 for seat in range(4)}
        for kwargs, originals in captured:
            self.assertEqual(kwargs["hu_class"], "liuju")
            for original, delta in kwargs["score_changes"].items():
                cumulative[original] += delta
            self.assertEqual(kwargs["player_to_score"], {seat: cumulative[original] for seat, original in originals.items()})
        self.assertEqual(cumulative, {player.original_player_index: player.score for player in state.player_list})

    async def test_audit_open_hand_blind_view_privacy_reconnect_and_timeout(self):
        for timeout in (False, True):
            state = make_state()
            flow = state.exchange_six
            state.current_player_index = 3
            flow.open_hands.add(3)
            for player in state.player_list[:3]:
                player.tag_list.append("peida")
            state.player_list[3].hand_tiles = [12]*4+[21,22,23,24,25,26,31,32,33]
            state.tiles_list = [tile for tile in state.tiles_list if tile not in (12,19)]
            state.tiles_list[0] = 19
            task = asyncio.create_task(flow.before_draw("normal"))
            await self.wait_for_opening(state)
            await self.action(state, 3, "changsha_blind_kong", target=12)
            for _ in range(40):
                if flow.phase == "blind_kong_view":
                    break
                await asyncio.sleep(0)
            self.assertEqual(flow.phase, "blind_kong_view")
            self.assertEqual(flow.packet(3).state.private_state["hand_tiles"][-1], 19)
            self.assertEqual(len(flow.packet(0).state.public_state["open_hands"]["3"]), 9)
            self.assertNotIn(19, flow.packet(0).state.public_state["open_hands"]["3"])
            await reconnected_send_pending_ask(state, 103)
            own = state.game_server.user_id_to_connection[103].websocket.messages[-1]["state"]
            self.assertEqual(own["private_state"]["hand_tiles"][-1], 19)
            self.assertNotIn(19, own["public_state"]["open_hands"]["3"])
            if timeout:
                with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
                    state.action_events[3].set()
                    await asyncio.wait_for(task, 2)
            else:
                await self.action(state, 3, "changsha_blind_reveal")
                await asyncio.wait_for(task, 1)
            self.assertIsNone(flow.private_reveal_pending)
            self.assertEqual(flow.packet(0).state.public_state["open_hands"]["3"][-1], 19)
            self.assertEqual(len(flow.packet(0).state.public_state["open_hands"]["3"]), 10)


if __name__ == "__main__":
    unittest.main()
