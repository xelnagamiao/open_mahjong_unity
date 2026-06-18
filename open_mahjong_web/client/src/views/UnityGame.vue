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

function installAndroidLeaveGuard() {
  if (!isAndroidWeb()) return

  history.pushState({ unityGameLeaveGuard: true }, '')

  androidPopStateHandler = async () => {
    if (skipLeaveConfirm) return

    const ok = await confirmLeavePlatform()
    if (ok) {
      skipLeaveConfirm = true
      history.back()
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

onBeforeRouteLeave(async (_to, _from, next) => {
  if (!isAndroidWeb() || skipLeaveConfirm) {
    next()
    return
  }

  const ok = await confirmLeavePlatform()
  if (ok) {
    skipLeaveConfirm = true
    next()
  } else {
    next(false)
  }
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
          if (gen !== unityMountGeneration) {
            try { instance.Quit() } catch (e) { /* ignore */ }
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
  removeUnityLoaderScript()
  if (unityInstance) {
    try {
      unityInstance.Quit()
    } catch (e) {
      console.error('Unity 实例清理失败:', e)
    }
    unityInstance = null
  }
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
