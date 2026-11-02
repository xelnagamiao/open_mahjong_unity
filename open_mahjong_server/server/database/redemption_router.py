"""Authenticated transports for redemption; caller-supplied user IDs are never trusted."""
import asyncio
import json
import logging
import os
from urllib.error import HTTPError, URLError
from urllib.request import Request as UrlRequest, urlopen

from fastapi import HTTPException, Request
from psycopg2 import IntegrityError
from .inventory import InventoryError
from .inventory_router import _admin_auth_url, _verify_admin, _send
from .redemption import (redeem_code, check_redemption_rate, list_campaigns, save_campaign,
                         set_campaign_status, export_codes, list_redemptions)
from .reward_receipts import notify_reward

logger = logging.getLogger(__name__)


def _verify_player(token):
    url = os.environ.get('REDEEM_PLAYER_AUTH_URL') or _admin_auth_url().replace('/admin/auth/me','/player/auth/me')
    try:
        with urlopen(UrlRequest(url,headers={'Authorization':token}),timeout=4) as response:
            data = json.load(response)
        uid = data['data']['user_id']
        if isinstance(uid,str) and uid.isascii() and uid.isdigit():
            uid = int(uid)
        if data.get('success') is not True or type(uid) is not int or uid < 11:
            raise ValueError()
        return uid
    except HTTPError as error:
        raise HTTPException(status_code=error.code if error.code in (401,403,429) else 503,detail='玩家身份验证失败')
    except (URLError,TimeoutError,ValueError,KeyError,TypeError):
        raise HTTPException(status_code=503,detail='登录认证服务暂不可用')


async def claim(server, uid, body, ip):
    await asyncio.to_thread(check_redemption_rate,server.db_manager,uid,ip)
    return await asyncio.to_thread(redeem_code,server.db_manager,uid,body)


async def handle_redemption_message(server,connection_id,message,websocket):
    response = dict(type='reward/redeem',request_id=str(message.get('request_id',''))[:64],success=False)
    try:
        player = server.players.get(connection_id)
        if not player or not player.user_id:
            raise InventoryError('请先登录后再兑换',401)
        if message.get('type') != 'reward/redeem':
            raise InventoryError('未知的奖励操作')
        ip = getattr(getattr(websocket,'client',None),'host','unknown')
        result = await claim(server,player.user_id,message,ip)
        response.update(result,success=True)
    except InventoryError as error:
        response.update(message=str(error),reward_retryable=error.status in (429,503))
    except Exception:
        logger.exception('兑换处理失败（不记录兑换码）')
        response.update(message='兑换服务暂不可用，请重试原操作',reward_retryable=True)
    await _send(websocket,response)


def _token(request):
    token = request.headers.get('authorization','')
    if not token.startswith('Bearer ') or len(token)>4096:
        raise HTTPException(status_code=401,detail='需要登录')
    return token


def register_redemption_routes(app,server):
    @app.post('/player/redeem',operation_id='reward_redeem')
    async def redeem(request:Request):
        uid = await asyncio.to_thread(_verify_player,_token(request))
        try:
            # The authenticated gateway owns HTTP IP limiting. Treating its
            # loopback address as a player would throttle the entire website.
            result = await claim(server,uid,await request.json(),None)
            await notify_reward(server,result)
            return dict(success=True,data=result)
        except InventoryError as error:
            raise HTTPException(status_code=error.status,detail=str(error))
        except (ValueError,TypeError):
            raise HTTPException(status_code=400,detail='请求参数无效')

    @app.get('/admin/redemption/{path:path}',operation_id='redemption_admin_get')
    @app.post('/admin/redemption/{path:path}',operation_id='redemption_admin_post')
    @app.patch('/admin/redemption/{path:path}',operation_id='redemption_admin_patch')
    async def admin(path:str,request:Request):
        actor = await asyncio.to_thread(_verify_admin,_token(request))
        parts = path.strip('/').split('/')
        try:
            if request.method == 'GET':
                page = int(request.query_params.get('page','1'))
                if path == 'campaigns':
                    data = await asyncio.to_thread(list_campaigns,server.db_manager,page)
                elif len(parts)==3 and parts[0]=='campaigns' and parts[2]=='claims':
                    data = await asyncio.to_thread(list_redemptions,server.db_manager,int(parts[1]),page)
                else:
                    raise HTTPException(status_code=404,detail='接口不存在')
            else:
                body = await request.json()
                if not isinstance(body,dict):
                    raise InventoryError('请求须为对象')
                if path=='campaigns' and request.method=='POST':
                    data = await asyncio.to_thread(save_campaign,server.db_manager,actor,body)
                elif len(parts)==2 and parts[0]=='campaigns' and request.method=='PATCH':
                    data = await asyncio.to_thread(save_campaign,server.db_manager,actor,body,int(parts[1]))
                elif len(parts)==3 and parts[0]=='campaigns' and parts[2]=='status' and request.method=='POST':
                    data = await asyncio.to_thread(set_campaign_status,server.db_manager,actor,int(parts[1]),body.get('status'),body.get('reason'))
                elif len(parts)==3 and parts[0]=='campaigns' and parts[2]=='export' and request.method=='POST':
                    data = await asyncio.to_thread(export_codes,server.db_manager,actor,int(parts[1]))
                else:
                    raise HTTPException(status_code=404,detail='接口不存在')
            return dict(success=True,data=data)
        except InventoryError as error:
            raise HTTPException(status_code=error.status,detail=str(error))
        except (ValueError,TypeError):
            raise HTTPException(status_code=400,detail='请求参数无效')
        except IntegrityError:
            raise HTTPException(status_code=409,detail='兑换码或操作编号已存在，请勿重复创建')
