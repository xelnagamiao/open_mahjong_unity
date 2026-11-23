"""Seed single-table replay fixtures in the same physical format as the service."""
from psycopg2.extras import Json
from .migrate import make_replay


def seed_single(db, sid, payload):
    body=make_replay([{"game_id":sid, "record":{"game_title":{},"game_round":{},**payload}}])
    db.sql("""WITH r AS (INSERT INTO game_replays(game_id,record) VALUES(%s,%s)
           RETURNING game_id,created_at)
           INSERT INTO game_record_tables(game_id,replay_id,table_id,created_at)
           SELECT game_id,game_id,'main',created_at FROM r""", (sid,Json(body)))
