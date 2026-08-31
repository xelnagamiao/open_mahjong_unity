import { ElMessage, ElMessageBox } from 'element-plus'

export function takeCountForQuota(quota, requested) {
  const need = Math.max(0, Math.floor(Number(requested) || 0))
  if (need <= 0) return { take: 0, partial: false, remaining: 0 }
  if (quota?.unlimited) {
    return { take: need, partial: false, remaining: need }
  }
  const remaining = Math.max(0, Number(quota?.remaining) || 0)
  const take = Math.min(need, remaining)
  return { take, partial: take < need, remaining }
}

/**
 * 下载前刷新后的配额确认。取消或额度为 0 时返回 0。
 * 超出剩余额度时提示无法全部下载，确认后返回实际可下载局数。
 */
export async function confirmRecordDownload(quota, requested, { actionLabel = '下载' } = {}) {
  const need = Math.max(0, Math.floor(Number(requested) || 0))
  if (need <= 0) return 0
  const used = Number(quota?.used) || 0
  const max = Number(quota?.max) || 200
  const { take, partial, remaining } = takeCountForQuota(quota, need)
  if (take <= 0) {
    ElMessage.error(`今日牌谱下载已达上限（${used}/${max} 局，凌晨 4 点刷新）`)
    return 0
  }
  const lines = quota?.unlimited
    ? [
      '开发模式：下载不限局数。',
      `本次将${actionLabel} ${need} 局。`,
    ]
    : [
      `今日限额：已用 ${used} / ${max} 局（凌晨 4 点刷新）。`,
      `本次需要 ${need} 局，剩余 ${remaining} 局。`,
    ]
  if (partial) {
    lines.push(`当前限额无法完全下载所有牌谱数据（将${actionLabel} ${take} / ${need} 局）。是否确定？`)
  } else {
    lines.push(`是否确定${actionLabel}这 ${take} 局？`)
  }
  try {
    await ElMessageBox.confirm(lines.join('<br/>'), '下载牌谱', {
      confirmButtonText: '确定',
      cancelButtonText: '取消',
      type: partial ? 'warning' : 'info',
      dangerouslyUseHTMLString: true,
      distinguishCancelAndClose: true,
    })
    return take
  } catch (_) {
    return 0
  }
}
