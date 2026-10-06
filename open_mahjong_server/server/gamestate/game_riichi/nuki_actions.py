"""Optional north extraction: declare, resolve robbing, then commit and draw replacement."""
from .rule_logic import option
from .action_check import check_hepai, refresh_waiting_tiles, YAOCHUU


def can_nuki(game, seat):
    player = game.player_list[seat]
    return (game.sub_rule == 'riichi/sanma' and seat == game.current_player_index
            and game.game_status in ('waiting_hand_action', 'deal_card_after_gang', 'deal_card_after_nuki', 'deal_card')
            and getattr(player, 'has_draw_slot', False) and 44 in player.hand_tiles
            and len(game.tiles_list) > game.dead_wall_count and game.rinshan_count < 8
            and ('riichi' not in player.tag_list or player.hand_tiles[-1] == 44))


def check_nuki_ron(game):
    actions = {i: [] for i in range(len(game.player_list))}
    game._ron_shape_waiters = []
    mode = option(game, 'nuki_ron')
    if mode == 'none':
        return actions
    for p in game.player_list:
        if p.player_index == game.current_player_index:
            continue
        if mode == 'kokushi' and (p.combination_tiles or len(p.hand_tiles) != 13
                                 or any(t not in YAOCHUU for t in p.hand_tiles)):
            continue
        refresh_waiting_tiles(game, p.player_index)
        if 44 in p.waiting_tiles:
            game._ron_shape_waiters.append(p.player_index)
            check_hepai(game, actions, 44, p.player_index, 'nuki_ron')
            if actions[p.player_index]:
                actions[p.player_index].append('pass')
    return actions


async def begin_nuki(game):
    from .boardcast import broadcast_do_action
    from .kan_actions import commit_kan
    if not can_nuki(game, game.current_player_index):
        raise ValueError('当前状态不能拔北')
    p = game.player_list[game.current_player_index]
    game._pending_kan = dict(kind='nuki', normal=44, actual=44,
                             is_mo_gang=p.hand_tiles[-1] == 44)
    game.jiagang_tile = 44
    game.action_dict = check_nuki_ron(game)
    if any(game.action_dict.values()):
        await broadcast_do_action(game, action_list=['nuki'], action_player=game.current_player_index,
                                  cut_tile=44, is_claim=True)
        game._pending_kan['announced'] = True
        game.game_status = 'waiting_action_qianggang'
    else:
        await commit_kan(game)


async def commit_nuki(game, pending):
    from .wait_action import _clear_ippatsu_and_notify
    from .boardcast import broadcast_do_action
    from ..public.hand_slot_utils import remove_cut_tile, has_draw_slot, clear_draw_slot
    from ..public.game_record_manager import append_action_tick
    p = game.player_list[game.current_player_index]
    remove_cut_tile(p.hand_tiles, 44, pending['is_mo_gang'], draw_slot=has_draw_slot(p))
    clear_draw_slot(p)
    p.huapai_list.append(44)
    append_action_tick(game, ['nuki', game.current_player_index, 44, 'T' if pending['is_mo_gang'] else 'F'])
    await broadcast_do_action(game, action_list=['nuki'], action_player=game.current_player_index,
                              buhua_tile=44, is_mo_buhua=pending['is_mo_gang'], silent=pending.get('announced', False))
    await _clear_ippatsu_and_notify(game)
    game._first_round_valid = False
    game._pending_kan = None
    game.jiagang_tile = None
    game.game_status = 'deal_card_after_nuki'
