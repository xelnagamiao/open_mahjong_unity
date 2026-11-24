// The instance promise (and the split build's scene-ready event), not the loader
// callback, decides when to show 100%.
export function unityEnginePercent(previous, progress, splitContent) {
  const start = splitContent ? 40 : 0
  const end = splitContent ? 81 : 98
  const fraction = Number.isFinite(progress) ? Math.min(1, Math.max(0, progress)) : 0
  const current = Number.isFinite(previous) ? Math.min(end, Math.floor(previous)) : 0
  return Math.max(current, start + Math.floor(fraction * (end - start)))
}

export function unityBundleProgress({ completed, total, progress }) {
  // A completion notification already includes that bundle in the completed count.
  const fraction = progress >= 1 ? 0 : Math.max(0, progress)
  return 82 + Math.floor(12 * Math.min(1, (completed + fraction) / Math.max(1, total)))
}

export function formatLoadingBytes({ loaded, total }) {
  if (!Number.isFinite(total) || total <= 0 || !Number.isFinite(loaded)) return ''
  return `${(Math.min(Math.max(0, loaded), total) / 1e6).toFixed(1)} / ${(total / 1e6).toFixed(1)} MB`
}

export const UNITY_LOADING_TEXT = {
  'zh-CN': {
    checking: '正在准备牌桌资源…',
    artDownload: '正在下载牌桌资源',
    artReady: '牌桌资源已就绪，正在启动游戏。',
    starting: '正在启动游戏。',
    ready: '准备完成，即将进入游戏。',
    notice: '平台声明',
    dismissNotice: '关闭声明，进入游戏',
    error: '游戏加载失败，请重试。',
    retry: '重新加载',
    progress: '游戏加载进度',
  },
  'zh-TW': {
    checking: '正在準備牌桌資源…',
    artDownload: '正在下載牌桌資源',
    artReady: '牌桌資源已就緒，正在啟動遊戲。',
    starting: '正在啟動遊戲。',
    ready: '準備完成，即將進入遊戲。',
    notice: '平台聲明',
    dismissNotice: '關閉聲明，進入遊戲',
    error: '遊戲載入失敗，請重試。',
    retry: '重新載入',
    progress: '遊戲載入進度',
  },
  en: {
    checking: 'Preparing table assets…',
    artDownload: 'Downloading table assets',
    artReady: 'Table assets ready. Starting the game.',
    starting: 'Starting the game.',
    ready: 'Ready. Entering the game.',
    notice: 'Platform notice',
    dismissNotice: 'Dismiss notice and enter game',
    error: 'The game could not load. Please try again.',
    retry: 'Reload',
    progress: 'Game loading progress',
  },
}

export function unityLoadingText(locale) {
  if (locale === 'zh-HK') return UNITY_LOADING_TEXT['zh-TW']
  return UNITY_LOADING_TEXT[locale] || UNITY_LOADING_TEXT.en
}
