import tiersByRule from '../../../shared/statisticsSceneTiers.json' with { type: 'json' };
import { statisticsSelection } from './statisticsCatalog.js';

export const STATISTICS_SCENE_LABELS = {
  beginner: '初级场', intermediate: '中级场', advanced: '高级场',
  mcrpl: 'mcrpl', elo: 'Elo 匹配', custom: '自定义', total: '总计',
};

export function statisticsLadderTiers(rule) {
  return tiersByRule[statisticsSelection(rule)?.key] || [];
}

export function platformSceneOptions(rule) {
  return [...statisticsLadderTiers(rule), 'custom'].map(value => ({
    value, label: STATISTICS_SCENE_LABELS[value],
  }));
}

export function platformSceneSelection(rule, selected) {
  const options = platformSceneOptions(rule);
  return selected === 'total' || options.some(option => option.value === selected)
    ? selected : options[0].value;
}

export function platformRecordsScope(scene, eventId = '') {
  if (eventId) return { event_id: eventId };
  if (scene === 'custom') return { room_type: 'custom' };
  if (scene === 'total') return { room_type: 'all' };
  return { match_tier: scene };
}
