import test from 'node:test'
import assert from 'node:assert/strict'
import { loadLoginPreferences, saveLoginPreferences, saveBrowserPassword, getBrowserPassword } from '../src/utils/loginPreferences.js'

function setGlobal(t, name, value) {
  const original = Object.getOwnPropertyDescriptor(globalThis, name)
  Object.defineProperty(globalThis, name, { configurable: true, writable: true, value })
  t.after(() => {
    if (original) Object.defineProperty(globalThis, name, original)
    else delete globalThis[name]
  })
}

function storage(t, initial) {
  const values = new Map(initial)
  setGlobal(t, 'localStorage', {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
  })
  return values
}

test('first visit defaults to 30 days without remembering a password', t => {
  storage(t)
  assert.deepEqual(loadLoginPreferences(), { username: '', rememberPassword: false, keepLoggedIn: true })
})

test('remembering a login persists options and username without storing the password', t => {
  const values = storage(t)
  saveLoginPreferences({ username: 'test-account', password: 'test-password', rememberPassword: true, keepLoggedIn: false })
  assert.deepEqual(loadLoginPreferences(), { username: 'test-account', rememberPassword: true, keepLoggedIn: false })
  assert.equal(JSON.stringify([...values]).includes('test-password'), false)
})

test('unchecking remember password removes the previously saved username', t => {
  storage(t)
  saveLoginPreferences({ username: 'test-account', rememberPassword: true, keepLoggedIn: true })
  saveLoginPreferences({ username: 'test-account', rememberPassword: false, keepLoggedIn: true })
  assert.equal(loadLoginPreferences().username, '')
  assert.equal(loadLoginPreferences().rememberPassword, false)
})

test('damaged or unavailable storage keeps the default options and does not break saving', t => {
  const values = storage(t, [['player_login_preferences', '{invalid-json']])
  assert.equal(loadLoginPreferences().keepLoggedIn, true)
  values.set('player_login_preferences', 'null')
  assert.equal(loadLoginPreferences().keepLoggedIn, true)
  t.mock.method(globalThis.localStorage, 'getItem', () => { throw new Error('Storage blocked') })
  t.mock.method(globalThis.localStorage, 'setItem', () => { throw new Error('Storage blocked') })
  assert.deepEqual(loadLoginPreferences(), { username: '', rememberPassword: false, keepLoggedIn: true })
  assert.doesNotThrow(() => saveLoginPreferences({ rememberPassword: false, keepLoggedIn: true }))
})

test('remember password uses the browser credential store and retrieves saved credentials', async t => {
  const credential = { id: 'test-account', password: 'test-password', type: 'password' }
  setGlobal(t, 'PasswordCredential', class {
    constructor(data) { Object.assign(this, data); this.type = 'password' }
  })
  setGlobal(t, 'navigator', { credentials: {
    async store(value) { assert.deepEqual({ ...value }, credential) },
    async get(options) {
      assert.deepEqual(options, { password: true, mediation: 'optional' })
      return credential
    },
  } })
  assert.equal(await saveBrowserPassword('test-account', 'test-password'), true)
  assert.equal(await getBrowserPassword(), credential)
})

test('declined password-manager requests are optional and do not reject login', async t => {
  setGlobal(t, 'PasswordCredential', class {})
  setGlobal(t, 'navigator', { credentials: {
    async store() { throw new Error('User declined') },
    async get() { throw new Error('User declined') },
  } })
  assert.equal(await saveBrowserPassword('test-account', 'test-password'), false)
  assert.equal(await getBrowserPassword(), null)
})

test('browsers without PasswordCredential fall back to regular form autocomplete', async t => {
  setGlobal(t, 'PasswordCredential', undefined)
  assert.equal(await saveBrowserPassword('test-account', 'test-password'), false)
  assert.equal(await getBrowserPassword(), null)
})
