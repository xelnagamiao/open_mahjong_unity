'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { parseTimestamp } = require('./time');

test('unqualified timestamps remain unknown, not silently interpreted in host timezone', () => {
  assert.equal(parseTimestamp('2026-11-14 05:00:00'), null);
  assert.equal(parseTimestamp('2026-11-14'), null);
  assert.equal(parseTimestamp('2026-11-14 05:00:00', 'Asia/Shanghai'), '2026-11-13T21:00:00.000Z');
  assert.equal(parseTimestamp('2026-11-14 05:00:00', 'UTC'), '2026-11-14T05:00:00.000Z');
  assert.equal(parseTimestamp('2026-11-14T05:00:00+08:00'), '2026-11-13T21:00:00.000Z');
  assert.equal(parseTimestamp('2026-11-14 05:00:00+08'), '2026-11-13T21:00:00.000Z');
  assert.equal(parseTimestamp('2026-02-31 05:00:00', 'UTC'), null);
});

test('DST ambiguous and missing wall clock instants are never invented', () => {
  assert.equal(parseTimestamp('2026-11-01 01:30:00', 'America/New_York'), null);
  assert.equal(parseTimestamp('2026-03-08 02:30:00', 'America/New_York'), null);
  assert.equal(parseTimestamp('2026-11-01 03:30:00', 'America/New_York'), '2026-11-01T08:30:00.000Z');
});
