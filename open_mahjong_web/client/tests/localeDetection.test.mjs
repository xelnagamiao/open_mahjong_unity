import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { detectBrowserLocale, normalizeLocale } from '../src/i18n/locale-utils.js'
import { LOCALE_STORAGE_KEY, locale, restorePersistentLocale, setLocale } from '../src/i18n/index.js'

const unityRoot = new URL('../../../open_mahjong_unity/', import.meta.url)

function runWebglSystemLanguage(nav) {
  const bridge = fs.readFileSync(new URL('Assets/Plugins/WebGL/SystemLanguage.jslib', unityRoot), 'utf8')
  const context = {
    navigator: nav,
    LibraryManager: { library: {} },
    mergeInto: (to, values) => Object.assign(to, values),
  }
  vm.runInNewContext(bridge, context)
  return context.LibraryManager.library.SalasasaSystemLanguage()
}

test('normalizeLocale matches primary language and Chinese script tags accurately', () => {
  assert.equal(normalizeLocale('en-HK'), 'en')
  assert.equal(normalizeLocale('en-TW'), 'en')
  assert.equal(normalizeLocale('zh-Hans-HK'), 'zh-CN')
  assert.equal(normalizeLocale('zh-Hans-TW'), 'zh-CN')
  assert.equal(normalizeLocale('zh-Hant-CN'), 'zh-TW')
  assert.equal(normalizeLocale('zh-Hant-HK'), 'zh-HK')
  assert.equal(normalizeLocale(' zh_MO '), 'zh-HK')
  assert.equal(normalizeLocale('ko-KR'), null)
  assert.equal(normalizeLocale('   '), null)
})

test('detectBrowserLocale iterates navigator.languages and falls back to navigator.language', () => {
  assert.equal(detectBrowserLocale({ languages: ['ko-KR', 'zh-CN'], language: 'en-US' }), 'zh-CN')
  assert.equal(detectBrowserLocale({ languages: ['pt-BR', 'ja-JP'], language: 'zh-CN' }), 'ja')
  assert.equal(detectBrowserLocale({ languages: [], language: 'fr-CA' }), 'fr')
  assert.equal(detectBrowserLocale({ languages: ['ko-KR'], language: 'vi-VN' }), 'zh-CN')
  assert.equal(detectBrowserLocale({ languages: ['ko-KR'], language: 'vi-VN' }, 'en'), 'en')
})

test('temporary URL locale override does not persist to localStorage and restores on leave', () => {
  const previousLocale = locale.value
  const previousWindow = globalThis.window
  const storage = new Map([[LOCALE_STORAGE_KEY, 'zh-CN']])
  globalThis.window = {
    localStorage: {
      getItem: (key) => (storage.has(key) ? storage.get(key) : null),
      setItem: (key, value) => storage.set(key, String(value)),
    },
  }
  try {
    setLocale('en', { persist: false })
    assert.equal(locale.value, 'en')
    assert.equal(storage.get(LOCALE_STORAGE_KEY), 'zh-CN')

    restorePersistentLocale()
    assert.equal(locale.value, 'zh-CN')

    setLocale('fr', { persist: false })
    assert.equal(locale.value, 'fr')
    assert.equal(storage.get(LOCALE_STORAGE_KEY), 'zh-CN')

    setLocale('ja')
    assert.equal(locale.value, 'ja')
    assert.equal(storage.get(LOCALE_STORAGE_KEY), 'ja')

    restorePersistentLocale()
    assert.equal(locale.value, 'ja')
  } finally {
    if (previousWindow === undefined) delete globalThis.window
    else globalThis.window = previousWindow
    setLocale(previousLocale, { persist: false })
  }
})

test('WebGL SystemLanguage.jslib traverses navigator.languages before falling back to English', () => {
  assert.equal(runWebglSystemLanguage({ languages: ['ko-KR', 'zh-CN'], language: 'en-US' }), 0)
  assert.equal(runWebglSystemLanguage({ languages: ['pt-BR', 'zh-TW'], language: 'en-US' }), 1)
  assert.equal(runWebglSystemLanguage({ languages: ['zh-Hans-TW'], language: 'en-US' }), 0)
  assert.equal(runWebglSystemLanguage({ languages: ['zh-Hant-CN'], language: 'en-US' }), 1)
  assert.equal(runWebglSystemLanguage({ languages: ['en-HK', 'zh-HK'], language: 'zh-HK' }), 2)
  assert.equal(runWebglSystemLanguage({ languages: ['vi-VN', 'ja-JP'], language: 'en-US' }), 6)
  assert.equal(runWebglSystemLanguage({ languages: [], language: 'de_DE' }), 4)
  assert.equal(runWebglSystemLanguage({ languages: ['ko-KR', 'vi-VN'], language: 'th-TH' }), 2)
})
