const assert = require('node:assert/strict');
const test = require('node:test');

const {
  normalizeUsername,
  usernameDisplayLength,
  validateUsername,
} = require('./username');

test('single CJK, kana and halfwidth kana are valid double-width names', () => {
  assert.equal(usernameDisplayLength('麻'), 2);
  assert.equal(usernameDisplayLength('あ'), 2);
  assert.equal(usernameDisplayLength('ｶ'), 2);
  assert.equal(validateUsername('あ'), null);
});

test('decomposed kana is normalized before measuring', () => {
  assert.equal(normalizeUsername(' は\u3099 '), 'ば');
  assert.equal(usernameDisplayLength(normalizeUsername('は\u3099')), 2);
});

test('display-width and code-point limits are both enforced', () => {
  assert.equal(validateUsername('あ'.repeat(10)), null);
  assert.equal(validateUsername('あ'.repeat(11)), '用户名显示长度不能超过20');
  assert.equal(validateUsername('a'.repeat(16)), null);
  assert.equal(validateUsername('a'.repeat(17)), '用户名不能超过16个字符');
});

test('invisible format characters are rejected', () => {
  assert.equal(validateUsername('a\u200db'), '用户名不能包含控制字符或不可见格式字符');
});
