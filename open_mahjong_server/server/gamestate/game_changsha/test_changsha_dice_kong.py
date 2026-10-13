import asyncio
import importlib
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from server.game_calculation.changsha.changsha_hepai_check import changsha_base_from_fans
from server.gamestate.game_changsha.ChangshaGameState import ChangshaGameState, ChangshaPlayer
from server.gamestate.game_changsha.action_check import check_action_hand_action
from server.gamestate.game_changsha.get_action import get_action
from server.gamestate.game_changsha.open_kong import open_kong_tile_positions, normalize_open_kong_count, roll_open_kong_dice
from server.gamestate.game_changsha.boardcast import reconnected_send_pending_ask_for_viewer
from server.gamestate.public.claim_protection import init_claim_protection_state
from server.room.room_validators import ChangshaRoomValidator

state_module = importlib.import_module('server.gamestate.game_changsha.ChangshaGameState')
wait_module = importlib.import_module('server.gamestate.game_changsha.wait_action')


class CapturingSocket:
    def __init__(self):
        self.messages = []

    async def send_json(self, message):
        self.messages.append(message)


class BatchCalculation:
    def __init__(self, winners=(), self_tiles=()):
        self.winners = winners
        self.self_tiles = set(self_tiles)

    def Changsha_tingpai_check(self, hand, melds):
        if 'G11' in melds:
            return self.self_tiles | {25}
        marker = hand[0] if hand else None
        if marker == 31 and 1 in self.winners:
            return {21, 22}
        if marker == 32 and 2 in self.winners:
            return {21}
        return set()

    def Changsha_hepai_check(self, hand, melds, ways, tile):
        if 'G11' in melds and tile in self.self_tiles:
            return 12, ['清一色', '杠上开花']
        marker = hand[0] if hand else None
        if (marker == 31 and 1 in self.winners and tile in (21, 22)) or (marker == 32 and 2 in self.winners and tile == 21):
            return 12, ['碰碰胡', '杠上炮']
        return 0, []

    def Changsha_base_from_fans(self, fans, dealer_related=False):
        return changsha_base_from_fans(fans, dealer_related)


def make_batch_state(tile_count=30, count=2, winners=(), self_tiles=(), hand=None):
    state = ChangshaGameState.__new__(ChangshaGameState)
    state.player_list = [ChangshaPlayer(100 + i, f'p{i}', [30 + i], 10) for i in range(4)]
    state.player_list[0].hand_tiles = list(hand or [11] * 4 + [12, 13, 14, 22, 23, 24, 27, 28, 29, 25])
    for i, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = i
    state.tiles_list = [(i % 27 // 9 + 1) * 10 + i % 9 + 1 for i in range(tile_count)]
    if tile_count >= 10:
        state.tiles_list[0:2] = [11, 22]
        state.tiles_list[8:10] = [21, 22]
    state.calculation_service = BatchCalculation(winners, self_tiles)
    state.current_player_index = 0
    state.current_round = state.round_index = state.max_round = 1
    state.round_random_seed = 456
    state.server_action_tick = 1
    state.player_action_tick = 0
    state.step_time = 5
    state.round_time = 10
    state.open_kong_replacement_count = count
    state.small_hu_self_draw_only = True
    state.score_cap_type = 'none'
    state.score_cap_value = 0
    state.base_score_no_dealer = True
    state.small_hu_score = 2
    state.big_hu_score = 6
    state.bird_count = 2
    state.dealer_bird = True
    state.room_rule = 'changsha'
    state.room_type = 'custom'
    state.room_id = 1
    state.gamestate_id = 'test'
    state.sub_rule = 'changsha/classic_double_bird'
    state.hepai_limit = 1
    state.tactical_call = state.claim_protection = False
    state.tips = state.open_cuohe = state.show_moqie_hint = False
    state.count_tips = False
    state.pointer_tips = True
    state.commitment = 0
    state.salt = ''
    state.isPlayerSetRandomSeed = False
    state.dihe_possible = state.last_draw_was_gang = False
    state.hu_class = None
    state.xunmu = 0
    state.backward_tiles_list_type = 'double'
    state.result_dict = {}
    state.action_events = {i: asyncio.Event() for i in range(4)}
    state.action_queues = {i: asyncio.Queue() for i in range(4)}
    state.waiting_players_list = []
    state.action_priority = dict(hu_self=6, hu_first=5, hu_second=4, hu_third=3, angang=0, buzhang=0, cut=0, pass_=0)
    state.action_priority['pass'] = state.action_priority['force_pass'] = 0
    state.game_record = {'game_title': {'rule': 'changsha'}, 'game_round': {'round_index_1': {'action_ticks': []}}}
    state.sockets = {i: CapturingSocket() for i in range(4)}
    state.game_server = SimpleNamespace(
        players={f'conn-{i}': SimpleNamespace(user_id=100 + i) for i in range(4)},
        user_id_to_connection={100 + i: SimpleNamespace(websocket=state.sockets[i]) for i in range(4)},
    )
    state.realtime_spectators = []
    state.broadcast_do_action = AsyncMock()
    state._reset_changsha_round_state()
    init_claim_protection_state(state)
    state.refresh_waiting_tiles(0)
    state.action_dict = check_action_hand_action(state, 0)
    state.game_status = 'waiting_hand_action'
    return state


async def submit_hand_action(state, action, target=None):
    task = asyncio.create_task(state.wait_action())
    for _ in range(100):
        if 0 in state.waiting_players_list:
            break
        await asyncio.sleep(0)
    await get_action(state, 'conn-0', action, False, 0, 0, target)
    await asyncio.wait_for(task, 2)


async def declare_and_draw(state, dice=(5, 6)):
    with patch.object(state_module, 'roll_open_kong_dice', return_value=list(dice)), patch.object(wait_module, 'broadcast_do_action', AsyncMock()):
        await submit_hand_action(state, 'angang', 11)
        await state.deal_gang_replacements()


class ChangshaDiceKongTest(unittest.TestCase):
    def test_user_examples_through_real_declaration_and_draw_entry(self):
        for tiles, count, expected_positions in ((30, 4, [8, 9, 6, 7]), (30, 2, [8, 9]), (22, 4, [0, 1]), (20, 4, [10, 11, 8, 9]), (8, 4, [])):
            with self.subTest(tiles=tiles, count=count):
                state = make_batch_state(tiles, count)
                original = list(state.tiles_list)
                asyncio.run(declare_and_draw(state))
                self.assertEqual(state.open_kong_wall_positions, expected_positions)
                self.assertEqual(state.tiles_list, [tile for i, tile in enumerate(original) if i not in expected_positions])
                self.assertEqual(state.player_list[0].hand_tiles, [12, 13, 14, 22, 23, 24, 27, 28, 29, 25])
                self.assertTrue(state.player_list[0].open_kong_locked)
                self.assertIn('G11', state.player_list[0].combination_tiles)
                self.assertEqual(state.game_status, 'deal_card')
                batch = state.game_record['game_round']['round_index_1']['changsha_open_kong_batches'][0]
                self.assertEqual(batch['tiles'], [original[i] for i in expected_positions])

    def test_half_stack_is_taken_without_small_die_fallback(self):
        state = make_batch_state(21, 4)
        asyncio.run(declare_and_draw(state))
        self.assertEqual(state.open_kong_wall_positions, [0])
        self.assertEqual(len(state.tiles_list), 20)

    def test_tail_half_stack_left_by_supplement_is_counted_separately(self):
        self.assertEqual(open_kong_tile_positions(54, 4, [5, 6], tail_half_stack=True), [33, 34, 31, 32])
        state = make_batch_state(54, 4)
        state.backward_tiles_list_type = 'single'
        original = list(state.tiles_list)
        asyncio.run(declare_and_draw(state))
        self.assertEqual(state.open_kong_wall_positions, [33, 34, 31, 32])
        self.assertEqual(state.tiles_list, [tile for i, tile in enumerate(original) if i not in (33, 34, 31, 32)])

    def test_recorded_positions_reconstruct_actual_wall_for_all_examples(self):
        for tiles, count in ((30, 4), (22, 4), (20, 4), (21, 4), (8, 4)):
            state = make_batch_state(tiles, count)
            wall = list(state.tiles_list)
            asyncio.run(declare_and_draw(state))
            ticks = state.game_record['game_round']['round_index_1']['action_ticks']
            for tick in ticks:
                if tick[0] != 'gd':
                    continue
                self.assertEqual(tick[3], 'changsha_open_kong')
                self.assertEqual(wall.pop(tick[4]), tick[1])
            self.assertEqual(wall, state.tiles_list)

    def test_reserved_sea_bottom_becomes_tail_half_stack_for_continuation(self):
        async def run():
            state = make_batch_state(6, 2, hand=[11] * 4 + [12] * 4 + [21, 22, 23, 25, 25, 25])
            original = list(state.tiles_list)
            await declare_and_draw(state, dice=(1, 6))
            self.assertEqual(state.open_kong_wall_positions, [4])
            self.assertEqual(state.backward_tiles_list_type, 'single')
            self.assertEqual(state.tiles_list[-1], original[-1])
            with patch.object(state_module, 'roll_open_kong_dice', return_value=[1, 1]), patch.object(wait_module, 'broadcast_do_action', AsyncMock()):
                await submit_hand_action(state, 'angang', 12)
                self.assertEqual(state.open_kong_wall_positions, [2, 3])
                await state.deal_gang_replacements()
            self.assertEqual(state.tiles_list, [original[0], original[1], original[-1]])
        asyncio.run(run())

    def test_supplement_keeps_single_hidden_draw_and_does_not_roll_dice(self):
        async def run():
            state = make_batch_state()
            with patch.object(state_module, 'roll_open_kong_dice') as roller, patch.object(wait_module, 'broadcast_do_action', AsyncMock()):
                await submit_hand_action(state, 'buzhang', 11)
                await state.deal_gang_replacements()
                roller.assert_not_called()
            self.assertFalse(state.player_list[0].open_kong_locked)
            self.assertIsNone(state.broadcast_do_action.call_args.kwargs['deal_tiles'])
            draws = [tick for tick in state.game_record['game_round']['round_index_1']['action_ticks'] if tick[0] == 'gd']
            self.assertEqual(len(draws), 1)
            self.assertEqual(len(draws[0]), 2)
        asyncio.run(run())

    def test_reconnect_and_realtime_snapshot_restore_all_public_tiles(self):
        from server.gamestate.game_changsha.boardcast import _build_game_start_payload_for_viewer
        async def run():
            state = make_batch_state(winners=(1,))
            await declare_and_draw(state)
            snapshot = _build_game_start_payload_for_viewer(state, 1)
            self.assertEqual(snapshot['detailed_config']['open_kong_tiles'], [21, 22])
            self.assertEqual(snapshot['detailed_config']['open_kong_player'], 0)
            self.assertIsNone(snapshot['players_info'][0]['hand_tiles'])
            await state.player_reconnect(101)
            restored = [message for message in state.sockets[1].messages if message['type'] == 'gamestate/changsha/game_start'][-1]
            self.assertEqual(restored['game_info']['detailed_config']['open_kong_tiles'], [21, 22])
        asyncio.run(run())

    def test_sea_bottom_is_reserved_and_zero_tiles_pass_turn(self):
        self.assertEqual(open_kong_tile_positions(2, 4, [1, 1]), [0])
        state = make_batch_state(1, 2, winners=(1,))
        original = list(state.tiles_list)
        asyncio.run(declare_and_draw(state, [1, 1]))
        self.assertEqual(state.tiles_list, original)
        self.assertEqual(state.game_status, 'deal_card')
        self.assertEqual(state._sea_bottom_waiting_candidates()[0], 1)
        self.assertTrue(state.player_list[0].open_kong_locked)
        self.assertFalse(state.player_list[0].has_draw_slot)

    def test_real_batch_multi_ron_shared_birds_cap_and_result_status(self):
        async def run():
            state = make_batch_state(winners=(1, 2))
            state.score_cap_type, state.score_cap_value = 'score', 64
            await declare_and_draw(state)
            self.assertEqual(state.game_status, 'waiting_gang_replacement_ron')
            self.assertEqual(len(state.pending_gang_replacement_ron_results[1]), 2)
            await reconnected_send_pending_ask_for_viewer(state, 101, 1)
            self.assertEqual(state.sockets[1].messages[-1]['ask_other_action_info']['cut_tile'], 21)
            await state.broadcast_ask_other_action()
            task = asyncio.create_task(state.wait_action())
            await asyncio.sleep(0)
            await get_action(state, 'conn-1', 'hu_first', False, 0, 0, None)
            await get_action(state, 'conn-2', 'hu_second', False, 0, 0, None)
            await asyncio.wait_for(task, 2)
            self.assertEqual([(e['winner'], e['tile']) for e in state.accepted_gang_replacement_ron_results], [(1, 21), (2, 21), (1, 22)])
            with patch.object(state_module, 'hu_result_ready_wait_seconds', return_value=0):
                result = await state._settle_gang_replacement_multi_ron({i: 0 for i in range(4)})
            self.assertEqual([p.score for p in state.player_list], [-88, 64, 24, 0])
            self.assertEqual(result['result_count'], 3)
            payloads = [m['show_result_info'] for m in state.sockets[0].messages if m['type'] == 'gamestate/changsha/show_result']
            self.assertEqual([p['next_status'] for p in payloads], ['round_continue', 'round_continue', 'round_end_by_ready'])
            self.assertTrue(all(p['bird_tiles'] == [11, 22] for p in payloads))
            self.assertEqual(sum(p.score for p in state.player_list), 0)
        asyncio.run(run())

    def test_batch_timeout_closes_only_expired_seat(self):
        async def run():
            state = make_batch_state(winners=(1, 2))
            await declare_and_draw(state)
            state.player_list[1].remaining_time = 0
            state.player_list[2].remaining_time = 30
            with patch.object(wait_module, 'get_ask_elapsed', side_effect=lambda _, i: 10 if i == 1 else 0):
                task = asyncio.create_task(state.wait_action())
                await asyncio.sleep(0)
                await get_action(state, 'conn-1', 'hu_first', False, 0, 0, None)
                await get_action(state, 'conn-2', 'pass', False, 0, 0, None)
                await asyncio.wait_for(task, 2)
            self.assertEqual(state.accepted_gang_replacement_ron_results, [])
            self.assertEqual(state.player_list[1].remaining_time, 0)
            self.assertEqual(state.player_list[2].remaining_time, 30)
            self.assertGreater(state.player_passed_hu_base[1], 0)
        asyncio.run(run())

    def test_generated_batch_supports_force_pass_real_action(self):
        async def run():
            state = make_batch_state(winners=(1,))
            state.tactical_call = True
            await declare_and_draw(state)
            self.assertIn('force_pass', state.action_dict[1])
            task = asyncio.create_task(state.wait_action())
            await asyncio.sleep(0)
            await get_action(state, 'conn-1', 'force_pass', False, 0, 0, None)
            await asyncio.wait_for(task, 2)
            self.assertEqual(state.game_status, 'deal_card')
            self.assertGreater(state.player_passed_hu_base[1], 0)
        asyncio.run(run())

    def test_ai_batch_pass_recovers_stale_waiting_tick(self):
        async def run():
            state = make_batch_state(winners=(1,))
            await declare_and_draw(state)
            state.player_list[1].user_id = 0
            state._waiting_action_tick = -1
            await state.broadcast_ask_other_action()
            await asyncio.wait_for(state.wait_action(), 2)
            self.assertEqual(state.game_status, 'deal_card')
            self.assertGreater(state.player_passed_hu_base[1], 0)
        asyncio.run(run())

    def test_continuation_kong_discards_unused_tile_and_refreshes_pass(self):
        async def run():
            state = make_batch_state(self_tiles=(21,), hand=[11] * 4 + [21] * 3 + [12, 13, 14, 22, 23, 24, 25])
            state.tiles_list[8:10] = [21, 31]
            await declare_and_draw(state)
            self.assertEqual(state.game_status, 'waiting_gang_replacement_self_hu')
            await submit_hand_action(state, 'pass')
            self.assertIn(0, state.player_passed_hu_until_draw)
            self.assertIn('angang', state.action_dict[0])
            with patch.object(state_module, 'roll_open_kong_dice', return_value=[1, 2]), patch.object(wait_module, 'broadcast_do_action', AsyncMock()):
                await submit_hand_action(state, 'angang', 21)
                self.assertEqual(state.player_list[0].discard_tiles, [31])
                await state.deal_gang_replacements()
            self.assertNotIn(0, state.player_passed_hu_until_draw)
            self.assertNotIn(0, state.player_passed_hu_base)
            self.assertEqual(state.open_kong_sequence, 2)
        asyncio.run(run())

    def test_open_kong_self_draw_record_uses_only_winning_replacements(self):
        for winning_tiles in ((21,), (21, 22)):
            async def run():
                state = make_batch_state(count=4, self_tiles=winning_tiles)
                state.tiles_list[8:10] = [21, 31]
                state.tiles_list[6:8] = [22, 32]
                await declare_and_draw(state)
                await submit_hand_action(state, 'hu_self')
                display_hand = state._hepai_display_hand(0)
                state._record_changsha_hu_result(48, ['杠上开花'], 0, [48, -16, -16, -16], [11, 22])
                tick = state.game_record['game_round']['round_index_1']['action_ticks'][-1]
                self.assertEqual(tick[5], winning_tiles[-1])
                self.assertEqual(tick[6], [11, 22])
                self.assertEqual(tick[7], 'changsha_open_kong_win')
                self.assertEqual(tick[8], display_hand)
                self.assertEqual(tick[8][-len(winning_tiles):], list(winning_tiles))
                self.assertNotIn(31, tick[8])
                self.assertNotIn(32, tick[8])
            asyncio.run(run())

    def test_self_draw_action_received_after_deadline_is_a_pass(self):
        async def run():
            state = make_batch_state(self_tiles=(21,))
            await declare_and_draw(state)
            state.player_list[0].remaining_time = 0
            state.step_time = 1
            await get_action(state, 'conn-0', 'hu_self', False, 0, 0, None)
            with patch.object(wait_module, 'get_ask_elapsed', side_effect=[0, 2]):
                await state.wait_action()
            self.assertIsNone(state.hu_class)
            self.assertEqual(state.game_status, 'deal_card')
            self.assertEqual(state.player_list[0].remaining_time, 0)
            self.assertIn(0, state.player_passed_hu_until_draw)
        asyncio.run(run())

    def test_game_loop_records_actual_capped_open_kong_self_draw_total(self):
        class RoundRecorded(Exception):
            pass

        async def run():
            state = make_batch_state(self_tiles=(21,))
            state.Debug = True
            state.current_round = 4
            state.match_tier = state.event_id = None
            state.detailed_config = {}
            state.score_cap_type, state.score_cap_value = 'score', 10
            state.broadcast_game_start = AsyncMock()
            state._detect_initial_hu_types = lambda: setattr(state, 'initial_hu_types', {})
            captured = AsyncMock()

            def stop_after_record(_state):
                raise RoundRecorded()

            with patch.object(state_module, 'setup_random_seed_system', return_value=(456, '', 0, False)), \
                 patch.object(state_module, 'capture_player_entry_order'), \
                 patch.object(state_module, 'init_game_record'), \
                 patch.object(state_module, 'init_game_round'), \
                 patch.object(state_module, 'init_changsha_tiles'), \
                 patch.object(state_module, 'vote_checkpoint', AsyncMock()), \
                 patch.object(state_module, 'roll_open_kong_dice', return_value=[5, 6]), \
                 patch.object(state_module, 'broadcast_result', captured), \
                 patch.object(state_module, 'player_action_record_round_end', side_effect=stop_after_record), \
                 patch.object(wait_module, 'broadcast_do_action', AsyncMock()):
                task = asyncio.create_task(state.game_loop_changsha())
                for expected_status, action, target in (
                    ('waiting_hand_action', 'angang', 11),
                    ('waiting_gang_replacement_self_hu', 'hu_self', None),
                ):
                    for _ in range(1000):
                        if task.done():
                            await task
                        if state.game_status == expected_status and 0 in state.waiting_players_list:
                            break
                        await asyncio.sleep(0)
                    self.assertEqual(state.game_status, expected_status)
                    await get_action(state, 'conn-0', action, False, 0, 0, target)
                with self.assertRaises(RoundRecorded):
                    await asyncio.wait_for(task, 2)
            tick = state.game_record['game_round']['round_index_1']['action_ticks'][-1]
            self.assertEqual(captured.call_args.kwargs['hu_score'], 30)
            self.assertEqual(tick[2], 30)
            self.assertEqual(tick[4], [30, -10, -10, -10])
            self.assertEqual(tick[8], captured.call_args.kwargs['hepai_player_hand'])
        asyncio.run(run())

    def test_open_kong_replacement_payload_is_public_but_supplement_is_hidden(self):
        from server.gamestate.game_changsha.boardcast import _build_do_action_payload
        state = make_batch_state(winners=(1,))
        asyncio.run(declare_and_draw(state))
        payload = _build_do_action_payload(state, ['deal_gang_tile'], 0, 1, deal_tile=22, deal_tiles=[21, 22])
        self.assertEqual(payload['deal_tile'], 22)
        self.assertEqual(payload['deal_tiles'], [21, 22])
        state.pending_gang_forced_discard = False
        payload = _build_do_action_payload(state, ['deal_gang_tile'], 0, 1, deal_tile=22, deal_tiles=[22])
        self.assertIsNone(payload['deal_tile'])
        self.assertEqual(payload['deal_tiles'], [0])
        owner_payload = _build_do_action_payload(state, ['deal_gang_tile'], 0, 0, deal_tile=22, deal_tiles=[22])
        self.assertEqual(owner_payload['deal_tile'], 22)
        self.assertIsNone(owner_payload['deal_tiles'])

    def test_config_accepts_only_two_or_four_and_normalizes_old_values(self):
        base = dict(room_name='长沙', game_round=1, round_timer=0, step_timer=5)
        for count in (2, 4):
            self.assertEqual(ChangshaRoomValidator(**base, open_kong_replacement_count=count).open_kong_replacement_count, count)
        for count in (1, 3, 0, 5):
            with self.assertRaises(ValueError):
                ChangshaRoomValidator(**base, open_kong_replacement_count=count)
            self.assertEqual(normalize_open_kong_count(count), 2)

    def test_dice_sequence_is_seed_reproducible(self):
        for sequence in (1, 2, 3):
            self.assertEqual(roll_open_kong_dice(42, 1, sequence), roll_open_kong_dice(42, 1, sequence))
            self.assertTrue(all(1 <= die <= 6 for die in roll_open_kong_dice(42, 1, sequence)))
