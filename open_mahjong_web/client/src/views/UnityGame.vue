<template>
  <div class="unity-game-container">
    <div id="unity-container" class="unity-container">
      <canvas id="unity-canvas" ref="unityCanvas"></canvas>
      <div v-if="loadingVisible" id="unity-loading-bar" class="unity-loading-bar" data-no-translate>
        <div class="unity-loading-title">open_mahjong_unity</div>
        <div id="unity-progress-bar-empty" class="unity-progress-bar-empty"
          role="progressbar" :aria-label="loadingText.progress" aria-valuemin="0" aria-valuemax="100"
          :aria-valuenow="loadingPercent" :aria-valuetext="loadingText[loadingPhase]"
          :class="{ 'is-starting': loadingPhase === 'initializing', 'is-error': loadingPhase === 'error' }">
          <div id="unity-progress-bar-full" class="unity-progress-bar-full" :style="{ width: loadingPercent + '%' }"></div>
        </div>
        <div class="unity-loading-caption">
          <span role="status" aria-live="polite">{{ loadingText[loadingPhase] }}</span>
          <span v-if="loadingPhase !== 'error'" class="unity-loading-percent" aria-hidden="true">{{ loadingPercent }}%</span>
        </div>
        <div class="unity-loading-details">
          <span>{{ downloadSizeText }}</span>
          <span>{{ loadingText.elapsed }} {{ totalTimeText }}</span>
        </div>
        <div v-if="loadingPhase === 'initializing'" class="unity-loading-stage-time">
          {{ loadingText.stageTime }} {{ stageTimeText }}
        </div>
        <button v-if="loadingPhase === 'error'" class="unity-loading-retry" @click="reloadGame">{{ loadingText.retry }}</button>
      </div>
      <div id="unity-warning" class="unity-warning"></div>
      <div id="unity-footer" class="unity-footer">
        <div id="unity-webgl-logo" class="unity-webgl-logo"></div>
        <div id="unity-fullscreen-button" class="unity-fullscreen-button"></div>
        <div id="unity-build-title" class="unity-build-title">open_mahjong_unity</div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, onMounted, onUnmounted, nextTick } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import { locale } from '@/i18n'
import { unityLoadingProgress, unityLoadingText, createUnityDownloadMeter, formatLoadingBytes, formatLoadingTime } from '@/utils/unityLoading.js'

const unityCanvas = ref(null)
const loadingVisible = ref(true)
const loadingPhase = ref('preparing')
const loadingPercent = ref(0)
const loadingText = computed(() => unityLoadingText(locale.value))
let loadingProgress = 0
const downloadSizeText = ref('')
const clockTime = ref(0)
const phaseStartedAt = ref(0)
let loadingStartedAt = 0
let loadingClock = null
let downloadObserver = null
let metadataAbort = null
let downloadMeter = null
const totalTimeText = computed(() => formatLoadingTime(clockTime.value - loadingStartedAt))
const stageTimeText = computed(() => formatLoadingTime(clockTime.value - phaseStartedAt.value))

function setLoadingPhase(phase) {
  if (loadingPhase.value !== phase) phaseStartedAt.value = performance.now()
  loadingPhase.value = phase
}

function stopLoadingMonitor() {
  clearInterval(loadingClock)
  loadingClock = null
  downloadObserver?.disconnect()
  downloadObserver = null
  metadataAbort?.abort()
  metadataAbort = null
}

function updateLoadingProgress(progress = loadingProgress) {
  if (!loadingVisible.value || loadingPhase.value === 'error' || leavingByHardNav) return
  const meter = downloadMeter?.snapshot(progress)
  const next = unityLoadingProgress(loadingProgress, progress, downloadObserver && meter ? meter.complete : null)
  if (meter?.total && next.phase === 'downloading') next.percent = Math.min(79, Math.floor(meter.loaded / meter.total * 80))
  loadingProgress = next.progress
  loadingPercent.value = Math.max(loadingPercent.value, next.percent)
  setLoadingPhase(next.phase)
  if (meter) downloadSizeText.value = formatLoadingBytes(meter)
}

function monitorDownloads(fileConfig, gen) {
  downloadMeter = createUnityDownloadMeter()
  const files = ['dataUrl', 'codeUrl', 'frameworkUrl'].map(key => [new URL(fileConfig[key], location.href).href, key])
  const keys = new Map(files)
  if (typeof PerformanceObserver !== 'undefined') {
    try {
      downloadObserver = new PerformanceObserver(list => {
        if (gen !== unityMountGeneration) return
        let changed = false
        for (const entry of list.getEntries()) {
          const key = keys.get(entry.name)
          if (key) {
            downloadMeter.complete(key, entry.encodedBodySize)
            changed = true
          }
        }
        if (changed) updateLoadingProgress()
      })
      downloadObserver.observe({ type: 'resource' })
    } catch {
      downloadObserver = null
    }
  }
  const controller = new AbortController()
  metadataAbort = controller
  const timeout = setTimeout(() => controller.abort(), 8000)
  // Only headers are requested; downloading starts immediately in parallel.
  // A separate query prevents HEAD timing entries from completing the GET meter.
  Promise.allSettled(files.map(async ([url, key]) => {
    const metadataUrl = new URL(url)
    metadataUrl.searchParams.set('download-size', '1')
    const response = await fetch(metadataUrl, { method: 'HEAD', signal: controller.signal })
    if (response.ok && gen === unityMountGeneration && loadingVisible.value) {
      downloadMeter.setSize(key, Number(response.headers.get('Content-Length')))
      downloadSizeText.value = formatLoadingBytes(downloadMeter.snapshot(loadingProgress))
    }
  })).finally(() => clearTimeout(timeout))
}

function failLoading(error) {
  console.error('Unity 加载失败:', error)
  setLoadingPhase('error')
  loadingVisible.value = true
  stopLoadingMonitor()
}

function reloadGame() {
  window.location.reload()
}

let unityInstance = null
let unityMountGeneration = 0
let skipLeaveConfirm = false
let leavingByHardNav = false
let leaveConfirmOpen = false
let popStateHandler = null
const UNITY_LOADER_SCRIPT_ID = 'unity-webgl-loader-script'
const LEAVE_GUARD_STATE = { unityGameLeaveGuard: true }

async function confirmLeavePlatform() {
  try {
    await ElMessageBox.confirm('退出salasasa平台？', '', {
      confirmButtonText: '确认',
      cancelButtonText: '返回',
      type: 'warning',
      showClose: false,
      closeOnClickModal: false,
      closeOnPressEscape: false,
    })
    return true
  } catch {
    return false
  }
}

async function askLeaveOrStay() {
  if (skipLeaveConfirm || leavingByHardNav) return true
  if (leaveConfirmOpen) return false
  leaveConfirmOpen = true
  try {
    const ok = await confirmLeavePlatform()
    if (ok) skipLeaveConfirm = true
    return ok
  } finally {
    leaveConfirmOpen = false
  }
}

/**
 * SPA 内调用 unityInstance.Quit() 会长时间卡住甚至冻死主线程。
 * Unity 官方说明：离开当前网页时不必手动 Quit，整页跳转由浏览器回收 WebGL/WASM。
 *
 * 注意：浏览器「后退」时地址栏可能已经是目标路径，此时 replace/href 同一 URL 不会刷新，
 * 必须 reload，否则会卡在仍挂着 WebGL 的半卸载页面。
 */
function hardNavigateAway(targetPath) {
  if (leavingByHardNav) return
  leavingByHardNav = true
  unityMountGeneration++
  unityInstance = null

  const path = targetPath || '/'
  const current = window.location.pathname + window.location.search + window.location.hash
  if (current === path) {
    window.location.reload()
  } else {
    window.location.href = path
  }
}

function restoreLeaveGuardState() {
  history.pushState({ ...(history.state || {}), ...LEAVE_GUARD_STATE }, '')
}

/**
 * 桌面后退/鼠标侧键、手机系统返回，都是浏览器历史导航。
 * 垫一层同 URL 的 history，popstate 时先垫回再询问，取消则留在游戏页。
 * 不能依赖 Vue Router 的 next(false)：后退场景下会 history.go(1)，和整页跳转打架。
 */
function installLeaveGuard() {
  restoreLeaveGuardState()

  popStateHandler = async () => {
    if (skipLeaveConfirm || leavingByHardNav) return
    restoreLeaveGuardState()
    if (await askLeaveOrStay()) hardNavigateAway('/')
  }

  window.addEventListener('popstate', popStateHandler)
}

function removeLeaveGuard() {
  if (popStateHandler) {
    window.removeEventListener('popstate', popStateHandler)
    popStateHandler = null
  }
}

onBeforeRouteLeave(async (to, _from, next) => {
  if (leavingByHardNav) {
    next(false)
    return
  }

  if (!await askLeaveOrStay()) {
    next(false)
    return
  }

  // 不要 next()：会触发 SPA 卸载 Unity。直接整页离开，文档卸载后守卫自然结束。
  hardNavigateAway(to.fullPath || '/')
})

function removeUnityLoaderScript() {
  const el = document.getElementById(UNITY_LOADER_SCRIPT_ID)
  if (el) el.remove()
}

async function parseUnityConfig() {
  try {
    const response = await fetch('/unity-game/index.html', { cache: 'no-store' })
    if (!response.ok) throw new Error(`Unity configuration: HTTP ${response.status}`)
    const html = await response.text()

    const loaderMatch = html.match(/var\s+loaderUrl\s*=\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)
    const dataMatch = html.match(/dataUrl:\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)
    const frameworkMatch = html.match(/frameworkUrl:\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)
    const codeMatch = html.match(/codeUrl:\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)

    const buildUrl = '/unity-game/Build'
    return {
      loaderUrl: loaderMatch ? buildUrl + '/' + loaderMatch[1] : null,
      dataUrl: dataMatch ? buildUrl + '/' + dataMatch[1] : null,
      frameworkUrl: frameworkMatch ? buildUrl + '/' + frameworkMatch[1] : null,
      codeUrl: codeMatch ? buildUrl + '/' + codeMatch[1] : null
    }
  } catch (error) {
    console.error('解析Unity配置失败:', error)
    return null
  }
}

function unityShowBanner(msg, type) {
  const warningBanner = document.querySelector('#unity-warning')
  if (!warningBanner) return

  function updateBannerVisibility() {
    warningBanner.style.display = warningBanner.children.length ? 'block' : 'none'
  }

  const div = document.createElement('div')
  div.innerHTML = msg
  warningBanner.appendChild(div)

  if (type === 'error') {
    failLoading(msg)
    div.style = 'background: red; padding: 10px;'
  } else if (type === 'warning') {
    div.style = 'background: yellow; padding: 10px;'
    setTimeout(function() {
      warningBanner.removeChild(div)
      updateBannerVisibility()
    }, 5000)
  }
  updateBannerVisibility()
}

function adjustUnityContainer() {
  const container = document.querySelector('#unity-container')
  const canvas = document.querySelector('#unity-canvas')
  const containerParent = document.querySelector('.unity-game-container')
  if (!container || !canvas || !containerParent) return

  const parentWidth = containerParent.clientWidth
  const parentHeight = containerParent.clientHeight

  const aspectRatio = 16 / 9
  let width = parentWidth
  let height = width / aspectRatio

  if (height > parentHeight) {
    height = parentHeight
    width = height * aspectRatio
  }

  container.style.width = width + 'px'
  container.style.height = height + 'px'
  canvas.style.width = width + 'px'
  canvas.style.height = height + 'px'
}

const onOrientationChange = () => {
  setTimeout(adjustUnityContainer, 100)
}

async function loadUnityGame(gen) {
  const canvas = unityCanvas.value
  if (!canvas) return

  const fullscreenButton = document.querySelector('#unity-fullscreen-button')

  loadingVisible.value = true
  loadingPhase.value = 'preparing'
  loadingPercent.value = 0
  loadingProgress = 0
  downloadSizeText.value = ''
  loadingStartedAt = performance.now()
  clockTime.value = loadingStartedAt
  phaseStartedAt.value = loadingStartedAt
  loadingClock = setInterval(() => { clockTime.value = performance.now() }, 250)

  const fileConfig = await parseUnityConfig()
  if (gen !== unityMountGeneration) return

  if (!fileConfig || !fileConfig.loaderUrl || !fileConfig.dataUrl || !fileConfig.frameworkUrl || !fileConfig.codeUrl) {
    failLoading('无法解析Unity配置文件')
    return
  }

  const config = {
    dataUrl: fileConfig.dataUrl,
    frameworkUrl: fileConfig.frameworkUrl,
    codeUrl: fileConfig.codeUrl,
    streamingAssetsUrl: '/unity-game/StreamingAssets',
    companyName: 'DefaultCompany',
    productName: 'open_mahjong_unity',
    productVersion: '0.0.31.0',
    autoSyncPersistentDataPath: true,
    showBanner: unityShowBanner
  }

  monitorDownloads(fileConfig, gen)

  removeUnityLoaderScript()
  const script = document.createElement('script')
  script.id = UNITY_LOADER_SCRIPT_ID
  script.src = fileConfig.loaderUrl
  setLoadingPhase('loader')
  script.onload = () => {
    if (gen !== unityMountGeneration) return
    setLoadingPhase('downloading')
    requestAnimationFrame(() => {
      if (gen !== unityMountGeneration) return
      if (typeof createUnityInstance !== 'function') {
        failLoading('createUnityInstance 未定义')
        return
      }
      createUnityInstance(canvas, config, (progress) => {
        if (gen !== unityMountGeneration || leavingByHardNav || loadingPhase.value === 'error') return
        updateLoadingProgress(progress)
      })
        .then((instance) => {
          if (gen !== unityMountGeneration || leavingByHardNav) {
            // 页面已在离开：不要调用 Quit（会卡死），交给即将到来的整页卸载
            return
          }
          unityInstance = instance
          loadingPercent.value = 100
          loadingVisible.value = false
          stopLoadingMonitor()
          if (fullscreenButton) {
            fullscreenButton.onclick = () => {
              if (unityInstance) {
                unityInstance.SetFullscreen(1)
              }
            }
          }
        })
        .catch((message) => {
          if (gen === unityMountGeneration && !leavingByHardNav) failLoading(message)
        })
    })
  }
  script.onerror = () => {
    if (gen === unityMountGeneration && !leavingByHardNav) failLoading('无法加载 Unity loader')
  }
  document.body.appendChild(script)
}

onMounted(() => {
  document.body.style.background = '#000000'
  document.documentElement.style.background = '#000000'

  skipLeaveConfirm = false
  leavingByHardNav = false
  leaveConfirmOpen = false

  const gen = ++unityMountGeneration
  adjustUnityContainer()
  window.addEventListener('resize', adjustUnityContainer)
  window.addEventListener('orientationchange', onOrientationChange)
  installLeaveGuard()

  nextTick(() => {
    requestAnimationFrame(() => {
      if (gen !== unityMountGeneration) return
      loadUnityGame(gen)
    })
  })
})

onUnmounted(() => {
  document.body.style.background = ''
  document.documentElement.style.background = ''

  unityMountGeneration++
  window.removeEventListener('resize', adjustUnityContainer)
  window.removeEventListener('orientationchange', onOrientationChange)
  removeLeaveGuard()
  stopLoadingMonitor()

  // 故意不调用 Quit：SPA 卸载时 Quit 会冻死主线程；硬跳转路径由浏览器整页回收
  unityInstance = null
})
</script>

<style scoped>
.unity-game-container {
  width: 100vw;
  height: 100vh;
  display: flex;
  justify-content: center;
  align-items: center;
  background: #000000;
  overflow: hidden;
  position: relative;
}

.unity-container {
  position: relative;
  background: #000000;
}

#unity-canvas {
  display: block;
  width: 100%;
  height: 100%;
  background: #000000;
}

.unity-loading-bar {
  position: absolute;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  width: 84%;
  max-width: 560px;
  box-sizing: border-box;
  z-index: 10;
  color: #dedede;
  font-family: system-ui, sans-serif;
  font-weight: 400;
}

.unity-loading-title {
  width: fit-content;
  max-width: 100%;
  box-sizing: border-box;
  margin: 0 0 8px;
  color: #eeeeee;
  font-size: 17px;
  line-height: 1.4;
  letter-spacing: 0.025em;
  overflow-wrap: anywhere;
}

.unity-progress-bar-empty {
  position: relative;
  width: 100%;
  height: 12px;
  box-sizing: border-box;
  margin-top: 0;
  padding: 2px;
  border: 1px solid #767676;
  background: #191919;
  overflow: hidden;
}

.unity-progress-bar-empty::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 80%;
  width: 1px;
  background: #191919;
}

.unity-progress-bar-full {
  width: 0%;
  height: 100%;
  background: #eeeeee;
  transition: width 160ms linear;
}

.unity-loading-caption {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 16px;
  margin-top: 10px;
  font-size: 13px;
  line-height: 1.5;
}

.unity-loading-percent {
  flex: none;
  font-variant-numeric: tabular-nums;
}

.unity-loading-details {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 5px;
  color: #aaa;
  font-size: 12px;
  line-height: 1.5;
  font-variant-numeric: tabular-nums;
}

.unity-loading-stage-time {
  margin-top: 4px;
  color: #aaa;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
}

.is-starting .unity-progress-bar-full {
  animation: unity-starting 1.6s ease-in-out infinite alternate;
}

.is-error {
  border-color: #b99292;
}

.unity-loading-retry {
  display: block;
  margin-top: 14px;
  padding: 6px 18px;
  border: 1px solid #767676;
  color: #eeeeee;
  background: #191919;
  font: inherit;
  font-size: 13px;
  cursor: pointer;
}

.unity-loading-retry:hover,
.unity-loading-retry:focus-visible {
  background: #333333;
}

@keyframes unity-starting {
  to { opacity: 0.5; }
}

@media (prefers-reduced-motion: reduce) {
  .unity-progress-bar-full {
    transition: none;
    animation: none;
  }
}

.unity-warning {
  position: absolute;
  left: 50%;
  top: 5%;
  transform: translateX(-50%);
  background: white;
  padding: 10px;
  display: none;
  z-index: 20;
}

.unity-footer {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  height: 38px;
  background: rgba(0, 0, 0, 0.5);
  display: none;
}

.unity-webgl-logo {
  float: left;
  width: 204px;
  height: 38px;
  background: url('/unity-game/TemplateData/webgl-logo.png') no-repeat center;
  background-size: contain;
}

.unity-build-title {
  float: right;
  margin-right: 10px;
  line-height: 38px;
  font-family: Arial, sans-serif;
  font-size: 18px;
  color: white;
}

.unity-fullscreen-button {
  cursor: pointer;
  float: right;
  width: 38px;
  height: 38px;
  background: url('/unity-game/TemplateData/fullscreen-button.png') no-repeat center;
  background-size: contain;
  margin-right: 10px;
}

.unity-fullscreen-button:hover {
  opacity: 0.8;
}
</style>
