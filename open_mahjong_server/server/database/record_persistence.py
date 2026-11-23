"""The shared atomic publication path for the nine rule-local replay adapters."""
import asyncio
import hashlib
import json
import logging
import re
from copy import deepcopy

from psycopg2.pool import PoolError
from .pool_retry import is_pool_exhausted, run_db_write, run_with_pool_retry
from .round_progress import apply_round_progress

logger = logging.getLogger(__name__)
_ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
_pending_record_writes = set()


def _game_id(title, factory):
    fixed = title.get('duplicate_game_id') or title.get('cloud_game_id')
    if fixed:
        if not re.fullmatch(r'[A-Za-z0-9]{1,16}', fixed):
            raise ValueError('牌谱保存编号无效')
        return fixed
    instance = title.get('game_instance_id') or title.get('rating_game_id')
    if instance:
        number = int.from_bytes(hashlib.sha256(str(instance).encode()).digest(), 'big')
        chars = []
        for _ in range(10):
            number, digit = divmod(number, 62)
            chars.append(_ALPHABET[digit])
        return ''.join(chars)
    return factory()


def _publish_record(db, cursor, record, players, room_type, match_type, id_factory, *, allow_duplicate, snapshot=None):
    title = record.get('game_title') or {}
    from .duplicate_walls import is_training_duplicate_record
    training = is_training_duplicate_record(title) and allow_duplicate
    duplicate = bool(title.get('duplicate_key')) and allow_duplicate
    from .rule_identity import canonical_game_record
    record['game_title'] = canonical_game_record(record)['game_title']
    title = record['game_title']
    title.setdefault('room_type', room_type)
    match_type = apply_round_progress(record, match_type, room_type)
    from ..gamestate.public.match_result import attach_match_result
    attach_match_result(record, players)
    game_id = _game_id(title, id_factory)
    title['cloud_game_id'] = game_id
    from .commerce import prepare_game_reward
    from .game_rewards import apply_currency_to_match_result
    # Prepare on a copy: rolled-back persistence must not leak an earned
    # amount into the later local record or live end-game broadcast.
    from ..records.store import lock_record_ids, paired_game_id
    match_id = title.get("duplicate_match_id")
    lock_record_ids(cursor, game_id, paired_game_id(match_id) if match_id else game_id)
    payload = json.loads(json.dumps(record, ensure_ascii=False, default=str))
    payload['currency_reward'] = prepare_game_reward(cursor, payload, [p.user_id for p in players], game_id=game_id)
    apply_currency_to_match_result(payload)
    from ..records.store import publish_table
    participant_snapshots = [{
        'user_id': p.user_id, 'username': p.username, 'score': p.score,
        'rank': p.record_counter.rank_result, 'original_player_index': p.original_player_index,
        **{key: getattr(p, key, None) for key in (
            'title_used', 'character_used', 'profile_used', 'voice_used', 'avatar_frame_used')},
    } for p in players]
    from ..records.format import PAIRED_RULES
    from ..records.store import load_paired_snapshot
    if snapshot is None and (duplicate or training) and title["sub_rule"] in PAIRED_RULES:
        snapshot = load_paired_snapshot(cursor, title, game_id, training=training)
    inserted = publish_table(cursor, game_id, payload, participant_snapshots, snapshot=snapshot)
    if inserted:
        from .scene_stats import normalize_scene_fields, record_game_metrics
        rule, sub_rule = title['rule'], title['sub_rule']
        room_type, tier, event_id = normalize_scene_fields(room_type, title.get('match_tier'), title.get('event_id'))
        if training:
            room_type, tier, event_id = 'custom', None, None
        for player in players:
            # Sichuan duplicate reference tables intentionally omit bot
            # participant rows; the original four seats remain in title.
            if rule == 'sichuan' and player.user_id <= 10:
                continue
            cursor.execute('''INSERT INTO game_player_records
                (game_id,user_id,username,score,rank,original_player_index,rule,sub_rule,
                 match_type,room_type,match_tier,event_id,title_used,character_used,
                 profile_used,voice_used,avatar_frame_used,pt_change)
                VALUES(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)''',
                (game_id, player.user_id, player.username, player.score,
                 player.record_counter.rank_result, player.original_player_index, rule, sub_rule,
                 match_type, room_type, tier, event_id, getattr(player, 'title_used', None),
                 getattr(player, 'character_used', None), getattr(player, 'profile_used', None),
                 getattr(player, 'voice_used', None), getattr(player, 'avatar_frame_used', 0),
                 getattr(player, 'pt', None) if room_type == 'match' else None))
        if rule == 'riichi':
            from .riichi.record_stats import write_stats
            for player in players:
                write_stats(cursor, game_id, player.user_id, record, player.original_player_index,
                            player.score, player.record_counter.rank_result)
        if not (duplicate or training):
            from .player_recent_records import update_player_recent_records
            update_player_recent_records(cursor, game_id, record)
        if duplicate:
            from .duplicate_walls import link_duplicate_record
            link_duplicate_record(cursor, game_id, record)
        if not training:
            record_game_metrics(db, game_id, record, players,
                dict(rule=rule, sub_rule=sub_rule, room_type=room_type,
                     match_tier=tier, event_id=event_id, match_type=match_type), cursor=cursor)
    from .commerce import settle_game_reward
    settle_game_reward(cursor, game_id, payload)
    return game_id, payload


def _apply_committed_reward(record, payload):
    record['currency_reward'] = payload['currency_reward']
    if 'match_result' in payload:
        record['match_result'] = payload['match_result']


async def store_single_table_record(store, record, players, room_type, match_type, *, after_deferred_save=None):
    """Return a cloud ID when saved, or deliver the local result while the pool recovers."""
    try:
        return await run_db_write(store, record, players, room_type, match_type, raise_on_pool_exhaustion=True)
    except PoolError as error:
        if not is_pool_exhausted(error):
            raise

    # The local result and the later cloud replay must address the same table.
    from ..gamestate.public.game_record_manager import generate_local_only_game_id
    title = record["game_title"]
    title["cloud_game_id"] = _game_id(title, generate_local_only_game_id)
    saved_record = deepcopy(record)
    saved_players = deepcopy(players)

    async def publish_when_available():
        try:
            game_id = await run_with_pool_retry(store, saved_record, saved_players, room_type, match_type,
                                                raise_on_pool_exhaustion=True)
            if game_id and after_deferred_save:
                await after_deferred_save(game_id, saved_record, saved_players, room_type, match_type)
        except Exception:
            logger.exception("后台写入终局牌谱或统计失败")

    task = asyncio.create_task(publish_when_available())
    _pending_record_writes.add(task)
    task.add_done_callback(_pending_record_writes.discard)
    return None


def persist_game_record(db, record, players, room_type, match_type, id_factory, *, allow_duplicate=False,
                        raise_on_pool_exhaustion=False):
    """Replay, participants, statistics and currency either all commit or all roll back."""
    from .duplicate_walls import is_training_duplicate_record
    title = record.get("game_title") or {}
    if any(getattr(p, "user_id", 0) <= 10 for p in players) and not (
            allow_duplicate and (title.get("duplicate_key") or is_training_duplicate_record(title))):
        return None
    conn = None
    try:
        conn = db._get_connection()
        with conn.cursor() as cursor:
            game_id, payload = _publish_record(db, cursor, record, players, room_type, match_type,
                                              id_factory, allow_duplicate=allow_duplicate)
        conn.commit()
        _apply_committed_reward(record, payload)
        return game_id
    except Exception as error:
        if conn is None and raise_on_pool_exhaustion and is_pool_exhausted(error):
            raise
        if conn is not None:
            conn.rollback()
        logger.exception('牌谱与货币结算失败，已回滚')
        return None
    finally:
        if conn is not None:
            db._put_connection(conn)


def store_sichuan_pair_records(db, match, tables):
    """Publish both final tables and their rewards in one transaction, using the live match identity."""
    from .duplicate_walls import is_training_duplicate_record
    from .sichuan.store_sichuan import generate_game_id

    conn = db._get_connection()
    published = {}
    try:
        with conn.cursor() as cursor:
            training = match["wall_source"] == "training"
            for side in ("open", "closed"):
                record, players, room_type, match_type = tables[side]
                title = record["game_title"]
                if (title.get("duplicate_match_id") != match["id"] or title.get("duplicate_table") != side
                        or title.get("sub_rule") != match["profile"]["sub_rule"]
                        or training != is_training_duplicate_record(title)):
                    raise ValueError("复式牌谱与本场比赛不匹配")
                if not training:
                    game_id = match["table_record_ids"][side]
                    if title.get("duplicate_key") != match["key"] or title.get("duplicate_game_id") != game_id:
                        raise ValueError("复式牌谱与本场密钥不匹配")
                published[side] = _publish_record(db, cursor, record, players, room_type, match_type,
                                                  generate_game_id, allow_duplicate=True, snapshot=match)
            if training:
                # This flag is authored only by the server's two-table settlement.
                cursor.execute("UPDATE game_record_tables SET training_match_id=%s WHERE game_id=ANY(%s)",
                               (match["id"], [value[0] for value in published.values()]))
            else:
                cursor.execute("""UPDATE duplicate_games SET paired_match_id=%s
                    WHERE wall_id=%s AND game_id=ANY(%s) AND (paired_match_id IS NULL OR paired_match_id=%s)""",
                    (match["id"], match["wall_id"], [value[0] for value in published.values()], match["id"]))
                if cursor.rowcount != 2:
                    raise ValueError("两桌终局牌谱未关联至同一场正式牌山")
        conn.commit()
    except BaseException:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
    for side, (_, payload) in published.items():
        _apply_committed_reward(tables[side][0], payload)
    return {side: result[0] for side, result in published.items()}
