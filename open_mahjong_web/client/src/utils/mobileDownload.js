/** 静态目录：client/public/android-game/（与 unity-game 并列） */
export const MOBILE_DOWNLOAD = {
  path: '/android-game/open_mahjong_unity.apk',
  filename: 'open_mahjong_unity.apk',
  label: 'Android APK',
  hint: '适用于 Android 手机与平板，下载后直接安装即可。',
}

export const MOBILE_DOWNLOAD_PAGE = {
  title: '手机版下载',
  description: '下载测试版 Android APK，可能会经常更新，可注意群906497522动态',
}

export function openMobileDownload() {
  const link = document.createElement('a')
  link.href = MOBILE_DOWNLOAD.path
  link.download = MOBILE_DOWNLOAD.filename
  link.rel = 'noopener'
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
}
