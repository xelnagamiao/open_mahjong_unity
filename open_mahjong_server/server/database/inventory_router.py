"""Inventory transport: private balances, public appearances, authenticated actors."""
import asyncio
import logging
import os
import json
import re
from pathlib import Path
from urllib.request import Request as UrlRequest, urlopen
from urllib.error import HTTPError, URLError

from fastapi import HTTPException, Request
from psycopg2 import IntegrityError
from .inventory import (InventoryError, get_inventory, get_catalog, get_admin_catalog,
                        mutate_inventory, save_definition, get_ledger)

logger = logging.getLogger(__name__)
WEB_ENV_PATH = Path(__file__).resolve().parents[3] / 'open_mahjong_web' / '.env'


def _lock(server):
    if not hasattr(server,'inventory_lock'): server.inventory_lock = asyncio.Lock()
    return server.inventory_lock


async def _send(socket,payload):
    try: await socket.send_json(payload)
    except Exception: logger.debug('背包同步目标已断开',exc_info=True)


def _candidates(server):
    return {uid for uid in set(server.user_id_to_connection) | set(server.gamestate_manager.user_id_to_game_state) if uid > 10}


async def _sync(server,user_ids=None):
    targets = _candidates(server)
    if user_ids is not None: targets.intersection_update(user_ids)
    appearances=[]
    for uid in sorted(targets):
        state=await asyncio.to_thread(get_inventory,server.db_manager,uid)
        appearance=state['appearance']
        appearances.append(appearance)
        for room in server.room_manager.rooms.values():
            for key in (uid,str(uid)):
                if key in room.get('player_settings',{}):
                    room['player_settings'][key].update(appearance)
        game=server.gamestate_manager.get_game_state_by_user_id(uid)
        if game:
            for player in getattr(game,'player_list',[]):
                if player.user_id == uid:
                    player.character_used=appearance['character_id']
                    player.voice_used=appearance['voice_id']
                    player.profile_used=appearance['profile_image_id']
                    player.avatar_frame_used=appearance['avatar_frame_id']
        connection=server.user_id_to_connection.get(uid)
        if connection: await _send(connection.websocket,{'type':'inventory/state','success':True,'inventory_state':state})
    catalog=await asyncio.to_thread(get_catalog,server.db_manager)
    payload={'type':'inventory/update','success':True,'inventory_catalog':catalog,'inventory_appearances':appearances}
    await asyncio.gather(*(_send(p.websocket,payload) for p in list(server.players.values())))


async def handle_inventory_message(server,connection_id,message,websocket):
    kind=message.get('type')
    request_id=message.get('request_id','')
    response={'type':kind,'request_id':str(request_id)[:64],'success':False}
    try:
        async with _lock(server):
            if kind == 'inventory/catalog':
                response.update(success=True,inventory_catalog=await asyncio.to_thread(get_catalog,server.db_manager))
            else:
                player=server.players.get(connection_id)
                if not player or not player.user_id: raise InventoryError('请先登录后再打开背包',401)
                if kind == 'inventory/get':
                    response.update(success=True,inventory_state=await asyncio.to_thread(get_inventory,server.db_manager,player.user_id))
                elif kind in ('inventory/equip','inventory/use'):
                    body={key:message[key] for key in ('request_id','item_id','slot','quantity') if key in message}
                    result=await asyncio.to_thread(mutate_inventory,server.db_manager,player.user_id,kind.split('/')[1],body)
                    response.update(success=True,inventory_state=result['inventory_state'],message=result['message'])
                else: raise InventoryError('未知的背包操作')
            await _send(websocket,response)
            if kind in ('inventory/equip','inventory/use'):
                try: await _sync(server,[player.user_id])
                except Exception: logger.exception('背包操作已保存，在线外观通知失败')
            return
    except InventoryError as error: response['message']=str(error)
    except Exception:
        logger.exception('背包操作失败')
        response['inventory_retryable']=True
        response['message']='背包服务暂不可用，请稍后刷新确认状态'
    await _send(websocket,response)


def _admin_auth_url():
    explicit = os.environ.get('INVENTORY_ADMIN_AUTH_URL', '').strip()
    if explicit:
        return explicit
    # Node's deployment port lives in its own .env (3000 locally, 8082 on the
    # shared host). Never derive an authentication destination from a request.
    port = '3000'
    try:
        for line in WEB_ENV_PATH.read_text(encoding='utf-8-sig').splitlines():
            match = re.match(r'^\s*(?:export\s+)?PORT\s*=\s*(.*?)\s*$', line)
            if match:
                value = match.group(1).split('#', 1)[0].strip().strip('\"\'')
                if not value.isascii() or not value.isdigit() or not 1 <= int(value) <= 65535:
                    raise HTTPException(status_code=503, detail='物品服务的 Web 端口配置无效')
                port = str(int(value))
    except FileNotFoundError:
        pass
    except OSError as error:
        logger.warning('无法读取物品管理认证端口配置: %s', type(error).__name__)
        raise HTTPException(status_code=503, detail='物品管理认证配置不可用') from error
    return f'http://127.0.0.1:{port}/api/admin/auth/me'


def _verify_admin(token):
    # Reuse Node's existing admin authentication instead of maintaining another
    # signing secret or trusting a caller-supplied administrator ID.
    url = _admin_auth_url()
    try:
        with urlopen(UrlRequest(url,headers={'Authorization':token}),timeout=4) as response:
            data=json.load(response)
        actor = data['data']['user_id']
        if data.get('success') is not True or type(actor) is not int or actor <= 0:
            raise ValueError('invalid administrator response')
        return actor
    except HTTPError as error: raise HTTPException(status_code=error.code if error.code in (401,403,429) else 503,detail='管理员身份验证失败')
    except (URLError,TimeoutError,ValueError,KeyError,TypeError):
        logger.warning('物品管理认证回查失败，请检查 Web 端口或 INVENTORY_ADMIN_AUTH_URL')
        raise HTTPException(status_code=503,detail='管理认证服务暂不可用')


def register_inventory_routes(app,server):
    async def admin(request):
        token=request.headers.get('authorization','')
        if not token.startswith('Bearer ') or len(token)>4096: raise HTTPException(status_code=401,detail='需要管理员身份')
        return await asyncio.to_thread(_verify_admin,token)

    @app.get('/admin/inventory/{path:path}',operation_id='inventory_admin_get')
    @app.post('/admin/inventory/{path:path}',operation_id='inventory_admin_post')
    @app.patch('/admin/inventory/{path:path}',operation_id='inventory_admin_patch')
    async def inventory_admin(path:str,request:Request):
        actor=await admin(request)
        try:
            parts=path.strip('/').split('/')
            async with _lock(server):
                if request.method=='GET':
                    if path=='catalog': data=await asyncio.to_thread(get_admin_catalog,server.db_manager)
                    elif len(parts)==2 and parts[0]=='users': data=await asyncio.to_thread(get_inventory,server.db_manager,int(parts[1]))
                    elif len(parts)==3 and parts[0]=='users' and parts[2]=='ledger': data=await asyncio.to_thread(get_ledger,server.db_manager,int(parts[1]))
                    else: raise HTTPException(status_code=404,detail='接口不存在')
                    return {'success':True,'data':data}
                body=await request.json()
                if not isinstance(body,dict): raise InventoryError('请求须为对象')
                if (request.method=='POST' and path=='catalog') or (request.method=='PATCH' and len(parts)==2 and parts[0]=='catalog'):
                    result=await asyncio.to_thread(save_definition,server.db_manager,actor,None if request.method=='POST' else int(parts[1]),body)
                    targets=None  # Every online inventory needs the revised catalog.
                    data=result['item']
                elif request.method=='POST' and len(parts)==3 and parts[0]=='users' and parts[2] in ('grant','revoke'):
                    uid=int(parts[1])
                    data=await asyncio.to_thread(mutate_inventory,server.db_manager,uid,parts[2],body,actor)
                    targets=[uid]
                else: raise HTTPException(status_code=404,detail='接口不存在')
                synced=True
                try: await _sync(server,targets)
                except Exception:
                    synced=False
                    logger.exception('物品已保存，在线通知失败')
                return {'success':True,'data':data,'synced_online':synced}
        except InventoryError as error: raise HTTPException(status_code=error.status,detail=str(error))
        except (ValueError,TypeError): raise HTTPException(status_code=400,detail='请求参数无效')
        except IntegrityError: raise HTTPException(status_code=409,detail='编码已存在或关联数据无效')


def attach_game_frames(game_state,room_data):
    for player in game_state.player_list:
        settings=room_data.get('player_settings',{}).get(player.user_id) or room_data.get('player_settings',{}).get(str(player.user_id)) or {}
        player.avatar_frame_used=settings.get('avatar_frame_id',0)
