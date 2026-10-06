import { buildGuobiaoRoomPayload, createDefaultGuobiaoRoomConfig } from './guobiaoRoomConfig.js'
import { shanxiRoomPayload, SHANXI_SUB_RULE } from './shanxiRoomConfig.js'
import { duplicateWallLabel } from './duplicateWalls.js'
import { buildGuizhouRoomPayload, loadGuizhouForm } from './guizhouRoomConfig.js'
import { buildYixingRoomPayload, loadYixingForm } from './yixingRoomConfig.js'
import { buildWenzhouRoomPayload, loadWenzhouForm } from './wenzhouRoomConfig.js'
import { buildHangzhouRoomPayload, loadHangzhouForm } from './hangzhouRoomConfig.js'
import { buildHongzhongRoomPayload, loadHongzhongForm } from './hongzhongRoomConfig.js'
import { buildChangchunRoomPayload, loadChangchunForm } from './changchunRoomConfig.js'
import { buildGuangdongRoomPayload, loadGuangdongForm, guangdongRows, guangdongProfiles } from './guangdongRoomConfig.js'
import { loadHongKongForm, hongKongDetail, hongKongRows, hkProfiles, hkProfileLabel } from './hongKongRoomConfig.js'

export function clearUnsupportedDuplicateRoom(form) {
  if (form.room_rule !== 'guobiao') form.duplicate_key = ''
}

/** Each editor owns a fresh form, so cancelling never changes another setting or preset. */
export function createEventRoomForm(settings = {}) {
  const defaults = { ...createDefaultGuobiaoRoomConfig(), starting_score: 25000 }
  const config = settings.room_config && typeof settings.room_config === 'object'
    ? settings.room_config
    : {}
  const form = { room_rule: settings.room_rule || 'guobiao', ...defaults }
  for (const key of Object.keys(defaults)) {
    if (key !== 'password' && config[key] !== undefined) form[key] = config[key]
  }
  if (form.room_rule === 'hongkong') form.sub_rule = config.sub_rule || 'hongkong/qingzhang'
  if (form.room_rule === 'guangdong') loadGuangdongForm(form, { ...config, sub_rule: config.sub_rule || 'guangdong/tuidao_mil2024' })
  if (form.room_rule === 'shanxi') form.sub_rule = SHANXI_SUB_RULE
  loadHongKongForm(form, config.detailed_config)
  if (form.room_rule === 'guizhou') loadGuizhouForm(form, config)
  if (form.room_rule === 'yixing') loadYixingForm(form, config)
  if (form.room_rule === 'wenzhou') loadWenzhouForm(form, config)
  if (form.room_rule === 'hangzhou') loadHangzhouForm(form, config)
  if (form.room_rule === 'hongzhong') loadHongzhongForm(form, config)
  if (form.room_rule === 'changchun') loadChangchunForm(form, config)
  form.password = String(settings.password || '')
  clearUnsupportedDuplicateRoom(form)
  return form
}

export function buildEventRoomSettings(form) {
  if (form.room_rule === 'guizhou') return buildGuizhouRoomPayload(form)
  if (form.room_rule === 'yixing') return buildYixingRoomPayload(form)
  if (form.room_rule === 'wenzhou') return buildWenzhouRoomPayload(form)
  if (form.room_rule === 'hangzhou') return buildHangzhouRoomPayload(form)
  if (form.room_rule === 'hongzhong') return buildHongzhongRoomPayload(form)
  if (form.room_rule === 'changchun') return buildChangchunRoomPayload(form)
  if (form.room_rule === 'guobiao') {
    return { room_rule: form.room_rule, ...buildGuobiaoRoomPayload(form) }
  }
  if (form.room_rule === 'guangdong') return buildGuangdongRoomPayload(form)
  const roomName = String(form.room_name || '').trim()
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将')
  if (form.room_rule === 'shanxi') return shanxiRoomPayload(form)
  if (form.room_rule === 'hongkong') {
    if (form.sub_rule !== 'hongkong/new13' && !hkProfiles.some(p => p.value === form.sub_rule)) throw new Error('请选择香港麻将子规则')
    return {
      room_rule: 'hongkong', password: String(form.password || '').trim(),
      room_config: {
        room_name: roomName, sub_rule: form.sub_rule, game_round: form.game_round,
        round_timer: form.round_timer, step_timer: form.step_timer, tips: form.tips,
        tourist_limit: form.tourist_limit, allow_spectator: form.allow_spectator,
        detailed_config: hongKongDetail(form),
      },
    }
  }
  if (form.room_rule === 'riichi') {
    const score = form.starting_score
    if (!Number.isInteger(score) || score < 1000 || score > 1000000 || score % 100 !== 0) {
      throw new Error('起始点数须为 1000–1000000 之间的整数，且为 100 的倍数')
    }
    return {
      room_rule: 'riichi',
      room_config: { ...(roomName ? { room_name: roomName } : {}), starting_score: score },
      password: String(form.password || '').trim(),
    }
  }
  return {
    room_rule: form.room_rule,
    room_config: { ...(roomName ? { room_name: roomName } : {}) },
    password: String(form.password || '').trim(),
  }
}

export function eventRoomSettingsSummary(settings, ruleLabels = {}) {
  const form = createEventRoomForm(settings)
  const rule = ruleLabels[form.room_rule] || form.room_rule
  const config = settings.room_config || {}
  const isDuplicate = Boolean(form.duplicate_key || config.is_duplicate || config.duplicate_wall_type)
  const duplicate = isDuplicate ? ` · ${duplicateWallLabel(config.duplicate_wall_type) === '—' ? '复式' : duplicateWallLabel(config.duplicate_wall_type)}` : ''
  if (form.room_rule === 'riichi') return `${rule} · 起始 ${form.starting_score} 点${duplicate}`
  if (form.room_rule === 'hongkong') return `${rule} · ${hkProfileLabel(form)}`
  if (form.room_rule === 'guangdong') return `${guangdongProfiles.find(p => p.value === form.sub_rule)?.label || form.sub_rule} · ${form.game_round * 4}局`
  if (form.room_rule === 'guizhou') return `${rule} · MIL 2023 · ${form.game_round * 4}局`
  if (form.room_rule === 'hangzhou') return `${rule} · MIL 2025 · ${form.game_round * 4}局`
  if (form.room_rule === 'hongzhong') return `${rule} · MIL 2024 · ${form.game_round * 4}局`
  if (form.room_rule === 'changchun') return `${rule} · MIL 2024 · ${form.game_round * 4}局`
  if (form.room_rule === 'yixing') return `${rule} · ${form.game_round}圈 · 七小对${form.seven_pairs ? '开启' : '关闭'}`
  if (form.room_rule === 'wenzhou') return `${rule} · MIL 2024 · ${form.game_round}圈`
  if (form.room_rule !== 'guobiao') return `${rule}${duplicate}`
  const round = ({ 1: '东风战', 2: '东南战', 3: '东西战', 4: '全庄战' })[form.game_round] || `${form.game_round} 圈`
  return `${rule} · ${isDuplicate ? (config.duplicate_round_count ? `${config.duplicate_round_count} 局` : '局数跟随密钥') : round} · 局时 ${form.round_timer}s · 步时 ${form.step_timer}s${duplicate}`
}

/** Full, read-only review of the canonical settings used for this one table. */
export function eventRoomSettingsRows(settings, ruleLabels = {}) {
  const form = createEventRoomForm(settings)
  const config = settings.room_config || {}
  const isDuplicate = Boolean(form.duplicate_key || config.is_duplicate || config.duplicate_wall_type)
  const enabled = value => value ? '开启' : '关闭'
  const subRules = {
    'guangdong/tuidao_mil2024': 'MIL 2024（无癞子）',
    'guangdong/mil2023': 'MIL 2023（花鬼）',
    'guizhou/standard': '贵州 MIL 2023',
    'yixing/standard': '宜兴标准规则',
    'wenzhou/mil2024': '温州 MIL 2024',
    'hangzhou/mil2025': 'MIL 杭州（2025）',
    'hongzhong/mil2024': '红中 MIL 2024',
    'changchun/mil2024': '长春 MIL 2024',
    'guobiao/standard': '国标标准', 'guobiao/xiaolin': '小林', 'guobiao/kshen': 'K神', 'guobiao/lanshi': '蓝十',
    'shanxi/mil2023': 'MIL山西（2023）',
    'riichi/standard': '立直标准', 'qingque/standard': '青雀标准', 'classical/standard': '古典标准',
    'hongkong/qingzhang': '清章十三张', 'hongkong/new13': '新章十三张', 'hongkong/new16': '新章十六张',
    'sichuan/standard': '四川标准', 'changsha/classic_double_bird': '长沙经典双鸟', 'taiwan/standard': '台湾标准',
  }
  const rows = [
    { label: '规则', value: ruleLabels[form.room_rule] || form.room_rule },
    { label: '房间名', value: form.room_name || '自动命名' },
    { label: '子规则', value: form.room_rule === 'hongkong' ? hkProfileLabel(form) : subRules[form.sub_rule] || form.sub_rule },
    { label: form.room_rule === 'guizhou' ? '局数' : '圈数', value: form.room_rule === 'guizhou' ? `${form.game_round * 4}局` : ({ 1: '东风战', 2: '东南战', 3: '东西战', 4: '全庄战' })[form.game_round] || `${form.game_round} 圈` },
    { label: form.room_rule === 'guizhou' ? '局时储备' : '局时', value: Number(form.round_timer) === 0 && form.room_rule !== 'guizhou' ? '不限时' : `${form.round_timer} 秒` },
    { label: '步时', value: `${form.step_timer} 秒` },
  ]
  if (form.room_rule === 'shanxi') {
    const timerRow = rows.find(row => row.label === '局时')
    if (timerRow) { timerRow.label = '局时储备'; timerRow.value = `${form.round_timer} 秒` }
  }
  if (isDuplicate) {
    rows.push({ label: '复式', value: settings.room_config?.duplicate_wall_type ? duplicateWallLabel(settings.room_config.duplicate_wall_type) : '开启' })
    const roundRow = rows.find(row => row.label === '圈数')
    if (roundRow) roundRow.value = config.duplicate_round_count ? `${config.duplicate_round_count} 局` : '跟随密钥设置'
  }
  if (form.room_rule === 'guangdong') {
    const row = rows.find(item => item.label === '圈数')
    if (row) { row.label = '局数'; row.value = `${form.game_round * 4}局` }
    rows.push(...guangdongRows(form))
  }
  if (form.room_rule === 'hongkong') {
    rows.push(...hongKongRows(form))
  }
  if (form.room_rule === 'hangzhou') {
    const row = rows.find(item => item.label === '圈数')
    if (row) { row.label = '局数'; row.value = `${form.game_round * 4}局` }
    const timerRow = rows.find(item => item.label === '局时')
    if (timerRow) { timerRow.label = '局时储备'; timerRow.value = `${form.round_timer} 秒` }
    rows.push({ label: '财神', value: '白板' }, { label: '封顶', value: '4番' }, { label: '战术鸣牌', value: enabled(form.tactical_call) }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'changchun') {
    const roundRow = rows.find(row => row.label === '圈数')
    if (roundRow) { roundRow.label = '局数'; roundRow.value = `${form.game_round * 4}局` }
    const timerRow = rows.find(row => row.label === '局时')
    if (timerRow) { timerRow.label = '局时储备'; timerRow.value = `${form.round_timer} 秒` }
    rows.push({ label: '规则版本', value: 'MIL 2024' }, { label: '封顶', value: '六番（含庄家、点和、自摸）' }, { label: '战术鸣牌', value: enabled(form.tactical_call) }, { label: '特殊杠', value: '首次三张不补牌，以后每加一张补一张' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'hongzhong') {
    const roundRow = rows.find(row => row.label === '圈数')
    if (roundRow) { roundRow.label = '局数'; roundRow.value = `${form.game_round * 4}局` }
    const timerRow = rows.find(row => row.label === '局时')
    if (timerRow) { timerRow.label = '局时储备'; timerRow.value = `${form.round_timer} 秒` }
    rows.push({ label: '规则版本', value: 'MIL 2024' }, { label: '和牌', value: '仅自摸，四番封顶' }, { label: '扎鸟', value: '至多两张，159和红中中鸟' }, { label: '流局', value: '退还杠分' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'guobiao') rows.push({ label: '起和番', value: `${form.hepai_limit} 番` })
  if (form.room_rule === 'riichi') rows.push({ label: '起始点数', value: `${form.starting_score} 点` })
  if (form.room_rule === 'guizhou') rows.push({ label: '规则版本', value: 'MIL 2023' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  if (form.room_rule === 'yixing') rows.push({ label: '七小对', value: enabled(form.seven_pairs) }, { label: '花牌', value: '固定八花' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  if (form.room_rule === 'wenzhou') rows.push({ label: '规则版本', value: 'MIL 2024' }, { label: '财神', value: '每局翻财，非财神白板固定代本牌' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  rows.push({ label: '提示', value: enabled(form.tips) })
  if (form.room_rule === 'guobiao') {
    rows.push({ label: '花牌', value: isDuplicate ? '跟随复式设置' : enabled(form.sub_rule !== 'guobiao/lanshi' && form.use_flowers !== false) })
    if (['guobiao/standard', 'guobiao/blood_battle'].includes(form.sub_rule)) {
      rows.push({ label: '天地人和', value: form.tian_di_ren_he ? '开启（各8番）' : '关闭' })
    }
    rows.push({ label: '错和', value: enabled(form.open_cuohe) })
    if (form.open_cuohe) rows.push({ label: '错和形式', value: Number(form.cuohe_type) === 1 ? '错和 -40，其余不加分' : '错和 -30，其余各 +10' })
  }
  rows.push({ label: '限制游客', value: enabled(form.tourist_limit) }, { label: '允许观战', value: enabled(form.allow_spectator) })
  if (form.room_rule === 'guobiao') rows.push({ label: '战术鸣牌', value: enabled(form.tactical_call) }, { label: '鸣牌保护', value: enabled(form.claim_protection) })
  return rows
}
