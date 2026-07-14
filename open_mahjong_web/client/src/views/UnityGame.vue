<template>
  <div class="unity-game-container">
    <div v-if="hintVisible" class="unity-page-hint">{{ hintText }}</div>
    <div id="unity-container" class="unity-container">
      <canvas id="unity-canvas" ref="unityCanvas"></canvas>
      <div id="unity-loading-bar" class="unity-loading-bar">
        <div id="unity-logo" class="unity-logo"></div>
        <div id="unity-progress-bar-empty" class="unity-progress-bar-empty">
          <div id="unity-progress-bar-full" class="unity-progress-bar-full"></div>
        </div>
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
import { ref, onMounted, onUnmounted, nextTick } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessageBox } from 'element-plus'

const unityCanvas = ref(null)
const hintVisible = ref(true)
const hintText = ref('正在准备 WebGL，界面可能短暂无响应属正常现象…')

let unityInstance = null
let unityMountGeneration = 0
let skipLeaveConfirm = false
let leavingByHardNav = false
let androidPopStateHandler = null
const UNITY_LOADER_SCRIPT_ID = 'unity-webgl-loader-script'

function isAndroidWeb() {
  return typeof navigator !== 'undefined' && /Android/i.test(navigator.userAgent || '')
}

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

function installAndroidLeaveGuard() {
  if (!isAndroidWeb()) return

  history.pushState({ unityGameLeaveGuard: true }, '')

  androidPopStateHandler = async () => {
    if (skipLeaveConfirm || leavingByHardNav) return

    const ok = await confirmLeavePlatform()
    if (ok) {
      skipLeaveConfirm = true
      hardNavigateAway('/')
    } else {
      history.pushState({ unityGameLeaveGuard: true }, '')
    }
  }

  window.addEventListener('popstate', androidPopStateHandler)
}

function removeAndroidLeaveGuard() {
  if (androidPopStateHandler) {
    window.removeEventListener('popstate', androidPopStateHandler)
    androidPopStateHandler = null
  }
}

onBeforeRouteLeave(async (to, _from, next) => {
  if (leavingByHardNav) {
    // 硬跳转已开始，阻止 Vue 再做软卸载
    next(false)
    return
  }

  if (isAndroidWeb() && !skipLeaveConfirm) {
    const ok = await confirmLeavePlatform()
    if (!ok) {
      next(false)
      return
    }
    skipLeaveConfirm = true
  }

  // 不要 next(false)：后退场景下会 history.go(1) 抢回游戏页，和整页跳转打架
  // 也不要 next()：会触发 SPA 卸载 Unity
  // 直接整页离开；不调用 next，文档卸载后守卫自然结束
  hardNavigateAway(to.fullPath || '/')
})

function removeUnityLoaderScript() {
  const el = document.getElementById(UNITY_LOADER_SCRIPT_ID)
  if (el) el.remove()
}

async function parseUnityConfig() {
  try {
    const response = await fetch('/unity-game/index.html', { cache: 'no-store' })
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

  const loadingBar = document.querySelector('#unity-loading-bar')
  const progressBarFull = document.querySelector('#unity-progress-bar-full')
  const fullscreenButton = document.querySelector('#unity-fullscreen-button')

  if (loadingBar) {
    loadingBar.style.display = 'block'
  }

  const fileConfig = await parseUnityConfig()
  if (gen !== unityMountGeneration) return

  if (!fileConfig || !fileConfig.loaderUrl) {
    console.error('无法解析Unity配置文件')
    alert('无法加载Unity游戏配置，请检查Unity构建文件是否存在')
    if (loadingBar) {
      loadingBar.style.display = 'none'
    }
    hintVisible.value = false
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
    showBanner: unityShowBanner
  }

  removeUnityLoaderScript()
  const script = document.createElement('script')
  script.id = UNITY_LOADER_SCRIPT_ID
  script.src = fileConfig.loaderUrl
  script.onload = () => {
    if (gen !== unityMountGeneration) return
    hintText.value = '正在下载与编译资源，请稍候…'
    requestAnimationFrame(() => {
      if (gen !== unityMountGeneration) return
      if (typeof createUnityInstance !== 'function') {
        console.error('createUnityInstance 未定义')
        alert('Unity 加载器异常')
        if (loadingBar) loadingBar.style.display = 'none'
        hintVisible.value = false
        return
      }
      createUnityInstance(canvas, config, (progress) => {
        if (progressBarFull) {
          progressBarFull.style.width = 100 * progress + '%'
        }
      })
        .then((instance) => {
          if (gen !== unityMountGeneration || leavingByHardNav) {
            // 页面已在离开：不要调用 Quit（会卡死），交给即将到来的整页卸载
            return
          }
          unityInstance = instance
          if (loadingBar) {
            loadingBar.style.display = 'none'
          }
          hintVisible.value = false
          if (fullscreenButton) {
            fullscreenButton.onclick = () => {
              if (unityInstance) {
                unityInstance.SetFullscreen(1)
              }
            }
          }
        })
        .catch((message) => {
          console.error('Unity 加载失败:', message)
          alert('Unity 游戏加载失败: ' + message)
          hintVisible.value = false
        })
    })
  }
  script.onerror = () => {
    console.error('无法加载 Unity loader')
    alert('无法加载 Unity 游戏文件')
    if (loadingBar) {
      loadingBar.style.display = 'none'
    }
    hintVisible.value = false
  }
  document.body.appendChild(script)
}

onMounted(() => {
  document.body.style.background = '#000000'
  document.documentElement.style.background = '#000000'

  skipLeaveConfirm = false
  leavingByHardNav = false

  const gen = ++unityMountGeneration
  adjustUnityContainer()
  window.addEventListener('resize', adjustUnityContainer)
  window.addEventListener('orientationchange', onOrientationChange)
  installAndroidLeaveGuard()

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
  removeAndroidLeaveGuard()

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

.unity-page-hint {
  position: absolute;
  top: 12px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 30;
  max-width: min(520px, 92vw);
  padding: 8px 14px;
  font-size: 13px;
  line-height: 1.45;
  color: #334155;
  background: rgba(255, 255, 255, 0.92);
  border-radius: 999px;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.15);
  pointer-events: none;
  text-align: center;
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
  display: none;
  z-index: 10;
}

.unity-logo {
  width: 154px;
  height: 130px;
  background: url('/unity-game/TemplateData/unity-logo-dark.png') no-repeat center;
  background-size: contain;
  margin: 0 auto;
}

.unity-progress-bar-empty {
  width: 141px;
  height: 18px;
  margin-top: 10px;
  margin-left: 6.5px;
  background: url('/unity-game/TemplateData/progress-bar-empty-dark.png') no-repeat center;
  background-size: contain;
}

.unity-progress-bar-full {
  width: 0%;
  height: 18px;
  margin-top: 10px;
  background: url('/unity-game/TemplateData/progress-bar-full-dark.png') no-repeat center;
  background-size: contain;
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
