"""Title ownership and equipment. All mutations recheck database authority."""
from pathlib import Path

from psycopg2.extras import RealDictCursor

NO_TITLE = 1


def ensure_title_tables(cursor):
    cursor.execute(Path(__file__).with_name("title_schema.sql").read_text(encoding="utf-8"))


def _catalog(cursor):
    cursor.execute("SELECT title_id, name, description, is_enabled, sort_order FROM titles ORDER BY sort_order, title_id")
    return [dict(row) for row in cursor.fetchall()]


def _state(cursor, user_id):
    catalog = _catalog(cursor)
    cursor.execute("""SELECT ut.title_id, ut.granted_at FROM user_titles ut
        JOIN titles t USING (title_id) WHERE user_id = %s ORDER BY t.sort_order, ut.title_id""", (user_id,))
    owned = [{"title_id": row["title_id"], "granted_at": row["granted_at"].isoformat()} for row in cursor.fetchall()]
    cursor.execute("SELECT title_id FROM user_settings WHERE user_id = %s", (user_id,))
    row = cursor.fetchone()
    selected = row["title_id"] if row else NO_TITLE
    allowed = {entry["title_id"] for entry in catalog if entry["is_enabled"]}
    if selected not in allowed.intersection(entry["title_id"] for entry in owned):
        selected = NO_TITLE
    return {"user_id": user_id, "equipped_title_id": selected, "catalog": catalog, "owned": owned}


def get_title_state(db, user_id):
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            return _state(cursor, user_id)
    finally:
        conn.rollback()
        db._put_connection(conn)


def get_title_catalog(db):
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            return _catalog(cursor)
    finally:
        conn.rollback()
        db._put_connection(conn)


def equip_title(db, user_id, title_id):
    if type(title_id) is not int or title_id < 1 or title_id > 2147483647:
        raise ValueError("无效的头衔 ID")
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            # Lock order matches Node: title -> user -> grant/settings. Disabling and
            # revoking cannot race an equip and leave an unauthorized title selected.
            if title_id != NO_TITLE:
                cursor.execute("SELECT is_enabled FROM titles WHERE title_id = %s FOR SHARE", (title_id,))
                title = cursor.fetchone()
                if not title or not title["is_enabled"]:
                    raise ValueError("此头衔已停用或不存在")
            cursor.execute("SELECT user_id FROM users WHERE user_id = %s FOR UPDATE", (user_id,))
            if not cursor.fetchone():
                raise ValueError("用户不存在")
            if title_id != NO_TITLE:
                cursor.execute("SELECT 1 FROM user_titles WHERE user_id = %s AND title_id = %s", (user_id, title_id))
                if not cursor.fetchone():
                    raise ValueError("尚未获授此头衔")
            cursor.execute("""INSERT INTO user_settings (user_id, title_id) VALUES (%s, %s)
                ON CONFLICT (user_id) DO UPDATE SET title_id = EXCLUDED.title_id, updated_at = CURRENT_TIMESTAMP""",
                           (user_id, title_id))
            state = _state(cursor, user_id)
        conn.commit()
        return state
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
