const assert = require('node:assert/strict');
const path = require('path');
const test = require('node:test');
const { PRODUCTION_DATA_ROOT, WEB_DATA, resolveDataRoot } = require('./runtimeData');

test('OM_DATA_DIR wins over defaults', () => {
  const prev = process.env.OM_DATA_DIR;
  process.env.OM_DATA_DIR = '/tmp/om-data-test';
  try {
    assert.equal(resolveDataRoot(), path.resolve('/tmp/om-data-test'));
  } finally {
    if (prev == null) delete process.env.OM_DATA_DIR;
    else process.env.OM_DATA_DIR = prev;
  }
});

test('non-production default is the web data directory', () => {
  const prevDir = process.env.OM_DATA_DIR;
  const prevEnv = process.env.NODE_ENV;
  delete process.env.OM_DATA_DIR;
  process.env.NODE_ENV = 'development';
  try {
    assert.equal(resolveDataRoot(), WEB_DATA);
  } finally {
    if (prevDir == null) delete process.env.OM_DATA_DIR;
    else process.env.OM_DATA_DIR = prevDir;
    if (prevEnv == null) delete process.env.NODE_ENV;
    else process.env.NODE_ENV = prevEnv;
  }
});

test('linux production default is outside the git checkout', () => {
  const prevDir = process.env.OM_DATA_DIR;
  const prevEnv = process.env.NODE_ENV;
  delete process.env.OM_DATA_DIR;
  process.env.NODE_ENV = 'production';
  try {
    if (process.platform === 'win32') {
      assert.equal(resolveDataRoot(), WEB_DATA);
    } else {
      assert.equal(resolveDataRoot(), PRODUCTION_DATA_ROOT);
    }
  } finally {
    if (prevDir == null) delete process.env.OM_DATA_DIR;
    else process.env.OM_DATA_DIR = prevDir;
    if (prevEnv == null) delete process.env.NODE_ENV;
    else process.env.NODE_ENV = prevEnv;
  }
});
