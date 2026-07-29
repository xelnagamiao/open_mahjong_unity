import {
  GAME_FONT_LICENSE_URLS,
  waitForGameFonts,
  setGameFontTheme,
  setGameLatinFontTheme,
} from './fontLoader'
import { ensureTexturesLoaded } from './scene/textures'
import { loadStoredSceneAppearance } from '../lib/storage'

export const GAME_SOUND_ASSETS = [
  ...[
    '01-start', '03-cd', '05-draw', '06-discard', '08-inquire', '09-cpk', '25-xchg',
  ].map((alias) => ({ alias, file: `${alias}.wav` })),
  { alias: 'fan-reveal', file: 'fan-reveal.mp3' },
  // Exact copy of Unity Resources/Sound/Effects/Timer02-0.1s.wav.
  { alias: 'decision-countdown-tick', file: 'guessfan-countdown.wav' },
  ...[1, 2].flatMap((voiceId) =>
    ['chi', 'peng', 'gang', 'buhua', 'hu'].map((voice) => ({
      alias: `voice-${voiceId}-${voice}`,
      file: `voices/${voiceId}/${voice}.mp3`,
    })),
  ),
]

const soundObjectUrls = new Map<string, string>()
let preloadPromise: Promise<void> | null = null

function soundUrl(file: string): string {
  return `${import.meta.env.BASE_URL}game2d-assets/sounds/${file}`
}

async function preloadSounds(): Promise<void> {
  await Promise.all(GAME_SOUND_ASSETS.map(async (sound) => {
    if (soundObjectUrls.has(sound.file)) return
    const response = await fetch(soundUrl(sound.file), { cache: 'force-cache' })
    if (!response.ok) throw new Error(`音效资源加载失败：${sound.file}`)
    const blob = await response.blob()
    soundObjectUrls.set(sound.file, URL.createObjectURL(blob))
  }))
}

async function preloadFontLicenses(): Promise<void> {
  await Promise.all(GAME_FONT_LICENSE_URLS.map(async (url) => {
    const response = await fetch(url, { cache: 'force-cache' })
    if (!response.ok) throw new Error(`字体授权文件加载失败：${url}`)
  }))
}

export function getPreloadedSoundUrl(file: string): string {
  return soundObjectUrls.get(file) ?? soundUrl(file)
}

/**
 * Complete 2D resource gate. The session store awaits this before opening WS,
 * so fonts, tiles and sounds cannot begin downloading only after table entry.
 */
export function preloadGame2dResources(): Promise<void> {
  if (preloadPromise) return preloadPromise
  const appearance = loadStoredSceneAppearance()
  setGameFontTheme(appearance.fontTheme)
  setGameLatinFontTheme(appearance.latinFontTheme)
  preloadPromise = Promise.all([
    waitForGameFonts(),
    preloadFontLicenses(),
    ensureTexturesLoaded(),
    preloadSounds(),
  ]).then(() => undefined)
  return preloadPromise
}
