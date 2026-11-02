from typing import Any, Dict, List, Optional


PUBLIC_VENUES_SQL = """
    SELECT e.event_id, e.name, e.description, e.status, e.kind,
           e.entry_config, e.created_at,
           schedule.planned_start_at, schedule.planned_end_at,
           members.member_count,
           EXISTS (
               SELECT 1 FROM event_registrations mine
               WHERE mine.event_id = e.event_id AND mine.user_id = %s
                 AND mine.status = 'approved'
           ) OR EXISTS (
               SELECT 1 FROM event_admins admin
               WHERE admin.event_id = e.event_id AND admin.user_id = %s
           ) AS is_registered
    FROM events e
    LEFT JOIN LATERAL (
        SELECT a.planned_start_at, a.planned_end_at
        FROM event_applications a
        WHERE a.event_id = e.event_id AND a.status = 'approved'
        ORDER BY a.reviewed_at DESC NULLS LAST, a.application_id DESC
        LIMIT 1
    ) schedule ON TRUE
    LEFT JOIN LATERAL (
        SELECT COUNT(*)::integer AS member_count FROM (
            SELECT r.user_id FROM event_registrations r
            WHERE r.event_id = e.event_id AND r.status = 'approved'
            UNION
            SELECT admin.user_id FROM event_admins admin
            WHERE admin.event_id = e.event_id
        ) membership
    ) members ON TRUE
    WHERE e.status IN ('active', 'registered')
      AND (%s IS NULL OR CASE WHEN COALESCE(e.kind, 'event') = 'base'
           THEN 'base' ELSE 'event' END = %s)
    ORDER BY CASE e.status WHEN 'active' THEN 0 ELSE 1 END,
             e.kind ASC, e.created_at DESC, e.event_id ASC
"""


def public_venue_rows(cursor, parse_entry_config, kind: Optional[str], user_id: Optional[int]) -> List[Dict[str, Any]]:
    selected_kind = kind if kind in ("event", "base") else None
    cursor.execute(PUBLIC_VENUES_SQL, (user_id, user_id, selected_kind, selected_kind))
    rows = []
    for row in cursor.fetchall():
        item = dict(row)
        item["entry_config"] = parse_entry_config(item.get("entry_config"))
        for key in ("created_at", "planned_start_at", "planned_end_at"):
            value = item.get(key)
            item[key] = (value.isoformat() if hasattr(value, "isoformat") else str(value)) if value is not None else ""
        item["description"] = (item.get("description") or "")[:180]
        item["member_count"] = int(item.get("member_count") or 0)
        item["is_registered"] = bool(item.get("is_registered"))
        rows.append(item)
    return rows
