"""The shared atomic publication path for the nine rule-local replay adapters."""
import hashlib
import json
import logging
import re

from psycopg2.extras import Json

from .round_progress import apply_round_progress

logger = logging.getLogger(__name__)
_ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'


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


def persist_game_record(db, record, players, room_type, match_type, id_factory, *, allow_duplicate=False):
    """Replay, participants, statistics and currency either all commit or all roll back."""
    conn = None
    try:
        title = record.get('game_title') or {}
        from .duplicate_walls import is_training_duplicate_record
        training = is_training_duplicate_record(title) and allow_duplicate
        duplicate = bool(title.get('duplicate_key')) and allow_duplicate
        if any(getattr(player, 'user_id', 0) <= 10 for player in players) and not (duplicate or training):
            return None
        from .rule_identity import canonical_game_record
        record['game_title'] = canonical_game_record(record)['game_title']
        title = record['game_title']
        title.setdefault('room_type', room_type)
        match_type = apply_round_progress(record, match_type, room_type)
        from ..gamestate.public.match_result import attach_match_result
        attach_match_result(record, players)
        game_id = _game_id(title, id_factory)
        title['cloud_game_id'] = game_id
        conn = db._get_connection()
        with conn.cursor() as cursor:
            from .commerce import prepare_game_reward
            from .game_rewards import apply_currency_to_match_result
            # Prepare on a copy: rolled-back persistence must not leak an earned
            # amount into the later local record or live end-game broadcast.
            payload = json.loads(json.dumps(record, ensure_ascii=False, default=str))
            payload['currency_reward'] = prepare_game_reward(cursor, payload, [p.user_id for p in players], game_id=game_id)
            apply_currency_to_match_result(payload)
            cursor.execute('''INSERT INTO game_records(game_id,record) VALUES(%s,%s)
                ON CONFLICT(game_id) DO NOTHING RETURNING game_id''', (game_id, Json(payload)))
            inserted = cursor.fetchone() is not None
            if not inserted:
                cursor.execute('SELECT record FROM game_records WHERE game_id=%s FOR UPDATE', (game_id,))
                stored = cursor.fetchone()[0]
                stored = json.loads(stored) if isinstance(stored, str) else stored
                # Another server version may have committed while our INSERT
                # waited. Reload its trusted entitlement before comparing.
                payload['currency_reward'] = prepare_game_reward(cursor, payload, [p.user_id for p in players], game_id=game_id)
                apply_currency_to_match_result(payload)
                comparable = json.loads(json.dumps(stored, ensure_ascii=False, default=str))
                comparable['currency_reward'] = payload['currency_reward']
                apply_currency_to_match_result(comparable)
                if comparable != payload:
                    raise ValueError('同一对局的重复牌谱内容冲突')
            else:
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
        conn.commit()
        record['currency_reward'] = payload['currency_reward']
        if 'match_result' in payload:
            record['match_result'] = payload['match_result']
        return game_id
    except Exception:
        if conn is not None:
            conn.rollback()
        logger.exception('牌谱与货币结算失败，已回滚')
        return None
    finally:
        if conn is not None:
            db._put_connection(conn)
