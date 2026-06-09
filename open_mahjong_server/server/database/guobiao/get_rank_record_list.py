"""
天梯（排位）对局记录列表（挂载到 DatabaseManager）
"""
import logging
from psycopg2 import Error
from psycopg2.extras import RealDictCursor

logger = logging.getLogger(__name__)


def get_rank_record_list(db_manager, limit: int = 20) -> list:
    """
    获取全服最近 N 局天梯对局元数据（room_type = match），非按玩家过滤。
    场次名优先从牌谱 JSON game_title.match_queue_type 解析。
    无记录时返回空列表，不抛错。
    """
    conn = None
    cursor = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor(cursor_factory=RealDictCursor)

        cursor.execute(
            """
            SELECT gr.game_id
            FROM game_records gr
            WHERE EXISTS (
                SELECT 1
                FROM game_player_records gpr
                WHERE gpr.game_id = gr.game_id
                  AND gpr.room_type = 'match'
            )
            ORDER BY gr.created_at DESC
            LIMIT %s
            """,
            (limit,),
        )
        game_rows = cursor.fetchall()
        if not game_rows:
            logger.info("全服暂无天梯对局记录")
            return []

        game_ids = [row["game_id"] for row in game_rows]
        placeholders = ",".join(["%s"] * len(game_ids))
        cursor.execute(
            f"""
            SELECT
                gpr.game_id,
                gpr.user_id,
                gpr.username,
                gpr.score,
                gpr."rank" AS player_rank,
                gpr.original_player_index,
                gpr.rule,
                gpr.sub_rule,
                gpr.match_type,
                gr.created_at,
                gr.record->'game_title'->>'match_queue_type' AS match_queue_type
            FROM game_player_records gpr
            INNER JOIN game_records gr ON gr.game_id = gpr.game_id
            WHERE gpr.game_id IN ({placeholders})
            ORDER BY gr.created_at DESC, gpr."rank", gpr.original_player_index NULLS LAST, gpr.score DESC
            """,
            game_ids,
        )

        games_dict = {}
        for row in cursor.fetchall():
            game_id = row["game_id"]
            if game_id not in games_dict:
                games_dict[game_id] = {
                    "game_id": game_id,
                    "created_at": str(row["created_at"]),
                    "rule": row["rule"],
                    "sub_rule": row.get("sub_rule"),
                    "match_type": row.get("match_type"),
                    "match_queue_type": row.get("match_queue_type"),
                    "players": [],
                }
            games_dict[game_id]["players"].append(
                {
                    "user_id": row["user_id"],
                    "username": row.get("username") or "",
                    "score": row["score"] if row["score"] is not None else 0,
                    "rank": row["player_rank"],
                    "original_player_index": row.get("original_player_index"),
                }
            )

        ordered = []
        for gid in game_ids:
            if gid in games_dict:
                ordered.append(games_dict[gid])

        logger.info(f"全服天梯对局 {len(ordered)} 条")
        return ordered
    except Exception as e:
        logger.error(f"获取天梯对局列表失败: {e}", exc_info=True)
        if conn:
            try:
                conn.rollback()
            except Exception:
                pass
        return []
    finally:
        if cursor is not None:
            try:
                cursor.close()
            except Exception:
                pass
        if conn is not None:
            db_manager._put_connection(conn)
