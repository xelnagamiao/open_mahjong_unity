"""Platform SBR doubles: reserve kong payments, then settle valid kongs at board end."""


def no_changes(state):
    return {p.player_index: 0 for p in state.player_list}


def record_kong(state, gainer, tile, kind, payer_index=None):
    from .board_settlement import event_snapshot
    if kind == 'guafeng':
        payers = {payer_index: 2} if payer_index is not None else {}
    elif kind in ('xiayu1', 'xiayu2'):
        amount = 1 if kind == 'xiayu1' else 2
        payers = {p.player_index: amount for p in state.player_list
                  if p.player_index != gainer and not p.is_hu}
    else:
        payers = {}
    payers = {payer: amount for payer, amount in payers.items()
              if state.player_list[payer].team_id != state.player_list[gainer].team_id}
    reason = None if payers else ('non_timely' if kind == 'non_timely' else 'no_payer')
    record = dict(type=kind, tile=tile, gainer=gainer, payers=payers,
                  total=sum(payers.values()), deferred=True, settled=False,
                  cancelled=not payers, void_reason=reason, presentation=event_snapshot(state, gainer))
    state.player_list[gainer].gang_score_records.append(record)
    # Even an unpaid kong supersedes the preceding kong for gang-shang-pao.
    state.paofen_watch = (gainer, record)
    if reason:
        queue_reveal(state, record)
    return no_changes(state)


def queue_reveal(state, record):
    pending = state.pending_kong_reveals
    key = (record['gainer'], record['tile'])
    if key not in pending:
        pending.append(key)


def cancel_kong(state, record, reason='gang_shang_pao'):
    changes = no_changes(state)
    if record.get('cancelled'):
        return changes
    if record.get('settled'):
        for payer, amount in record['payers'].items():
            state.player_list[payer].score += amount
            state.player_list[record['gainer']].score -= amount
            changes[payer] += amount
            changes[record['gainer']] -= amount
    record['cancelled'] = True
    record['void_reason'] = reason
    queue_reveal(state, record)
    return changes


async def broadcast_reveals(state):
    from .boardcast import broadcast_do_action
    from ..public.game_record_manager import append_action_tick

    pending, state.pending_kong_reveals = state.pending_kong_reveals, []
    for seat, tile in pending:
        player = state.player_list[seat]
        for index, combination in enumerate(player.combination_tiles):
            if combination[:1] not in ('g', 'G') or int(combination[1:]) != tile:
                continue  # A robbed added kong has already reverted to a pung.
            if len(player.combination_mask[index]) != 8:
                continue
            mask = [0, tile] * 4
            player.combination_mask[index] = mask
            append_action_tick(state, ['sichuan_kong_reveal', seat, tile])
            await broadcast_do_action(state, action_list=['sichuan_kong_reveal'],
                                      action_player=seat, combination_target=combination,
                                      combination_mask=mask)
            break


def settle_kongs(state):
    changes = no_changes(state)
    labels = []
    kinds = {'guafeng': '明杠', 'xiayu1': '加杠', 'xiayu2': '暗杠', 'non_timely': '加杠'}
    reasons = {'non_timely': '非及时', 'no_payer': '不计分', 'gang_shang_pao': '杠上炮退税',
               'no_ting': '未听退税', 'robbed_kong': '被抢杠'}
    for player in state.player_list:
        for record in player.gang_score_records:
            if not record.get('deferred') or record.get('settled'):
                continue
            record['settled'] = True
            amount = 0 if record['cancelled'] else record['total']
            if amount:
                for payer, payment in record['payers'].items():
                    state.player_list[payer].score -= payment
                    player.score += payment
                    changes[payer] -= payment
                    changes[player.player_index] += payment
            reason = reasons.get(record.get('void_reason'))
            label = f"{player.username} · {kinds[record['type']]}"
            labels.append(f"{label}（{reason}） 0分" if reason else f"{label} +{amount}分")
    return changes, labels
