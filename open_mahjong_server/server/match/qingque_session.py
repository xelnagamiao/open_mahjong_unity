"""Durable completed-hand checkpoints for Qingque point-rated sessions.

The running game keeps its match reservation while writes are retried. A server
restart settles only complete hands, then publishes their replay exactly once.
No client supplied score participates in this path.
"""
import asyncio
import json
import logging
from datetime import datetime, timezone
from types import SimpleNamespace

from psycopg2.extras import Json

from .qingque_score_elo import ALGORITHM, validate_scores
from .rating_rules import get_ranked_queue

logger = logging.getLogger(__name__)


def uses_point_rating(state):
    return (state.room_rule == 'qingque' and state.room_type == 'match'
            and getattr(state, 'match_rating_algorithm', None) == ALGORITHM)


def completed_hands(record):
    return sum(any(isinstance(t, list) and t and t[0] == 'end'
                   for t in rd.get('action_ticks', []))
               for rd in record.get('game_round', {}).values() if isinstance(rd, dict))


def checkpoint_state(state, final=False):
    if not uses_point_rating(state):
        return
    record = json.loads(json.dumps(state.game_record, default=str))
    n = completed_hands(record)
    if not n:
        return
    # Never checkpoint an unfinished hand, even when recovering an exception.
    record['game_round'] = {key: rd for key, rd in record.get('game_round', {}).items()
                            if completed_hands({'game_round': {key: rd}})}
    ordered = sorted(state.player_list, key=lambda p: (-p.score, p.original_player_index))
    places = {p.user_id: rank for rank, p in enumerate(ordered, 1)}
    players = [dict(user_id=p.user_id, username=p.username, net_points=p.score,
                    original_player_index=p.original_player_index, place=places[p.user_id],
                    avatar_frame_used=getattr(p, 'avatar_frame_used', 0),
                    **{key: getattr(p, key, None) for key in
                       ('title_used', 'character_used', 'profile_used', 'voice_used')})
               for p in state.player_list]
    validate_scores(players, n)
    title = record['game_title']
    title.update(rating_game_id=str(state.gamestate_id), rating_algorithm=ALGORITHM,
                 rating_rule='qingque', match_queue_type=state.match_queue_type,
                 rating_completed_hands=n, match_tier='elo')
    # This is a private checkpoint, never a live spectator/client snapshot.
    title.setdefault('end_time', datetime.now(timezone.utc).isoformat())
    title['master_seed_hex'] = format(state.master_seed, '064x')
    conn = state.db_manager._get_connection()
    try:
        with conn.cursor() as cur:
            cur.execute("""INSERT INTO qingque_rating_sessions
                (game_id,queue_type,players,record,completed_hands,status)
                VALUES(%s,%s,%s,%s,%s,%s)
                ON CONFLICT(game_id) DO UPDATE SET players=EXCLUDED.players,
                record=EXCLUDED.record,completed_hands=EXCLUDED.completed_hands,
                status=EXCLUDED.status,updated_at=CURRENT_TIMESTAMP
                WHERE qingque_rating_sessions.status='open'
                  AND qingque_rating_sessions.completed_hands<=EXCLUDED.completed_hands""",
                (str(state.gamestate_id),state.match_queue_type,Json(players),Json(record),n,
                 'pending' if final else 'open'))
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        state.db_manager._put_connection(conn)


def finish_checkpoint(db, game_id):
    from ..database.rule_ratings import settle_rated_game
    from ..database.qingque.store_qingque import store_qingque_game_record
    conn = db._get_connection()
    try:
        with conn.cursor() as cur:
            # A separate transaction key serializes live and recovery publishers.
            cur.execute('SELECT pg_advisory_xact_lock(hashtextextended(%s,0))', ('qingque-finish:' + str(game_id),))
            cur.execute('SELECT queue_type,players,record,completed_hands,status,cloud_game_id FROM qingque_rating_sessions WHERE game_id=%s', (str(game_id),))
            row = cur.fetchone()
            if row is None or row[4] == 'open':
                raise ValueError('青雀成绩尚未冻结')
            queue, participants, record, n, status, cloud_id = row
            if status == 'complete':
                conn.commit()
                return record['game_title']['rating_result'], cloud_id, record
            spec = get_ranked_queue(queue)
            if spec is None or spec.rule != 'qingque':
                raise ValueError('青雀恢复队列无效')
            inputs = [{key: p[key] for key in ('user_id','place','net_points')} for p in participants]
            result = settle_rated_game(db, game_id, queue, inputs,
                ended_at=record['game_title']['end_time'], rating_algorithm=ALGORITHM, completed_hands=n)
            record['game_title']['rating_result'] = result
            match_type = f'{"unlimited" if spec.unlimited_rounds else spec.rounds}/4_rank'
            # A crash after the replay commit must not create a second replay.
            cur.execute("SELECT game_id FROM game_records WHERE record->'game_title'->>'rating_game_id'=%s LIMIT 1", (str(game_id),))
            existing = cur.fetchone()
            cloud_id = existing[0] if existing else None
            if cloud_id is None:
                players = [SimpleNamespace(**p, score=p['net_points'],
                    record_counter=SimpleNamespace(rank_result=p['place'])) for p in participants]
                cloud_id = store_qingque_game_record(db, record, players, 'match', match_type)
                if not cloud_id:
                    raise RuntimeError('青雀牌谱发布失败，保留待结算成绩重试')
            cur.execute("UPDATE qingque_rating_sessions SET status='complete',record=%s,cloud_game_id=%s,updated_at=CURRENT_TIMESTAMP WHERE game_id=%s",
                        (Json(record), str(cloud_id), str(game_id)))
        conn.commit()
        return result, cloud_id, record
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def finish_state(state):
    checkpoint_state(state, final=True)
    result, cloud_id, record = finish_checkpoint(state.db_manager, state.gamestate_id)
    state.game_record = record
    for player in state.player_list:
        for key, value in result[str(player.user_id)].items():
            setattr(player, key, value)
    state.point_rating_completed = True
    spec = get_ranked_queue(state.match_queue_type)
    return f'{"unlimited" if spec.unlimited_rounds else spec.rounds}/4_rank', cloud_id


def finish_aborted_state(state):
    """Settle the last durable hand, never the interrupted hand's live scores."""
    conn = state.db_manager._get_connection()
    try:
        with conn.cursor() as cur:
            cur.execute("""UPDATE qingque_rating_sessions SET status='pending',
                record=jsonb_set(record,'{game_title,finish_reason}',%s::jsonb)
                WHERE game_id=%s AND status='open'""",
                (json.dumps(getattr(state, 'close_reason', None) or 'interrupted'), str(state.gamestate_id)))
            cur.execute('SELECT status FROM qingque_rating_sessions WHERE game_id=%s', (str(state.gamestate_id),))
            row = cur.fetchone()
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        state.db_manager._put_connection(conn)
    if row and row[0] != 'complete':
        finish_checkpoint(state.db_manager, state.gamestate_id)


async def retry_rating_io(function, *args):
    delay = 1
    while True:
        try:
            # The existing DB manager is synchronous. Completed-hand records are
            # immutable during this await; perform its I/O off the network loop.
            write = asyncio.create_task(asyncio.to_thread(function, *args))
            try:
                return await asyncio.shield(write)
            except asyncio.CancelledError:
                # Cancellation cannot stop the DB thread. Drain it before the
                # cleanup path freezes its last committed checkpoint.
                await write
                raise
        except (ValueError, TypeError):
            raise  # Invalid evidence is never converted into another algorithm.
        except Exception:
            logger.exception('青雀成绩写入失败，保留对局并重试')
            await asyncio.sleep(delay)
            delay = min(10, delay * 2)


def recover_checkpoints(db, include_open=False):
    conn = db._get_connection()
    try:
        with conn.cursor() as cur:
            if include_open:
                # Run once, before accepting connections in the single game server.
                cur.execute("UPDATE qingque_rating_sessions SET status='pending',record=jsonb_set(record,'{game_title,finish_reason}',%s::jsonb) WHERE status='open'", (json.dumps('server_restart'),))
            cur.execute("SELECT game_id FROM qingque_rating_sessions WHERE status='pending' ORDER BY updated_at,game_id")
            ids = [row[0] for row in cur.fetchall()]
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
    for game_id in ids:
        finish_checkpoint(db, game_id)


async def recovery_loop(db):
    while True:
        await asyncio.sleep(30)
        try:
            await asyncio.to_thread(recover_checkpoints, db)
        except Exception:
            logger.exception('青雀待结算恢复失败，将继续重试')
