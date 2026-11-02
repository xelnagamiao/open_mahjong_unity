"""Private/venue chat uses the game session so clients cannot grant membership."""
import asyncio
import logging

from ..database import chat_conversations as store
from ..gamestate.public.connection_delivery import send_bounded

logger = logging.getLogger(__name__)


async def handle_conversation_message(game_server, connect_id, message, websocket):
    type_name = str(message.get('type') or '')
    request_id = str(message.get('request_id') or '')[:64]
    player = game_server.players.get(connect_id)
    uid = getattr(player, 'user_id', None)
    response = dict(type=type_name, request_id=request_id, user_id=uid or 0, success=False, message='请先登录')
    if not uid or getattr(player, 'websocket', None) is not websocket or game_server.user_id_to_connection.get(uid) is not player:
        await send_bounded(websocket, response)
        return
    db = game_server.db_manager
    try:
        payload = {}
        recipients = []
        key = message.get('conversation_key')
        if type_name == 'chat/list':
            payload['conversations'] = await asyncio.to_thread(store.list_conversations, db, uid)
        elif type_name == 'chat/venues':
            payload['venues'] = await asyncio.to_thread(store.list_chat_venues, db, uid)
        elif type_name == 'chat/open_direct':
            payload = await asyncio.to_thread(store.open_conversation, db, uid, peer_user_id=message.get('peer_user_id'))
        elif type_name == 'chat/open_venue':
            payload = await asyncio.to_thread(store.open_conversation, db, uid, event_id=message.get('event_id'))
        elif type_name == 'chat/history':
            payload = await asyncio.to_thread(store.history, db, uid, key, message.get('before_id', 0))
        elif type_name == 'chat/send':
            payload = await asyncio.to_thread(store.send_message, db, uid, key, message.get('content'), request_id)
            all_recipients = payload.pop('recipient_ids')
            if payload.pop('inserted'):
                recipients = [recipient for recipient in all_recipients if recipient != uid]
        elif type_name == 'chat/read':
            payload['conversation'] = await asyncio.to_thread(store.mark_read, db, uid, key, message.get('last_message_id', 0))
        elif type_name == 'chat/remove':
            payload['removed_key'] = await asyncio.to_thread(store.remove_conversation, db, uid, key)
        else:
            raise store.ChatError('未知的聊天请求')
        response.update(success=True, message='ok', **payload)
        await send_bounded(websocket, response)
        if recipients:
            await asyncio.gather(*(_deliver(game_server, recipient, payload['chat_message']) for recipient in recipients))
    except store.ChatError as exc:
        response.update(success=False, message=str(exc))
        await send_bounded(websocket, response)
    except Exception:
        logger.exception('Conversation request failed: type=%s user_id=%s', type_name, uid)
        await send_bounded(websocket, dict(type=type_name, request_id=request_id, user_id=uid, success=False, message='聊天请求失败，请稍后重试'))


async def _deliver(game_server, recipient, message):
    connection = game_server.user_id_to_connection.get(recipient)
    if connection is None:
        return
    try:
        summary = await asyncio.to_thread(store.recipient_summary, game_server.db_manager, recipient, message['conversation_key'])
        if summary is None or game_server.user_id_to_connection.get(recipient) is not connection:
            return
        await send_bounded(connection.websocket, dict(type='chat/message', request_id='', user_id=recipient,
                           success=True, message='ok', chat_message=message, conversation=summary))
    except store.ChatError:
        # Recheck membership after commit; removed members receive no new payload.
        return
    except Exception:
        logger.warning('Chat delivery deferred to history: user_id=%s', recipient, exc_info=True)
