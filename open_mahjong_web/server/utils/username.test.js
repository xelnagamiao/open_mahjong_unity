const assert = require('node:assert/strict');
const test = require('node:test');

const {
  USERNAME_RULE_HINT,
  normalizeUsername,
  usernameDisplayLength,
  validateUsername,
} = require('./username');

test('Chinese counts as two characters and English as one', () => {
  assert.equal(usernameDisplayLength('麻'), 2);
  assert.equal(usernameDisplayLength('ab'), 2);
  assert.equal(validateUsername('麻'), null);
  assert.equal(validateUsername('ab'), null);
  assert.equal(validateUsername('a'), USERNAME_RULE_HINT);
});

test('only Chinese, English letters and digits are allowed', () => {
  assert.equal(validateUsername('あ'), USERNAME_RULE_HINT);
  assert.equal(validateUsername('user_1'), USERNAME_RULE_HINT);
  assert.equal(validateUsername('user1'), null);
  assert.equal(validateUsername('玩家01'), null);
});

test('display length must stay between 2 and 20', () => {
  assert.equal(validateUsername('中'.repeat(10)), null);
  assert.equal(validateUsername('中'.repeat(11)), USERNAME_RULE_HINT);
  assert.equal(validateUsername('a'.repeat(20)), null);
  assert.equal(validateUsername('a'.repeat(21)), USERNAME_RULE_HINT);
});

test('usernames are trimmed and NFC-normalized', () => {
  assert.equal(normalizeUsername(' 玩家01 '), '玩家01');
  assert.equal(validateUsername(' 玩家01 '), null);
});
