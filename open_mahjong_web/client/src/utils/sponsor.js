/** 赞助到期时间展示与计算 */

export function getSponsorStatus(expiresAt) {
  if (!expiresAt) {
    return { label: '非赞助者', tagType: 'info', remaining: null, isActive: false }
  }
  const exp = new Date(expiresAt)
  if (Number.isNaN(exp.getTime())) {
    return { label: '无效时间', tagType: 'warning', remaining: null, isActive: false }
  }
  const now = Date.now()
  if (exp.getTime() <= now) {
    return {
      label: '已过期',
      tagType: 'danger',
      remaining: '已过期',
      isActive: false,
      expiresAt: exp,
    }
  }
  return {
    label: '赞助中',
    tagType: 'success',
    remaining: formatRemaining(exp.getTime() - now),
    isActive: true,
    expiresAt: exp,
  }
}

export function formatRemaining(ms) {
  if (ms <= 0) return '已过期'
  const sec = Math.floor(ms / 1000)
  const days = Math.floor(sec / 86400)
  const hours = Math.floor((sec % 86400) / 3600)
  const minutes = Math.floor((sec % 3600) / 60)
  if (days > 0) return `剩余 ${days} 天 ${hours} 小时`
  if (hours > 0) return `剩余 ${hours} 小时 ${minutes} 分钟`
  return `剩余 ${minutes} 分钟`
}

export function addDays(baseDate, days) {
  const d = baseDate ? new Date(baseDate) : new Date()
  if (Number.isNaN(d.getTime())) return new Date(Date.now() + days * 86400000)
  return new Date(d.getTime() + days * 86400000)
}

export function toPickerValue(date) {
  if (!date) return null
  const d = date instanceof Date ? date : new Date(date)
  if (Number.isNaN(d.getTime())) return null
  const pad = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}
