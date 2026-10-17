import test from 'node:test'
import assert from 'node:assert/strict'
import { loadStoredAssistSettings, normalizeAssistSettings, resetRoundAssistSettings, saveStoredAssistSettings } from '../src/game2d/lib/assistSettings.ts'

const key = 'salasasa.game2d.assistSettings'
const desktopAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36'
const phoneAgents = [
  'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1',
  'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 Chrome/140.0.0.0 Mobile Safari/537.36',
]

function inBrowser({ width = 390, coarse = true, userAgent = phoneAgents[0], stored = null }, run) {
  const priorWindow = Object.getOwnPropertyDescriptor(globalThis, 'window')
  const priorStorage = Object.getOwnPropertyDescriptor(globalThis, 'localStorage')
  const entries = new Map(stored === null ? [] : [[key, stored]])
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {
    innerWidth: width,
    navigator: { userAgent },
    matchMedia: query => ({ matches: coarse && (!query.includes('max-width') || width <= 560) }),
  } })
  Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: {
    getItem: name => entries.get(name) ?? null,
    setItem: (name, value) => entries.set(name, value),
    removeItem: name => entries.delete(name),
  } })
  try { run(entries) } finally {
    if (priorWindow) Object.defineProperty(globalThis, 'window', priorWindow)
    else delete globalThis.window
    if (priorStorage) Object.defineProperty(globalThis, 'localStorage', priorStorage)
    else delete globalThis.localStorage
  }
}

test('phone browsers default to confirmation when first opened in either orientation', () => {
  for (const userAgent of phoneAgents) {
    for (const width of [390, 844]) {
      inBrowser({ userAgent, width }, () => {
        const settings = loadStoredAssistSettings()
        assert.equal(settings.confirmDiscard, true)
        assert.equal(settings.autoFlower, true)
        assert.equal(settings.autoDiscard, false)
      })
    }
  }
})

test('touch-primary browsers keep the mobile default with a desktop user agent and wide viewport', () => {
  inBrowser({ userAgent: desktopAgent, width: 1024 }, () => {
    assert.equal(loadStoredAssistSettings().confirmDiscard, true)
  })
})

test('a phone browser still defaults to confirmation with a precise pointing device', () => {
  for (const userAgent of phoneAgents) {
    inBrowser({ userAgent, coarse: false, width: 844 }, () => {
      assert.equal(loadStoredAssistSettings().confirmDiscard, true)
    })
  }
})

test('a narrow desktop browser does not acquire the phone default', () => {
  for (const width of [390, 1440]) {
    inBrowser({ userAgent: desktopAgent, coarse: false, width }, () => {
      assert.equal(loadStoredAssistSettings().confirmDiscard, false)
    })
  }
})

test('legacy preferences without confirmation inherit the mobile default', () => {
  inBrowser({ stored: JSON.stringify({ autoFlower: false, silentTiles: [65] }), width: 844 }, () => {
    const settings = loadStoredAssistSettings()
    assert.equal(settings.confirmDiscard, true)
    assert.equal(settings.autoFlower, false)
    assert.deepEqual(settings.silentTiles, [65])
  })
})

test('corrupt preferences recover to the phone default', () => {
  inBrowser({ stored: '{broken json', width: 844 }, entries => {
    assert.equal(loadStoredAssistSettings().confirmDiscard, true)
    assert.equal(entries.has(key), false)
  })
})

test('explicit confirmation preferences survive reload, rotation and round resets', () => {
  for (const enabled of [false, true]) {
    inBrowser({ stored: JSON.stringify({ confirmDiscard: enabled }) }, () => {
      const settings = loadStoredAssistSettings()
      assert.equal(settings.confirmDiscard, enabled)
      window.innerWidth = 844
      assert.equal(resetRoundAssistSettings(settings).confirmDiscard, enabled)
      saveStoredAssistSettings(settings)
      assert.equal(loadStoredAssistSettings().confirmDiscard, enabled)
    })
  }
})

test('the preference remains safe to normalize outside a browser', () => {
  assert.equal(normalizeAssistSettings(null).confirmDiscard, false)
})
