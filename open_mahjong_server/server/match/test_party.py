import asyncio
import time
import unittest
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, Mock

from .match_manager import MatchManager
from .party_manager import PartyManager
from .rating_rules import DOUBLES_QUEUE, DOUBLES_RATING_RULE
from .team_elo import team_elo_deltas


class PartyTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.s = NS(players={}, user_id_to_connection={},
            room_manager=NS(rooms={}, event_seating_users=set()),
            gamestate_manager=NS(is_user_in_active_game=Mock(return_value=False), remove_spectator_from_all_games=AsyncMock()),
            db_manager=NS(get_user_settings=Mock(side_effect=lambda uid: {'username': str(uid)}),
                list_mutual_friends=Mock(return_value=[{'user_id': u} for u in range(1, 9)]),
                get_rank_data=Mock(return_value={'ratings': {}})))
        for u in range(1, 9):
            c = NS(user_id=u, username=str(u), is_tourist=False, current_room_id=None, websocket=NS(send_json=AsyncMock()))
            self.s.players[str(u)] = c
            self.s.user_id_to_connection[u] = c
        self.mm = self.s.match_manager = MatchManager(self.s)
        self.mm._start_game = AsyncMock()
        self.pm = self.s.party_manager = PartyManager(self.s)

    async def asyncTearDown(self):
        if self.pm.match_task:
            self.pm.match_task.cancel()
            await asyncio.gather(self.pm.match_task, return_exceptions=True)

    async def request(self, uid, op, **data):
        return await self.pm.handle(str(uid), {'type': 'party/' + op, **data})

    async def pair(self, a, b):
        r = await self.request(a, 'invite', target_user_id=b)
        self.assertTrue(r.success, r.message)
        r = await self.request(b, 'respond', invite_id=r.party_invite['invite_id'], accept=True)
        self.assertTrue(r.success, r.message)
        return self.pm.get(a)

    async def test_either_member_can_start_and_the_other_can_cancel_without_ready(self):
        p = await self.pair(1, 2)
        for starter, canceller in ((1, 2), (2, 1)):
            self.assertFalse(p.ready)
            self.assertTrue((await self.request(starter, 'queue')).success)
            self.assertEqual(self.mm.queues[DOUBLES_QUEUE], [1, 2])
            self.assertTrue((await self.mm.leave_queue(str(canceller))).success)
            self.assertEqual(self.mm.queues[DOUBLES_QUEUE], [])
            self.assertFalse(p.queued_at)
            for member in (1, 2):
                notices = [call.args[0]['message'] for call in self.s.players[str(member)].websocket.send_json.call_args_list
                           if call.args[0]['type'] == 'party/notice']
                self.assertEqual(notices[-2:], [f'{starter} 开始了组队匹配', f'{canceller} 取消了组队匹配'])

    async def test_duplicate_start_and_cancel_requests_notify_each_member_only_once(self):
        p = await self.pair(1, 2)
        for member in (1, 2):
            self.s.players[str(member)].websocket.send_json.reset_mock()
        started = await asyncio.gather(self.request(1, 'queue'), self.request(2, 'queue'))
        self.assertTrue(all(response.success for response in started))
        self.assertEqual(self.mm.queues[DOUBLES_QUEUE], [1, 2])
        stopped = await asyncio.gather(self.request(2, 'cancel'), self.request(1, 'cancel'))
        self.assertTrue(all(response.success for response in stopped))
        self.assertFalse(p.queued_at)
        for member in (1, 2):
            notices = [call.args[0] for call in self.s.players[str(member)].websocket.send_json.call_args_list
                       if call.args[0]['type'] == 'party/notice']
            self.assertEqual([notice['message'] for notice in notices], ['1 开始了组队匹配', '2 取消了组队匹配'])

    async def test_two_parties_commit_together_and_preserve_opposite_seats(self):
        await self.pair(1, 2); await self.pair(3, 4)
        for u in (2, 4):
            await self.request(u, 'ready', ready=True)
        await self.request(1, 'queue'); await self.request(3, 'queue')
        await asyncio.sleep(0)
        self.assertEqual(self.mm.committed_users, {1, 2, 3, 4})
        self.mm._start_game.assert_awaited_once_with(DOUBLES_QUEUE, [1, 3, 2, 4])
        self.assertFalse((await self.request(2, 'leave')).success)

    async def test_simultaneous_accepts_do_not_overfill(self):
        a = await self.request(1, 'invite', target_user_id=2)
        b = await self.request(1, 'invite', target_user_id=3)
        results = await asyncio.gather(*[self.request(u, 'respond', invite_id=r.party_invite['invite_id'], accept=True) for u, r in ((2, a), (3, b))])
        self.assertEqual(sum(r.success for r in results), 1)
        self.assertEqual(len(self.pm.get(1).members), 2)

    async def test_expired_wrong_recipient_and_revoked_invites(self):
        r = await self.request(1, 'invite', target_user_id=2)
        key = r.party_invite['invite_id']
        self.assertFalse((await self.request(3, 'respond', invite_id=key, accept=True)).success)
        self.assertTrue((await self.request(1, 'revoke', invite_id=key)).success)
        self.assertFalse((await self.request(2, 'respond', invite_id=key, accept=True)).success)
        r = await self.request(1, 'invite', target_user_id=2)
        self.pm.invites[r.party_invite['invite_id']]['expires_at'] = time.time() - 1
        self.assertFalse((await self.request(2, 'respond', invite_id=r.party_invite['invite_id'], accept=True)).success)

    async def test_failed_delivery_does_not_leave_a_phantom_invitation(self):
        self.s.user_id_to_connection[2].websocket.send_json.side_effect = ConnectionError('closed')
        result = await self.request(1, 'invite', target_user_id=2)
        self.assertFalse(result.success)
        self.assertFalse(self.pm.invites)

    async def test_solo_queue_and_room_conflicts(self):
        await self.pair(1, 2)
        solo = await self.mm.join_queue('1', 'sichuan_elo_xuezhan')
        self.assertFalse(solo.success)
        self.assertEqual(solo.message, '组队期间无法进行单人匹配')
        self.assertFalse((await self.mm.join_queue('3', DOUBLES_QUEUE)).success)
        self.s.players['3'].current_room_id = '123'
        self.assertFalse((await self.request(1, 'invite', target_user_id=3)).success)

    async def test_event_auto_seating_does_not_split_a_lobby_party(self):
        from ..room.room_manager import RoomManager
        await self.pair(1, 2)
        rooms = RoomManager(self.s)
        self.assertFalse(rooms._event_auto_player_allowed({}, 1, self.s.players['1']))
        self.assertFalse(rooms._reject_room_entry_conflicts(1).success)

    async def test_disconnect_cancels_entire_party_and_reconnect_keeps_members(self):
        p = await self.pair(1, 2)
        await self.request(2, 'ready', ready=True); await self.request(1, 'queue')
        self.s.user_id_to_connection.pop(2)
        self.mm.player_disconnect(2)
        self.assertFalse(p.queued_at)
        self.assertEqual(self.mm.user_to_queues, {})
        self.s.user_id_to_connection[2] = self.s.players['2']
        await self.pm.reconnected(2)
        self.assertEqual(p.members, [1, 2])
        self.assertEqual(p.ready, set())

    async def test_disconnect_is_visible_before_connection_cleanup_finishes(self):
        await self.pair(1, 2)
        # GameServer awaits friend/game cleanup before removing this mapping.
        self.mm.player_disconnect(2)
        view = self.pm.snapshot(1)
        self.assertFalse(next(m for m in view['members'] if m['user_id'] == 2)['online'])
        self.assertFalse((await self.request(2, 'ready', ready=True)).success)

    async def test_leader_transfer_kick_and_leave(self):
        p = await self.pair(1, 2)
        self.assertFalse((await self.request(2, 'kick', target_user_id=1)).success)
        self.assertTrue((await self.request(1, 'transfer', target_user_id=2)).success)
        self.assertTrue((await self.request(2, 'leave')).success)
        self.assertEqual(p.leader_user_id, 1)
        self.assertEqual(p.members, [1])

    async def test_member_rating_gap_rejects_the_whole_queue_entry(self):
        p = await self.pair(1, 2)
        await self.request(2, 'ready', ready=True)
        self.s.db_manager.get_rank_data.side_effect = lambda uid: {'ratings': {DOUBLES_RATING_RULE: {'elo': 1500 if uid == 1 else 2001}}}
        result = await self.request(1, 'queue')
        self.assertFalse(result.success)
        self.assertIn('500', result.message)
        self.assertFalse(p.queued_at)
        self.assertFalse(self.mm.user_to_queues)

    async def test_offline_leader_expires_and_online_member_keeps_the_party(self):
        p = await self.pair(1, 2)
        self.pm.RECONNECT_SECONDS = 0
        self.s.user_id_to_connection.pop(1)
        self.mm.player_disconnect(1)
        token = self.pm.disconnect_tokens[1]
        await self.pm.expire_member(1, token)
        self.assertEqual(p.members, [2])
        self.assertEqual(p.leader_user_id, 2)
        self.assertIsNone(self.pm.get(1))

    async def test_elo_ranges_expand_for_both_teams_and_member_gap_is_checked(self):
        a = await self.pair(1, 2); b = await self.pair(3, 4)
        for u in (2, 4):
            await self.request(u, 'ready', ready=True)
        self.s.db_manager.get_rank_data.side_effect = lambda uid: {'ratings': {DOUBLES_RATING_RULE: {'elo': 1500 if uid < 3 else 1800}}}
        await self.request(1, 'queue'); await self.request(3, 'queue')
        self.assertFalse(self.mm.committed_users)
        a.queued_at -= 80
        await self.pm.try_match()
        self.assertFalse(self.mm.committed_users)
        b.queued_at -= 80
        await self.pm.try_match()
        self.assertEqual(self.mm.committed_users, {1, 2, 3, 4})

    async def test_room_invite_keeps_identity_and_has_no_password(self):
        import uuid
        room = {'room_id': '12345', 'instance_id': str(uuid.uuid4()), 'player_list': [1], 'password': 'secret'}
        self.s.room_manager.rooms['12345'] = room
        r = await self.request(1, 'invite', kind='room', room_id='12345', target_user_id=2)
        self.assertTrue(r.success, r.message)
        self.assertNotIn('password', r.party_invite)
        self.s.room_manager.rooms['12345'] = {**room, 'instance_id': str(uuid.uuid4())}
        r = await self.request(2, 'respond', invite_id=r.party_invite['invite_id'], accept=True)
        self.assertFalse(r.success)


class TeamEloTests(unittest.TestCase):
    def players(self, score=1):
        return [{'team_id': t, 'team_match_points': score if t == 0 else 1-score} for t in (0, 1, 0, 1)]

    def test_team_outcome_same_delta_zero_sum_and_draw(self):
        self.assertEqual(team_elo_deltas([1500]*4, self.players()), [16, -16, 16, -16])
        self.assertEqual(team_elo_deltas([1000]*4, self.players(.5)), [0]*4)
        d = team_elo_deltas([1200, 1800, 1400, 2000], self.players())
        self.assertEqual(d[0], d[2]); self.assertEqual(d[1], d[3])
        self.assertEqual(sum(d), 0); self.assertGreater(d[0], 16)

    def test_invalid_outcome_is_rejected(self):
        p = self.players(); p[2]['team_match_points'] = 0
        with self.assertRaises(ValueError):
            team_elo_deltas([1500]*4, p)


def test_ranked_doubles_completes_eight_real_boards_and_dispatches_team_outcome():
    from unittest.mock import patch
    from ..gamestate.game_sichuan.test_doubles import make_state
    from ..gamestate.game_sichuan.test_doubles_full_match import play_match
    state, messages = make_state()
    state.max_round = 2
    state.room_type = 'match'
    state.match_queue_type = DOUBLES_QUEUE
    def settle(game_id, queue, players, **kwargs):
        assert queue == DOUBLES_QUEUE
        changes = team_elo_deltas([1500]*4, players)
        return {str(p['user_id']): dict(rating_rule=DOUBLES_RATING_RULE, rating_system='elo', elo_before=1500,
            elo_after=1500+d, elo_delta=d, rating_games=1, rating_algorithm='team_elo_mean_2400_v1') for p, d in zip(players, changes)}
    state.db_manager.settle_rated_game.side_effect = settle
    with patch('server.gamestate.game_sichuan.test_doubles_full_match.make_state', return_value=(state, messages)):
        result, _ = asyncio.run(play_match(20261107))
    assert len(result.team_round_history) == 8
    assert len(result.game_record['game_round']) == 8
    state.db_manager.settle_rated_game.assert_called_once()
    assert result.game_record['game_title']['rating_rule'] == DOUBLES_RATING_RULE


def test_four_loopback_websocket_peers_invite_prepare_and_commit_as_two_teams():
    import json
    import websockets
    from .test_lifecycle import server_fixture, USERS
    async def run():
        s = server_fixture(); mm = s.match_manager
        mm.committed_users.clear(); mm.winning_queues.clear(); mm._start_game = AsyncMock()
        s.db_manager.list_mutual_friends.return_value = [{'user_id': uid} for uid in USERS]
        s.party_manager = pm = PartyManager(s)
        async def handle(ws):
            uid = int(ws.request.path.strip('/')); cid = str(uid)
            class Adapter:
                async def send_json(self, value):
                    await ws.send(json.dumps(value))
            s.players[cid].websocket = Adapter()
            try:
                async for raw in ws:
                    result = await pm.handle(cid, json.loads(raw))
                    await s.players[cid].websocket.send_json(result.dict(exclude_none=True))
            finally:
                s.user_id_to_connection.pop(uid, None); mm.player_disconnect(uid)
        async with websockets.serve(handle, '127.0.0.1', 0) as listener:
            port = listener.sockets[0].getsockname()[1]
            clients = [await websockets.connect(f'ws://127.0.0.1:{port}/{uid}') for uid in USERS]
            seen = {uid: [] for uid in USERS}
            async def receive(i, kind):
                while True:
                    value = json.loads(await asyncio.wait_for(clients[i].recv(), 5)); seen[USERS[i]].append(value)
                    if value['type'] == kind:
                        return value
            async def request(i, op, **fields):
                await clients[i].send(json.dumps({'type': 'party/' + op, **fields}))
                result = await receive(i, 'party/' + op)
                assert result['success'], result
                return result
            try:
                for leader, member in ((0, 1), (2, 3)):
                    invitation = await request(leader, 'invite', target_user_id=USERS[member])
                    await receive(member, 'party/incoming')
                    await request(member, 'respond', invite_id=invitation['party_invite']['invite_id'], accept=True)
                    await request(member, 'ready', ready=True)
                await request(0, 'queue'); await request(2, 'queue')
                for i in (0, 1, 3):
                    await receive(i, 'match/match_found')
                assert all(sum(v['type'] == 'match/match_found' for v in seen[uid]) == 1 for uid in USERS)
                assert mm.committed_users == set(USERS)
                mm._start_game.assert_awaited_once_with(DOUBLES_QUEUE, [USERS[0], USERS[2], USERS[1], USERS[3]])
            finally:
                await asyncio.gather(*(client.close() for client in clients))
                if pm.match_task:
                    pm.match_task.cancel(); await asyncio.gather(pm.match_task, return_exceptions=True)
    asyncio.run(run())


def test_team_queue_creates_real_sichuan_game_with_team_seats_and_eight_hand_length():
    from unittest.mock import patch
    from .test_lifecycle import server_fixture, USERS
    async def run():
        s = server_fixture(); mm = s.match_manager
        s.gamestate_manager.run_game = AsyncMock()
        with patch('server.match.match_manager.asyncio.sleep', new=AsyncMock()):
            await mm._start_game(DOUBLES_QUEUE, USERS)
        assert len(s.gamestate_manager.gamestate_id_to_game_state) == 1
        state = next(iter(s.gamestate_manager.gamestate_id_to_game_state.values()))
        assert state.sub_rule == 'sichuan/doubles' and state.max_round == 2
        assert [p.team_id for p in state.player_list] == [0, 1, 0, 1]
        assert state.match_queue_type == DOUBLES_QUEUE
        await s.gamestate_manager.cleanup_game_state_complete(state.gamestate_id)
    asyncio.run(run())
