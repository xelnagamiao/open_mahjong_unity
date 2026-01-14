<template>
  <div class="unity-game-container">
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
import { ref, onMounted, onUnmounted } from 'vue'

const unityCanvas = ref(null)
let unityInstance = null

// Unity 配置
const buildUrl = '/unity-game/Build'
const loaderUrl = buildUrl + '/webbuild.loader.js'

const config = {
  dataUrl: buildUrl + '/webbuild.data.gz',
  frameworkUrl: buildUrl + '/webbuild.framework.js.gz',
  codeUrl: buildUrl + '/webbuild.wasm.gz',
  streamingAssetsUrl: '/unity-game/StreamingAssets',
  companyName: 'DefaultCompany',
  productName: 'open_mahjong_unity',
  productVersion: '0.0.31.0',
  showBanner: unityShowBanner
}

// 显示警告横幅
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

// 调整 Unity 容器大小以保持 16:9 宽高比
function adjustUnityContainer() {
  const container = document.querySelector('#unity-container')
  const canvas = document.querySelector('#unity-canvas')
  if (!container || !canvas) return

  const containerParent = container.parentElement
  if (!containerParent) return

  const parentWidth = containerParent.clientWidth
  const parentHeight = containerParent.clientHeight

  // 计算 16:9 宽高比
  const aspectRatio = 16 / 9
  let width = parentWidth
  let height = width / aspectRatio

  // 如果高度超出父容器，则以高度为准
  if (height > parentHeight) {
    height = parentHeight
    width = height * aspectRatio
  }

  // 设置容器尺寸
  container.style.width = width + 'px'
  container.style.height = height + 'px'
  canvas.style.width = width + 'px'
  canvas.style.height = height + 'px'
}

// 加载 Unity 游戏
function loadUnityGame() {
  const canvas = unityCanvas.value
  if (!canvas) return

  const loadingBar = document.querySelector('#unity-loading-bar')
  const progressBarFull = document.querySelector('#unity-progress-bar-full')
  const fullscreenButton = document.querySelector('#unity-fullscreen-button')

  if (loadingBar) {
    loadingBar.style.display = 'block'
  }

  // 加载 Unity loader
  const script = document.createElement('script')
  script.src = loaderUrl
  script.onload = () => {
    if (typeof createUnityInstance === 'function') {
      createUnityInstance(canvas, config, (progress) => {
        if (progressBarFull) {
          progressBarFull.style.width = 100 * progress + '%'
        }
      })
        .then((instance) => {
          unityInstance = instance
          if (loadingBar) {
            loadingBar.style.display = 'none'
          }
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
        })
    }
  }
  script.onerror = () => {
    console.error('无法加载 Unity loader')
    alert('无法加载 Unity 游戏文件')
  }
  document.body.appendChild(script)
}

onMounted(() => {
  // 初始调整大小
  adjustUnityContainer()

  // 监听窗口大小变化
  window.addEventListener('resize', adjustUnityContainer)

  // 加载 Unity 游戏
  loadUnityGame()
})

onUnmounted(() => {
  // 清理事件监听
  window.removeEventListener('resize', adjustUnityContainer)
  
  // 清理 Unity 实例
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

