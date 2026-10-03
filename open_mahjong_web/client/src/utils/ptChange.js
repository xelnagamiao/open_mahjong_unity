export function formatPtChange(value) {
  if (value == null || value === '') return '—'
  const number = Number(value)
  if (!Number.isFinite(number)) return '—'
  const text = number.toFixed(2).replace(/\.?0+$/, '') || '0'
  return `${number > 0 ? '+' : ''}${text}`
}
