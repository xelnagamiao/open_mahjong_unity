AI_RESERVED_MAX_USER_ID = 10

def ai_player_count(player_list) -> int:
    return sum(1 for p in player_list if p.user_id <= AI_RESERVED_MAX_USER_ID)

def too_many_ai_for_spectator(player_list) -> bool:
    return ai_player_count(player_list) >= 3
