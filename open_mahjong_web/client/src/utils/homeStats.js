import { buildPlayerStatsRows, buildPlatformStatsRows, SCENE_TOTAL_KEYS, ratio } from './statsDisplay.js';

export const HOME_RULE_DEFS = [
  { value: 'guobiao', label: '国标' },
  { value: 'riichi', label: '立直' },
  { value: 'qingque', label: '青雀' },
  { value: 'classical', label: '古典' },
  { value: 'sichuan', label: '川麻(血战)' },
  { value: 'changsha', label: '长沙' },
];
const EXTRA_RULE_DEFS = [
  { value: 'riichi_sanma', label: '立直(三人)' },
  { value: 'guobiao_sanma', label: '国标(三人)' },
  { value: 'sichuan_xueliu_exchange', label: '川麻(血流换三)' },
  { value: 'sichuan_xueliu', label: '川麻(血流)' },
  { value: 'zhongyong', label: '中庸麻将' },
  { value: 'taiwan', label: '台湾麻将' },
  { value: 'shanghai', label: '上海麻将（敲麻）' },
  { value: 'shanghai_qinghunpeng', label: '上海麻将（清混碰）' },
  { value: 'hongque', label: '虹雀' }, { value: 'hongkong', label: '香港麻将' },
  { value: 'guangdong', label: '广东麻将' }, { value: 'guangdong_tuidao', label: '广东麻将（推倒和）' },
  { value: 'shanxi', label: '山西麻将' },
  { value: 'changchun', label: '长春麻将' }, { value: 'free', label: '自由模式' },
  { value: 'guizhou', label: '贵州麻将' }, { value: 'yixing', label: '宜兴麻将' },
  { value: 'wenzhou', label: '温州麻将' }, { value: 'hangzhou', label: '杭州麻将' },
  { value: 'hongzhong', label: '红中麻将' },
];

function displayRule(row) {
  if (['jiandan', 'nanque'].includes(row.rule) || (row.rule === 'zhongyong' && row.sub_rule === 'zhongyong/nanque')) return 'nanque';
  if (row.rule === 'guangdong' && row.sub_rule === 'guangdong/tuidao_mil2024') return 'guangdong_tuidao';
  if (row.rule === 'guobiao' && row.sub_rule === 'guobiao/sanma') return 'guobiao_sanma';
  if (row.rule === 'shanghai' && row.sub_rule === 'shanghai/qinghunpeng') return 'shanghai_qinghunpeng';
  if (row.rule === 'sichuan' && row.sub_rule === 'sichuan/xueliu') return 'sichuan_xueliu';
  if (row.rule === 'sichuan' && row.sub_rule === 'sichuan/xueliu_exchange') return 'sichuan_xueliu_exchange';
  return row.rule;
}

export function homeRuleOptions(rows) {
  const options = [...HOME_RULE_DEFS.map(rule => rule.value === 'classical' ? { ...rule, label: '古典麻将' } : rule), ...EXTRA_RULE_DEFS];
  for (const row of rows) {
    const rule = displayRule(row);
    if (rule && rule !== 'nanque' && !options.some(option => option.value === rule)) options.push({ value: rule, label: row.rule_label || rule });
  }
  options.push({ value: 'nanque', label: '南雀' });
  return options;
}
export const isRiichiHomeRule = rule => rule === 'riichi' || rule === 'riichi_sanma';
const isSichuanHomeRule = rule => ['sichuan', 'sichuan_xueliu', 'sichuan_xueliu_exchange'].includes(rule);

export function homeSceneOptions(rule) {
  const other = [{ value: 'custom', label: '自定义' }, { value: 'events', label: '比赛场' }];
  const tiers = [{ value: 'beginner', label: '初级' }, { value: 'intermediate', label: '中级' },
    { value: 'advanced', label: '高级' }, { value: 'mcrpl', label: 'mcrpl' }];
  if (isRiichiHomeRule(rule)) return [...tiers.slice(0, 3), ...other];
  if (isSichuanHomeRule(rule) || rule === 'qingque') return [{ value: 'rank', label: '段位场' }, ...other];
  return rule === 'guobiao' ? [...tiers, ...other] : other;
}

export const homeDefaultScene = rule => homeSceneOptions(rule)[0].value;

export function homeGameType(row) {
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
