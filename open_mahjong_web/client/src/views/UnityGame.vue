<template>
  <div class="unity-game-container">
    <div id="unity-container" class="unity-container">
      <canvas id="unity-canvas" ref="unityCanvas" :class="{ 'waiting-for-content': loadingVisible }" :inert="loadingVisible"></canvas>
      <div v-if="loadingVisible" id="unity-loading-bar" class="unity-loading-bar" data-no-translate>
        <div class="unity-loading-title">open_mahjong_unity</div>
        <div id="unity-progress-bar-empty" class="unity-progress-bar-empty"
          role="progressbar" :aria-label="loadingText.progress" aria-valuemin="0" aria-valuemax="100"
          :aria-valuenow="loadingPercent" :aria-valuetext="loadingPercent + '% · ' + loadingCaption"
          :class="{ 'is-error': loadingPhase === 'error' }">
          <div id="unity-progress-bar-full" class="unity-progress-bar-full" :style="{ width: loadingPercent + '%' }"></div>
        </div>
        <div class="unity-loading-caption">
          <span><span role="status">{{ loadingText[loadingPhase] }}</span><span v-if="downloadSizeText" class="unity-download-size">{{ downloadSizeText }}</span></span>
          <span class="unity-loading-percent" aria-hidden="true">{{ loadingPercent }}%</span>
        </div>
        <button v-if="loadingPhase === 'error'" class="unity-loading-retry" @click="reloadGame">{{ loadingText.retry }}</button>
      </div>
      <button v-if="noticeUrl" v-show="noticeVisible" type="button" class="unity-notice"
        :aria-label="loadingText.dismissNotice" @click="dismissNotice">
        <img :src="noticeUrl" :alt="loadingText.notice" @load="noticeLoaded = true" @error="dismissNotice">
      </button>
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
import { prepareUnityContent } from '@/utils/unityContent.js'
import { unityEnginePercent, unityLoadingText, formatLoadingBytes, unityBundleProgress } from '@/utils/unityLoading.js'

const unityCanvas = ref(null)
const loadingVisible = ref(true)
const loadingPhase = ref('checking')
const loadingPercent = ref(0)
const loadingText = computed(() => unityLoadingText(locale.value))
const artDownload = ref(null)
const downloadSizeText = computed(() => loadingPhase.value === 'artDownload' && artDownload.value
  ? formatLoadingBytes(artDownload.value) : '')
const loadingCaption = computed(() => [loadingText.value[loadingPhase.value], downloadSizeText.value].filter(Boolean).join(' '))
let splitContent = false
let mountingArt = false
let sceneReady = false
let readyTimer = null
let contentAbort = null
let preparedContent = null
const noticeUrl = ref('')
const noticeVisible = ref(false)
let noticeLoaded = false
let noticeTimer = null

function dismissNotice() {
  clearTimeout(noticeTimer)
  noticeVisible.value = false
}
function stopLoadingMonitor() {
  clearTimeout(readyTimer)
  readyTimer = null
  contentAbort?.abort()
  contentAbort = null
  preparedContent?.dispose()
  preparedContent = null
}

function revealWhenLoaded() {
  if (!loadingVisible.value || !unityInstance || (splitContent && !sceneReady) ||
      loadingPhase.value === 'error' || loadingPhase.value === 'ready') return
  loadingPercent.value = 100
  loadingPhase.value = 'ready'
  stopLoadingMonitor()
  readyTimer = setTimeout(() => {
    readyTimer = null
    loadingVisible.value = false
    if (noticeLoaded) {
      noticeVisible.value = true
      noticeTimer = setTimeout(dismissNotice, 2000)
    }
  }, 400)
}

function onBundleProgress({ detail }) {
  if (!splitContent || !loadingVisible.value || loadingPhase.value === 'error' || leavingByHardNav || !detail) return
  if (detail.phase === 4) return failLoading('WebGL resource bundle failed')
  if (loadingPhase.value === 'ready') return
  mountingArt = true
  if (detail.phase === 0) {
    loadingPercent.value = Math.max(loadingPercent.value, unityBundleProgress(detail))
  } else if (detail.phase === 1) {
    loadingPercent.value = Math.max(loadingPercent.value, 95)
  } else if (detail.phase === 2) {
    loadingPercent.value = 98
  } else if (detail.phase === 3) {
    sceneReady = true
    revealWhenLoaded()
  }
}

function failLoading(error) {
  console.error('Unity 加载失败:', error)
  loadingPhase.value = 'error'
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
  stopLoadingMonitor()
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

async function parseUnityConfig(signal) {
  try {
    const response = await fetch('/unity-game/index.html', { cache: 'no-store', signal })
    if (!response.ok) throw new Error(`Unity configuration: HTTP ${response.status}`)
    const html = await response.text()

    const loaderMatch = html.match(/var\s+loaderUrl\s*=\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)
    const dataMatch = html.match(/dataUrl:\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)
    const frameworkMatch = html.match(/frameworkUrl:\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)
    const codeMatch = html.match(/codeUrl:\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)
    const versionMatch = html.match(/\bproductVersion\s*:\s*(["'])([^"'\r\n]+)\1/)

    const entry = new DOMParser().parseFromString(html, 'text/html')
    const manifest = entry.getElementById('om-web-content')
    const notice = html.match(/canvas\.style\.background\s*=\s*[^\r\n]*?buildUrl\s*\+\s*"\/([\w.-]+\.(?:jpg|png))'/)
    if (!manifest && entry.querySelector('meta[name="om-web-texture-progress"]')) {
      throw new Error('此 WebGL 构建缺少启动前资源清单')
    }
    const buildUrl = '/unity-game/Build'
    return {
      content: manifest ? JSON.parse(manifest.textContent) : null,
      noticeUrl: notice ? buildUrl + '/' + notice[1] : '',
      loaderUrl: loaderMatch ? buildUrl + '/' + loaderMatch[1] : null,
      dataUrl: dataMatch ? buildUrl + '/' + dataMatch[1] : null,
      frameworkUrl: frameworkMatch ? buildUrl + '/' + frameworkMatch[1] : null,
      codeUrl: codeMatch ? buildUrl + '/' + codeMatch[1] : null,
      // 旧模板省略版本时，用带哈希的数据文件名区分构建。
      productVersion: versionMatch?.[2] ?? dataMatch?.[1]
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
  loadingPhase.value = 'checking'
  loadingPercent.value = 0
  artDownload.value = null

  contentAbort = new AbortController()
  const signal = contentAbort.signal
  const fileConfig = await parseUnityConfig(signal)
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
    productVersion: fileConfig.productVersion,
    autoSyncPersistentDataPath: true,
    showBanner: unityShowBanner
  }

  splitContent = !!fileConfig.content
  if (splitContent) noticeUrl.value = fileConfig.noticeUrl
  if (splitContent) {
    try {
      const content = await prepareUnityContent(fileConfig.content, { signal,
        onProgress({ loaded, total }) {
          if (gen !== unityMountGeneration || signal.aborted) return
          loadingPercent.value = Math.max(loadingPercent.value, Math.floor(40 * loaded / total))
        },
        onDownloadProgress({ loaded, total }) {
          if (gen !== unityMountGeneration || signal.aborted) return
          loadingPhase.value = total > 0 ? 'artDownload' : 'checking'
          artDownload.value = total > 0 ? { loaded, total } : null
        }
      })
      if (gen !== unityMountGeneration || signal.aborted) { content.dispose(); return }
      config.omWebContent = preparedContent = content
    } catch (error) {
      if (gen === unityMountGeneration && !signal.aborted) failLoading(error)
      return
    }
  }

  removeUnityLoaderScript()
  const script = document.createElement('script')
  script.id = UNITY_LOADER_SCRIPT_ID
  script.src = fileConfig.loaderUrl
  loadingPhase.value = splitContent ? 'artReady' : 'starting'
  script.onload = () => {
    if (gen !== unityMountGeneration) return
    requestAnimationFrame(() => {
      if (gen !== unityMountGeneration) return
      if (typeof createUnityInstance !== 'function') {
        failLoading('createUnityInstance 未定义')
        return
      }
      createUnityInstance(canvas, config, (progress) => {
        if (gen !== unityMountGeneration || leavingByHardNav || mountingArt || !loadingVisible.value ||
            loadingPhase.value === 'error' || loadingPhase.value === 'ready') return
        loadingPercent.value = unityEnginePercent(loadingPercent.value, progress, splitContent)
      })
        .then((instance) => {
          if (gen !== unityMountGeneration || leavingByHardNav) {
            // 页面已在离开：不要调用 Quit（会卡死），交给即将到来的整页卸载
            return
          }
          unityInstance = instance
          revealWhenLoaded()
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
  window.addEventListener('unity-web-texture-progress', onBundleProgress)
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
  window.removeEventListener('unity-web-texture-progress', onBundleProgress)
  removeLeaveGuard()
  stopLoadingMonitor()
  dismissNotice()

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

#unity-canvas.waiting-for-content {
  opacity: 0;
  pointer-events: none;
}

.unity-notice {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  padding: 0;
  border: 0;
  background: #000;
  cursor: pointer;
}

.unity-notice img { width: 100%; height: 100%; object-fit: contain; }

.unity-loading-bar {
  position: absolute;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  width: 80%;
  max-width: 500px;
  box-sizing: border-box;
  z-index: 10;
  color: #dedede;
  font-family: system-ui, "Microsoft YaHei", sans-serif;
  font-weight: 400;
}

.unity-loading-title {
  width: fit-content;
  max-width: 100%;
  box-sizing: border-box;
  margin: 0 0 8px;
  color: #eeeeee;
  font-size: 20px;
  line-height: 1.5;
  letter-spacing: 0.02em;
  overflow-wrap: anywhere;
}

.unity-progress-bar-empty {
  position: relative;
  width: 100%;
  height: 10px;
  box-sizing: border-box;
  margin-top: 0;
  padding: 2px;
  border: 1px solid #767676;
  background: #191919;
  overflow: hidden;
}

.unity-progress-bar-full {
  width: 0%;
  height: 100%;
  background: #eeeeee;
  transition: width 160ms linear;
}

.unity-loading-caption {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: baseline;
  gap: 8px;
  margin-top: 9px;
  font-size: 14px;
  line-height: 1.5;
  font-variant-numeric: tabular-nums;
}

.unity-loading-percent {
  text-align: right;
  white-space: nowrap;
}

.unity-download-size {
  margin-inline-start: 0.5em;
  white-space: nowrap;
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
