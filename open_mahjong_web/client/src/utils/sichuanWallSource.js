import { isSichuanDuplicate } from './sichuanDuplicate.js'
import { DUPLICATE_ROUND_COUNTS } from './duplicateWalls.js'

export const SICHUAN_WALL_SOURCES = Object.freeze([
  { value: 'training', label: '训练牌山', description: '训练牌山：直接输入随机种子或随机生成随机种子，允许观战，不使用复式密钥' },
  { value: 'key', label: '密钥牌山', description: '密钥牌山：自动生成随机种子生成牌墙，构建后的手牌在首次使用时，玩家和密钥创建者均不可知' },
  { value: 'seed', label: '复现牌山', description: '复现牌山：使用已经确定的随机种子构建手牌和牌山，密钥创建者了解牌山的具体内容' },
  { value: 'manual', label: '手动牌山', description: '手动牌山：使用手动设牌构建的牌山，密钥创建者了解牌山的具体内容，一般复原线下对局时使用' },
])
export const isPairedRoom = form => form.room_rule === 'sichuan' && isSichuanDuplicate(form.sub_rule)
export const isTrainingWall = form => isPairedRoom(form) && form.wall_source === 'training'

export function loadSichuanWallSource(form, config = {}) {
  form.wall_source = ''
  if (!isPairedRoom(form)) return
  // Older paired rooms did not retain their wall type in their creation settings.
  // Keep their existing-key path until the user explicitly selects a source.
  form.wall_source = config.wall_source || (['key', 'seed', 'manual'].includes(config.duplicate_wall_type) ? config.duplicate_wall_type : '')
  if (form.wall_source === 'training') form.allow_spectator = config.allow_spectator !== false
  else form.allow_spectator = false
}

export function selectSichuanWallSource(form, source) {
  if (!SICHUAN_WALL_SOURCES.some(item => item.value === source)) throw new Error('请选择有效的牌山来源')
  form.wall_source = source
  form.allow_spectator = source === 'training'
  form.scene_reproduction = false
  form.duplicate_enabled = false
  form.auto_duplicate = false
  form.duplicate_key = ''
  form.duplicate_key_source = source === 'key' ? 'auto' : 'existing'
}

export function generateTrainingSeed(cryptoProvider = globalThis.crypto) {
  const bytes = new Uint8Array(32)
  cryptoProvider.getRandomValues(bytes)
  // Keep the existing non-zero scene-seed contract even with deterministic test RNGs.
  if (bytes.every(value => value === 0)) bytes[31] = 1
  return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('')
}

export function selectSichuanKeySource(form, source) {
  form.duplicate_key_source = source
  if (source === 'auto') form.duplicate_key = ''
}

export function buildSichuanKeyRequest(form) {
  if (!isPairedRoom(form) || (form.wall_source && form.wall_source !== 'key') || form.duplicate_key_source !== 'existing') throw new Error('请先选择密钥牌山并切换到输入已有密钥')
  if (!DUPLICATE_ROUND_COUNTS.includes(form.duplicate_round_count)) throw new Error('请选择比赛副数')
  return {
    rule: form.sub_rule === 'sichuan/doubles_duplicate' ? 'sichuan/sbr' : 'sichuan/xueliu',
    wall_type: 'key', round_count: form.duplicate_round_count, use_flowers: false,
  }
}
