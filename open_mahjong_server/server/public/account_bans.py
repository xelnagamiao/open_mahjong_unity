"""Account restrictions shared by matchmaking, game sessions and chat."""
import asyncio
import logging

RANKED_BANS = ('match', 'login', 'full')
CHAT_BANS = ('chat', 'login', 'full')
RANKED_MESSAGE = '账号当前已被禁止排位'


async def enforce_online_bans(server):
    """Also revoke waiting reservations and idle sessions after an admin edit."""
    while True:
        try:
            online = dict(server.user_id_to_connection)
            bans = await asyncio.to_thread(server.db_manager.active_account_bans, list(online), RANKED_BANS)
            for uid, ban in bans.items():
                if server.user_id_to_connection.get(uid) is not online[uid]:
                    continue
                if ban['ban_type'] == 'match':
                    async with server.party_manager.lock:
                        await server.match_manager.cancel_banned_queue(uid)
                else:
                    await server.kick_user_by_id(uid, server.db_manager.build_login_ban_message(ban))
                    secret = getattr(getattr(server, 'chat_server', None), 'secret_key', '')
                    if secret:
                        from ..chat_server.access import notify_chat_revoke
                        asyncio.create_task(asyncio.to_thread(notify_chat_revoke, secret, uid, True, 'account_banned'))
        except Exception:
            logging.exception('同步在线账号封禁状态失败')
        await asyncio.sleep(3)
