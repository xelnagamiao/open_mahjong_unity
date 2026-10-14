import { buildEventRoomSettings, createEventRoomForm } from './eventRoomSettings.js'
import { DUPLICATE_ROUND_COUNTS } from './duplicateWalls.js'
import { isSichuanDuplicate, supportsDuplicateKey } from './sichuanDuplicate.js'

export const EVENT_ROOM_COUNTS = Object.freeze([1, 3, 5, 8, 10, 20])

export function createEventRoomCreationForm(settings = {}) {
  const form = createEventRoomForm(settings)
  return { ...form, room_count: 1, duplicate_enabled: Boolean(form.duplicate_key?.trim()), auto_duplicate: false, duplicate_round_count: 16 }
}

export function buildEventRoomCreation(form) {
  if (!EVENT_ROOM_COUNTS.includes(form.room_count)) throw new Error('房间数量请选择 1、3、5、8、10 或 20 个')
  const settings = buildEventRoomSettings(form)
  if (form.room_rule === 'sichuan' && isSichuanDuplicate(form.sub_rule)) {
    if (settings.room_config.duplicate_key && form.room_count !== 1) throw new Error('一个已有牌山只能绑定一组开闭室')
    if (!DUPLICATE_ROUND_COUNTS.includes(form.duplicate_round_count)) throw new Error('请选择比赛副数')
    settings.room_config.duplicate_round_count = form.duplicate_round_count
    settings.room_config.allow_spectator = false
    return { ...settings, room_count: form.room_count }
  }
  if (!form.duplicate_enabled && (form.auto_duplicate || settings.room_config.duplicate_key)) throw new Error('请先开启复式')
  if (form.duplicate_enabled) {
    if (['guobiao/transparent', 'guobiao/transparent_realtime', 'guobiao/huahun', 'guobiao/four_jokers', 'guobiao/sixteen'].includes(form.sub_rule)) throw new Error(`${['guobiao/transparent', 'guobiao/transparent_realtime'].includes(form.sub_rule) ? '透明国标' : form.sub_rule === 'guobiao/sixteen' ? '国标十六张' : '四癞子'}不能使用普通复式牌墙`)
    if (!supportsDuplicateKey(form)) throw new Error('复式房间仅支持国标麻将和川麻血流')
    if (!form.auto_duplicate && !settings.room_config.duplicate_key) throw new Error('请输入复式密钥或开启自动生成复式密钥')
  }
  if (form.auto_duplicate) {
    if (!supportsDuplicateKey(form)) throw new Error('自动生成复式密钥仅支持国标麻将和川麻血流')
    if (settings.room_config.duplicate_key) throw new Error('自动生成密钥不能同时使用已有密钥')
    if (!DUPLICATE_ROUND_COUNTS.includes(form.duplicate_round_count)) throw new Error('请选择复式局数')
  }
  return {
    ...settings, room_count: form.room_count,
    ...(supportsDuplicateKey(form) ? { duplicate_enabled: Boolean(form.duplicate_enabled) } : {}),
    ...(form.auto_duplicate ? { auto_duplicate: true, duplicate_round_count: form.duplicate_round_count } : {}),
  }
}

export function eventRoomCreationError(error) {
  const message = error.response?.data?.message || error.message || '创建失败'
  const data = error.response?.data?.data
  if (!data?.created_count && !data?.generated_key_count) return message
  const keys = data.generated_key_count ? `已生成 ${data.generated_key_count} 个密钥，可在赛事复式密钥中查看并复用。` : ''
  if (data.paired) return `已创建 ${data.created_count} / ${data.requested_count} 组开闭室，后续创建已停止。${message}。请核对房间列表后再创建。`
  return `已创建 ${data.created_count} / ${data.requested_count} 个房间，后续创建已停止。${keys}${message}。请核对房间列表后再创建。`
}
