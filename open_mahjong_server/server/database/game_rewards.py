"""One reward contract for replay snapshots, live settlement and history repair."""
from .round_progress import completed_hand_count, participant_ids, reward_human_ids

REWARD_VERSION = 3
REWARD_MINOR_PER_HAND = 5


def eligible_reward_ids(record, indexed_ids):
    if (record.get('game_title') or {}).get('room_type') != 'match':
        return []
    return reward_human_ids(record, indexed_ids)


def build_currency_reward(record, indexed_ids, registered_ids):
    """Registration comes from users, never from an ID range or a client packet."""
    hands = completed_hand_count(record)
    humans = eligible_reward_ids(record, indexed_ids)
    registered = set(registered_ids)
    return dict(version=REWARD_VERSION, completed_hands=hands, eligible=bool(humans), players=[
        dict(user_id=uid, currency_delta_minor=hands * REWARD_MINOR_PER_HAND
             if uid in humans and uid in registered else 0,
             currency_reward_status='saved' if uid in humans and uid in registered and hands
             else 'not_registered' if uid in humans and uid not in registered else 'not_applicable')
        for uid in participant_ids(record)])


def apply_currency_to_match_result(record):
    result = record.get('match_result')
    if result is None:
        return
    reward = record.get('currency_reward')
    saved = {p['user_id']: p for p in reward['players']} if reward else {}
    pending = bool(eligible_reward_ids(record, participant_ids(record)))
    for player in result['players']:
        snapshot = saved.get(player['user_id'])
        player['currency_delta_minor'] = snapshot['currency_delta_minor'] if snapshot else None if pending else 0
        player['currency_reward_status'] = snapshot['currency_reward_status'] if snapshot else 'pending' if pending else 'not_applicable'
        player['currency_reward_version'] = reward['version'] if reward else REWARD_VERSION


def attach_currency_to_end_packet(state, packet):
    """Delivery uses committed replay output; a failed save never advertises a reward."""
    info = packet.get('game_end_info')
    if not info:
        return packet
    record = getattr(state, 'game_record', None) or {}
    reward = record.get('currency_reward')
    snapshots = {p['user_id']: p for p in reward['players']} if reward else {}
    humans = eligible_reward_ids(record, participant_ids(record))
    seats = {getattr(p, 'original_player_index', None): p.user_id for p in getattr(state, 'player_list', [])}
    rows = {}
    for key, player in info.get('player_final_data', {}).items():
        uid = player.get('user_id') or seats.get(player.get('original_player_index'))
        saved = snapshots.get(uid)
        rows[key] = dict(player, currency_delta_minor=saved['currency_delta_minor'] if saved else None if uid in humans else 0,
                         currency_reward_status=saved['currency_reward_status'] if saved else 'pending' if uid in humans else 'not_applicable',
                         currency_reward_version=reward['version'] if reward else REWARD_VERSION)
    return dict(packet, game_end_info=dict(info, player_final_data=rows))
