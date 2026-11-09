import test from 'node:test'
import assert from 'node:assert/strict'
import { unityLoadingProgress, unityLoadingText, createUnityDownloadMeter, formatLoadingBytes, formatLoadingTime } from '../src/utils/unityLoading.js'

test('Unity download and startup are separate and never claim ready before its promise', () => {
  assert.deepEqual(unityLoadingProgress(0, 0.45), { progress: 0.45, percent: 40, phase: 'downloading' })
  assert.equal(unityLoadingProgress(0.89, 0.9).phase, 'initializing')
  assert.deepEqual(unityLoadingProgress(0.9, 1), { progress: 1, percent: 95, phase: 'initializing' })
  assert.deepEqual(unityLoadingProgress(0.9, 0.9, false), { progress: 0.9, percent: 79, phase: 'downloading' })
  assert.deepEqual(unityLoadingProgress(0.9, 0.9, true), { progress: 0.9, percent: 80, phase: 'initializing' })
})

test('regressive, invalid and out-of-range callbacks cannot reset or overflow the bar', () => {
  for (const value of [0, -1, NaN, undefined, Infinity]) {
    assert.deepEqual(unityLoadingProgress(0.6, value), { progress: 0.6, percent: 53, phase: 'downloading' })
  }
  assert.equal(unityLoadingProgress(0, 4).percent, 95)
})

test('package sizes use server bytes and remain unknown until all headers are available', () => {
  const meter = createUnityDownloadMeter()
  meter.setSize('dataUrl', 74e6)
  meter.setSize('codeUrl', 19e6)
  assert.equal(formatLoadingBytes(meter.snapshot(0.4)), '')
  meter.setSize('frameworkUrl', 0.2e6)
  meter.complete('frameworkUrl')
  const partial = meter.snapshot(0.45)
  assert.equal(partial.total, 93.2e6)
  assert.equal(formatLoadingBytes(partial), '37.2 / 93.2 MB')
  assert.equal(meter.snapshot(0.2).loaded, partial.loaded)
})

test('estimated download never claims complete until every actual response finishes', () => {
  const meter = createUnityDownloadMeter()
  meter.setSize('dataUrl', 74e6)
  meter.complete('codeUrl', 19e6)
  meter.complete('frameworkUrl', 0.2e6)
  assert.equal(meter.snapshot(1).complete, false)
  assert.ok(meter.snapshot(1).loaded < meter.snapshot(1).total)
  meter.complete('dataUrl', 74e6)
  assert.deepEqual(meter.snapshot(1), { loaded: 93.2e6, total: 93.2e6, complete: true })
  assert.equal(formatLoadingBytes(meter.snapshot(1)), '93.2 / 93.2 MB')
})

test('resource timing can fill missing headers; missing sizes never prevent completion', () => {
  const meter = createUnityDownloadMeter()
  meter.setSize('dataUrl', NaN)
  meter.setSize('frameworkUrl', -1)
  for (const key of ['dataUrl', 'codeUrl', 'frameworkUrl']) meter.complete(key)
  assert.equal(meter.snapshot().complete, true)
  assert.equal(formatLoadingBytes(meter.snapshot()), '')
  meter.complete('dataUrl', 3e6)
  meter.complete('codeUrl', 2e6)
  meter.complete('frameworkUrl', 1e6)
  assert.equal(formatLoadingBytes(meter.snapshot()), '6.0 / 6.0 MB')
})

test('elapsed time remains real and formats minute rollover', () => {
  assert.equal(formatLoadingTime(0), '0:00')
  assert.equal(formatLoadingTime(-5), '0:00')
  assert.equal(formatLoadingTime(65432), '1:05')
})

test('loading text uses the existing website locale without changing saved game language', () => {
  assert.equal(unityLoadingText('zh-CN').downloading, '正在下载游戏资源…')
  assert.equal(unityLoadingText('zh-HK'), unityLoadingText('zh-TW'))
  assert.equal(unityLoadingText('en').retry, 'Reload')
  assert.equal(unityLoadingText('ja'), unityLoadingText('en'))
})
