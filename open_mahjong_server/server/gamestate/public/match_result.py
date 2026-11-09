"""Immutable, rule-neutral match results stored with completed replays.

Ratings are settlement output, never recalculated by replay readers. Historical
records are upgraded offline; this module has no legacy-record fallback.
"""
from copy import deepcopy
import math

VERSION = 1


def number(value, field):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError(f"终局字段 {field} 必须为有限数字")
    return value


def build_match_result(record, participants, *, missing_rating_system=None):
    title = record['game_title']
    rule, sub = title['rule'], title['sub_rule']
    if rule == 'free':
        raise ValueError('自由模式不保存牌谱终局')
    if not title.get('end_time'):
        raise ValueError('对局尚未结束')
    players = sorted(deepcopy(participants), key=lambda p: p['original_player_index'])
    from .player_count import player_count_for_sub_rule
    count = player_count_for_sub_rule(sub)
    if [p['original_player_index'] for p in players] != list(range(count)):
        raise ValueError('终局玩家原始座位不完整')
    rated = title.get('room_type') == 'match'
    ratings = title.get('rating_result') or {}
    teams = title.get('team_by_player_index')
    roles = title.get('team_role_by_player_index')
    team_result = title.get('doubles_result')
    special = title.get(f'{rule}_final_result') or {}
    final = []
    for p in players:
        seat, uid = p['original_player_index'], p['user_id']
        if int(title[f'p{seat}_uid']) != uid:
            raise ValueError('终局玩家与牌谱原始座位不一致')
        item = {key: p[key] for key in ('user_id', 'username', 'original_player_index', 'rank', 'score')}
        number(item['score'], 'score')
        if not 1 <= item['rank'] <= count:
            raise ValueError('终局名次无效')
        item.update(pt=0.0, rating_pt=0.0, rating_system='none', rating_status='not_applicable')
        if rated:
            rating = ratings.get(str(uid))
            if not rating:
                if missing_rating_system not in ('grade', 'elo'):
                    raise ValueError(f'匹配场缺少玩家 {uid} 的评分结算')
                # Only the one-time migration explicitly marks known missing history.
                rating = dict(rating_system=missing_rating_system, rating_pt=None,
                              elo_before=None, elo_after=None, elo_delta=None)
            system = rating.get('rating_system')
            if system not in ('grade', 'elo'):
                raise ValueError('匹配场评分类型无效')
            item.update(deepcopy(rating))
            if ratings.get(str(uid)) is None:
                item.update(pt=None, rating_status='missing')
            elif system == 'grade':
                item['rating_status'] = 'saved'
                item['pt'] = item['rating_pt'] = number(rating.get('rating_pt'), 'rating_pt')
            else:
                item['rating_status'] = 'saved'
                for key in ('elo_before', 'elo_after', 'elo_delta'):
                    item[key] = number(rating.get(key), key)
                item['pt'] = item['rating_pt'] = 0.0
        points = title.get('riichi_points', {}).get(str(seat))
        if points is not None:
            item['match_points'] = number(points, 'match_points')
        detail = special.get(str(seat), special.get(seat, {}))
        if 'competition_points' in detail:
            item['competition_points'] = number(detail['competition_points'], 'competition_points')
        if 'points' in detail:
            item['match_points'] = number(detail['points'], 'match_points')
        if teams is not None:
            if len(teams) != count or not team_result:
                raise ValueError('双打终局缺少队伍结果')
            team = teams[seat]
            item.update(team_id=team, team_result_reason=team_result['reason'])
            for source, target in (('scores', 'team_score'), ('match_points', 'team_match_points'),
                                   ('small_scores', 'team_small_score'), ('imp_scores', 'team_imp_score')):
                values = team_result.get(source)
                if values is not None:
                    value = values[team] if isinstance(values, list) else values.get(str(team), values.get(team))
                    item[target] = number(value, target)
            if roles is not None:
                item['team_role'] = roles[seat]
        final.append(item)
    result = dict(version=VERSION, rule=rule, sub_rule=sub, room_type=title.get('room_type', 'custom'),
                player_count=count, players=final,
                duplicate_table=title.get('duplicate_table'),
                duplicate_match=deepcopy(record.get('duplicate_match')),
                sichuan_match=deepcopy(title.get('sichuan_match')))
    from ...database.game_rewards import apply_currency_to_match_result
    apply_currency_to_match_result(dict(record, match_result=result))
    return result


def attach_match_result(record, player_list):
    """Called before persistence, including completed bot games saved locally."""
    if not (record.get('game_title') or {}).get('end_time'):
        return  # An in-progress recording has no match result.
    participants = [dict(user_id=p.user_id, username=p.username, score=p.score,
                         original_player_index=p.original_player_index,
                         rank=p.record_counter.rank_result) for p in player_list]
    record['match_result'] = build_match_result(record, participants)
