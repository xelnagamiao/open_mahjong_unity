import { statisticsSelection } from './statisticsCatalog.js';

const SBR_FAN_VALUES = {
  '平和': 0, '杠': 1, '根': 1, '大对子': 1, '金钩钓': 1,
  '清一色': 2, '七对': 2, '杠上花': 1, '杠上炮': 1, '抢杠': 1, '海底': 1,
};

export function sichuanWinFan(title, rd, tick, tickIndex) {
  const saved = rd.win_fan_by_tick?.[tickIndex];
  if (saved != null) {
    if (!Number.isInteger(saved) || saved < 0) throw new Error('Invalid recorded Sichuan fan');
    return saved;
  }
  if (title.sub_rule?.startsWith('sichuan/xueliu')) return Number(tick[2]);
  if (!Array.isArray(tick[3]) || !tick[3].length) throw new Error('Sichuan record has no fan evidence');
  return tick[3].reduce((sum, raw) => {
    const match = String(raw).trim().match(/^(.+?)(?:\s*[*×]\s*(\d+))?$/);
    const value = SBR_FAN_VALUES[match?.[1]?.trim()];
    if (value == null) throw new Error(`Unknown historical Sichuan fan: ${raw}`);
    return sum + value * Number(match[2] || 1);
  }, 0);
}

/** Legacy score columns have rule-specific units, independent of payment deltas. */
export function recordWinValues(title, rd, tick, tickIndex, hu, payer) {
  const definition = statisticsSelection(title.sub_rule || title.rule);
  let value = hu.fanScore;
  if (title.rule === 'sichuan') value = sichuanWinFan(title, rd, tick, tickIndex);
  if (title.rule === 'classical') {
    const fu = rd.action_ticks.find(t => t?.[0] === 'shuhewei')?.[1];
    value = fu?.[hu.winnerSeat] ?? Math.floor(value / (hu.winnerSeat === 0 ? 6 : 4));
  }
  const winUnit = definition?.win_stat_unit;
  const lossUnit = definition?.deal_in_stat_unit;
  return {
    win: winUnit === 'none' ? 0 : winUnit === 'points'
      ? Math.max(0, hu.scoreChanges?.[hu.winnerSeat] || 0) : value,
    loss: lossUnit === 'none' ? 0 : lossUnit === 'points'
      ? Math.max(0, -(hu.scoreChanges?.[payer] || 0)) : value,
  };
}
