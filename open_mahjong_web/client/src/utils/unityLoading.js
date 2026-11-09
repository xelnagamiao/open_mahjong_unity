// Unity reserves the final 10% for engine startup. Its progress callback can reach
// 1 before WaitForInitialization resolves, so only the instance promise means ready.
export function unityLoadingProgress(previous, value, downloadsComplete = null) {
  const progress = Number.isFinite(value) ? Math.max(previous, Math.min(1, Math.max(0, value))) : previous
  const initializing = downloadsComplete ?? progress >= 0.9
  return {
    progress,
    percent: initializing ? (progress >= 1 ? 95 : 80) : Math.min(79, Math.floor(progress / 0.9 * 80)),
    phase: initializing ? 'initializing' : 'downloading',
  }
}

// Content-Length describes the delivered package, not Unity's decoded buffers.
// Unity exposes a loading ratio, not live compressed transfer bytes. In-flight
// amounts are estimates; completed ResourceTiming entries confirm final sizes.
export function createUnityDownloadMeter() {
  const files = new Map(['dataUrl', 'codeUrl', 'frameworkUrl'].map(key => [key, { size: 0, done: false }]))
  let dataFraction = 0
  return {
    setSize(key, size) {
      if (files.has(key) && Number.isFinite(size) && size > 0) files.get(key).size = size
    },
    complete(key, size = 0) {
      if (!files.has(key)) return
      this.setSize(key, size)
      files.get(key).done = true
    },
    snapshot(progress = 0) {
      if (Number.isFinite(progress)) dataFraction = Math.max(dataFraction, Math.min(0.98, Math.max(0, progress) / 0.9))
      const known = [...files.values()].every(file => file.size > 0)
      const complete = [...files.values()].every(file => file.done)
      const total = known ? [...files.values()].reduce((sum, file) => sum + file.size, 0) : 0
      const loaded = known ? [...files.entries()].reduce((sum, [key, file]) => sum + file.size * (file.done ? 1 : key === 'dataUrl' ? dataFraction : 0), 0) : 0
      return { loaded, total, complete }
    },
  }
}

export function formatLoadingBytes({ loaded, total }) {
  if (!total) return ''
  return `${(Math.min(loaded, total) / 1e6).toFixed(1)} / ${(total / 1e6).toFixed(1)} MB`
}

export function formatLoadingTime(milliseconds) {
  const seconds = Math.max(0, Math.floor(milliseconds / 1000))
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
}

export const UNITY_LOADING_TEXT = {
  'zh-CN': {
    preparing: '正在读取游戏配置…',
    loader: '正在加载游戏启动器…',
    downloading: '正在下载游戏资源…',
    initializing: '正在编译与初始化游戏…',
    elapsed: '总耗时',
    stageTime: '本阶段',
    error: '游戏加载失败，请重试。',
    retry: '重新加载',
    progress: '游戏加载进度',
  },
  'zh-TW': {
    preparing: '正在讀取遊戲設定…',
    loader: '正在載入遊戲啟動器…',
    downloading: '正在下載遊戲資源…',
    initializing: '正在編譯與初始化遊戲…',
    elapsed: '總耗時',
    stageTime: '本階段',
    error: '遊戲載入失敗，請重試。',
    retry: '重新載入',
    progress: '遊戲載入進度',
  },
  en: {
    preparing: 'Reading game configuration…',
    loader: 'Loading game launcher…',
    downloading: 'Downloading game resources…',
    initializing: 'Compiling and starting the game…',
    elapsed: 'Elapsed',
    stageTime: 'This stage',
    error: 'The game could not load. Please try again.',
    retry: 'Reload',
    progress: 'Game loading progress',
  },
}

export function unityLoadingText(locale) {
  if (locale === 'zh-HK') return UNITY_LOADING_TEXT['zh-TW']
  return UNITY_LOADING_TEXT[locale] || UNITY_LOADING_TEXT.en
}
