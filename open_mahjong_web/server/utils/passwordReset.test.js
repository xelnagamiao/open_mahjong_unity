const test = require('node:test');
const assert = require('node:assert/strict');
const { createPasswordResetHandlers } = require('./passwordReset');
const { hashPassword, verifyPassword } = require('./password');
const { findLoginAccount } = require('./loginAccount');

// In-memory persistence adapter exercises the exported production handlers without
// production credentials, network mail or importing the running server.
function fixture() {
  let timestamp = Date.now();
  let users = [{ user_id: 7, username: 'player', email: 'player@example.com', password: hashPassword('Oldpass1!'), is_tourist: false }];
  let codes = new Map();
  const sent = [];
  let tail = Promise.resolve();
  let failDelete = false;
  const execute = async (sql, p = []) => {
    const q = sql.replace(/\s+/g, ' ').trim();
    if (q.startsWith('SELECT user_id, username') && q.includes('WHERE username')) return { rows: users.filter(u => u.username === p[0]) };
    if (q.startsWith('SELECT') && q.includes('FROM users')) return { rows: users.filter(u => u.email?.toLowerCase() === p[0] && !u.is_tourist).slice(0, 2).map(u => ({ ...u })) };
    if (q.startsWith('INSERT INTO password_reset_codes')) {
      const old = codes.get(p[0]);
      if (old && old.created_at > p[6]) return { rows: [] };
      codes.set(p[0], { email: p[0], user_id: p[1], code_hash: p[2], password_digest: p[3], expires_at: p[4], created_at: p[5], attempts: 0 });
      return { rows: [{ email: p[0] }] };
    }
    if (q.startsWith('SELECT * FROM password_reset_codes')) return { rows: codes.has(p[0]) ? [{ ...codes.get(p[0]) }] : [] };
    if (q.startsWith('UPDATE password_reset_codes')) { codes.get(p[0]).attempts++; return { rows: [] }; }
    if (q.startsWith('UPDATE users SET password')) { users.find(u => u.user_id === p[1]).password = p[0]; return { rows: [] }; }
    if (q.startsWith('DELETE FROM password_reset_codes')) {
      if (failDelete) throw new Error('storage failure');
      if (p.length === 1 || codes.get(p[0])?.code_hash === p[1]) codes.delete(p[0]);
      return { rows: [] };
    }
    if (q.startsWith('DELETE FROM email_bind_codes')) return { rows: [] };
    throw new Error(`Unexpected SQL: ${q}`);
  };
  const pool = {
    query: execute,
    async connect() {
      let snapshot, unlock;
      return {
        async query(sql, p) {
          if (sql === 'BEGIN') {
            const previous = tail;
            tail = new Promise(resolve => { unlock = resolve; });
            await previous;
            snapshot = { users: structuredClone(users), codes: structuredClone(codes) };
            return;
          }
          if (sql === 'ROLLBACK') { users = snapshot.users; codes = snapshot.codes; unlock(); return; }
          if (sql === 'COMMIT') { unlock(); return; }
          return execute(sql, p);
        },
        release() {},
      };
    },
  };
  const handler = createPasswordResetHandlers({ pool, secret: 'test-secret', hashPassword, now: () => timestamp,
    mailEnabled: () => true, sendCode: async message => sent.push(message) });
  const invoke = async (fn, body) => {
    const res = { statusCode: 200, status(code) { this.statusCode = code; return this; }, json(data) { this.data = data; return this; } };
    await fn({ body }, res);
    return res;
  };
  return {
    handler, pool, sent, invoke,
    get users() { return users; }, get codes() { return codes; },
    advance(ms) { timestamp += ms; }, failDelete() { failDelete = true; },
    send(email = ' Player@Example.COM ') { return invoke(handler.send, { email }); },
    reset(overrides = {}) { return invoke(handler.reset, { email: 'player@example.com', code: sent.at(-1)?.code, new_password: 'Newpass1!', confirm_password: 'Newpass1!', ...overrides }); },
  };
}

test('mailbox proof resets the shared password; code is hashed and single-use', async () => {
  const f = fixture();
  assert.equal((await f.send()).statusCode, 200);
  assert.equal(f.sent[0].to, 'player@example.com');
  assert.notEqual(f.codes.get('player@example.com').code_hash, f.sent[0].code);
  assert.equal((await f.reset()).statusCode, 200);
  assert.equal(verifyPassword('Newpass1!', f.users[0].password), true);
  assert.equal(verifyPassword('Oldpass1!', f.users[0].password), false);
  assert.equal((await f.reset()).statusCode, 400);
});

test('unknown, duplicate and tourist emails do not send or choose an account', async () => {
  const f = fixture();
  const unknown = await f.send('missing@example.com');
  f.users.push({ ...f.users[0], user_id: 8 });
  const duplicate = await f.send();
  assert.deepEqual(duplicate.data, unknown.data);
  f.users.splice(1); f.users[0].is_tourist = true;
  assert.deepEqual((await f.send()).data, unknown.data);
  assert.equal(f.sent.length, 0);
});

test('resend cooldown is atomic and does not replace a valid code', async () => {
  const f = fixture();
  await Promise.all([f.send(), f.send(), f.send()]);
  assert.equal(f.sent.length, 1);
  const first = f.sent[0].code;
  f.advance(60001); await f.send();
  assert.equal(f.sent.length, 2);
  if (f.sent[1].code !== first) assert.equal((await f.reset({ code: first })).statusCode, 400);
  assert.equal((await f.reset()).statusCode, 200);
});

test('five wrong codes lock out even the correct code', async () => {
  const f = fixture(); await f.send();
  for (let i = 0; i < 5; i++) assert.equal((await f.reset({ code: '000000' })).statusCode, 400);
  assert.equal((await f.reset()).statusCode, 400);
  assert.equal(verifyPassword('Oldpass1!', f.users[0].password), true);
});

test('expiry, email changes and password changes invalidate outstanding codes', async () => {
  for (const change of [f => f.advance(600000), f => { f.users[0].email = 'other@example.com'; }, f => { f.users[0].password = hashPassword('Changed1!'); }, f => { f.users.push({ ...f.users[0], user_id: 9 }); }]) {
    const f = fixture(); await f.send(); change(f);
    assert.equal((await f.reset()).statusCode, 400);
    assert.equal(verifyPassword('Newpass1!', f.users[0].password), false);
  }
});

test('concurrent confirmations can consume a code only once', async () => {
  const f = fixture(); await f.send();
  const results = await Promise.all([f.reset(), f.reset()]);
  assert.deepEqual(results.map(r => r.statusCode).sort(), [200, 400]);
});

test('database error rolls password change back with the code', async () => {
  const f = fixture(); await f.send(); f.failDelete();
  assert.equal((await f.reset()).statusCode, 500);
  assert.equal(verifyPassword('Oldpass1!', f.users[0].password), true);
});

test('invalid payloads cannot reset a password', async () => {
  const f = fixture(); await f.send();
  for (const body of [{ email: null }, { code: 123456 }, { code: '123456\n' }, { new_password: 'short' }, { new_password: 'Secret1!\n', confirm_password: 'Secret1!\n' }, { new_password: '含中文密码12' }, { confirm_password: 'mismatch' }]) {
    assert.equal((await f.reset(body)).statusCode, 400);
  }
  assert.equal(verifyPassword('Oldpass1!', f.users[0].password), true);
});

test('mail failures invalidate the unsent code; unavailable mail is reported', async () => {
  const f = fixture();
  const deps = { pool: f.pool, secret: 'test', hashPassword, mailEnabled: () => true, sendCode: async () => { throw new Error('mail offline'); } };
  assert.equal((await f.invoke(createPasswordResetHandlers(deps).send, { email: 'player@example.com' })).statusCode, 502);
  assert.equal(f.codes.size, 0);
  deps.mailEnabled = () => false;
  assert.equal((await f.invoke(createPasswordResetHandlers(deps).send, { email: 'player@example.com' })).statusCode, 503);
});

test('email login finds one account, rejects duplicates, preserves username priority', async () => {
  const f = fixture();
  assert.equal((await findLoginAccount(f.pool, ' Player@Example.COM ')).user.user_id, 7);
  f.users.push({ ...f.users[0], user_id: 8, username: 'second' });
  assert.match((await findLoginAccount(f.pool, 'player@example.com')).error, /多个账户/);
  f.users.push({ user_id: 9, username: 'player@example.com', password: 'different', email: null });
  assert.equal((await findLoginAccount(f.pool, 'player@example.com')).user.user_id, 9);
});
