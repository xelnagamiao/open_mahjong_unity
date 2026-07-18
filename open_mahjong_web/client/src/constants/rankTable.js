export const RANK_TABLE = [
  { name: '10级', startScore: 0, promoteScore: 20, canDemote: false },
  { name: '9级', startScore: 0, promoteScore: 20, canDemote: false },
  { name: '8级', startScore: 0, promoteScore: 20, canDemote: false },
  { name: '7级', startScore: 0, promoteScore: 20, canDemote: false },
  { name: '6级', startScore: 0, promoteScore: 40, canDemote: false },
  { name: '5级', startScore: 0, promoteScore: 40, canDemote: false },
  { name: '4级', startScore: 0, promoteScore: 60, canDemote: false },
  { name: '3级', startScore: 0, promoteScore: 60, canDemote: true },
  { name: '2级', startScore: 0, promoteScore: 80, canDemote: true },
  { name: '1级', startScore: 1, promoteScore: 100, canDemote: true },
  { name: '初段', startScore: 200, promoteScore: 400, canDemote: true },
  { name: '二段', startScore: 400, promoteScore: 800, canDemote: true },
  { name: '三段', startScore: 600, promoteScore: 1200, canDemote: true },
  { name: '四段', startScore: 800, promoteScore: 1600, canDemote: true },
  { name: '五段', startScore: 1000, promoteScore: 2000, canDemote: true },
  { name: '六段', startScore: 1200, promoteScore: 2400, canDemote: true },
  { name: '七段', startScore: 1400, promoteScore: 2800, canDemote: true },
  { name: '八段', startScore: 1600, promoteScore: 3200, canDemote: true },
  { name: '九段', startScore: 3200, promoteScore: 7000, canDemote: true },
];

export const RANK_NAMES = RANK_TABLE.map((r) => r.name);
export const TOP_RANK_NAME = '九段';

const RANK_NAME_TO_INDEX = Object.fromEntries(RANK_NAMES.map((name, i) => [name, i]));

export function getRankEntry(rankName) {
  const idx = RANK_NAME_TO_INDEX[rankName];
  if (idx === undefined) return null;
  return { ...RANK_TABLE[idx], index: idx };
}

export function getScoreBounds(rankName) {
  const entry = getRankEntry(rankName);
  if (!entry) return null;
  const isTop = rankName === TOP_RANK_NAME;
  const minScore = entry.startScore > 0 ? entry.startScore : 0;
  const maxScore = isTop ? null : Math.round((entry.promoteScore - 0.01) * 100) / 100;
  return {
    startScore: entry.startScore,
    promoteScore: entry.promoteScore,
    canDemote: entry.canDemote,
    minScore,
    maxScore,
    isTopRank: isTop,
  };
}

export function getPromotionProgress(rankName, score) {
  const entry = getRankEntry(rankName);
  if (!entry) return null;
  const numScore = Number(score) || 0;
  if (rankName === TOP_RANK_NAME) {
    return { current: numScore, target: null, percent: 100, remaining: 0, isMaxRank: true };
  }
  const target = entry.promoteScore;
  const percent = Math.min(100, Math.max(0, (numScore / target) * 100));
  return {
    current: numScore,
    target,
    percent: Math.round(percent * 10) / 10,
    remaining: Math.max(0, Math.round((target - numScore) * 100) / 100),
    isMaxRank: false,
  };
}

export function clampScoreToRank(rankName, score) {
  const bounds = getScoreBounds(rankName);
  if (!bounds) return score;
  let s = Math.round(Number(score) * 100) / 100;
  if (Number.isNaN(s)) s = bounds.minScore;
  if (s < bounds.minScore) s = bounds.minScore;
  if (bounds.maxScore != null && s > bounds.maxScore) s = bounds.maxScore;
  return s;
}

export function validateRankScore(rankName, score) {
  const bounds = getScoreBounds(rankName);
  if (!bounds) return { valid: false, message: '无效的段位名称' };
  const numScore = Number(score);
  if (Number.isNaN(numScore)) return { valid: false, message: '无效的分数' };
  const rounded = Math.round(numScore * 100) / 100;
  if (rounded < bounds.minScore) {
    return { valid: false, message: `${rankName} 的 PT 不能低于 ${bounds.minScore}`, bounds };
  }
  if (!bounds.isTopRank && rounded >= bounds.promoteScore) {
    return {
      valid: false,
      message: `${rankName} 的 PT 须小于升段分 ${bounds.promoteScore}（最高可设 ${bounds.maxScore}）`,
      bounds,
    };
  }
  return { valid: true, bounds, normalizedScore: rounded };
}
