export function parseYixingFan(label) {
  const fields = String(label || '').split('|')
  if (fields.length < 4 || fields[0] !== 'YX') return null
  return { name: fields[3], value: fields[2].startsWith('×') ? fields[2] : `${fields[2]}花` }
}
