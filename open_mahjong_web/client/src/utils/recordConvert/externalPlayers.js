export function isExternalRecord(title = {}) {
  return title?.is_external === true
    || ['tziakcha', 'mjai', 'botzone'].includes(title?.source_format)
    || Object.prototype.hasOwnProperty.call(title || {}, 'tziakcha_session_id')
}

export function externalPlayerName(title, index, fallback) {
  const name = title?.[`p${index}_name`]
  return typeof name === 'string' && name.length ? name : fallback
}
