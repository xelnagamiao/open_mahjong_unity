export function parseMalaysiaFan(label) {
  const parts = String(label).split('|')
  return parts.length >= 4 && parts[0] === 'MY'
    ? {name: parts[3], value: parts[2] === '满番' ? '满番' : `${parts[2]}番`} : null
}

export function malaysiaInfoAt(round, node) {
  let info = round?.malaysia ?? null
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    if (tick[0] === 'malaysia' && tick[1] === 'state') info = tick[2]
  }
  return info
}
