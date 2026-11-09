"""Authenticated shop purchases, wallet reads and Web administrator operations."""
import asyncio
import logging

from .commerce import get_shop, get_wallet, purchase
from .inventory import InventoryError
from .inventory_router import _send

logger = logging.getLogger(__name__)


async def handle_commerce_message(server, connection_id, message, websocket):
    kind = message.get('type')
    request_id = str(message.get('request_id', ''))[:64]
    response = dict(type=kind, request_id=request_id, success=False)
    try:
        player = server.players.get(connection_id)
        if not player or not player.user_id:
            raise InventoryError('请先登录后再打开商店', 401)
        # Identity always comes from the authenticated connection, never message.user_id.
        if kind == 'shop/get':
            result = await asyncio.to_thread(get_shop, server.db_manager, player.user_id)
        elif kind == 'wallet/get':
            result = await asyncio.to_thread(get_wallet, server.db_manager, player.user_id,
                                             before=message.get('before_entry_id'))
        elif kind == 'shop/purchase':
            body = {key: message[key] for key in ('request_id', 'offer_id', 'quantity', 'price_version') if key in message}
            result = await asyncio.to_thread(purchase, server.db_manager, player.user_id, body)
        else:
            raise InventoryError('未知的商店操作')
        response.update(result, success=True)
    except InventoryError as error:
        response.update(message=str(error), commerce_status=error.status)
    except Exception:
        logger.exception('商店请求失败')
        response.update(message='商店服务暂不可用，请重新打开商店或重试原购买', commerce_retryable=True)
    await _send(websocket, response)


def register_commerce_routes(app, server):
    from fastapi import HTTPException, Request
    from psycopg2 import IntegrityError
    from .commerce_admin import get_admin_shop, get_shop_orders, save_offer
    from .inventory_router import _verify_admin

    @app.get('/admin/shop/{path:path}', operation_id='shop_admin_get')
    @app.post('/admin/shop/{path:path}', operation_id='shop_admin_post')
    @app.patch('/admin/shop/{path:path}', operation_id='shop_admin_patch')
    async def shop_admin(path: str, request: Request):
        token = request.headers.get('authorization', '')
        if not token.startswith('Bearer ') or len(token) > 4096:
            raise HTTPException(status_code=401, detail='需要管理员身份')
        actor = await asyncio.to_thread(_verify_admin, token)
        try:
            parts = path.strip('/').split('/')
            if request.method == 'GET':
                if path == 'offers':
                    data = await asyncio.to_thread(get_admin_shop, server.db_manager)
                elif path == 'orders':
                    query = request.query_params
                    data = await asyncio.to_thread(get_shop_orders, server.db_manager,
                        page=int(query.get('page', 1)), limit=int(query.get('limit', 30)),
                        user_id=int(query['user_id']) if query.get('user_id') else None,
                        offer_id=query.get('offer_id'), order_id=query.get('order_id'),
                        from_time=query.get('from_time'), to_time=query.get('to_time'))
                else:
                    raise HTTPException(status_code=404, detail='接口不存在')
            elif ((request.method == 'POST' and path == 'offers') or
                    (request.method == 'PATCH' and len(parts) == 2 and parts[0] == 'offers')):
                body = await request.json()
                if not isinstance(body, dict):
                    raise InventoryError('请求须为对象')
                data = await asyncio.to_thread(save_offer, server.db_manager, actor,
                                              None if request.method == 'POST' else parts[1], body)
            else:
                raise HTTPException(status_code=404, detail='接口不存在')
            return dict(success=True, data=data)
        except InventoryError as error:
            raise HTTPException(status_code=error.status, detail=str(error))
        except (ValueError, TypeError):
            raise HTTPException(status_code=400, detail='请求参数无效')
        except IntegrityError:
            raise HTTPException(status_code=409, detail='商品编号已存在或关联物品无效')
