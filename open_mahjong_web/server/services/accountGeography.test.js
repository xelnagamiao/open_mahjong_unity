const test = require('node:test');
const assert = require('node:assert/strict');
const { createAccountGeography, normalizePublicIp, DISTRIBUTION_SQL } = require('./accountGeography');
const { recordSuccessfulWebLogin } = require('./loginHistory');

test('normalizes IPv4-mapped IPv6 and rejects private, invalid and reserved addresses', () => {
  assert.equal(normalizePublicIp('::ffff:8.8.8.8'), '8.8.8.8');
  assert.equal(normalizePublicIp('::ffff:808:808'), '8.8.8.8');
  assert.equal(normalizePublicIp('2606:4700:4700::1111'), '2606:4700:4700::1111');
  for (const ip of [null, 'unknown', '127.0.0.1', '10.1.2.3', '::1', '::', 'fc00::1', 'fe80::1', '2001:db8::1',
    '::ffff:127.0.0.1', '::ffff:7f00:1', '172.16.0.1', '192.168.0.1', '100.64.0.1', '224.0.0.1', '8.8.8.8, 10.0.0.1']) {
    assert.equal(normalizePublicIp(ip), null, ip);
  }
});

test('counts accounts sharing an IP separately; caches concurrent requests and expires lazily', async () => {
  let queries = 0, opens = 0, lookups = 0, clock = 0;
  const service = createAccountGeography({
    pool: { query: async ({ text }) => {
      queries++;
      assert.equal(text, DISTRIBUTION_SQL);
      return { rows: [{ ip_address: '8.8.8.8', accounts: 3 }, { ip_address: '1.1.1.1', accounts: 2 }, { ip_address: null, accounts: 4 }] };
    } },
    now: () => clock,
    openDatabase: async () => { opens++; return { get: () => { lookups++; return { country: { iso_code: 'FR' } }; } }; },
  });
  assert.equal(queries, 0);
  const [a, b] = await Promise.all([service.distribution(), service.distribution()]);
  assert.equal(a, b);
  assert.equal(a.total_accounts, 9);
  assert.equal(a.countries.find(c => c.country_code === 'FR').accounts, 5);
  assert.equal(a.countries.find(c => c.country_code === null).accounts, 4);
  await service.distribution();
  assert.deepEqual([queries, opens, lookups], [1, 1, 2]);
  clock = 300001;
  await service.distribution();
  assert.equal(queries, 2);
  assert.equal(opens, 1);
});

test('missing database is visible and retried after TTL without failing user details', async () => {
  let attempts = 0, clock = 0;
  const service = createAccountGeography({ pool: { query: async () => ({ rows: [] }) }, now: () => clock,
    openDatabase: async () => { attempts++; throw new Error('missing'); } });
  assert.equal((await service.lookup('8.8.8.8')).reason, 'database_unavailable');
  assert.equal((await service.distribution()).database_available, false);
  assert.equal(attempts, 1);
  clock = 300001;
  await service.lookup('8.8.8.8');
  assert.equal(attempts, 2);
});

test('web login history is parameterized, bounded to 20 entries and failure does not reject', async () => {
  let query;
  const pool = { query: async q => { query = q; } };
  assert.equal(await recordSuccessfulWebLogin(pool, 10000001, '8.8.8.8'), true);
  assert.deepEqual(query.values.slice(0, 2), [10000001, '8.8.8.8']);
  assert.match(query.text, /OFFSET 19/);
  assert.equal(query.query_timeout, 2000);
  assert.equal(await recordSuccessfulWebLogin(pool, 9, '8.8.8.8'), false);
  assert.equal(await recordSuccessfulWebLogin({ query: async () => { throw Error('offline'); } }, 10000001, '8.8.8.8', { warn() {} }), false);
});
