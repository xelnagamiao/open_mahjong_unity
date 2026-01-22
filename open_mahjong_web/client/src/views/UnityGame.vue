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

// 从Unity的index.html中解析文件名
async function parseUnityConfig() {
  try {
    const response = await fetch('/unity-game/index.html')
    const html = await response.text()
    
    // 解析loaderUrl
    const loaderMatch = html.match(/var\s+loaderUrl\s*=\s*buildUrl\s*\+\s*["']\/([^"']+)["']/)
    // 解析config对象中的文件名
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

// 加载 Unity 游戏
async function loadUnityGame() {
  const canvas = unityCanvas.value
  if (!canvas) return

  const loadingBar = document.querySelector('#unity-loading-bar')
  const progressBarFull = document.querySelector('#unity-progress-bar-full')
  const fullscreenButton = document.querySelector('#unity-fullscreen-button')

  if (loadingBar) {
    loadingBar.style.display = 'block'
  }

  // 先从Unity的index.html中解析文件名
  const fileConfig = await parseUnityConfig()
  if (!fileConfig || !fileConfig.loaderUrl) {
    console.error('无法解析Unity配置文件')
    alert('无法加载Unity游戏配置，请检查Unity构建文件是否存在')
    if (loadingBar) {
      loadingBar.style.display = 'none'
    }
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

  // 加载 Unity loader
  const script = document.createElement('script')
  script.src = fileConfig.loaderUrl
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
    if (loadingBar) {
      loadingBar.style.display = 'none'
    }
  }
  document.body.appendChild(script)
}

onMounted(() => {
  // 初始调整大小
  adjustUnityContainer()

  // 监听窗口大小变化和屏幕方向变化
  window.addEventListener('resize', adjustUnityContainer)
  window.addEventListener('orientationchange', () => {
    // 方向改变后延迟一点时间再调整，确保尺寸已更新
    setTimeout(adjustUnityContainer, 100)
  })

  // 加载 Unity 游戏
  loadUnityGame()
})

onUnmounted(() => {
  // 清理事件监听
  window.removeEventListener('resize', adjustUnityContainer)
  window.removeEventListener('orientationchange', adjustUnityContainer)
  
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

