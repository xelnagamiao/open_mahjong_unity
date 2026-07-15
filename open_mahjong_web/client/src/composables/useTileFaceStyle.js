import { ref, watch } from 'vue'

const STORAGE_KEY = 'omu-tile-face-style'

/** 小助手 face PNG（默认） / 立体 SVG */
export const TILE_FACE_STYLE = {
  helper: 'helper',
  oblique: 'oblique',
}

function readStoredStyle() {
  try {
    const v = localStorage.getItem(STORAGE_KEY)
    if (v === TILE_FACE_STYLE.oblique || v === TILE_FACE_STYLE.helper) return v
  } catch {
    /* ignore */
  }
  return TILE_FACE_STYLE.helper
}

const tileFaceStyle = ref(readStoredStyle())

watch(tileFaceStyle, (v) => {
  try {
    localStorage.setItem(STORAGE_KEY, v)
  } catch {
    /* ignore */
  }
})

export function useTileFaceStyle() {
  const setTileFaceStyle = (style) => {
    if (style === TILE_FACE_STYLE.helper || style === TILE_FACE_STYLE.oblique) {
      tileFaceStyle.value = style
    }
  }
  return { tileFaceStyle, setTileFaceStyle, TILE_FACE_STYLE }
}
