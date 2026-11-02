"""Mail transport reuses login sessions and the existing Web administrator auth."""
import asyncio
import logging

from fastapi import HTTPException, Request
from psycopg2 import IntegrityError
from .inventory import InventoryError
from .inventory_router import _lock, _verify_admin
from .mail import claim_mail, list_mail, read_mail, send_mail

logger = logging.getLogger(__name__)


async def handle_mail_message(server, connection_id, message, websocket):
    kind = message.get('type')
    response = dict(type=kind, request_id=str(message.get('request_id', ''))[:64], success=False)
    try:
        player = server.players.get(connection_id)
        if not player or not player.user_id:
            raise InventoryError('请先登录后再打开邮件', 401)
        async with _lock(server):
            if kind == 'mail/list':
                result = await asyncio.to_thread(list_mail, server.db_manager, player.user_id)
            elif kind == 'mail/read':
                result = await asyncio.to_thread(read_mail, server.db_manager, player.user_id, message.get('mail_id'))
            elif kind == 'mail/claim':
                result = await asyncio.to_thread(claim_mail, server.db_manager, player.user_id, message.get('mail_id'))
            else:
                raise InventoryError('未知的邮件操作')
        response.update(result, success=True)
    except InventoryError as error:
        response['message'] = str(error)
    except Exception:
        logger.exception('邮件操作失败')
        response.update(mail_retryable=True, message='邮件服务暂不可用，请稍后重试')
    await websocket.send_json(response)


def register_mail_routes(app, server):
    async def admin(request):
        token = request.headers.get('authorization', '')
        if not token.startswith('Bearer ') or len(token) > 4096:
            raise HTTPException(status_code=401, detail='需要管理员身份')
        return await asyncio.to_thread(_verify_admin, token)

    @app.post('/admin/mail', operation_id='player_mail_send')
    async def deliver(request: Request):
        actor = await admin(request)
        try:
            body = await request.json()
            async with _lock(server):
                result = await asyncio.to_thread(send_mail, server.db_manager, actor, body)
            synced = False
            connection = server.user_id_to_connection.get(body['user_id'])
            if connection and not result['duplicate']:
                try:
                    await connection.websocket.send_json(dict(type='mail/changed', success=True))
                    synced = True
                except Exception:
                    logger.debug('邮件已保存，收件人已离线')
            return dict(success=True, data=result, synced_online=synced)
        except InventoryError as error:
            raise HTTPException(status_code=error.status, detail=str(error))
        except (ValueError, TypeError):
            raise HTTPException(status_code=400, detail='邮件参数无效')
        except IntegrityError:
            raise HTTPException(status_code=409, detail='投递编号已使用或关联数据无效')

    @app.get('/admin/mail/users/{user_id}', operation_id='player_mail_admin_list')
    async def inspect(user_id: int, request: Request):
        await admin(request)
        try:
            return dict(success=True, data=await asyncio.to_thread(list_mail, server.db_manager, user_id))
        except InventoryError as error:
            raise HTTPException(status_code=error.status, detail=str(error))
