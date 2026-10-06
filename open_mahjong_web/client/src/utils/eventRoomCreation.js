import { buildEventRoomSettings, createEventRoomForm } from './eventRoomSettings.js'
import { DUPLICATE_ROUND_COUNTS } from './duplicateWalls.js'

export const EVENT_ROOM_COUNTS = Object.freeze([1, 3, 5, 8, 10, 20])

export function createEventRoomCreationForm(settings = {}) {
  const form = createEventRoomForm(settings)
  return { ...form, room_count: 1, duplicate_enabled: Boolean(form.duplicate_key?.trim()), auto_duplicate: false, duplicate_round_count: 16 }
}

export function buildEventRoomCreation(form) {
  if (!EVENT_ROOM_COUNTS.includes(form.room_count)) throw new Error('房间数量请选择 1、3、5、8、10 或 20 个')
  const settings = buildEventRoomSettings(form)
  if (!form.duplicate_enabled && (form.auto_duplicate || settings.room_config.duplicate_key)) throw new Error('请先开启复式')
  if (form.duplicate_enabled) {
    if (form.room_rule !== 'guobiao') throw new Error('复式房间仅支持国标麻将')
    if (!form.auto_duplicate && !settings.room_config.duplicate_key) throw new Error('请输入复式密钥或开启自动生成复式密钥')
  }
  if (form.auto_duplicate) {
    if (form.room_rule !== 'guobiao') throw new Error('自动生成复式密钥仅支持国标麻将')
    if (settings.room_config.duplicate_key) throw new Error('自动生成密钥不能同时使用已有密钥')
    if (!DUPLICATE_ROUND_COUNTS.includes(form.duplicate_round_count)) throw new Error('请选择复式局数')
  }
  return {
    ...settings, room_count: form.room_count,
    ...(form.room_rule === 'guobiao' ? { duplicate_enabled: Boolean(form.duplicate_enabled) } : {}),
    ...(form.auto_duplicate ? { auto_duplicate: true, duplicate_round_count: form.duplicate_round_count } : {}),
  }
}

export function eventRoomCreationError(error) {
  const message = error.response?.data?.message || error.message || '创建失败'
  const data = error.response?.data?.data
  if (!data?.created_count && !data?.generated_key_count) return message
  const keys = data.generated_key_count ? `已生成 ${data.generated_key_count} 个密钥，可在赛事复式密钥中查看并复用。` : ''
  return `已创建 ${data.created_count} / ${data.requested_count} 个房间，后续创建已停止。${keys}${message}。请核对房间列表后再创建。`
}
