"""Independent ratings and atomic, idempotent ranked settlement."""
from psycopg2.extras import Json, RealDictCursor
from datetime import datetime, timedelta, timezone
from ..match.rating_rules import (RULES, GRADE_RULES, QUEUES, DEFAULT_R,
    ELO_ALGORITHM_VERSION, RATING_WRITE_LOCK, default_rating, elo_deltas)
from ..match.rank_calculator import calculate_pt, apply_pt, get_rank_index
from ..match.riichi_rank_calculator import RIICHI_PT_ALGORITHM, LEGACY_PT_ALGORITHM, riichi_pt_details
from ..match.sanma_rank_calculator import SANMA_PT_ALGORITHM, sanma_pt_details


def rating_end_time(value):
    if not isinstance(value, datetime):
        if not isinstance(value, str) or not value.strip():
            raise ValueError("评分重算缺少对局结束时间")
        value = datetime.fromisoformat(value.replace('Z', '+00:00'))
    if value.tzinfo is None:
        value = value.replace(tzinfo=timezone(timedelta(hours=8)))
    return value.astimezone(timezone.utc)


def ensure_rating_schema(cursor):
    cursor.execute("""
        CREATE TABLE IF NOT EXISTS rule_ratings (
            user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
            rule TEXT NOT NULL CHECK (rule IN ('guobiao','riichi','qingque','sichuan','riichi_sanma','sichuan_xueliu_exchange')),
            rank_name TEXT NOT NULL DEFAULT '10级', rank_score DOUBLE PRECISION NOT NULL DEFAULT 0,
            elo DOUBLE PRECISION DEFAULT 1500, games INTEGER NOT NULL DEFAULT 0,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (user_id, rule));
        CREATE TABLE IF NOT EXISTS rating_settlements (
            game_id TEXT PRIMARY KEY, rule TEXT NOT NULL, result JSONB NOT NULL,
            players JSONB, ended_at TIMESTAMPTZ,
            created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
        ALTER TABLE rule_ratings ALTER COLUMN elo DROP NOT NULL;
        ALTER TABLE rule_ratings DROP CONSTRAINT IF EXISTS rule_ratings_rule_check;
        ALTER TABLE rule_ratings ADD CONSTRAINT rule_ratings_rule_check
            CHECK (rule IN ('guobiao','riichi','qingque','sichuan','riichi_sanma','sichuan_xueliu_exchange'));
        ALTER TABLE rating_settlements ADD COLUMN IF NOT EXISTS players JSONB;
        ALTER TABLE rating_settlements ADD COLUMN IF NOT EXISTS ended_at TIMESTAMPTZ;
    """)


def read_ratings(cursor, user_id, national_rank="10级", national_score=0):
    ratings = {rule: default_rating(rule) for rule in RULES}
    cursor.execute("SELECT rule,rank_name,rank_score,elo,games FROM rule_ratings WHERE user_id=%s", (user_id,))
    for rule, rank, score, elo, games in cursor.fetchall():
        ratings[rule].update(rank_name=rank if rule in GRADE_RULES else "", rank_score=float(score), games=games)
        if rule not in GRADE_RULES:
            ratings[rule]['elo'] = float(elo) if elo is not None else DEFAULT_R
    # The existing national rank remains authoritative, including admin changes.
    ratings["guobiao"].update(rank_name=national_rank, rank_score=float(national_score))
    return ratings


def settle_rated_game(db, game_id, queue_type, players, ended_at=None, rating_algorithm=None):
    spec = QUEUES[queue_type]
    rating_rule = spec.rating_rule
    if (len(players) != spec.player_count or len({p["user_id"] for p in players}) != spec.player_count
            or any(p["user_id"] <= 10000000 or type(p['place']) is not int
                   or not 1 <= p['place'] <= spec.player_count for p in players)):
        raise ValueError(f"排位结算需要{spec.player_count}名独立的注册玩家")
    conn = db._get_connection()
    try:
        with conn.cursor() as cur:
            cur.execute("SELECT pg_advisory_xact_lock_shared(hashtextextended(%s,0))", (RATING_WRITE_LOCK,))
            # Serialize retries of the same game, including concurrent deliveries.
            cur.execute("SELECT pg_advisory_xact_lock(hashtextextended(%s,0))", (str(game_id),))
            cur.execute("SELECT rule,result FROM rating_settlements WHERE game_id=%s", (str(game_id),))
            existing = cur.fetchone()
            if existing:
                if existing[0] != rating_rule or set(existing[1]) != {str(p['user_id']) for p in players}:
                    raise ValueError("重复结算的规则或玩家不一致")
                conn.commit()
                if spec.graded:
                    return {uid: {k: v for k, v in data.items() if not k.startswith('elo_')}
                            for uid, data in existing[1].items()}
                return existing[1]
            if spec.rule == 'riichi':
                rating_algorithm = rating_algorithm or (SANMA_PT_ALGORITHM if spec.player_count == 3 else RIICHI_PT_ALGORITHM)
                allowed = (SANMA_PT_ALGORITHM,) if spec.player_count == 3 else (RIICHI_PT_ALGORITHM, LEGACY_PT_ALGORITHM)
                if rating_algorithm not in allowed:
                    raise ValueError('不支持的立直段位算法版本')
                if rating_algorithm == SANMA_PT_ALGORITHM:
                    sanma_pt_details(spec.tier, spec.mode, '10级', players[0]['place'], [p['place'] for p in players])
                elif rating_algorithm == RIICHI_PT_ALGORITHM:
                    for player in players:
                        riichi_pt_details(spec.tier, spec.mode, '10级', player.get('match_points'))
            before = {}
            # Stable lock order prevents cross-game deadlocks.
            for player in sorted(players, key=lambda p: p['user_id']):
                uid = player['user_id']
                cur.execute("INSERT INTO rank_data(user_id) VALUES(%s) ON CONFLICT DO NOTHING", (uid,))
                cur.execute("SELECT guobiao_rank,guobiao_score FROM rank_data WHERE user_id=%s FOR UPDATE", (uid,))
                national = cur.fetchone()
                cur.execute("INSERT INTO rule_ratings(user_id,rule,elo) VALUES(%s,%s,%s) ON CONFLICT DO NOTHING", (uid,rating_rule,None if spec.graded else DEFAULT_R))
                cur.execute("SELECT rank_name,rank_score,elo,games FROM rule_ratings WHERE user_id=%s AND rule=%s FOR UPDATE", (uid,rating_rule))
                rank, score, elo, games = cur.fetchone()
                if spec.rule == 'guobiao':
                    rank, score = national
                before[uid] = (rank,float(score),None if spec.graded else float(elo),games)
            changes = [0.0] * len(players) if spec.graded else elo_deltas(
                [before[p['user_id']][2] for p in players], [p['place'] for p in players])
            result = {}
            for player, delta in zip(players, changes):
                uid = player['user_id']
                rank, score, elo, games = before[uid]
                pt = 0.0
                details = {}
                if spec.graded:
                    if rating_rule == 'riichi_sanma':
                        details = sanma_pt_details(spec.tier, spec.mode, rank, player['place'], [p['place'] for p in players])
                        pt = details['rating_pt']
                    elif spec.rule == 'riichi' and rating_algorithm == RIICHI_PT_ALGORITHM:
                        details = riichi_pt_details(spec.tier, spec.mode, rank, player['match_points'])
                        pt = details['rating_pt']
                    else:
                        start = 1 + sum(p['place'] < player['place'] for p in players)
                        count = sum(p['place'] == player['place'] for p in players)
                        pt = round(sum(calculate_pt(spec.tier,spec.mode,pos,rank) for pos in range(start,start+count))/count,2)
                        if spec.rule == 'riichi':
                            details['rating_algorithm'] = LEGACY_PT_ALGORITHM
                    new_rank, new_score = apply_pt(rank,score,pt)
                else:
                    new_rank, new_score = '', 0.0
                new_elo = None if spec.graded else round(elo+delta,2)
                result[str(uid)] = dict(rating_rule=rating_rule, rating_system='grade' if spec.graded else 'elo',
                    rank_before=rank if spec.graded else '', score_before=score, rank_after=new_rank,
                    score_after=new_score, pt=pt, rating_pt=pt, rating_games=games+1)
                result[str(uid)].update(details)
                if not spec.graded:
                    result[str(uid)].update(elo_before=elo, elo_after=new_elo, elo_delta=delta,
                                           rating_algorithm=ELO_ALGORITHM_VERSION)
                cur.execute("UPDATE rule_ratings SET rank_name=%s,rank_score=%s,elo=%s,games=%s,updated_at=CURRENT_TIMESTAMP WHERE user_id=%s AND rule=%s", (new_rank,new_score,new_elo,games+1,uid,rating_rule))
                if spec.rule == 'guobiao':
                    cur.execute("UPDATE rank_data SET guobiao_rank=%s,guobiao_score=%s,updated_at=CURRENT_TIMESTAMP WHERE user_id=%s", (new_rank,new_score,uid))
            cur.execute("INSERT INTO rating_settlements(game_id,rule,result,players,ended_at) VALUES(%s,%s,%s,%s,%s)",
                (str(game_id),rating_rule,Json(result),Json(players),
                 rating_end_time(ended_at) if ended_at is not None else datetime.now(timezone.utc)))
        conn.commit()
        return result
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def get_rule_leaderboard(db, rule='guobiao', limit=100):
    if rule not in RULES:
        raise ValueError('不支持的匹配规则')
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cur:
            cur.execute("""SELECT u.user_id, COALESCE(u.username,'') AS username,
                COALESCE(s.profile_image_id,1) AS profile_image_id,
                CASE WHEN %s='guobiao' THEN n.guobiao_rank ELSE r.rank_name END AS rank_name,
                CASE WHEN %s='guobiao' THEN n.guobiao_score ELSE r.rank_score END AS rank_score,
                COALESCE(r.elo,1500) AS elo, COALESCE(r.games,0) AS games
                FROM users u LEFT JOIN user_settings s ON s.user_id=u.user_id
                LEFT JOIN rank_data n ON n.user_id=u.user_id
                LEFT JOIN rule_ratings r ON r.user_id=u.user_id AND r.rule=%s
                WHERE u.user_id>10000000 AND (COALESCE(r.games,0)>0 OR (%s='guobiao' AND n.guobiao_rank<>'10级'))""", (rule,rule,rule,rule))
            rows = [dict(row) for row in cur.fetchall()]
        rows.sort(key=lambda r: (-get_rank_index(r['rank_name']),-r['rank_score'],r['user_id']) if rule in GRADE_RULES else (-r['elo'],-r['games'],r['user_id']))
        for i,row in enumerate(rows[:limit],1):
            if rule in GRADE_RULES:
                row.pop('elo', None)
            row.update(rank_position=i, rule=rule, system='grade' if rule in GRADE_RULES else 'elo',
                       guobiao_rank=row['rank_name'] or '', guobiao_score=row['rank_score'] or 0)
        return rows[:limit]
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def get_ranked_history(db, user_id, rule):
    if rule not in RULES:
        raise ValueError('不支持的匹配规则')
    if rule in ('riichi', 'riichi_sanma'):
        from .riichi.record_stats import get_history_stats
        return get_history_stats(db, user_id, ranked=True, sanma=rule == 'riichi_sanma')
    fields = ('total_rounds','win_count','self_draw_count','deal_in_count','total_fan_score',
              'total_win_turn','total_fangchong_score','first_place_count','second_place_count',
              'third_place_count','fourth_place_count','fulu_round_count','cuohe_count','total_round_score')
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cur:
            columns = ','.join(f'COALESCE(SUM({f}),0) AS {f}' for f in fields)
            from ..match.rating_rules import ranked_record_scope
            game_rule, sub_rule = ranked_record_scope(rule)
            cur.execute(f"SELECT rule,match_type AS mode,COUNT(*) AS total_games,{columns} FROM game_player_metrics WHERE user_id=%s AND rule=%s AND room_type='match' AND (%s IS NULL OR COALESCE(NULLIF(sub_rule, ''), 'sichuan/standard')=%s) GROUP BY rule,match_type", (user_id,game_rule,sub_rule,sub_rule))
            return [dict(row, rule=rule) for row in cur.fetchall()]
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
