"""历史统计修复共用的严格玩家身份解析。"""


def original_player_index(record, user_id, fallback, count):
    title = record.get("game_title") or {}
    matches = [i for i in range(count) if str(title.get(f"p{i}_uid")) == str(user_id)]
    if len(matches) == 1:
        return matches[0]
    # 原谱记录了参与者时，不允许用旧索引覆盖缺失或冲突的身份。
    if matches or any(title.get(f"p{i}_uid") is not None for i in range(count)):
        raise ValueError("participant identity is missing or ambiguous in the record")
    if type(fallback) is int and 0 <= fallback < count:
        return fallback
    raise ValueError("cannot resolve original player index")
