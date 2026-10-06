"""Declare a kan first; only consume four tiles after all robbing claims pass."""
from .action_check import check_action_jiagang, check_action_ankan, _normalize
from .boardcast import broadcast_do_action
from ..public.hand_slot_utils import has_draw_slot, clear_draw_slot, resolve_is_mo_gang, remove_angang_tiles, remove_cut_tile
from ..public.game_record_manager import player_action_record_angang, player_action_record_jiagang, append_action_tick


async def begin_kan(game, kind, tile):
    player = game.player_list[game.current_player_index]
    normal = _normalize(tile)
    actual = next(t for t in reversed(player.hand_tiles) if _normalize(t) == normal)
    while getattr(game, '_pending_kan_dora_count', 0) > 0:
        game._pending_kan_dora_count -= 1
        await game._reveal_kan_dora()
    game._pending_kan = dict(kind=kind, normal=normal, actual=actual,
        is_mo_gang=resolve_is_mo_gang(player.hand_tiles, normal, draw_slot=has_draw_slot(player)))
    game.jiagang_tile = actual
    game.action_dict = check_action_ankan(game, actual) if kind == 'angang' else check_action_jiagang(game, actual)
    if any(game.action_dict.values()):
        await broadcast_do_action(game, action_list=[kind], action_player=game.current_player_index,
                                  cut_tile=actual, is_claim=True)
        game._pending_kan['announced'] = True
        game.game_status = 'waiting_action_qianggang'
    else:
        await commit_kan(game)


async def commit_kan(game):
    from .wait_action import _clear_ippatsu_and_notify
    pending = getattr(game, '_pending_kan', None)
    if pending is None:
        # Old replay/test states can still resume the existing waiting stage.
        await _clear_ippatsu_and_notify(game)
        game.game_status = 'deal_card_after_gang'
        return
    player = game.player_list[game.current_player_index]
    normal, kind = pending['normal'], pending['kind']
    if kind == 'nuki':
        from .nuki_actions import commit_nuki
        await commit_nuki(game, pending)
        return
    if kind == 'angang':
        removed = remove_angang_tiles(player.hand_tiles, normal, draw_slot=has_draw_slot(player))
        mask=[2,removed[0],0,removed[1],0,removed[2],2,removed[3]]
        player.combination_tiles.append(f'G{normal}'); player.combination_mask.append(mask)
        player_action_record_angang(game,angang_tile=normal,is_mo_gang=pending['is_mo_gang'],combination_mask=mask)
        target=f'G{normal}'
        game._last_kan_type='ankan'
    else:
        index=player.combination_tiles.index(f'k{normal}')
        actual=remove_cut_tile(player.hand_tiles,pending['actual'],pending['is_mo_gang'],draw_slot=has_draw_slot(player))
        mask=player.combination_mask[index]
        flag_index=next(i for i in range(0,len(mask),2) if mask[i]==1)
        mask[flag_index:flag_index]=[3,actual]
        player.combination_tiles[index]=f'g{normal}'
        player_action_record_jiagang(game,jiagang_tile=normal,is_mo_gang=pending['is_mo_gang'],actual_tile=actual)
        target=f'k{normal}'
        game._last_kan_type='shouminkan'
    clear_draw_slot(player)
    await broadcast_do_action(game,action_list=[kind],action_player=game.current_player_index,
        combination_mask=mask,combination_target=target,is_mo_gang=pending['is_mo_gang'],silent=pending.get('announced',False))
    await game._broadcast_langyong_tags_if_changed()
    await _clear_ippatsu_and_notify(game)
    game._pending_kan=None;game.jiagang_tile=None;game.game_status='deal_card_after_gang'


async def rob_pending_kan(game):
    pending=getattr(game,'_pending_kan',None)
    if pending is None:
        return False
    player=game.player_list[game.current_player_index]
    if pending['kind'] == 'jiagang':
        meld_index = player.combination_tiles.index(f"k{pending['normal']}")
        declared_tiles = list(player.combination_mask[meld_index][1::2])
    elif pending['kind'] == 'nuki':
        declared_tiles = [44]
    else:
        declared_tiles = [t for t in player.hand_tiles if _normalize(t) == pending['normal']]
    actual=remove_cut_tile(player.hand_tiles,pending['actual'],pending['is_mo_gang'],draw_slot=has_draw_slot(player))
    clear_draw_slot(player)
    append_action_tick(game,['rk',game.current_player_index,actual,1 if pending['is_mo_gang'] else 0,
                             pending['kind'],declared_tiles])
    await broadcast_do_action(game,action_list=['rob_kan'],action_player=game.current_player_index,
                              cut_tile=actual,is_mo_gang=pending['is_mo_gang'],silent=True)
    game._pending_kan=None;game.jiagang_tile=None
    return True
