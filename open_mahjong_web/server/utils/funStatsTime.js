/** 趣味数据周榜：北京时间 04:00 切日，统计最近 7 个已结束的统计日。 */

const SHANGHAI_OFFSET_MS = 8 * 60 * 60 * 1000;
const STAT_DAY_OFFSET_HOURS = 4;
const REFRESH_HOUR = 4;
const WEEK_DAYS = 7;

function pad2(n) {
  return String(n).padStart(2, '0');
}

function formatYmd(year, monthIndex, day) {
  return `${year}-${pad2(monthIndex + 1)}-${pad2(day)}`;
}

function addDaysYmd(ymd, days) {
  const [year, month, day] = String(ymd).split('-').map(Number);
  const dt = new Date(Date.UTC(year, month - 1, day + days));
  return formatYmd(dt.getUTCFullYear(), dt.getUTCMonth(), dt.getUTCDate());
}

function shanghaiParts(now = new Date()) {
  const shifted = new Date(now.getTime() + SHANGHAI_OFFSET_MS);
  return {
    year: shifted.getUTCFullYear(),
    month: shifted.getUTCMonth(),
    day: shifted.getUTCDate(),
    hour: shifted.getUTCHours(),
    minute: shifted.getUTCMinutes(),
    second: shifted.getUTCSeconds(),
    ms: shifted.getUTCMilliseconds(),
  };
}

function shanghaiDateFromParts(year, month, day, hour = 0, minute = 0, second = 0, ms = 0) {
  return new Date(Date.UTC(year, month, day, hour, minute, second, ms) - SHANGHAI_OFFSET_MS);
}

function currentStatDate(now = new Date()) {
  const shifted = new Date(now.getTime() + SHANGHAI_OFFSET_MS - STAT_DAY_OFFSET_HOURS * 3600 * 1000);
  return formatYmd(shifted.getUTCFullYear(), shifted.getUTCMonth(), shifted.getUTCDate());
}

function lastCompleteStatDate(now = new Date()) {
  return addDaysYmd(currentStatDate(now), -1);
}

function weekRange(now = new Date()) {
  const dateTo = lastCompleteStatDate(now);
  return {
    date_from: addDaysYmd(dateTo, -(WEEK_DAYS - 1)),
    date_to: dateTo,
  };
}

function nextShanghaiRefreshAt(hour = REFRESH_HOUR, now = new Date()) {
  const parts = shanghaiParts(now);
  let candidate = shanghaiDateFromParts(parts.year, parts.month, parts.day, hour, 0, 0, 0);
  if (candidate.getTime() <= now.getTime()) {
    candidate = new Date(candidate.getTime() + 24 * 3600 * 1000);
  }
  return candidate;
}

function msUntilNextShanghaiHour(hour = REFRESH_HOUR, now = new Date()) {
  return Math.max(1000, nextShanghaiRefreshAt(hour, now).getTime() - now.getTime());
}

function cacheMatchesWeek(cache, now = new Date()) {
  if (!cache || typeof cache !== 'object') return false;
  const expected = weekRange(now);
  return cache.date_from === expected.date_from && cache.date_to === expected.date_to;
}

module.exports = {
  SHANGHAI_OFFSET_MS,
  STAT_DAY_OFFSET_HOURS,
  REFRESH_HOUR,
  WEEK_DAYS,
  addDaysYmd,
  currentStatDate,
  lastCompleteStatDate,
  weekRange,
  nextShanghaiRefreshAt,
  msUntilNextShanghaiHour,
  cacheMatchesWeek,
};
