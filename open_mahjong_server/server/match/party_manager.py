"""Server-owned lobby parties and reusable friend invitations.

All mutations are serialized on the server event loop. A party is independent
of a game team; only the queue policy maps its members to a team.
"""
import asyncio
import time
import uuid
import logging
from dataclasses import dataclass, field

from ..response import Response
from .rating_rules import DOUBLES_QUEUE, DOUBLES_RATING_RULE

log = logging.getLogger(__name__)


@dataclass
class Party:
    party_id: str
    leader_user_id: int
    members: list
    capacity: int = 2
    ready: set = field(default_factory=set)
    revision: int = 0
    queued_at: float = 0
    rating: float = 1500
    selected_queue: str = DOUBLES_QUEUE


class PartyManager:
    INVITE_SECONDS = 60
    RECONNECT_SECONDS = 60
    MAX_MEMBER_GAP = 500

    def __init__(self, server):
        self.server = server
        self.parties = {}
        self.by_user = {}
        self.invites = {}
        self.lock = asyncio.Lock()
        self.match_task = None
        self.disconnect_tokens = {}

    def get(self, uid):
        return self.parties.get(self.by_user.get(uid))

    def multi(self, uid):
        p = self.get(uid)
        return p is not None and len(p.members) > 1

    def committed(self, p):
        return any(self.server.match_manager.is_user_committed(uid) for uid in p.members)

    def busy(self, uid):
        conn = self.server.user_id_to_connection.get(uid)
        mm = self.server.match_manager
        if conn is None or uid in self.disconnect_tokens:
            return '玩家已离线'
        if getattr(conn, 'is_tourist', False):
            return '游客无法组队，请先注册账号'
        if (getattr(conn, 'current_room_id', None)
                or any(uid in r.get('player_list', []) for r in self.server.room_manager.rooms.values())):
            return '请先退出当前房间'
        if (mm.is_user_committed(uid) or self.server.gamestate_manager.is_user_in_active_game(uid)
                or uid in getattr(self.server.room_manager, 'event_seating_users', ())):
            return '玩家正在对局或入座中'
        if mm.is_user_in_queue(uid):
            return '请先取消当前匹配'
        return None

    def snapshot(self, uid):
        p = self.get(uid)
        if p is None:
            return {'party_id': '', 'members': [], 'revision': 0}
        if not p.queued_at and not self.committed(p):
            ratings = [(self.server.db_manager.get_rank_data(member) or {}).get('ratings', {}).get(DOUBLES_RATING_RULE, {}).get('elo', 1500)
                       for member in p.members]
            p.rating = sum(ratings) / len(ratings)
        rows = []
        for member in p.members:
            settings = self.server.db_manager.get_user_settings(member) or {}
            conn = self.server.user_id_to_connection.get(member)
            rows.append(dict(user_id=member, username=settings.get('username') or getattr(conn, 'username', f'用户{member}'),
                profile_image_id=settings.get('profile_image_id', 1),
                avatar_frame_id=settings.get('avatar_frame_id', 0), online=conn is not None and member not in self.disconnect_tokens,
                ready=member in p.ready))
        state = 'in_game' if self.committed(p) else 'queueing' if p.queued_at else 'idle'
        return dict(party_id=p.party_id, leader_user_id=p.leader_user_id, members=rows, capacity=p.capacity,
            revision=p.revision, state=state, selected_queue=p.selected_queue,
            queued_at=p.queued_at, team_elo=p.rating,
            search_range=self.search_range(p.queued_at) if p.queued_at else 100)

    async def send(self, uid, response):
        conn = self.server.user_id_to_connection.get(uid)
        if conn:
            try:
                from ..gamestate.public.connection_delivery import send_bounded
                await send_bounded(conn.websocket, response.dict(exclude_none=True))
                return True
            except Exception:
                log.warning('队伍通知发送失败: %s', uid)
        return False

    async def publish(self, users):
        for uid in dict.fromkeys(users):
            await self.send(uid, Response(type='party/state', success=True, message='', party=self.snapshot(uid),
                **self.server.match_manager.snapshot(uid)))

    async def queue_notice(self, p, uid, started):
        conn = self.server.user_id_to_connection.get(uid)
        name = getattr(conn, 'username', None) or f'用户{uid}'
        message = f'{name} 开始了组队匹配' if started else f'{name} 取消了组队匹配'
        for member in p.members:
            await self.send(member, Response(type='party/notice', success=True, message=message))

    def changed(self, p):
        p.revision += 1

    def create(self, uid):
        p = Party(str(uuid.uuid4()), uid, [uid])
        self.parties[p.party_id] = p
        self.by_user[uid] = p.party_id
        return p

    def cancel_queue(self, p):
        p.queued_at = 0
        p.ready.clear()
        for uid in p.members:
            self.server.match_manager._remove_queues(uid)
        self.changed(p)

    async def handle(self, connect_id, msg):
        conn = self.server.players.get(connect_id)
        uid = getattr(conn, 'user_id', None)
        kind = msg.get('type', '')
        if not uid:
            return Response(type=kind, success=False, message='请先登录')
        async with self.lock:
            # An old connection cannot mutate the replacement login's party.
            if self.server.user_id_to_connection.get(uid) is not conn:
                return Response(type=kind, success=False, message='连接已更新，请重新登录')
            try:
                result = await self._handle(uid, msg)
                return Response(type=kind, success=True, message='', party=self.snapshot(uid),
                    **self.server.match_manager.snapshot(uid), **(result or {}))
            except ValueError as exc:
                return Response(type=kind, success=False, message=str(exc), party=self.snapshot(uid),
                    **self.server.match_manager.snapshot(uid))

    def require(self, condition, message):
        if not condition:
            raise ValueError(message)

    async def _handle(self, uid, msg):
        op = msg['type'].split('/')[-1]
        p = self.get(uid)
        if op == 'get':
            return
        if op == 'invite':
            return await self.invite(uid, msg)
        if op in ('respond', 'revoke'):
            return await self.respond(uid, msg, revoke=op == 'revoke')
        self.require(p is not None, '当前没有队伍')
        self.require(not self.committed(p), '已匹配到对局，请完成当前对局')
        if op == 'cancel':
            was_queued = bool(p.queued_at)
            self.cancel_queue(p)
            if was_queued:
                await self.queue_notice(p, uid, False)
        elif op == 'leave':
            was_queued = bool(p.queued_at)
            self.cancel_queue(p)
            if was_queued:
                await self.queue_notice(p, uid, False)
            self.remove_member(p, uid)
        elif op in ('kick', 'transfer'):
            self.require(p.leader_user_id == uid, '仅队长可以操作')
            self.require(not p.queued_at, '请先取消匹配')
            target = msg.get('target_user_id')
            self.require(type(target) is int and target in p.members and target != uid, '无效的队伍成员')
            if op == 'kick':
                self.remove_member(p, target)
                await self.publish([target])
            else:
                p.leader_user_id = target
                p.ready.clear()
                self.changed(p)
        elif op == 'ready':
            self.require(not p.queued_at, '请先取消匹配')
            self.require(type(msg.get('ready')) is bool, '准备状态无效')
            if msg['ready']:
                self.require(not self.busy(uid), self.busy(uid) or '')
                p.ready.add(uid)
            else:
                p.ready.discard(uid)
            self.changed(p)
        elif op == 'queue':
            await self.enqueue(uid, p)
        else:
            raise ValueError('不支持的队伍操作')
        await self.publish([uid, *p.members])

    def remove_member(self, p, uid):
        p.members.remove(uid)
        self.by_user.pop(uid, None)
        p.ready.clear()
        self.changed(p)
        if not p.members:
            self.parties.pop(p.party_id, None)
        elif p.leader_user_id == uid:
            p.leader_user_id = next((u for u in p.members if u in self.server.user_id_to_connection), p.members[0])

    async def invite(self, uid, msg):
        target = msg.get('target_user_id')
        self.require(type(target) is int and target != uid, '请选择好友')
        self.require(any(int(row['user_id']) == target for row in self.server.db_manager.list_mutual_friends(uid)), '只能邀请好友')
        self.require(target in self.server.user_id_to_connection, '好友已离线')
        self.require(not self.busy(target), self.busy(target) or '')
        kind = msg.get('kind', 'party')
        p = self.get(uid)
        data = dict(kind=kind)
        if kind == 'party':
            self.require(not self.busy(uid), self.busy(uid) or '')
            self.require(self.get(target) is None, '好友已经加入其他队伍')
            p = p or self.create(uid)
            self.require(p.leader_user_id == uid and len(p.members) < p.capacity, '仅队长可邀请，且队伍需要有空位')
            data['party_id'] = p.party_id
        elif kind == 'room':
            self.require(isinstance(msg.get('room_id'), str), '房间编号无效')
            room = self.server.room_manager.rooms.get(msg.get('room_id'))
            self.require(room is not None and uid in room.get('player_list', []), '请先加入邀请的房间')
            self.require(msg.get('room_instance_id', room['instance_id']) == room['instance_id'], '房间邀请已失效，请重新打开邀请面板')
            self.require(not room.get('is_game_running') and not room.get('admin_closing'), '房间正在对局或关闭中')
            self.require(not self.multi(target), '好友需要先离开当前队伍')
            data.update(room_id=room['room_id'], room_instance_id=room['instance_id'])
        else:
            raise ValueError('邀请类型无效')
        now = time.time()
        self.invites = {key: value for key, value in self.invites.items() if value['expires_at'] > now}
        for value in self.invites.values():
            self.require(not (value['from_user_id'] == uid and value['to_user_id'] == target and value['kind'] == kind), '已发送邀请，请等待回应')
        self.require(sum(v['from_user_id'] == uid for v in self.invites.values()) < 10, '待处理邀请过多')
        settings = self.server.db_manager.get_user_settings(uid) or {}
        data.update(invite_id=str(uuid.uuid4()), from_user_id=uid, to_user_id=target,
            from_username=settings.get('username', f'用户{uid}'), expires_at=now + self.INVITE_SECONDS)
        self.invites[data['invite_id']] = data
        delivered = await self.send(target, Response(type='party/incoming', success=True, message='', party_invite=data))
        if not delivered:
            self.invites.pop(data['invite_id'], None)
            raise ValueError('邀请发送失败，好友可能已经离线')
        if p:
            await self.publish(p.members)
        return dict(party_invite=data)

    async def respond(self, uid, msg, revoke=False):
        self.require(isinstance(msg.get('invite_id'), str), '邀请编号无效')
        invite = self.invites.get(msg.get('invite_id'))
        self.require(invite is not None, '邀请已失效')
        self.require(uid == invite['from_user_id'] if revoke else uid == invite['to_user_id'], '无权处理此邀请')
        self.require(invite['expires_at'] > time.time(), '邀请已过期')
        if revoke or msg.get('accept') is False:
            self.invites.pop(invite['invite_id'], None)
            invite = {**invite, 'status': 'revoked' if revoke else 'declined'}
            await self.send(invite['to_user_id'] if revoke else invite['from_user_id'],
                Response(type='party/invite_closed', success=True, message='邀请已撤销' if revoke else '好友拒绝了邀请', party_invite=invite))
            return dict(party_invite=invite)
        self.require(msg.get('accept') is True, '邀请回应无效')
        self.require(not self.busy(uid), self.busy(uid) or '')
        if invite['kind'] == 'party':
            self.require(self.get(uid) is None, '请先离开当前队伍')
            p = self.parties.get(invite['party_id'])
            self.require(p is not None and p.leader_user_id == invite['from_user_id'], '队伍邀请已失效')
            self.require(not p.queued_at and not self.committed(p) and len(p.members) < p.capacity, '队伍已满或正在匹配')
            for member in p.members:
                self.require(not self.busy(member), self.busy(member) or '')
            p.members.append(uid)
            self.by_user[uid] = p.party_id
            p.ready.clear()
            self.changed(p)
            await self.publish(p.members)
        else:
            from ..room.room_invites import matching_room
            room = matching_room(self.server.room_manager, invite['room_id'], invite['room_instance_id'])
            self.require(room is not None and invite['from_user_id'] in room.get('player_list', []), '房间邀请已失效')
            self.require(not self.multi(uid), '请先离开当前队伍')
            # The client resolves this lifetime-bound identity through the normal
            # room invitation/password flow. Never expose a room password here.
        self.invites.pop(invite['invite_id'], None)
        await self.send(invite['from_user_id'], Response(type='party/invite_closed', success=True,
            message='', party_invite={**invite, 'status': 'accepted'}))
        return dict(party_invite={**invite, 'status': 'accepted'})

    @staticmethod
    def search_range(queued_at, now=None):
        elapsed = (time.time() if now is None else now) - queued_at
        return 100 if elapsed < 30 else 200 if elapsed < 60 else 350 if elapsed < 120 else 500

    async def enqueue(self, uid, p):
        self.require(uid in p.members, '当前不在该队伍中')
        if p.queued_at:
            return
        self.require(len(p.members) == 2, '进入2v2匹配需要邀请好友组队')
        ratings = []
        for member in p.members:
            self.require(not self.busy(member), self.busy(member) or '')
            rank = self.server.db_manager.get_rank_data(member)
            self.require(rank is not None, '无法读取双打评分，请稍后重试')
            ratings.append((rank.get('ratings', {}).get(DOUBLES_RATING_RULE) or {}).get('elo', 1500))
        self.require(abs(ratings[0] - ratings[1]) <= self.MAX_MEMBER_GAP, '队内双打 Elo 分差不能超过 500')
        # Membership already blocks individual queues/room entry. Recheck after
        # spectator cleanup yields, including the connection epoch.
        conns = [self.server.user_id_to_connection.get(u) for u in p.members]
        for member in p.members:
            await self.server.gamestate_manager.remove_spectator_from_all_games(member)
        for member, conn in zip(p.members, conns):
            self.require(self.server.user_id_to_connection.get(member) is conn and not self.busy(member), '成员状态已变化，请重新准备')
        p.rating = sum(ratings) / 2
        p.ready.update(p.members)
        p.queued_at = time.time()
        self.changed(p)
        mm = self.server.match_manager
        for member in p.members:
            mm.queues[DOUBLES_QUEUE].append(member)
            mm.user_to_queues[member] = [DOUBLES_QUEUE]
            mm._changed(member)
        await self.queue_notice(p, uid, True)
        await self.try_match()
        if self.match_task is None or self.match_task.done():
            self.match_task = asyncio.create_task(self.match_loop())

    async def try_match(self):
        # A disconnect can arrive while another request awaits delivery. Drop
        # incomplete reservations before selecting whole teams.
        for p in list(self.parties.values()):
            if p.queued_at and any(u not in self.server.user_id_to_connection or u in self.disconnect_tokens for u in p.members):
                self.cancel_queue(p)
        waiting = sorted((p for p in self.parties.values() if p.queued_at), key=lambda p: p.queued_at)
        for a in waiting:
            if not a.queued_at:
                continue
            candidates = [b for b in waiting if b is not a and b.queued_at
                and abs(a.rating - b.rating) <= min(self.search_range(a.queued_at), self.search_range(b.queued_at))]
            if not candidates:
                continue
            b = min(candidates, key=lambda p: (abs(a.rating - p.rating), p.queued_at))
            users = [a.members[0], b.members[0], a.members[1], b.members[1]]
            a.queued_at = b.queued_at = 0
            self.changed(a); self.changed(b)
            await self.server.match_manager._try_start_match(DOUBLES_QUEUE, users)
            await self.publish(users)

    async def match_loop(self):
        while any(p.queued_at for p in self.parties.values()):
            await asyncio.sleep(5)
            async with self.lock:
                await self.try_match()
                await self.publish([u for p in self.parties.values() if p.queued_at for u in p.members])

    def disconnected(self, uid):
        p = self.get(uid)
        if p is None:
            return
        if not self.committed(p):
            self.cancel_queue(p)
        token = object()
        self.disconnect_tokens[uid] = token
        asyncio.create_task(self.publish(p.members))
        asyncio.create_task(self.expire_member(uid, token))

    async def expire_member(self, uid, token):
        await asyncio.sleep(self.RECONNECT_SECONDS)
        async with self.lock:
            if self.disconnect_tokens.get(uid) is not token or uid in self.server.user_id_to_connection:
                return
            self.disconnect_tokens.pop(uid, None)
            p = self.get(uid)
            if p and not self.committed(p):
                self.remove_member(p, uid)
                await self.publish(p.members)

    async def reconnected(self, uid):
        self.disconnect_tokens.pop(uid, None)
        p = self.get(uid)
        await self.publish(p.members if p else [uid])

    async def released(self, users):
        async with self.lock:
            for pid in {self.by_user.get(u) for u in users} - {None}:
                p = self.parties.get(pid)
                if p:
                    p.ready.clear()
                    for uid in list(p.members):
                        if uid not in self.server.user_id_to_connection:
                            self.remove_member(p, uid)
                    self.changed(p)
                    await self.publish(p.members)
