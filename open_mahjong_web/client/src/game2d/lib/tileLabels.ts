export type TileLabelMode = 'auto' | 'on' | 'off'

export function normalizeTileLabelMode(value: unknown): TileLabelMode {
  return value === 'on' || value === 'off' ? value : 'auto'
}

export function shouldShowTileLabels(mode: TileLabelMode, language: string): boolean {
  if (mode !== 'auto') return mode === 'on'
  return !/^(?:zh|ja)(?:-|$)/i.test(String(language).replaceAll('_', '-'))
}

export function tileFaceLabel(faceId: number): string {
  if (!Number.isInteger(faceId)) return ''
  if ([105, 205, 305].includes(faceId)) return '5'
  if ([1, 2, 3].includes(Math.floor(faceId / 10)) && faceId % 10 >= 1 && faceId % 10 <= 9) {
    return String(faceId % 10)
  }
  if (faceId >= 41 && faceId <= 47) {
    const labels = ['E', 'S', 'W', 'N', 'R', 'Wh', 'G']
    return labels[faceId - 41]
  }
  if (faceId >= 51 && faceId <= 58) return String((faceId - 51) % 4 + 1)
  return ''
}

export function tileFaceLabelColor(faceId: number): string {
  return faceId === 45 || faceId === 46 ? '#151515' : '#d00000'
}
