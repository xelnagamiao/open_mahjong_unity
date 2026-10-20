"""A deterministic legal-action bot; never reads opponents' concealed tiles."""
import time
from ..public.ai.auto_cut_policy import is_auto_cut_bot, choose_auto_cut_action
from collections import Counter
from ...game_calculation.vietnamese.tiles import TILES,JOKERS,BONUS
from ..public.ai.pacing import wait_bot_delay


def choose_action(state,index):
    offered=state.action_dict[index];p=state.player_list[index]
    if is_auto_cut_bot(p):
        if 'vn_replace' in offered and (state.opening_queue is not None or 'cut' not in offered):
            return dict(action_type='vn_replace',target_tile=sum(1<<JOKERS.index(t) for t in state.replacement_candidates(index)))
        return choose_auto_cut_action(p, offered, state.legal_discard_tiles(index) if 'cut' in offered else None)
    for action in ('hu_self','hu','hu_special','buhua'):
        if action in offered:return dict(action_type=action)
    if 'vn_replace' in offered and (state.opening_queue is not None or 'cut' not in offered):
        # A first-draw optional replacement cannot be auto-passed forever.
        return dict(action_type='vn_replace',target_tile=sum(1<<JOKERS.index(t) for t in state.replacement_candidates(index)))
    for action in ('angang','jiagang'):
        if action in offered:return dict(action_type=action,target_tile=min(state.kong_tiles(index,action)))
    if 'vn_bounce' in offered:return dict(action_type='vn_bounce',target_tile=state.bounce_candidates(index)[0])
    if 'cut' in offered:
        counts=Counter(p.hand_tiles)
        def keep(tile):
            if tile in JOKERS:return 20
            if tile in BONUS:return -100
            return counts[tile]*3+sum(counts[tile+d]*(3-abs(d)) for d in (-2,-1,1,2) if tile<40 and (tile+d)//10==tile//10)
        legal=state.legal_discard_tiles(index)
        pos=min((i for i,t in enumerate(p.hand_tiles) if t in legal),key=lambda i:(keep(p.hand_tiles[i]),-i))
        return dict(action_type='cut',TileId=p.hand_tiles[pos],cutIndex=pos,cutClass=p.has_draw_slot and pos==len(p.hand_tiles)-1)
    if 'vn_claim' in offered:
        option=min(state.claim_options[index],key=lambda o:({'g':0,'k':1,'s':2}[o['kind']],o['joker_count'],o['versatility'],o['id']))
        return dict(action_type='vn_claim',target_tile=option['id'])
    return dict(action_type='ready' if 'ready' in offered else 'pass')


async def play_bot(state,index,tick):
    started=time.monotonic();data=choose_action(state,index)
    await wait_bot_delay(state,started)
    if state.server_action_tick!=tick or index not in state.waiting_players_list:return
    try:await state.submit_action(index,**data)
    except (TypeError,ValueError):return
