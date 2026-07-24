/** Persisted 2D table assist / silent-tile preferences (aligned with Unity AutoAction). */

export interface AssistSettings {
  /** 自动补花 */
  autoFlower: boolean
  /** 自动摸切 */
  autoDiscard: boolean
  /** 自动和牌（开/关） */
  autoWin: boolean
  /**
   * 自动过牌（主按钮「不吃碰杠」）：与 不吃+不碰+不明杠 全选联动。
   * 开启时级联三子项；任一子项关闭时本项变暗。
   */
  autoPass: boolean
  /** 不吃 */
  passChi: boolean
  /** 不碰 */
  passPeng: boolean
  /** 不杠（不明杠） */
  passMingGang: boolean
  /** 不点和：阻止自动荣和，并参与自动过牌筛除 */
  noRon: boolean
  /** 不自摸：自动和牌开启时阻止自动自摸 */
  noTsumo: boolean
  /** 不抢杠：自动和牌开启时阻止自动抢杠和 */
  noRobKong: boolean
  /** 选中牌张：命中河牌/加杠牌时不询问任何操作 */
  silentTiles: number[]
  /** 选中牌不自动自摸（默认开） */
  silentSkipTsumo: boolean
}

export const DEFAULT_ASSIST_SETTINGS: AssistSettings = {
  autoFlower: true,
  autoDiscard: false,
  autoWin: false,
  autoPass: false,
  passChi: false,
  passPeng: false,
  passMingGang: false,
  noRon: false,
  noTsumo: false,
  noRobKong: false,
  silentTiles: [],
  silentSkipTsumo: true,
}

const STORAGE_KEY = 'salasasa.game2d.assistSettings'

function normalizeTileId(value: unknown): number | null {
  const tile = Number(value)
  if (!Number.isInteger(tile) || tile <= 0) return null
  return tile
}

export function normalizeAssistSettings(raw: Partial<AssistSettings> | null | undefined): AssistSettings {
  const silent = Array.isArray(raw?.silentTiles)
    ? [...new Set(raw.silentTiles.map(normalizeTileId).filter((tile): tile is number => tile != null))]
    : []

  // Migrate legacy autoWin 0|1|2 and declineCalls.
  let autoWin = Boolean(raw?.autoWin)
  if (typeof raw?.autoWin === 'number') autoWin = Number(raw.autoWin) > 0
  let autoPass = Boolean(raw?.autoPass)
  let passChi = Boolean(raw?.passChi)
  let passPeng = Boolean(raw?.passPeng)
  let passMingGang = Boolean(raw?.passMingGang)
  const legacy = raw as AssistSettings & { declineCalls?: boolean }
  if (legacy?.declineCalls && !('autoPass' in (raw ?? {}))) {
    autoPass = true
    passChi = true
    passPeng = true
    passMingGang = true
  }
  // Keep main autoPass in sync with the three meld filters.
  autoPass = passChi && passPeng && passMingGang

  return {
    autoFlower: raw?.autoFlower !== false,
    autoDiscard: Boolean(raw?.autoDiscard),
    autoWin,
    autoPass,
    passChi,
    passPeng,
    passMingGang,
    noRon: Boolean(raw?.noRon),
    noTsumo: Boolean(raw?.noTsumo),
    noRobKong: Boolean(raw?.noRobKong),
    silentTiles: silent,
    silentSkipTsumo: raw?.silentSkipTsumo !== false,
  }
}

/** Toggle main autoPass and cascade 不吃/不碰/不明杠. */
export function withAutoPass(settings: AssistSettings, enabled: boolean): AssistSettings {
  return normalizeAssistSettings({
    ...settings,
    autoPass: enabled,
    passChi: enabled,
    passPeng: enabled,
    passMingGang: enabled,
  })
}

/** Update one of 不吃/不碰/不明杠 and resync autoPass. */
export function withMeldPassOption(
  settings: AssistSettings,
  key: 'passChi' | 'passPeng' | 'passMingGang',
  enabled: boolean,
): AssistSettings {
  const next = { ...settings, [key]: enabled }
  next.autoPass = Boolean(next.passChi && next.passPeng && next.passMingGang)
  return normalizeAssistSettings(next)
}

export function loadStoredAssistSettings(): AssistSettings {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (!raw) return { ...DEFAULT_ASSIST_SETTINGS, silentTiles: [] }
  try {
    return normalizeAssistSettings(JSON.parse(raw) as Partial<AssistSettings>)
  } catch {
    localStorage.removeItem(STORAGE_KEY)
    return { ...DEFAULT_ASSIST_SETTINGS, silentTiles: [] }
  }
}

export function saveStoredAssistSettings(settings: AssistSettings): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(normalizeAssistSettings(settings)))
}

/** Standard 34-tile MMCR ids for the silent-tile picker. */
export function standardSilentTileChoices(): number[] {
  const tiles: number[] = []
  for (const suit of [0x40, 0x60, 0xc0]) {
    for (let rank = 1; rank <= 9; rank += 1) tiles.push(suit | rank)
  }
  for (let rank = 1; rank <= 7; rank += 1) tiles.push(0xa0 | rank)
  return tiles
}
