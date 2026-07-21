import simkaiUrl from '../fonts/simkai.woff2'
import simfangUrl from '../fonts/simfang-old.woff2'
import latinModernUrl from '../fonts/latinmodern-math.woff2'

function isAnyFontAvailable(fontNames: string[]): boolean {
  const canvas = document.createElement('canvas')
  const context = canvas.getContext('2d')
  if (!context) {
    return false
  }

  const testString = '中文WmliAa'
  const size = '72px'

  context.font = `${size} monospace`
  const monoWidth = context.measureText(testString).width
  context.font = `${size} sans-serif`
  const sansWidth = context.measureText(testString).width

  for (const name of fontNames) {
    context.font = `${size} '${name}', monospace`
    if (context.measureText(testString).width !== monoWidth) {
      return true
    }
    context.font = `${size} '${name}', sans-serif`
    if (context.measureText(testString).width !== sansWidth) {
      return true
    }
  }

  return false
}

let fontsInjected = false

export function loadMissingFonts(): void {
  if (fontsInjected) {
    return
  }

  const fonts = [
    {
      name: 'SimKai',
      localNames: ['SimKai', 'KaiTi', 'KaiTi_GB2312', 'STKaiti', 'AR PL UKai CN', '楷体'],
      url: simkaiUrl,
      alwaysDownload: false,
    },
    {
      name: 'SimFang',
      localNames: ['SimFang', 'FangSong', 'FangSong_GB2312', 'STFangsong', '仿宋', '仿宋_GB2312'],
      url: simfangUrl,
      alwaysDownload: false,
    },
    {
      name: 'CmuSerif',
      localNames: ['CMU Serif', 'CMU Serif Regular', 'Latin Modern Math'],
      url: latinModernUrl,
      alwaysDownload: true,
    },
  ]

  const rules = fonts.map((font) => {
    const hasLocal = !font.alwaysDownload && isAnyFontAvailable(font.localNames)
    const localSrc = font.localNames.map((name) => `local('${name}')`).join(', ')
    const src = hasLocal ? localSrc : `${localSrc}, url('${font.url}') format('woff2')`
    return `
@font-face {
  font-family: '${font.name}';
  src: ${src};
  font-weight: normal;
  font-style: normal;${hasLocal ? '' : "\n  font-display: swap;"}
}`
  })

  const styleTag = document.createElement('style')
  styleTag.textContent = rules.join('\n')
  document.head.appendChild(styleTag)
  fontsInjected = true
}

export async function waitForGameFonts(): Promise<void> {
  loadMissingFonts()
  await Promise.allSettled([
    document.fonts.load('300px SimKai'),
    document.fonts.load('300px SimFang'),
    document.fonts.load('300px CmuSerif'),
  ])
}