def _ceil100(value):
    return (value + 99) // 100 * 100


def win_payments(win, winner, *, discarder=None, dealer=0, honba=0, riichi_sticks=0, liabilities=None, rinshan_payer=None):
    if not win.valid or not 0 <= winner < 4 or discarder == winner:
        raise ValueError('和牌结算参数无效')
    changes = [0] * 4
    tsumo = discarder is None
    pay = {}
    if win.fan > 0:
        if win.variant == 'guobiao':
            pay = {seat: 100 + (win.fan if tsumo or seat == discarder else 0) for seat in range(4) if seat != winner}
        else:
            base = win.base_points
            if tsumo:
                pay = {seat: _ceil100(base * (2 if winner == dealer or seat == dealer else 1)) for seat in range(4) if seat != winner}
            else:
                pay = {discarder: _ceil100(base * (6 if winner == dealer else 4))}
            remaining, honba_payer = win.yakuman, None
            for name, liable in (liabilities or {}).items():
                if remaining <= 0 or liable == winner or not any(entry.startswith(name + '（') for entry in win.entries):
                    continue
                remaining -= 1
                if honba_payer is None:
                    honba_payer = liable
                total = 48000 if winner == dealer else 32000
                if tsumo:
                    for seat in pay:
                        pay[seat] -= 16000 if winner == dealer or seat == dealer else 8000
                    pay[liable] = pay.get(liable, 0) + total
                else:
                    pay[discarder] -= total // 2
                    pay[liable] = pay.get(liable, 0) + total // 2
            if tsumo and rinshan_payer is not None:
                pay = {rinshan_payer: sum(pay.values())}
            if tsumo:
                if rinshan_payer is not None or honba_payer is not None:
                    payer = rinshan_payer if rinshan_payer is not None else honba_payer
                    pay[payer] = pay.get(payer, 0) + honba * 300
                else:
                    for seat in range(4):
                        if seat != winner:
                            pay[seat] = pay.get(seat, 0) + honba * 100
            else:
                payer = honba_payer if honba_payer is not None else discarder
                pay[payer] = pay.get(payer, 0) + honba * 300
    for seat, value in pay.items():
        changes[seat] -= value
        changes[winner] += value
    if win.variant == 'riichi':
        changes[winner] += riichi_sticks * 1000
    return changes


def exhaustive_payments(tenpai):
    ready = set(tenpai)
    if not ready or len(ready) == 4:
        return [0] * 4
    return [3000 // len(ready) if seat in ready else -3000 // (4 - len(ready)) for seat in range(4)]
