"""Legacy rule room creators extracted from RoomManager with shared host/finalization helpers."""
from __future__ import annotations

import logging
from typing import Any, Optional
import uuid

from ..response import Response
from ..database.duplicate_walls import apply_duplicate_room_config
from ..gamestate.public.player_count import player_count_for_sub_rule

logger = logging.getLogger(__name__)


def _build_host_profile(player: Any, host_settings: dict) -> dict:
    return {
        "user_id": player.user_id,
        "username": host_settings.get("username", player.username),
        "title_id": host_settings.get("title_id", 1),
        "profile_image_id": host_settings.get("profile_image_id", 1),
        "character_id": host_settings.get("character_id", 1),
        "voice_id": host_settings.get("voice_id", 1),
    }


def _prepare_host(manager, player_id: str, *, event_id: Optional[str] = None,
                  check_event: bool = True, load_settings: bool = True):
    if player_id not in manager.game_server.players:
        return None, None, None, Response(type="tips", success=False, message="请先登录")
    player = manager.game_server.players[player_id]
    if not player.user_id:
        return None, None, None, Response(type="tips", success=False, message="请先登录")
    blocked = manager._reject_room_entry_conflicts(player.user_id, "创建房间")
    if blocked:
        return None, None, None, blocked
    if check_event:
        event_id = manager._normalize_event_id(event_id)
        event_blocked = manager._validate_event_for_room(event_id, player.user_id)
        if event_blocked:
            return None, None, None, event_blocked
    profile = None
    if load_settings:
        host_settings = manager.game_server.db_manager.get_user_settings(player.user_id)
        if not host_settings:
            return None, None, None, Response(type="tips", success=False, message="获取用户设置失败")
        profile = _build_host_profile(player, host_settings)
    return player, profile, event_id, None


async def _finalize_room(
    manager,
    player,
    host_profile: dict,
    *,
    room_rule: str,
    sub_rule: str,
    validated,
    password: str,
    tips: bool,
    tourist_limit: bool,
    allow_spectator: bool,
    max_player: int = 4,
    hepai_limit: Optional[int] = None,
    event_id: Optional[str] = None,
    apply_event: bool = True,
    count_tips: Optional[bool] = False,
    pointer_tips: bool = True,
    pre_fields: Optional[dict] = None,
    post_fields: Optional[dict] = None,
    success_message: str = "房间创建成功",
) -> Response:
    room_id = manager._generate_room_id()
    has_password = bool(password)
    room_data = {
        "room_id": room_id,
        "room_type": "custom",
        "room_rule": room_rule,
        "sub_rule": sub_rule,
        "tourist_limit": tourist_limit,
        "allow_spectator": allow_spectator,
        "max_player": max_player,
        "player_list": [player.user_id],
        "player_settings": {player.user_id: host_profile},
        "has_password": has_password,
        "tips": tips,
        "show_moqie_hint": False,
        "host_user_id": player.user_id,
        "host_name": player.username,
        "is_game_running": False,
    }
    if hepai_limit is not None:
        room_data["hepai_limit"] = hepai_limit
    if pre_fields:
        room_data.update(pre_fields)
    if apply_event:
        manager._apply_event_fields(room_data, event_id)
    room_data.update(validated.model_dump())
    if post_fields:
        room_data.update(post_fields)
    room_data["is_player_set_random_seed"] = validated.random_seed != 0
    if count_tips is not None:
        room_data["count_tips"] = count_tips
    apply_duplicate_room_config(room_data)
    room_data["pointer_tips"] = bool(pointer_tips)
    room_data.setdefault("instance_id", str(uuid.uuid4()))

    manager.rooms[room_id] = room_data
    if has_password:
        manager.room_passwords[room_id] = password
    player.current_room_id = room_id
    await manager._broadcast_room_info(room_id)
    return Response(type="room/create_room_done", success=True, message=success_message, room_info=room_data)


async def create_gb_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int, tips: bool,
    random_seed: int = 0, open_cuohe: bool = False, sub_rule: str = "guobiao/standard",
    hepai_limit: int = 8, tourist_limit: bool = False, allow_spectator: bool = True,
    tactical_call: bool = False, claim_protection: bool = True, cuohe_type: int = 0,
    event_id: Optional[str] = None, count_tips: bool = False, use_flowers: bool = True,
    pointer_tips: bool = True, tian_di_ren_he: bool = False, joker_multi_wait: bool = False,
    no_joker_bonus: str = "add8", black_flower_progressive: bool = True,
    jinluo_mode: bool = False, jinluo_weather: str = "dragon_pairs",
) -> Response:
    try:
        player, profile, event_id, err = _prepare_host(manager, player_id, event_id=event_id)
        if err:
            return err
        if sub_rule == "guobiao/blood_battle":
            open_cuohe, tactical_call, hepai_limit = False, False, 8
        if sub_rule == "guobiao/lanshi":
            hepai_limit, open_cuohe, cuohe_type = 5, True, 1
        if sub_rule == "guobiao/huahun":
            hepai_limit = 8
        hepai_limit = max(1, min(64, hepai_limit))
        cuohe_type = 0 if cuohe_type not in (0, 1) else cuohe_type
        room_config = {
            "room_name": room_name, "game_round": gameround,
            "round_timer": roundTimerValue, "step_timer": stepTimerValue,
            "random_seed": random_seed, "open_cuohe": open_cuohe,
            "cuohe_type": cuohe_type, "tactical_call": tactical_call,
            "claim_protection": claim_protection,
            "jinluo_mode": jinluo_mode, "jinluo_weather": jinluo_weather,
            "use_flowers": False if sub_rule == "guobiao/lanshi" else True if sub_rule == "guobiao/huahun" else use_flowers,
            "no_joker_bonus": no_joker_bonus, "black_flower_progressive": black_flower_progressive,
            "tian_di_ren_he": tian_di_ren_he if sub_rule in (
                "guobiao/standard", "guobiao/sanma", "guobiao/blood_battle",
                "guobiao/transparent", "guobiao/transparent_realtime",
                "guobiao/four_jokers", "guobiao/huahun", "guobiao/sixteen",
            ) else False,
            "joker_multi_wait": joker_multi_wait if sub_rule in ("guobiao/four_jokers", "guobiao/huahun") else False,
        }
        try:
            validated = manager.room_validators["guobiao"](**room_config)
            from ..game_calculation.guobiao_jinluo import validate_room_config
            validate_room_config(sub_rule, validated.jinluo_mode, validated.jinluo_weather)
            if validated.jinluo_mode:
                validated.open_cuohe, hepai_limit = True, 8
                validated.tian_di_ren_he = True
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="guobiao", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, max_player=player_count_for_sub_rule(sub_rule),
            hepai_limit=hepai_limit, event_id=event_id, count_tips=count_tips, pointer_tips=pointer_tips,
        )
    except Exception as e:
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")


async def create_qingque_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int,
    tips: bool, random_seed: int = 0, open_cuohe: bool = False,
    sub_rule: str = "qingque/standard", tourist_limit: bool = False,
    allow_spectator: bool = True, tactical_call: bool = True,
    claim_protection: bool = True, event_id: Optional[str] = None,
    count_tips: bool = False, pointer_tips: bool = True, unlimited_rounds: bool = False,
) -> Response:
    if type(unlimited_rounds) is not bool:
        return Response(type="tips", success=False, message="无固定时限设置必须为布尔值")
    if unlimited_rounds and event_id:
        return Response(type="tips", success=False, message="赛事房间暂不支持无固定时限")
    try:
        player, profile, event_id, err = _prepare_host(manager, player_id, event_id=event_id)
        if err:
            return err
        try:
            validated = manager.room_validators["guobiao"](
                room_name=room_name, game_round=4 if unlimited_rounds else gameround,
                round_timer=roundTimerValue, step_timer=stepTimerValue,
                random_seed=random_seed, open_cuohe=False,
                tactical_call=tactical_call, claim_protection=claim_protection,
            )
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="qingque", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, hepai_limit=1, event_id=event_id,
            count_tips=count_tips, pointer_tips=pointer_tips,
            pre_fields={"unlimited_rounds": unlimited_rounds},
        )
    except Exception as e:
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")


async def create_changsha_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int,
    tips: bool, random_seed: int = 0, sub_rule: str = "changsha/classic_double_bird",
    tourist_limit: bool = False, allow_spectator: bool = True,
    tactical_call: bool = False, claim_protection: bool = True,
    open_kong_replacement_count: int = 2,
    initial_hu_si_xi: bool = True, initial_hu_ban_ban_hu: bool = True,
    initial_hu_que_yi_se: bool = True, initial_hu_liu_liu_shun: bool = True,
    initial_hu_san_tong: bool = True, bird_count: int = 2, dealer_bird: bool = True,
    base_score_no_dealer: bool = False, small_hu_score: int = 2, big_hu_score: int = 8,
    event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True,
    small_hu_self_draw_only: bool = False, score_cap_type: str = "none",
    score_cap_value: int = 0, exchange_selection_seconds: int = 30,
) -> Response:
    try:
        player, profile, event_id, err = _prepare_host(manager, player_id, event_id=event_id)
        if err:
            return err
        room_config = {
            "room_name": room_name, "game_round": gameround,
            "round_timer": roundTimerValue, "step_timer": stepTimerValue,
            "random_seed": random_seed, "open_cuohe": False,
            "tactical_call": tactical_call, "claim_protection": claim_protection,
            "open_kong_replacement_count": open_kong_replacement_count,
            "initial_hu_si_xi": initial_hu_si_xi, "initial_hu_ban_ban_hu": initial_hu_ban_ban_hu,
            "initial_hu_que_yi_se": initial_hu_que_yi_se, "initial_hu_liu_liu_shun": initial_hu_liu_liu_shun,
            "initial_hu_san_tong": initial_hu_san_tong, "bird_count": bird_count,
            "dealer_bird": dealer_bird, "base_score_no_dealer": base_score_no_dealer,
            "small_hu_score": small_hu_score, "big_hu_score": big_hu_score,
            "small_hu_self_draw_only": small_hu_self_draw_only,
            "score_cap_type": score_cap_type, "score_cap_value": score_cap_value,
            "exchange_selection_seconds": exchange_selection_seconds,
        }
        from ..gamestate.game_changsha.exchange_six_config import normalize_config
        room_config = normalize_config(room_config, sub_rule)
        try:
            validated = manager.room_validators["changsha"](**room_config)
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="changsha", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, hepai_limit=1, event_id=event_id,
            count_tips=count_tips, pointer_tips=pointer_tips,
        )
    except Exception as e:
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")


async def create_zhongyong_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int,
    tips: bool, random_seed: int = 0, sub_rule: str = "zhongyong/standard",
    tourist_limit: bool = False, allow_spectator: bool = True,
    tactical_call: bool = False, claim_protection: bool = True,
    event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True,
) -> Response:
    try:
        if sub_rule not in {"zhongyong/standard", "zhongyong/nanque"}:
            return Response(type="tips", success=False, message="不支持的中庸子规则")
        player, profile, event_id, err = _prepare_host(manager, player_id, event_id=event_id)
        if err:
            return err
        try:
            validated = manager.room_validators["zhongyong"](
                room_name=room_name, game_round=gameround,
                round_timer=roundTimerValue, step_timer=stepTimerValue,
                random_seed=random_seed, tactical_call=tactical_call,
                claim_protection=claim_protection,
            )
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="zhongyong", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, hepai_limit=0, event_id=event_id,
            count_tips=count_tips, pointer_tips=pointer_tips,
            pre_fields={"open_cuohe": False},
        )
    except Exception as e:
        logger.error("创建中庸／南雀房间失败: %s", e, exc_info=True)
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")


async def create_hongque_room(
    manager, player_id: str, room_name: str, gameround: int, password: str,
    roundTimerValue: int, stepTimerValue: int, tips: bool,
    random_seed: int = 0, sub_rule: str = "hongque/v1.6",
    tourist_limit: bool = False, allow_spectator: bool = True,
    hepai_way: str = "multi_ron", count_tips: bool = False, pointer_tips: bool = True,
    hongque_colourless: bool = True, hongque_min_fan: int = 0, hongque_min_points: int = 0,
) -> Response:
    try:
        player, profile, _, err = _prepare_host(manager, player_id, check_event=False)
        if err:
            return err
        validated = manager.room_validators["hongque"](
            room_name=room_name, game_round=gameround,
            round_timer=roundTimerValue, step_timer=stepTimerValue,
            random_seed=random_seed,
        )
        from .hongque_room import normalize_hongque_config
        hongque_config = normalize_hongque_config({
            "sub_rule": sub_rule, "hepai_way": hepai_way,
            "hongque_colourless": hongque_colourless,
            "hongque_min_fan": hongque_min_fan, "hongque_min_points": hongque_min_points,
        })
        return await _finalize_room(
            manager, player, profile,
            room_rule="hongque", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=bool(allow_spectator), hepai_limit=0, apply_event=False,
            count_tips=count_tips, pointer_tips=pointer_tips,
            pre_fields={
                "open_cuohe": False,
                "hepai_way": hepai_way if hepai_way in ("head_bump", "multi_ron") else "multi_ron",
            },
            post_fields={**hongque_config, "game_round": validated.game_round * 4},
            success_message="虹雀原型房间创建成功",
        )
    except ValueError as exc:
        return Response(type="tips", success=False, message=f"房间配置无效: {exc}")
    except Exception as exc:
        logger.error("创建虹雀原型房间失败: %s", exc, exc_info=True)
        return Response(type="error_message", success=False, message=f"创建房间失败: {exc}")


async def create_free_room(
    manager, player_id: str, room_name: str, password: str,
    random_seed: int = 0, sub_rule: str = "free/standard",
    tourist_limit: bool = False,
    wall_wan: bool = True, wall_tong: bool = True, wall_suo: bool = True,
    wall_winds: bool = True, wall_dragons: bool = True, wall_flowers: bool = True,
    pointer_tips: bool = True,
) -> Response:
    try:
        player, profile, _, err = _prepare_host(manager, player_id, check_event=False)
        if err:
            return err
        validated = manager.room_validators["free"](
            room_name=room_name, random_seed=random_seed,
            wall_wan=wall_wan, wall_tong=wall_tong, wall_suo=wall_suo,
            wall_winds=wall_winds, wall_dragons=wall_dragons, wall_flowers=wall_flowers,
        )
        return await _finalize_room(
            manager, player, profile,
            room_rule="free", sub_rule=sub_rule or "free/standard", validated=validated,
            password=password, tips=False, tourist_limit=tourist_limit,
            allow_spectator=False, hepai_limit=0, apply_event=False,
            count_tips=None, pointer_tips=pointer_tips,
            pre_fields={"open_cuohe": False, "game_round": 1, "round_timer": 0, "step_timer": 0},
            post_fields={"min_players_to_start": 1},
            success_message="自由模式房间创建成功",
        )
    except ValueError as exc:
        return Response(type="tips", success=False, message=f"房间配置无效: {exc}")
    except Exception as exc:
        logger.error("创建自由模式房间失败: %s", exc, exc_info=True)
        return Response(type="error_message", success=False, message=f"创建房间失败: {exc}")


async def create_classical_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int,
    tips: bool, random_seed: int = 0, sub_rule: str = "classical/standard",
    tourist_limit: bool = False, allow_spectator: bool = True,
    event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True,
) -> Response:
    try:
        player, profile, event_id, err = _prepare_host(manager, player_id, event_id=event_id)
        if err:
            return err
        try:
            validated = manager.room_validators["guobiao"](
                room_name=room_name, game_round=gameround,
                round_timer=roundTimerValue, step_timer=stepTimerValue,
                random_seed=random_seed, open_cuohe=False,
            )
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="classical", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, hepai_limit=1, event_id=event_id,
            count_tips=count_tips, pointer_tips=pointer_tips,
        )
    except Exception as e:
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")


async def create_shanghai_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int,
    tips: bool, random_seed: int = 0, sub_rule: str = "shanghai/qiaoma",
    tourist_limit: bool = False, allow_spectator: bool = True,
    event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True,
    hepai_limit: int = 0, detailed_config: Optional[dict] = None,
) -> Response:
    try:
        player, profile, event_id, err = _prepare_host(manager, player_id, event_id=event_id)
        if err:
            return err
        try:
            validated = manager.room_validators["shanghai"](
                room_name=room_name, game_round=gameround,
                round_timer=roundTimerValue, step_timer=stepTimerValue,
                random_seed=random_seed, open_cuohe=False,
                sub_rule=sub_rule, hepai_limit=hepai_limit, detailed_config=detailed_config,
            )
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="shanghai", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, hepai_limit=validated.hepai_limit,
            event_id=event_id, count_tips=count_tips, pointer_tips=pointer_tips,
        )
    except Exception as e:
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")


async def create_sichuan_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int,
    tips: bool, random_seed: int = 0, sub_rule: str = "sichuan/standard",
    tourist_limit: bool = False, allow_spectator: bool = True,
    tactical_call: bool = True, blood_battle: bool = True, claim_protection: bool = True,
    event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True,
    hepai_limit: int = 0, duplicate_key: str = "", duplicate_round_count=None,
    duplicate_sync_mode: bool = False, wall_source=None,
    real_time: bool = False, tactical_grace_seconds: int = 5, reuse_training_room: bool = False,
) -> Response:
    from ..game_calculation.sichuan.duplicate_rules import is_duplicate_sub_rule
    try:
        player, _, event_id, err = _prepare_host(manager, player_id, event_id=event_id, load_settings=False)
        if err:
            return err
        from .sichuan_timing import timing_config
        try:
            duplicate_timing = timing_config(sub_rule, dict(real_time=real_time, tactical_grace_seconds=tactical_grace_seconds))
        except ValueError as error:
            return Response(type="tips", success=False, message=f"房间配置无效: {error}")
        if reuse_training_room and not is_duplicate_sub_rule(sub_rule):
            raise ValueError("重复使用仅支持训练牌山")
        if is_duplicate_sub_rule(sub_rule):
            if tourist_limit and getattr(player, "is_tourist", False):
                return Response(type="tips", success=False, message="游客不能创建限制游客加入的复式房间")
            if event_id and not manager.game_server.db_manager.get_event_admin_role(event_id, player.user_id):
                return Response(type="tips", success=False, message="赛事或基地复式须由该场馆管理员创建")
            from .sichuan_duplicate import create_pair
            config = dict(
                room_name=room_name, sub_rule=sub_rule, round_timer=roundTimerValue,
                step_timer=stepTimerValue, random_seed=random_seed, tips=tips,
                count_tips=count_tips, pointer_tips=pointer_tips, tourist_limit=tourist_limit,
                tactical_call=tactical_call, claim_protection=claim_protection,
                allow_spectator=allow_spectator, duplicate_key=duplicate_key,
                duplicate_sync_mode=duplicate_sync_mode, wall_source=wall_source,
                reuse_training_room=reuse_training_room, **duplicate_timing,
            )
            if duplicate_round_count is not None:
                config["duplicate_round_count"] = duplicate_round_count
            response = await create_pair(manager, event_id, sub_rule, config, password, player.user_id)
            if not response.success:
                return response
            from .sichuan_lobby import join, public_lobby
            root = manager.rooms[response.room_info["room_id"]]
            joined = await join(manager, player_id, root, password)
            if not joined.success:
                return joined
            return Response(type="room/create_room_done", success=True, message="八人复式房间已创建",
                            room_info=public_lobby(manager, root))
        if duplicate_key:
            return Response(type="tips", success=False, message="川麻复式密钥仅支持双打复式子规则")
        if wall_source is not None:
            return Response(type="tips", success=False, message="牌山来源选项仅支持川麻双打复式")

        host_settings = manager.game_server.db_manager.get_user_settings(player.user_id)
        if not host_settings:
            return Response(type="tips", success=False, message="获取用户设置失败")
        profile = _build_host_profile(player, host_settings)
        try:
            validated = manager.room_validators["sichuan"](
                room_name=room_name, game_round=gameround,
                round_timer=roundTimerValue, step_timer=stepTimerValue,
                random_seed=random_seed, sub_rule=sub_rule, tactical_call=tactical_call,
                blood_battle=False if sub_rule in ("sichuan/xueliu", "sichuan/xueliu_exchange", "sichuan/xueliu_doubles") else (sub_rule == "sichuan/xuezhan_exchange" or blood_battle),
                hepai_limit=hepai_limit, claim_protection=claim_protection,
            )
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="sichuan", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, hepai_limit=validated.hepai_limit,
            event_id=event_id, count_tips=count_tips, pointer_tips=pointer_tips,
        )
    except Exception as e:
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")


async def create_taiwan_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int,
    tips: bool, random_seed: int = 0, sub_rule: str = "taiwan/standard",
    tourist_limit: bool = False, allow_spectator: bool = True,
    open_cuohe: bool = False, cuohe_type: int = 0,
    detailed_config: Optional[dict] = None, event_id: Optional[str] = None,
    count_tips: bool = False, pointer_tips: bool = True,
) -> Response:
    try:
        player, profile, event_id, err = _prepare_host(manager, player_id, event_id=event_id)
        if err:
            return err
        cuohe_type = 0 if cuohe_type not in (0, 1) else cuohe_type
        try:
            validated = manager.room_validators["taiwan"](
                room_name=room_name, game_round=gameround,
                round_timer=roundTimerValue, step_timer=stepTimerValue,
                random_seed=random_seed, sub_rule=sub_rule or "taiwan/standard",
                tips=tips, open_cuohe=open_cuohe, cuohe_type=cuohe_type,
                detailed_config=detailed_config,
            )
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="taiwan", sub_rule=validated.sub_rule, validated=validated,
            password=password, tips=validated.tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, hepai_limit=validated.detailed_config["minimum_tai"],
            event_id=event_id, count_tips=count_tips, pointer_tips=pointer_tips,
            pre_fields={
                "open_cuohe": validated.open_cuohe, "cuohe_type": validated.cuohe_type,
                "tactical_call": False, "claim_protection": False,
            },
        )
    except Exception as e:
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")


async def create_riichi_room(
    manager, player_id: str, room_name: str, gameround: int,
    password: str, roundTimerValue: int, stepTimerValue: int,
    tips: bool, random_seed: int = 0, sub_rule: str = "riichi/standard",
    open_cuohe: bool = False, hepai_limit: int = 1, red_dora: bool = True,
    allow_kuikae: bool = False, open_xiru: bool = True, open_tobi: bool = True,
    hepai_way: str = "head_bump", tourist_limit: bool = False,
    allow_spectator: bool = True, event_id: Optional[str] = None,
    count_tips: bool = False, starting_score: Optional[int] = None,
    pointer_tips: bool = True, detailed_config: Optional[dict] = None,
    claim_protection: bool = False,
) -> Response:
    try:
        player, profile, event_id, err = _prepare_host(manager, player_id, event_id=event_id)
        if err:
            return err
        hepai_limit = max(1, min(64, hepai_limit))
        try:
            validated = manager.room_validators["riichi"](
                room_name=room_name, claim_protection=claim_protection,
                game_round=gameround, round_timer=roundTimerValue,
                step_timer=stepTimerValue, random_seed=random_seed,
                open_cuohe=open_cuohe, hepai_limit=hepai_limit, red_dora=red_dora,
                starting_score=starting_score if starting_score is not None else (
                    35000 if sub_rule == "riichi/sanma" else 50000 if sub_rule == "riichi/langyong" else 25000
                ),
                sub_rule=sub_rule, detailed_config=detailed_config,
                allow_kuikae=allow_kuikae, open_xiru=open_xiru,
                open_tobi=open_tobi, hepai_way=hepai_way,
            )
        except ValueError as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")
        return await _finalize_room(
            manager, player, profile,
            room_rule="riichi", sub_rule=sub_rule, validated=validated,
            password=password, tips=tips, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, max_player=3 if sub_rule == "riichi/sanma" else 4,
            event_id=event_id, count_tips=count_tips, pointer_tips=pointer_tips,
        )
    except Exception as e:
        return Response(type="error_message", success=False, message=f"创建房间失败: {e}")
