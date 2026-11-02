/** 每次点击都由服务端校验当前 APK，不在网页中固定发行版号。 */
export const MOBILE_DOWNLOAD = {
  metadataPath: '/api/client-updates/android/latest',
  label: 'Android APK',
  hint: '适用于 Android 手机与平板，下载后直接安装即可。',
}

export const MOBILE_DOWNLOAD_PAGE = {
  title: '手机版下载',
  description: '下载测试版 Android APK，可能会经常更新，可注意群906497522动态',
}

export async function resolveMobileDownload(fetchImpl = globalThis.fetch) {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), 15000)
  try {
    const nonce = `${Date.now()}-${Math.random().toString(36).slice(2)}`
    const response = await fetchImpl(`${MOBILE_DOWNLOAD.metadataPath}?request=${nonce}`, {
      cache: 'no-store',
      headers: { Accept: 'application/json' },
      signal: controller.signal,
    })
    if (!response.ok) throw new Error('获取安装包信息失败，请稍后重试。')
    const release = await response.json()
    if (release?.status !== 'available') throw new Error('新安装包尚未就绪，请稍后重试。')
    if (release.schemaVersion !== 1 || !/^[a-f0-9]{64}$/.test(release.sha256)
        || typeof release.versionName !== 'string' || !/^[0-9A-Za-z.+_-]{1,64}$/.test(release.versionName)
        || !Number.isSafeInteger(release.versionCode) || release.versionCode < 1
        || !Number.isSafeInteger(release.sizeBytes) || release.sizeBytes < 1
        || release.downloadPath !== `/api/client-updates/android/apk/${release.sha256}`) {
      throw new Error('安装包信息无效，请稍后重试。')
    }
    return {
      path: release.downloadPath,
      filename: `open_mahjong_unity-${release.versionName}-${release.versionCode}.apk`,
      versionName: release.versionName,
      versionCode: release.versionCode,
    }
  } catch (error) {
    if (error?.name === 'AbortError') throw new Error('获取安装包信息超时，请重试。')
    throw error
  } finally {
    clearTimeout(timer)
  }
}

export async function openMobileDownload({ fetchImpl = globalThis.fetch, documentRef = globalThis.document } = {}) {
  const release = await resolveMobileDownload(fetchImpl)
  const link = documentRef.createElement('a')
  link.href = release.path
  link.download = release.filename
  link.rel = 'noopener'
  documentRef.body.appendChild(link)
  try {
    link.click()
  } finally {
    documentRef.body.removeChild(link)
  }
  return release
}
