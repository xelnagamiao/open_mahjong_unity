"""Replay saved ranked games from a verified rank snapshot, without running the game server.

Only missing game_player_records.pt_change values are written. Every player's replay
must agree with the live rank balance and all recorded admin adjustments first.
"""
import argparse
from datetime import datetime
import hashlib
import inspect
import json
from pathlib import Path
import runpy
import sys

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parents[3]))
from server.match import rank_calculator as rules


def rules_fingerprint():
    constants = {key: getattr(rules, key) for key in
                 ('RANK_TABLE', 'TIER_BASE_SCORE', 'GAME_TYPE_MULTIPLIER',
                  'RANK_COEFFICIENTS', 'RANK_AVG_LOSS_PT')}
    # Source text is stable across Python versions; ast.dump formatting is not.
    functions = [inspect.getsource(fn).replace('\r\n', '\n').strip()
                 for fn in (rules.calculate_pt, rules.apply_pt)]
    data = json.dumps([constants, functions], sort_keys=True, ensure_ascii=True)
    return hashlib.sha256(data.encode()).hexdigest()


def same_rank(actual, expected):
    return actual[0] == expected[0] and abs(float(actual[1]) - float(expected[1])) < 0.001


def replay(baseline, users, ranks, games, audits):
    if baseline['rules_fingerprint'] != rules_fingerprint():
        raise ValueError('PT rules changed; the historical baseline needs review')
    cutoff = baseline['cutoff']
    states = {int(uid): (row['rank'], float(row['score'])) for uid, row in baseline['players'].items()}
    problems = {}
    for user in users:
        uid = user['user_id']
        if uid in states:
            if str(user['created_at']) != baseline['players'][str(uid)]['created_at']:
                problems[uid] = 'user identity differs from baseline'
        elif str(user['created_at']) > cutoff:
            states[uid] = ('10级', 0.0)
        else:
            problems[uid] = 'no verified initial rank'
    events = [(str(row['created_at']), 'game', row) for row in games]
    events += [(str(row['created_at']), 'audit', row) for row in audits]
    changes = []
    for timestamp, kind, row in sorted(events, key=lambda item: (item[0], item[1])):
        if timestamp <= cutoff:
            raise ValueError('Replay input predates baseline')
        if kind == 'audit':
            uid = int(row['target_id'])
            before, after = row['payload']['before'], row['payload']['after']
            if before and (uid not in states or not same_rank(states[uid],
                    (before['guobiao_rank'], before['guobiao_score']))):
                problems[uid] = 'admin adjustment does not match replay'
            states[uid] = (after['guobiao_rank'], float(after['guobiao_score']))
            continue
        players = row['players']
        queue = row.get('queue_type')
        parsed = rules.parse_queue_type(queue) if queue else None
        if parsed is None:
            tier = row.get('title_tier') or (players[0].get('match_tier') if players else None)
            game_type = {'1': 'dongfeng', '2': 'banzhuang', '4': 'quanzhuang'}.get(str(row.get('max_round')))
        else:
            tier, game_type = parsed
        valid = (len(players) == 4 and len({p['user_id'] for p in players}) == 4
                 and all(p['rule'] == 'guobiao' for p in players)
                 and tier in rules.TIER_BASE_SCORE and game_type in rules.GAME_TYPE_MULTIPLIER)
        if not valid:
            for player in players:
                problems[player['user_id']] = 'incomplete game or unrecognized ranked rules'
            continue
        for player in players:
            uid = player['user_id']
            if uid not in states:
                problems[uid] = 'no verified initial rank'
                continue
            old_rank, old_score = states[uid]
            position = 1 + sum(peer['score'] > player['score'] for peer in players)
            tied = sum(peer['score'] == player['score'] for peer in players)
            pt = round(sum(rules.calculate_pt(tier, game_type, pos, old_rank)
                           for pos in range(position, position + tied)) / tied, 2)
            states[uid] = rules.apply_pt(old_rank, old_score, pt)
            existing = player.get('pt_change')
            if existing is not None and abs(float(existing) - pt) > 0.001:
                problems[uid] = 'saved PT differs from replay'
            elif existing is None:
                changes.append(dict(game_id=row['game_id'], user_id=uid, pt_change=pt,
                                    score=player['score'], rank=player['rank']))
    final = {row['user_id']: (row['guobiao_rank'], row['guobiao_score']) for row in ranks}
    for uid in {p['user_id'] for game in games for p in game['players']}:
        if uid not in states or uid not in final or not same_rank(states[uid], final[uid]):
            # Settlements update rank_data before saving the game; a later run retries these users.
            problems[uid] = 'current rank differs from replay (possibly a settlement in progress)'
    return [row for row in changes if row['user_id'] not in problems], problems


def read_inputs(conn, cutoff):
    from psycopg2.extras import RealDictCursor
    queries = {
        'users': ('SELECT user_id, created_at FROM users', None),
        'ranks': ('SELECT user_id, guobiao_rank, guobiao_score FROM rank_data', None),
        'games': ("""SELECT gr.game_id, gr.created_at,
            gr.record #>> '{game_title,match_queue_type}' AS queue_type,
            gr.record #>> '{game_title,match_tier}' AS title_tier,
            gr.record #>> '{game_title,max_round}' AS max_round,
            jsonb_agg(jsonb_build_object('user_id',p.user_id,'score',p.score,'rank',p.rank,
              'pt_change',p.pt_change,'match_tier',p.match_tier,'rule',p.rule) ORDER BY p.user_id) AS players
          FROM game_records gr JOIN game_player_records p ON p.game_id=gr.game_id
          WHERE p.room_type='match' AND gr.created_at > %s::timestamp
          GROUP BY gr.game_id ORDER BY gr.created_at,gr.game_id""", (cutoff,)),
        'audits': ("""SELECT id, action, target_id, payload, created_at FROM admin_audit_log
          WHERE action IN ('rank.update','rank.reset') AND created_at > %s::timestamp
          ORDER BY created_at,id""", (cutoff,)),
    }
    result = {}
    with conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute("SET LOCAL statement_timeout = '20s'")
        for name, (sql, params) in queries.items():
            cur.execute(sql, params)
            result[name] = [dict(row) for row in cur.fetchall()]
    return result


def apply_changes(conn, changes):
    from psycopg2.extras import execute_values
    written = 0
    for start in range(0, len(changes), 200):
        chunk = changes[start:start + 200]
        values = [(r['game_id'], r['user_id'], r['pt_change'], r['score'], r['rank']) for r in chunk]
        with conn:
            with conn.cursor() as cur:
                cur.execute("SET LOCAL lock_timeout = '500ms'; SET LOCAL statement_timeout = '5s'")
                execute_values(cur, """UPDATE game_player_records p SET pt_change=v.pt::numeric(12,2)
                    FROM (VALUES %s) AS v(game_id,user_id,pt,score,rank)
                    WHERE p.game_id=v.game_id AND p.user_id=v.user_id AND p.score=v.score
                      AND p.rank=v.rank AND p.room_type='match' AND p.rule='guobiao'
                      AND p.pt_change IS NULL""", values, page_size=200)
                written += cur.rowcount
                execute_values(cur, """SELECT COUNT(*) FROM game_player_records p
                    JOIN (VALUES %s) AS v(game_id,user_id,pt,score,rank)
                      ON p.game_id=v.game_id AND p.user_id=v.user_id
                    WHERE p.pt_change=v.pt::numeric(12,2) AND p.score=v.score AND p.rank=v.rank""",
                    values, page_size=200)
                if cur.fetchone()[0] != len(chunk):
                    raise ValueError('Records changed since replay; batch rolled back')
    return written


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', required=True)
    parser.add_argument('--config', required=True)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    baseline_path = Path(args.baseline)
    baseline = json.loads(baseline_path.read_text(encoding='utf-8'))
    cfg = runpy.run_path(args.config)['Config']
    import psycopg2
    conn = psycopg2.connect(host=cfg.host, port=cfg.port, user=cfg.user,
                           password=cfg.password, dbname=cfg.database,
                           application_name='weekly_pt_backfill', connect_timeout=5)
    try:
        conn.set_session(readonly=True, isolation_level='REPEATABLE READ')
        with conn.cursor() as cur:
            cur.execute('SELECT pg_try_advisory_lock(70620, 20260925)')
            if not cur.fetchone()[0]:
                print(json.dumps(dict(busy=True, written=0)))
                return
        inputs = read_inputs(conn, baseline['cutoff'])
        changes, problems = replay(baseline, **inputs)
        conn.rollback()
        summary = dict(games=len(inputs['games']), candidates=len(changes),
                       pending_users=len(problems), written=0, cutoff=baseline['cutoff'])
        if args.apply and changes:
            audit_dir = baseline_path.parent / 'runs'
            audit_dir.mkdir(parents=True, exist_ok=True)
            audit_path = audit_dir / (datetime.now().strftime('%Y%m%d-%H%M%S-%f') + '.json')
            with audit_path.open('x', encoding='utf-8') as stream:
                json.dump(dict(summary=summary, changes=changes, problems=problems,
                               baseline_sha256=hashlib.sha256(baseline_path.read_bytes()).hexdigest()),
                          stream, ensure_ascii=False, default=str)
            conn.set_session(readonly=False, isolation_level='READ COMMITTED')
            summary['written'] = apply_changes(conn, changes)
        print(json.dumps(summary, ensure_ascii=True))
    finally:
        conn.close()


if __name__ == '__main__':
    main()
