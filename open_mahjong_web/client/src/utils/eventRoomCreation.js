import { buildEventRoomSettings, createEventRoomForm } from './eventRoomSettings.js'
import { DUPLICATE_ROUND_COUNTS } from './duplicateWalls.js'
import { isSichuanDuplicate, supportsDuplicateKey } from './sichuanDuplicate.js'
import { isPairedRoom, isTrainingWall } from './sichuanWallSource.js'

export const EVENT_ROOM_COUNTS = Object.freeze([1, 3, 5, 8, 10, 20])

export function createEventRoomCreationForm(settings = {}) {
  const form = createEventRoomForm(settings)
  if (isPairedRoom(form) && !form.wall_source && !form.duplicate_key?.trim() && !form.random_seed) form.wall_source = 'key'
  return { ...form, room_count: 1, duplicate_enabled: Boolean(form.duplicate_key?.trim()), auto_duplicate: false,
    duplicate_key_source: form.duplicate_key?.trim() ? 'existing' : 'auto',
    duplicate_round_count: settings.room_config?.duplicate_round_count ?? (form.sub_rule === 'sichuan/doubles_duplicate' ? 8 : 16),
    duplicate_sync_mode: settings.room_config?.duplicate_sync_mode === true }
}

export function buildEventRoomCreation(form) {
  if (!EVENT_ROOM_COUNTS.includes(form.room_count)) throw new Error('房间数量请选择 1、3、5、8、10 或 20 个')
  const settings = buildEventRoomSettings(form)
  if (form.room_rule === 'sichuan' && isSichuanDuplicate(form.sub_rule)) {
    const training = isTrainingWall(form)
    const existing = !training && (['seed', 'manual'].includes(form.wall_source) || form.duplicate_key_source === 'existing')
    if (!existing) delete settings.room_config.duplicate_key
    if (existing && !settings.room_config.duplicate_key) throw new Error('请输入复式密钥')
    if (!DUPLICATE_ROUND_COUNTS.includes(form.duplicate_round_count)) throw new Error('请选择比赛副数')
    settings.room_config.duplicate_round_count = form.duplicate_round_count
    settings.room_config.duplicate_sync_mode = form.duplicate_sync_mode === true
    settings.room_config.allow_spectator = training ? form.allow_spectator !== false : false
    return { ...settings, room_count: form.room_count }
  }
  if (!form.duplicate_enabled && (form.auto_duplicate || settings.room_config.duplicate_key)) throw new Error('请先开启复式')
  if (form.duplicate_enabled) {
    if (['guobiao/transparent', 'guobiao/transparent_realtime', 'guobiao/huahun', 'guobiao/four_jokers', 'guobiao/sixteen'].includes(form.sub_rule)) throw new Error(`${['guobiao/transparent', 'guobiao/transparent_realtime'].includes(form.sub_rule) ? '透明国标' : form.sub_rule === 'guobiao/sixteen' ? '国标十六张' : '四癞子'}不能使用普通复式牌墙`)
    if (!supportsDuplicateKey(form)) throw new Error('复式房间仅支持国标麻将和川麻双打复式')
    if (!form.auto_duplicate && !settings.room_config.duplicate_key) throw new Error('请输入复式密钥或开启自动生成复式密钥')
  }
  if (form.auto_duplicate) {
    if (!supportsDuplicateKey(form)) throw new Error('自动生成复式密钥仅支持国标麻将和川麻双打复式')
    if (settings.room_config.duplicate_key) throw new Error('自动生成密钥不能同时使用已有密钥')
    if (!DUPLICATE_ROUND_COUNTS.includes(form.duplicate_round_count)) throw new Error('请选择复式局数')
  }
  return {
    ...settings, room_count: form.room_count,
    ...(supportsDuplicateKey(form) ? { duplicate_enabled: Boolean(form.duplicate_enabled) } : {}),
    ...(form.auto_duplicate ? { auto_duplicate: true, duplicate_round_count: form.duplicate_round_count } : {}),
  }
}

export function sharedWallConfirmation(body) {
  if (body.room_count <= 1) return ''
  if (body.room_config?.duplicate_key) return '在输入固定密钥以后创建多个房间，多个房间将会使用相同的密钥，是否确认？'
  if (body.room_config?.random_seed) return '在输入固定随机种子以后创建多个房间，多个房间将会使用相同的随机种子，是否确认？'
  return ''
}

export function eventRoomCreationError(error) {
  const message = error.response?.data?.message || error.message || '创建失败'
  const data = error.response?.data?.data
  if (!data?.created_count && !data?.generated_key_count) return message
  const keys = data.generated_key_count ? `已生成 ${data.generated_key_count} 个密钥，可在赛事与基地复式密钥中查看并复用。` : ''
  if (data.paired) return `已创建 ${data.created_count} / ${data.requested_count} 个八人复式房间，后续创建已停止。${message}。请核对房间列表后再创建。`
  return `已创建 ${data.created_count} / ${data.requested_count} 个房间，后续创建已停止。${keys}${message}。请核对房间列表后再创建。`
}
