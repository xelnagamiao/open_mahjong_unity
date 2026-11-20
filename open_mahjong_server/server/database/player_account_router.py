"""Web account access to the same mail and inventory used by the game client."""
import asyncio
import logging

from fastapi import HTTPException, Request
from .inventory import InventoryError, get_inventory, mutate_inventory
from .inventory_router import _lock, _sync
from .mail import list_mail, mail_summary, read_mail, claim_mail
from .redemption_router import _token, _verify_player
from .reward_receipts import notify_reward

logger = logging.getLogger(__name__)


def register_player_account_routes(app, server):
    async def owner(request):
        return await asyncio.to_thread(_verify_player, _token(request))

    @app.get('/player/mail/summary', operation_id='account_mail_summary')
    async def summary(request: Request):
        uid = await owner(request)
        return dict(success=True, data=await asyncio.to_thread(mail_summary, server.db_manager, uid))

    @app.get('/player/mail', operation_id='account_mail_list')
    async def inbox(request: Request, page: int = 1, filter: str = 'all'):
        uid = await owner(request)
        if page < 1 or page > 100000 or filter not in ('all', 'unread', 'rewards'):
            raise HTTPException(status_code=400, detail='邮件筛选参数无效')
        result = await asyncio.to_thread(list_mail, server.db_manager, uid,
                                         limit=20, offset=(page - 1) * 20, filter_by=filter)
        return dict(success=True, data=dict(result, page=page, page_size=20))

    @app.post('/player/mail/{mail_id}/{action}', operation_id='account_mail_action')
    async def mail_action(mail_id: int, action: str, request: Request):
        uid = await owner(request)
        operation = {'read': read_mail, 'claim': claim_mail}.get(action)
        if operation is None:
            raise HTTPException(status_code=404, detail='邮件操作不存在')
        try:
            async with _lock(server):
                result = await asyncio.to_thread(operation, server.db_manager, uid, mail_id)
            if action == 'claim' and not result.get('already_claimed'):
                try:
                    await notify_reward(server, result)
                except Exception:
                    logger.exception('Web 邮件附件已领取，在线同步失败')
            return dict(success=True, data=result)
        except InventoryError as error:
            raise HTTPException(status_code=error.status, detail=str(error))

    @app.get('/player/inventory', operation_id='account_inventory_get')
    async def inventory(request: Request):
        uid = await owner(request)
        try:
            return dict(success=True, data=await asyncio.to_thread(get_inventory, server.db_manager, uid))
        except InventoryError as error:
            raise HTTPException(status_code=error.status, detail=str(error))

    @app.post('/player/inventory/{action}', operation_id='account_inventory_action')
    async def inventory_action(action: str, request: Request):
        uid = await owner(request)
        if action not in ('equip', 'use'):
            raise HTTPException(status_code=404, detail='物品操作不存在')
        try:
            body = await request.json()
            if not isinstance(body, dict):
                raise InventoryError('请求须为对象')
            # Actor and recipient come only from authentication, never the request body.
            body = {key: body[key] for key in ('request_id', 'item_id', 'slot', 'quantity') if key in body}
            async with _lock(server):
                result = await asyncio.to_thread(mutate_inventory, server.db_manager, uid, action, body)
            try:
                await _sync(server, [uid])
                await notify_reward(server, result)
            except Exception:
                logger.exception('Web 物品操作已保存，在线同步失败')
            return dict(success=True, data=result)
        except InventoryError as error:
            raise HTTPException(status_code=error.status, detail=str(error))
        except (ValueError, TypeError):
            raise HTTPException(status_code=400, detail='物品参数无效')
