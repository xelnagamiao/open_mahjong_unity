'use strict';

// A timestamp without an offset is not an instant until its storage timezone is
// explicitly declared. Never let the machine running the checker choose it.
function validTimezone(value) {
  if (typeof value !== 'string' || !value) return false;
  try { new Intl.DateTimeFormat('en', { timeZone: value }); return true; } catch { return false; }
}
function parseTimestamp(value, timezone = null) {
  if (value instanceof Date) return Number.isFinite(value.getTime()) ? value.toISOString() : null;
  if (typeof value !== 'string' || !value.trim()) return null;
  const text = value.trim();
  if (/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d{1,6})?)?(?:Z|[+-]\d{2}(?::?\d{2})?)$/i.test(text)) {
    let normalized = text.replace(' ', 'T');
    if (/[+-]\d{2}$/.test(normalized)) normalized += ':00';
    if (/[+-]\d{4}$/.test(normalized)) normalized = normalized.slice(0,-2) + ':' + normalized.slice(-2);
    const timestamp = Date.parse(normalized);
    return Number.isFinite(timestamp) ? new Date(timestamp).toISOString() : null;
  }
  if (!validTimezone(timezone)) return null;
  const match = text.match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,6}))?$/);
  if (!match) return null;
  const [year, month, day, hour, minute, second] = match.slice(1,7).map(Number);
  const ms = Number((match[7] || '').padEnd(3, '0').slice(0,3));
  const wall = Date.UTC(year, month - 1, day, hour, minute, second);
  const check = new Date(wall);
  if (check.getUTCFullYear() !== year || check.getUTCMonth() !== month - 1 || check.getUTCDate() !== day || hour > 23 || minute > 59 || second > 59) return null;
  const format = new Intl.DateTimeFormat('en-CA', { timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
  function wallAt(instant) {
    const p = Object.fromEntries(format.formatToParts(new Date(instant)).map(x => [x.type, x.value]));
    return Date.UTC(Number(p.year), Number(p.month) - 1, Number(p.day), Number(p.hour), Number(p.minute), Number(p.second));
  }
  // Test offsets on both sides of a possible DST transition. An ambiguous or
  // nonexistent local time remains unknown instead of picking one silently.
  const offsets = new Set([-36, 0, 36].map(hours => {
    const instant = wall + hours * 3600000;
    return wallAt(instant) - instant;
  }));
  const matches = [...offsets].map(offset => wall - offset).filter(instant => wallAt(instant) === wall);
  return matches.length === 1 ? new Date(matches[0] + ms).toISOString() : null;
}

module.exports = { parseTimestamp, validTimezone };
