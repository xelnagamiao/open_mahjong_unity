"""补花广播：补花与补花摸牌拆成两条 do_action，中间固定间隔（与杠后摸牌对齐）。"""
import asyncio

from ..public.hand_slot_utils import has_draw_slot, resolve_is_mo_buhua
from ..public.game_record_manager import player_action_record_buhua, player_action_record_deal
from .boardcast import broadcast_do_action

# 手补/手杠后岭上摸牌的显示间隔（秒）；服务端真实等待，客户端即时处理即可
HAND_SETTLE_GAP_SEC = 0.3


async def broadcast_buhua_then_deal(
    game_state,
    *,
    action_player: int,
    max_tile: int,
    deal_tile: int,
    is_mo_buhua: bool,
) -> None:
    """先广播补花，等待 HAND_SETTLE_GAP_SEC，再广播补花摸牌。"""
    await broadcast_do_action(
        game_state,
        action_list=["buhua"],
        action_player=action_player,
        buhua_tile=max_tile,
        is_mo_buhua=is_mo_buhua,
    )
    await asyncio.sleep(HAND_SETTLE_GAP_SEC)
    await broadcast_do_action(
        game_state,
        action_list=["deal_buhua_tile"],
        action_player=action_player,
        deal_tile=deal_tile,
    )


async def perform_buhua_and_broadcast(
    game_state,
    player_index: int,
    *,
    refresh_waiting: bool = False,
    huapai_before_draw: bool = True,
    instant: bool = False,
) -> int:
    """
    执行补花逻辑并广播。返回补花后摸到的 deal_tile。
    instant=True：开局补花轮光速摸牌，补花与摸牌合并为一条 do_action（与历史行为一致）；
    instant=False：对局中补花，拆成两条 do_action 并在中间等待 HAND_SETTLE_GAP_SEC。
    huapai_before_draw=True：开局补花轮顺序（先 huapai 再 get_gang_tile）；
    False：对局中补花（refresh → get_gang_tile → huapai）。
    """
    from .duplicate_rules import duplicate_rules_for
    duplicate_rules = duplicate_rules_for(game_state)
    if duplicate_rules and not duplicate_rules.can_draw(player_index):
        raise ValueError("本人牌山已空，不能补花")
    player = game_state.player_list[player_index]
    hand = player.hand_tiles
    max_tile = max(hand)
    is_mo_buhua = resolve_is_mo_buhua(hand, max_tile, draw_slot=has_draw_slot(player))
    hand.remove(max_tile)

    if refresh_waiting:
        from .action_check import refresh_waiting_tiles
        refresh_waiting_tiles(game_state, player_index)

    if huapai_before_draw:
        player.huapai_list.append(max_tile)
        player.get_gang_tile(game_state.tiles_list, game_state)
    else:
        player.get_gang_tile(game_state.tiles_list, game_state)
        player.huapai_list.append(max_tile)

    deal_tile = player.hand_tiles[-1]
    player_action_record_buhua(
        game_state,
        max_tile=max_tile,
        action_player=player_index,
        is_mo_buhua=is_mo_buhua,
    )
    player_action_record_deal(
        game_state,
        deal_tile=deal_tile,
        deal_type="bd",
        action_player=player_index,
    )
    if instant:
        await broadcast_do_action(
            game_state,
            action_list=["buhua", "deal_buhua_tile"],
            action_player=player_index,
            buhua_tile=max_tile,
            deal_tile=deal_tile,
            is_mo_buhua=is_mo_buhua,
        )
    else:
        await broadcast_buhua_then_deal(
            game_state,
            action_player=player_index,
            max_tile=max_tile,
            deal_tile=deal_tile,
            is_mo_buhua=is_mo_buhua,
        )
    return deal_tile
