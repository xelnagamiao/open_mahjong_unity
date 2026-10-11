const net = require('node:net');
const path = require('node:path');
const { resolveDataRoot } = require('../utils/runtimeData');

// One indexed latest-login lookup per registered account; never run on a login or game request.
const DISTRIBUTION_SQL = `
  SELECT last_login.ip_address, COUNT(*)::int AS accounts
  FROM users u
  LEFT JOIN LATERAL (
    SELECT ip_address FROM user_login_ips
    WHERE user_id = u.user_id
    ORDER BY logged_at DESC, id DESC LIMIT 1
  ) last_login ON true
  WHERE u.user_id > 10000000 AND COALESCE(u.is_tourist, false) = false
  GROUP BY last_login.ip_address`;

function normalizePublicIp(value) {
  let ip = String(value || '').trim();
  if (/^::ffff:/i.test(ip)) {
    const tail = ip.slice(7);
    if (net.isIPv4(tail)) ip = tail;
    else if (/^[0-9a-f]{1,4}:[0-9a-f]{1,4}$/i.test(tail)) {
      const [a, b] = tail.split(':').map(n => parseInt(n, 16));
      ip = [a >> 8, a & 255, b >> 8, b & 255].join('.');
    }
  }
  const version = net.isIP(ip);
  if (!version) return null;
  if (version === 4) {
    const [a, b, c] = ip.split('.').map(Number);
    if (a === 0 || a === 10 || a === 127 || a >= 224
      || (a === 100 && b >= 64 && b <= 127)
      || (a === 169 && b === 254) || (a === 172 && b >= 16 && b <= 31)
      || (a === 192 && b === 168) || (a === 192 && b === 0 && (c === 0 || c === 2))
      || (a === 198 && (b === 18 || b === 19 || (b === 51 && c === 100)))
      || (a === 203 && b === 0 && c === 113)) return null;
  } else {
    // Only global unicast (2000::/3); reject the documentation range as well.
    const first = parseInt(ip.split(':')[0], 16);
    if (!Number.isFinite(first) || first < 0x2000 || first > 0x3fff || /^2001:0?db8:/i.test(ip)) return null;
  }
  return ip;
}

function createAccountGeography({ pool, databasePath, openDatabase, now = Date.now, ttlMs = 300_000 }) {
  let reader, opening, retryAfter = 0, snapshot, pending;
  const ipCache = new Map();
  const names = new Intl.DisplayNames(['zh-CN'], { type: 'region' });
  async function database() {
    if (reader) return reader;
    if (now() < retryAfter) return null;
    if (!opening) opening = Promise.resolve().then(() => openDatabase(databasePath)).then(value => {
      reader = value;
      return value;
    }).catch(() => {
      // A missing or invalid database does not affect authentication or admin user details.
      retryAfter = now() + ttlMs;
      return null;
    }).finally(() => { opening = null; });
    return opening;
  }
  async function lookup(value) {
    const ip = normalizePublicIp(value);
    if (!ip) return { country_code: null, country_name: '未知', reason: value ? 'non_public_ip' : 'no_login' };
    const db = await database();
    if (!db) return { country_code: null, country_name: '未知', reason: 'database_unavailable' };
    if (ipCache.has(ip)) return ipCache.get(ip);
    let record;
    try { record = db.get(ip); } catch { record = null; }
    const code = record?.country?.iso_code;
    const result = /^[A-Z]{2}$/.test(code || '')
      ? { country_code: code, country_name: names.of(code) || code, reason: null }
      : { country_code: null, country_name: '未知', reason: 'not_found' };
    if (ipCache.size >= 10_000) ipCache.delete(ipCache.keys().next().value);
    ipCache.set(ip, result);
    return result;
  }
  async function distribution() {
    if (snapshot && now() < snapshot.expires) return snapshot.data;
    if (pending) return pending;
    pending = (async () => {
      const db = await database();
      const { rows } = await pool.query({ text: DISTRIBUTION_SQL, query_timeout: 10_000 });
      const groups = new Map();
      let total = 0;
      for (const row of rows) {
        const geo = await lookup(row.ip_address);
        const key = geo.country_code || 'unknown';
        const count = Number(row.accounts);
        total += count;
        const group = groups.get(key) || { country_code: geo.country_code, country_name: geo.country_name, accounts: 0 };
        group.accounts += count;
        groups.set(key, group);
      }
      const data = {
        total_accounts: total,
        countries: [...groups.values()].sort((a, b) => b.accounts - a.accounts || a.country_name.localeCompare(b.country_name)),
        generated_at: new Date(now()).toISOString(), cache_seconds: ttlMs / 1000,
        database_available: !!db, source: 'latest_successful_login',
      };
      snapshot = { data, expires: now() + ttlMs };
      return data;
    })().finally(() => { pending = null; });
    return pending;
  }
  return { lookup, distribution };
}

let service;
function getAccountGeography() {
  if (!service) service = createAccountGeography({
    pool: require('../config/database'),
    databasePath: process.env.OM_GEOIP_DATABASE || path.join(resolveDataRoot(), 'geoip', 'country.mmdb'),
    openDatabase: file => require('maxmind').open(file, { cache: { max: 1000 }, watchForUpdates: false }),
  });
  return service;
}

module.exports = { normalizePublicIp, createAccountGeography, getAccountGeography, DISTRIBUTION_SQL };
