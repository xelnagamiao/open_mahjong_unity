export const IOS_VERSION_PATH = '/api/client-updates/ios/version'

export function mobileDownloadRedirect(route, device = {}) {
  if (!/^\/mobile-download\/?$/.test(route.path)) return null
  const platform = route.query?.platform
  const overrides = Array.isArray(platform) ? platform : [platform]
  if (overrides.some(value => typeof value === 'string' && value.toLowerCase() === 'android')) return null
  const ios = /iPhone|iPad|iPod/i.test(device?.userAgent ?? '')
    || (device?.platform === 'MacIntel' && device.maxTouchPoints > 1)
  if (!ios) return null
  return { path: '/ios-download', query: route.query, hash: route.hash, replace: true }
}

export function normalizeIosRelease(value, origin) {
  const release = value?.data ?? value
  if (!release || !['pending', 'available'].includes(release.status)) {
    throw new Error('iOS版本信息无效，请稍后重试。')
  }
  const channel = ['sideload', 'testflight', 'appstore'].includes(release.channel)
    ? release.channel : 'sideload'
  let installUrl = ''
  if (release.status === 'available') {
    if (!['sideload', 'testflight', 'appstore'].includes(release.channel)) {
      throw new Error('iOS安装方式无效，请稍后重试。')
    }
    const sourceUrl = channel === 'sideload' ? release.downloadPath : release.installUrl
    if (channel === 'sideload' && !/^\/api\/client-updates\/ios\/ipa\/[a-f0-9]{64}$/.test(sourceUrl ?? '')) {
      throw new Error('iOS安装地址无效，请稍后重试。')
    }
    if (typeof sourceUrl !== 'string' || !sourceUrl.trim()) {
      throw new Error('iOS安装地址尚未就绪，请稍后重试。')
    }
    const url = new URL(sourceUrl, origin)
    const localHttp = url.protocol === 'http:' && url.origin === origin
      && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
    if ((url.protocol !== 'https:' && !localHttp) || url.username || url.password) {
      throw new Error('iOS安装地址无效，请稍后重试。')
    }
    if ((channel === 'testflight' && url.hostname !== 'testflight.apple.com')
        || (channel === 'appstore' && url.hostname !== 'apps.apple.com')) {
      throw new Error('iOS安装地址与发布渠道不匹配，请稍后重试。')
    }
    installUrl = url.href
  }
  return {
    status: release.status,
    channel,
    installUrl,
    versionName: typeof release.versionName === 'string' ? release.versionName : '',
    buildNumber: Number.isSafeInteger(release.buildNumber) && release.buildNumber > 0 ? release.buildNumber : '',
    releaseVersion: Number.isSafeInteger(release.releaseVersion) ? String(release.releaseVersion) : '',
    notes: typeof release.notes === 'string' ? release.notes : '',
  }
}

export async function fetchIosRelease({ fetchImpl = globalThis.fetch, origin = globalThis.location?.origin } = {}) {
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), 15000)
  try {
    const response = await fetchImpl(IOS_VERSION_PATH, {
      cache: 'no-store', headers: { Accept: 'application/json' }, signal: controller.signal,
    })
    if (!response.ok) throw new Error('暂时无法获取iOS版本，请稍后重试。')
    return normalizeIosRelease(await response.json(), origin)
  } catch (error) {
    if (error?.name === 'AbortError') throw new Error('获取iOS版本超时，请重试。')
    throw error
  } finally {
    clearTimeout(timeout)
  }
}
