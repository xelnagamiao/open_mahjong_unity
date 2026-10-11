import { buildPlayerStatsRows, buildPlatformStatsRows, SCENE_TOTAL_KEYS, ratio } from './statsDisplay.js';

import { STATISTICS_RULES, statisticsSelection, statisticRuleKey, statisticsGameType } from './statisticsCatalog.js';

export const HOME_RULE_DEFS = STATISTICS_RULES.filter(r => r.home_header)
  .map(r => ({ value: r.key, label: r.home_label || r.stats_name }));
const displayRule = statisticRuleKey;
export function homeRuleOptions() {
  return STATISTICS_RULES.map(r => ({ value: r.key, label: r.stats_name, standard: r.standard }));
}
export const isRiichiHomeRule = rule => statisticsSelection(rule)?.rule === 'riichi';
const isSichuanHomeRule = rule => statisticsSelection(rule)?.rule === 'sichuan';

export function homeSceneOptions(rule) {
  const other = [{ value: 'custom', label: '自定义' }, { value: 'events', label: '比赛场' }];
  const tiers = [{ value: 'beginner', label: '初级' }, { value: 'intermediate', label: '中级' },
    { value: 'advanced', label: '高级' }, { value: 'mcrpl', label: 'mcrpl' }];
  if (['riichi','riichi_sanma'].includes(rule)) return [...tiers.slice(0, 3), ...other];
  if (['sichuan','sichuan_xueliu_exchange','hongque'].includes(rule) || rule === 'qingque') return [{ value: 'rank', label: '段位场' }, ...other];
  return rule === 'guobiao' ? [...tiers, ...other] : other;
}

export const homeDefaultScene = rule => homeSceneOptions(rule)[0].value;

export function homeModeOptions(rule, scene) {
  if (statisticsSelection(rule)?.rule === 'hongque') {
    const definition = statisticsSelection(rule);
    const modes = ['custom','events'].includes(scene) ? definition.custom_modes : definition.ranked_modes;
    return [...modes].reverse().map(mode => ({value: statisticsGameType(rule, mode), label: `${mode.split('/')[0]}局`}));
  }
  return [{value:'quanzhuang',label:'全庄战'},{value:'xifeng',label:'东西战'},
    {value:'banzhuang',label:'半庄战'},{value:'dongfeng',label:'东风战'}];
}

export function homeGameType(row) {
  if (displayRule(row) === 'hongque') return statisticsGameType('hongque', row.mode);
  if (row.game_type) return row.game_type;
  const match = /^(\d+)\/(?:3|4)(?:_sanma)?(?:_rank)?$/.exec(String(row.mode || ''));
  return ({ 1: 'dongfeng', 2: 'banzhuang', 3: 'xifeng', 4: 'quanzhuang' })[match?.[1]] || null;
}

/** 筛选场次后再合计原始计数，保留日麻明细和个人样本数。 */
export function selectHomeStats(allRows, rule, scene, gameType = null) {
  let rows = allRows.filter(row => displayRule(row) === rule && (!gameType || homeGameType(row) === gameType));
  if (scene === 'rank') rows = rows.filter(row => row.room_type === 'match');
  else if (scene === 'custom' || scene === 'events') rows = rows.filter(row => row.room_type === scene);
  else rows = rows.filter(row => row.room_type === 'match' && row.match_tier === scene);

  const fromRecords = rows.some(row => row.source === 'record_metrics');
  if (!isRiichiHomeRule(rule) && !fromRecords) {
    if (scene === 'custom' || scene === 'events') rows = rows.filter(row => row.source !== 'metrics');
    else {
      const metrics = rows.filter(row => row.source === 'metrics' && row.match_tier);
      rows = metrics.length ? metrics : rows.filter(row => row.source === 'history');
    }
  }
  const out = { rule };
  for (const key of SCENE_TOTAL_KEYS) out[key] = rows.reduce((total, row) => total + (Number(row[key]) || 0), 0);
  if (isRiichiHomeRule(rule)) {
    out.player_game_count = rows.reduce((total, row) => total + (Number(row.player_game_count) || 0), 0);
    out.analyzed_player_games = rows.reduce((total, row) => total + (Number(row.analyzed_player_games) || 0), 0);
    out.details_available = rows.every(row => row.details_available === true && row.riichi_details != null);
    out.riichi_details = {};
    for (const row of rows) {
      for (const [key, value] of Object.entries(row.riichi_details || {})) {
        out.riichi_details[key] = (out.riichi_details[key] || 0) + (Number(value) || 0);
      }
    }
  }
  if (fromRecords || isSichuanHomeRule(rule)) out.details_available = rows.length > 0 && rows.every(row => row.details_available === true);
  return out;
}

export function buildHomeStatsRows(stats) {
  if (isSichuanHomeRule(stats.rule)) {
    return buildPlatformStatsRows(stats).map(row => {
      const countKey = row.label === '和牌率' ? 'win_count' : row.label === '放铳率' ? 'deal_in_count' : null;
      const mapped = countKey ? { ...row, label: countKey === 'win_count' ? '局均和牌次数' : '局均放铳次数',
        value: stats.total_rounds > 0 ? (stats[countKey] / stats.total_rounds).toFixed(2) : '0.00',
        tip: '全桌对应事件次数 / 小局数；川麻一小局可以有多次和牌或放铳。' } : row;
      return mapped;
    });
  }
  if (!isRiichiHomeRule(stats.rule)) {
    return buildPlatformStatsRows(stats).filter(row => stats.rule !== 'guobiao_sanma' || row.label !== '四位率').map(row =>
      stats.rule === 'guobiao_sanma' && row.label === '副露率' ? { ...row, value: ratio(stats.fulu_round_count, stats.total_rounds * 3) } : row);
  }
  // 日麻局均点用玩家参赛样本数；首页总对局显示去重后的实际桌数。
  const personal = { ...stats, rule: 'riichi', total_games: stats.player_game_count };
  return buildPlayerStatsRows(personal).filter(row => stats.rule !== 'riichi_sanma' || row.label !== '四位率').map(row => {
    if (row.label === '总对局') return { ...row, value: String(stats.total_games || 0), tip: '按已保存牌谱去重后的实际对局数。' };
    return row;
  });
}
