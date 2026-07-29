import notoSerifScUrl from '../fonts/noto-serif-sc.woff2'
import notoSerifLatinUrl from '../fonts/noto-serif-latin.woff2'
import notoSansLatinUrl from '../fonts/noto-sans-latin.woff2'
import latinModernUrl from '../fonts/latinmodern-math.woff2'
import arphicKaitiUrl from '../fonts/gkai00mp.ttf'
import notoSerifLicenseUrl from '../fonts/OFL-Noto-Serif-SC.txt?url'
import notoSansLicenseUrl from '../fonts/OFL-Noto-Sans-CJK-SC.txt?url'
import latinModernLicenseUrl from '../fonts/GUST-FONT-LICENSE-Latin-Modern.txt?url'
import arphicLicenseUrl from '../fonts/ARPHIC-PUBLIC-LICENSE.txt?url'

/** Chinese typeface used after the Latin-only family in the CSS font stack. */
export type GameFontTheme = 'source-serif' | 'system-kaiti' | 'system-default' | 'arphic-ukai'

/** Latin-only typeface, selected independently from the Chinese typeface. */
export type GameLatinFontTheme = 'latin-modern' | 'noto-serif-latin' | 'noto-sans-latin'

type FontAsset = {
  family: string
  url: string
  format: 'woff2' | 'truetype'
  weight: string
  testWeight: number
}

const DEFAULT_GAME_FONT_THEME: GameFontTheme = 'arphic-ukai'
const DEFAULT_LATIN_FONT_THEME: GameLatinFontTheme = 'latin-modern'

const FONT_FAMILIES = {
  notoSerifCjk: 'Game2D Noto Serif SC',
  notoSerifLatin: 'Game2D Noto Serif Latin',
  notoSansLatin: 'Game2D Noto Sans Latin',
  latinModern: 'Game2D Latin Modern Math',
  arphicKaiti: 'Game2D AR PL KaitiM GB',
} as const

const FONT_ASSETS: FontAsset[] = [
  {
    family: FONT_FAMILIES.notoSerifCjk,
    url: notoSerifScUrl,
    format: 'woff2',
    weight: '400',
    testWeight: 400,
  },
  {
    family: FONT_FAMILIES.notoSerifLatin,
    url: notoSerifLatinUrl,
    format: 'woff2',
    weight: '400',
    testWeight: 400,
  },
  {
    family: FONT_FAMILIES.notoSansLatin,
    url: notoSansLatinUrl,
    format: 'woff2',
    weight: '400',
    testWeight: 400,
  },
  {
    family: FONT_FAMILIES.latinModern,
    url: latinModernUrl,
    format: 'woff2',
    weight: '400',
    testWeight: 400,
  },
  {
    family: FONT_FAMILIES.arphicKaiti,
    url: arphicKaitiUrl,
    format: 'truetype',
    weight: '400',
    testWeight: 400,
  },
]

export const GAME_FONT_LICENSE_URLS = [
  notoSerifLicenseUrl,
  notoSansLicenseUrl,
  latinModernLicenseUrl,
  arphicLicenseUrl,
] as const

let fontsInjected = false
let activeTheme: GameFontTheme = DEFAULT_GAME_FONT_THEME
let activeLatinTheme: GameLatinFontTheme = DEFAULT_LATIN_FONT_THEME
let fontLoadPromise: Promise<void> | null = null

export function normalizeGameFontTheme(value: unknown): GameFontTheme {
  switch (value) {
    case 'source-serif':
    case 'system-kaiti':
    case 'system-default':
    case 'arphic-ukai':
      return value
    // Migrate removed Chinese font choices.
    case 'private-simkai':
    case 'private-simfang':
    case 'wenkai':
    case 'ma-shan-zheng':
    case 'long-cang':
      return 'system-kaiti'
    default:
      return DEFAULT_GAME_FONT_THEME
  }
}

export function normalizeGameLatinFontTheme(value: unknown): GameLatinFontTheme {
  switch (value) {
    case 'latin-modern':
    case 'noto-serif-latin':
    case 'noto-sans-latin':
      return value
    default:
      return DEFAULT_LATIN_FONT_THEME
  }
}

function getLatinFontFamily(theme: GameLatinFontTheme): string {
  switch (theme) {
    case 'noto-serif-latin':
      return `"${FONT_FAMILIES.notoSerifLatin}", "${FONT_FAMILIES.notoSansLatin}", "${FONT_FAMILIES.latinModern}"`
    case 'noto-sans-latin':
      return `"${FONT_FAMILIES.notoSansLatin}", "${FONT_FAMILIES.notoSerifLatin}", "${FONT_FAMILIES.latinModern}"`
    default:
      return `"${FONT_FAMILIES.latinModern}", "${FONT_FAMILIES.notoSansLatin}", "${FONT_FAMILIES.notoSerifLatin}"`
  }
}

function getChineseFontFamily(theme: GameFontTheme): string {
  switch (theme) {
    case 'system-default':
      return `system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", sans-serif`
    case 'system-kaiti':
      return `"KaiTi", "楷体", "KaiTi_GB2312", "STKaiti", "AR PL UKai CN", "${FONT_FAMILIES.notoSerifCjk}", serif`
    case 'arphic-ukai':
      return `"${FONT_FAMILIES.arphicKaiti}", "AR PL KaitiM GB", "AR PL UKai CN", "KaiTi", "楷体", "STKaiti", "${FONT_FAMILIES.notoSerifCjk}", serif`
    default:
      return `"${FONT_FAMILIES.notoSerifCjk}", serif`
  }
}

export function getGameFontFamily(
  theme: GameFontTheme = activeTheme,
  latinTheme: GameLatinFontTheme = activeLatinTheme,
): string {
  return `${getLatinFontFamily(normalizeGameLatinFontTheme(latinTheme))}, ${getChineseFontFamily(normalizeGameFontTheme(theme))}`
}

export function setGameFontTheme(theme: GameFontTheme): void {
  activeTheme = normalizeGameFontTheme(theme)
  document.documentElement.style.setProperty('--game2d-font-family', getGameFontFamily())
}

export function setGameLatinFontTheme(theme: GameLatinFontTheme): void {
  activeLatinTheme = normalizeGameLatinFontTheme(theme)
  document.documentElement.style.setProperty('--game2d-font-family', getGameFontFamily())
}

export function loadGameFontFaces(): void {
  if (fontsInjected) return
  const styleTag = document.createElement('style')
  styleTag.dataset.game2dFonts = 'standard'
  styleTag.textContent = FONT_ASSETS.map((font) => `
@font-face {
  font-family: '${font.family}';
  src: url('${font.url}') format('${font.format}');
  font-weight: ${font.weight};
  font-style: normal;
  font-display: block;
}`).join('\n')
  document.head.appendChild(styleTag)
  fontsInjected = true
}

async function preloadFontAsset(font: FontAsset): Promise<void> {
  const descriptor = `${font.testWeight} 300px "${font.family}"`
  const loadedFaces = await document.fonts.load(descriptor)
  if (loadedFaces.length === 0 || loadedFaces.some((face) => face.status !== 'loaded')) {
    throw new Error(`字体资源加载失败：${font.family}`)
  }
}

/**
 * Download and parse every selectable 2D font before the game websocket opens.
 */
export function waitForGameFonts(): Promise<void> {
  if (fontLoadPromise) return fontLoadPromise
  loadGameFontFaces()
  fontLoadPromise = Promise.all(FONT_ASSETS.map(preloadFontAsset)).then(() => undefined)
  return fontLoadPromise
}
