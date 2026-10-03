"""Pure decisions for riichi actions, round endings and final results."""
from ...game_calculation.riichi.rule_config import normalize_riichi_config
from ...game_calculation.riichi.riichi_tile_converter import tile_id_to_34, convert_combination

DEFAULTS = normalize_riichi_config()


def option(game, key):
    return getattr(game, 'detailed_config', {}).get(key, DEFAULTS[key])


def is_first_draw(game, player):
    return not player.discard_origin_tiles and not any(p.combination_tiles for p in game.player_list)


def ankan_allowed(game, player, tile):
    from .action_check import _normalize
    normal = _normalize(tile)
    if sum(_normalize(t) == normal for t in player.hand_tiles) != 4:
        return False
    if 'riichi' not in player.tag_list:
        return True
    if not player.hand_tiles or _normalize(player.hand_tiles[-1]) != normal:
        return False
    before = [_normalize(t) for t in player.hand_tiles[:-1]]
    after = [_normalize(t) for t in player.hand_tiles if _normalize(t) != normal]
    melds = list(player.combination_tiles) + [f'G{normal}']
    old_waits = game.calculation_service.Riichi_tingpai_check(before, player.combination_tiles)
    new_waits = game.calculation_service.Riichi_tingpai_check(after, melds)
    if not old_waits or set(old_waits) != set(new_waits):
        return False
    if option(game, 'riichi_kan_rule') == 'wait':
        return True
    from mahjong.hand_calculating.divider import HandDivider
    from ...game_calculation.riichi.riichi_hepai_check import Riichi_Hepai_Check
    checker = Riichi_Hepai_Check()
    library_melds = checker._build_melds(player.combination_tiles, player.combination_mask, set())
    exposed = [tile for combo in player.combination_tiles for tile in convert_combination(combo)[1]]
    for wait in old_waits:
        counts = [0] * 34
        for tid in before + [wait] + exposed:
            counts[tile_id_to_34(tid)] += 1
        triplet = [tile_id_to_34(normal)] * 3
        shapes = HandDivider.divide_hand(counts, library_melds)
        if not shapes or any(triplet not in shape for shape in shapes):
            return False
        if option(game, 'riichi_kan_rule') == 'shape_yaku':
            ctx = dict(is_riichi=True, is_tsumo=True, player_wind=player.player_index,
                       round_wind=(game.current_round-1)//4%4, red_dora=False,
                       has_open_tanyao=option(game, 'open_tanyao'))
            a = checker.hepai_check(before+[wait], player.combination_tiles, [], wait, ctx)
            b = checker.hepai_check(after+[wait], melds, [], wait, ctx)
            if a.get('han', 0) > b.get('han', 0):
                return False
    return True


def match_should_end(game, renchan, round_after):
    if game.open_tobi and any(p.score < 0 for p in game.player_list):
        return True
    if getattr(game, '_cuohe_triggered', False):
        return False
    scheduled = game.max_round * 4
    # An abortive draw repeats the hand even when the dealer leads.
    if game.hu_class in ('jiuzhongjiupai','four_wind_abort','four_kan_abort','four_riichi_abort','three_ron_abort'):
        return False
    if round_after < scheduled or (round_after == scheduled and not renchan):
        return False
    players = sorted(game.player_list, key=lambda p: (-p.score, p.original_player_index))
    dealer = game.player_list[0]
    target = option(game, 'target_score') or game._starting_score() * 6 // 5
    if renchan:
        stopping = option(game, 'tenpai_yame' if game.hu_class == 'ryuukyoku' else 'agari_yame')
        return stopping and players[0] is dealer and dealer.score >= target
    if game.max_round >= 4 or not game.open_xiru:
        return round_after > scheduled
    if any(p.score >= target for p in players):
        return True
    limit = option(game, 'extension_rounds')
    return bool(limit and round_after > scheduled + limit)


def nagashi_winners(game):
    from .action_check import YAOCHUU, _normalize
    if not option(game, 'nagashi_mangan'):
        return []
    return [p.player_index for p in game.player_list
            if p.discard_origin_tiles and len(p.discard_origin_tiles) == len(p.discard_tiles)
            and all(_normalize(t) in YAOCHUU for t in p.discard_origin_tiles)
            and (option(game, 'nagashi_allow_calls') or not p.combination_tiles)]


def draw_payments(game, tenpai):
    winners = nagashi_winners(game)
    changes = {i: 0 for i in range(4)}
    if winners:
        for winner in winners:
            for payer in range(4):
                if payer == winner:
                    continue
                pay = 4000 if winner == 0 or payer == 0 else 2000
                changes[payer] -= pay
                changes[winner] += pay
        return changes, winners
    noten = [i for i in range(4) if i not in tenpai]
    total = option(game, 'noten_penalty')
    if tenpai and noten:
        for i in noten: changes[i] -= total // len(noten)
        for i in tenpai: changes[i] += total // len(tenpai)
    return changes, []


def record_pao_call(game, caller, discarder, action, tile):
    if not option(game, 'pao') or action not in ('peng', 'gang'):
        return
    player = game.player_list[caller]
    if not hasattr(player, 'pao_liability'):
        player.pao_liability = {}
    visible = {int(c[1:]) for c in player.combination_tiles if c[0] in ('k','g','G')}
    if tile in (45,46,47) and {45,46,47} <= visible:
        player.pao_liability.setdefault('大三元', discarder)
    if tile in (41,42,43,44) and {41,42,43,44} <= visible:
        player.pao_liability.setdefault('大四喜', discarder)
    if action == 'gang' and option(game, 'pao_suukantsu') and sum(c[0] in ('g','G') for c in player.combination_tiles) == 4:
        player.pao_liability.setdefault('四杠子', discarder)


def apply_pao(game, changes, winner, result, apply_honba):
    if not option(game, 'pao') or not result.get('yakuman_multiplier'):
        return
    liabilities = getattr(game.player_list[winner], 'pao_liability', {})
    components = result.get('yakuman_components', {})
    payments = [(seat, min(int(components[name]), int(result['yakuman_multiplier'])))
                for name, seat in liabilities.items() if name in components]
    if not payments:
        return
    if option(game, 'pao_scope') == 'all':
        payments = [(payments[0][0], int(result['yakuman_multiplier']))]
    remaining = int(result['yakuman_multiplier'])
    tsumo = game.hu_class == 'hu_self'
    def factor(seat):
        return game._langyong_multiplier(seat, winner) if game._is_langyong() else 1
    for liable, multiplier in payments:
        count = min(multiplier, remaining); remaining -= count
        total = count * (48000 if winner == 0 else 32000)
        if tsumo:
            for seat in range(4):
                if seat != winner:
                    original = count * (16000 if winner == 0 or seat == 0 else 8000) * factor(seat)
                    changes[seat] += original
                    changes[winner] -= original
            changes[liable] -= total * factor(liable)
            changes[winner] += total * factor(liable)
        else:
            shift = total // 2
            refunded = shift * factor(game.current_player_index)
            charged = shift * factor(liable)
            changes[game.current_player_index] += refunded
            changes[liable] -= charged
            changes[winner] += charged - refunded
    if apply_honba and game.honba:
        liable = payments[0][0]
        if tsumo:
            for seat in range(4):
                if seat != winner: changes[seat] += game.honba * 100
            changes[liable] -= game.honba * 300
        elif option(game, 'pao_honba') == 'liable':
            changes[game.current_player_index] += game.honba * 300
            changes[liable] -= game.honba * 300


def finalize_scores(game):
    """Settle deposits once, then calculate result points without rewriting table scores."""
    if getattr(game, '_riichi_finalized', False):
        return
    game._riichi_finalized = True
    ranked = sorted(game.player_list, key=lambda p: (-p.score, p.original_player_index))
    tie_scores = {p.original_player_index: p.score for p in ranked}
    deposits = game.riichi_sticks * 1000
    mode = option(game, 'end_deposits')
    if deposits and mode != 'discard':
        recipients = ranked[:1] if mode == 'winner' else [p for p in ranked if p.score == ranked[0].score]
        units, remainder = divmod(10, len(recipients))
        for i,p in enumerate(recipients): p.score += (units + (i < remainder)) * 100 * game.riichi_sticks
        game.riichi_sticks = 0
    # Ties for uma are determined before distributing split deposits.
    kind = option(game, 'rank_points')
    uma = {'none':[0,0,0,0], '5_15':[15,5,-5,-15], '10_20':[20,10,-10,-20],
           '10_30':[30,10,-10,-30]}.get(kind)
    if kind == 'jpml':
        floating = sum(p.score >= 30000 for p in ranked)
        uma = {0:[0,0,0,0],1:[12,-1,-3,-8],2:[8,4,-4,-8],3:[8,3,1,-12],4:[0,0,0,0]}[floating]
    start = game._starting_score()
    returned = option(game, 'return_score') or start
    awards = [int(x*1000) for x in uma]
    starting_total = sum(game._player_starting_score(p.original_player_index) for p in ranked)
    awards[0] += returned * 4 - starting_total
    if option(game, 'tie_break') == 'shared':
        # Original tied groups remain tied even if a 100-point deposit remainder exists.
        groups = tie_scores
        i=0
        while i<4:
            j=i+1
            while j<4 and groups[ranked[j].original_player_index] == groups[ranked[i].original_player_index]: j+=1
            total=sum(awards[i:j]); quotient,remainder=divmod(total//100,j-i)
            for k in range(i,j): awards[k]=100*(quotient+(k-i<remainder))
            i=j
    for i,p in enumerate(ranked):
        p.riichi_points=(p.score-returned+awards[i])/1000-getattr(p,'chombo_result_penalty',0)
    title = game.game_record.setdefault('game_title', {})
    title['riichi_final_scores'] = [p.score for p in sorted(ranked, key=lambda p: p.original_player_index)]
    title['riichi_final_sticks'] = game.riichi_sticks
