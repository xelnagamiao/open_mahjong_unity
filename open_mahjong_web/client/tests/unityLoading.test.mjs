import test from 'node:test'
import assert from 'node:assert/strict'
import { unityEnginePercent, unityLoadingText, formatLoadingBytes, unityBundleProgress } from '../src/utils/unityLoading.js'

test('engine progress stays within the dedicated startup range until readiness is confirmed', () => {
  for (const [split, start, mid, end] of [[true, 40, 60, 81], [false, 0, 49, 98]]) {
    assert.equal(unityEnginePercent(0, 0, split), start)
    assert.equal(unityEnginePercent(start, 0.5, split), mid)
    for (const value of [0, -1, NaN, null, undefined, Infinity]) {
      assert.equal(unityEnginePercent(mid, value, split), mid)
    }
    for (const value of [1, 4]) assert.equal(unityEnginePercent(mid, value, split), end)
  }
})

test('the same bar continues through mounting without counting completed bundles twice', () => {
  assert.equal(unityBundleProgress({ completed: 0, total: 5, progress: 0 }), 82)
  assert.equal(unityBundleProgress({ completed: 2, total: 5, progress: 1 }), 86)
  assert.equal(unityBundleProgress({ completed: 2, total: 5, progress: 0.5 }), 88)
  assert.equal(unityBundleProgress({ completed: 5, total: 5, progress: 1 }), 94)
})

test('download copy uses received bytes and the actual download total', () => {
  const text = unityLoadingText('zh-CN')
  assert.equal(text.artDownload + ' ' + formatLoadingBytes({ loaded: 17.6e6, total: 41.8e6 }), '正在下载牌桌资源 17.6 / 41.8 MB')
  assert.equal(formatLoadingBytes({ loaded: 50e6, total: 41.8e6 }), '41.8 / 41.8 MB')
  assert.equal(formatLoadingBytes({ loaded: 0, total: 0 }), '')
})

test('checking, cache hit, startup and ready states do not claim to be downloads', () => {
  const text = unityLoadingText('zh-CN')
  assert.equal(text.checking, '正在准备牌桌资源…')
  assert.equal(text.artReady, '牌桌资源已就绪，正在启动游戏。')
  assert.equal(text.starting, '正在启动游戏。')
  assert.equal(text.ready, '准备完成，即将进入游戏。')
})

test('all supported locales cover every loading phase, accessibility and retry action', () => {
  const keys = Object.keys(unityLoadingText('zh-CN')).sort()
  for (const locale of ['zh-TW', 'en']) assert.deepEqual(Object.keys(unityLoadingText(locale)).sort(), keys)
  assert.equal(unityLoadingText('zh-HK'), unityLoadingText('zh-TW'))
  assert.equal(unityLoadingText('ja'), unityLoadingText('en'))
})
