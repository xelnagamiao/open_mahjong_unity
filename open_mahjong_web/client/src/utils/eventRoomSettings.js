import { buildGuobiaoRoomPayload, createDefaultGuobiaoRoomConfig } from './guobiaoRoomConfig.js'
import { shanxiRoomPayload, SHANXI_SUB_RULE } from './shanxiRoomConfig.js'
import { duplicateWallLabel } from './duplicateWalls.js'
import { supportsDuplicateKey } from './sichuanDuplicate.js'
import { buildGuizhouRoomPayload, loadGuizhouForm } from './guizhouRoomConfig.js'
import { buildYixingRoomPayload, loadYixingForm } from './yixingRoomConfig.js'
import { buildMalaysiaRoomPayload, loadMalaysiaForm } from './malaysiaRoomConfig.js'
import { buildShenyangRoomPayload, loadShenyangForm } from './shenyangRoomConfig.js'
import { buildNanjingRoomPayload, loadNanjingForm } from './nanjingRoomConfig.js'
import { buildFuzhouRoomPayload, loadFuzhouForm } from './fuzhouRoomConfig.js'
import { buildWenzhouRoomPayload, loadWenzhouForm } from './wenzhouRoomConfig.js'
import { buildNanchangRoomPayload, loadNanchangForm } from './nanchangRoomConfig.js'
import { buildShangraoRoomPayload, loadShangraoForm, SHANGRAO_OPTIONS } from './shangraoRoomConfig.js'
import { buildHangzhouRoomPayload, loadHangzhouForm } from './hangzhouRoomConfig.js'
import { buildSingaporeRoomPayload, loadSingaporeForm } from './singaporeRoomConfig.js'
import { buildJiujiangRoomPayload, loadJiujiangForm } from './jiujiangRoomConfig.js'
import { buildWuhanRoomPayload, loadWuhanForm } from './wuhanRoomConfig.js'
import { buildHongzhongRoomPayload, loadHongzhongForm } from './hongzhongRoomConfig.js'
import { buildChangchunRoomPayload, loadChangchunForm } from './changchunRoomConfig.js'
import { buildGuangdongRoomPayload, loadGuangdongForm, guangdongRows, guangdongProfiles } from './guangdongRoomConfig.js'
import { loadHongKongForm, hongKongDetail, hongKongRows, hkProfiles, hkProfileLabel } from './hongKongRoomConfig.js'
import { riichiWinLineLabel } from './riichiWinRequirements.js'
import { loadRiichiForm, buildRiichiRoomPayload, selectedRiichiPreset } from './riichiRoomConfig.js'
import { loadChangshaForm, buildChangshaRoomPayload, changshaRows } from './changshaRoomConfig.js'

export function clearUnsupportedDuplicateRoom(form) {
  if (!supportsDuplicateKey(form)) form.duplicate_key = ''
}

/** Each editor owns a fresh form, so cancelling never changes another setting or preset. */
export function createEventRoomForm(settings = {}) {
  const rule = settings.room_rule || 'guobiao'
  const defaults = { ...createDefaultGuobiaoRoomConfig(), sub_rule: rule === 'changsha' ? 'changsha/classic_double_bird' : `${rule}/standard`, starting_score: 25000, riichi_hepai_limit: 1, minimum_win_score: 0 }
  const config = settings.room_config && typeof settings.room_config === 'object'
    ? settings.room_config
    : {}
  const form = { room_rule: rule, ...defaults }
  for (const key of Object.keys(defaults)) {
    if (key !== 'password' && config[key] !== undefined) form[key] = config[key]
  }
  if (form.room_rule === 'hongkong') form.sub_rule = config.sub_rule || 'hongkong/qingzhang'
  if (form.room_rule === 'guangdong') loadGuangdongForm(form, { ...config, sub_rule: config.sub_rule || 'guangdong/tuidao_mil2024' })
  if (form.room_rule === 'shanxi') form.sub_rule = SHANXI_SUB_RULE
  if (form.room_rule === 'sichuan') form.sub_rule = config.sub_rule || 'sichuan/standard'
  loadHongKongForm(form, config.detailed_config)
  if (form.room_rule === 'guizhou') loadGuizhouForm(form, config)
  if (form.room_rule === 'yixing') loadYixingForm(form, config)
  if (form.room_rule === 'malaysia') loadMalaysiaForm(form, config)
  if (form.room_rule === 'shenyang') loadShenyangForm(form, config)
  if (form.room_rule === 'nanjing') loadNanjingForm(form, config)
  if (form.room_rule === 'fuzhou') loadFuzhouForm(form, config)
  if (form.room_rule === 'wenzhou') loadWenzhouForm(form, config)
  if (form.room_rule === 'nanchang') loadNanchangForm(form, config)
  if (form.room_rule === 'shangrao') loadShangraoForm(form, config)
  if (form.room_rule === 'hangzhou') loadHangzhouForm(form, config)
  if (form.room_rule === 'wuhan') loadWuhanForm(form, config)
  if (form.room_rule === 'hongzhong') loadHongzhongForm(form, config)
  if (form.room_rule === 'singapore') loadSingaporeForm(form, config)
  if (form.room_rule === 'jiujiang') loadJiujiangForm(form, config)
  if (form.room_rule === 'changchun') loadChangchunForm(form, config)
  if (form.room_rule === 'changsha') loadChangshaForm(form, config)
  if (form.room_rule === 'riichi') {
    loadRiichiForm(form, config)
  }
  form.password = String(settings.password || '')
  clearUnsupportedDuplicateRoom(form)
  return form
}

/** Run only for a user's rule selection; loading a saved preset keeps its exact settings. */
export function changeEventRoomRule(form, rule) {
  Object.assign(form, createEventRoomForm({
    room_rule: rule,
    room_config: { room_name: form.room_name },
    password: form.password,
  }))
  form.duplicate_enabled = false
  form.auto_duplicate = false
}

export function buildEventRoomSettings(form) {
  if (form.room_rule === 'changsha') return buildChangshaRoomPayload(form)
  if (form.room_rule === 'guizhou') return buildGuizhouRoomPayload(form)
  if (form.room_rule === 'yixing') return buildYixingRoomPayload(form)
  if (form.room_rule === 'malaysia') return buildMalaysiaRoomPayload(form)
  if (form.room_rule === 'shenyang') return buildShenyangRoomPayload(form)
  if (form.room_rule === 'nanjing') return buildNanjingRoomPayload(form)
  if (form.room_rule === 'fuzhou') return buildFuzhouRoomPayload(form)
  if (form.room_rule === 'wenzhou') return buildWenzhouRoomPayload(form)
  if (form.room_rule === 'nanchang') return buildNanchangRoomPayload(form)
  if (form.room_rule === 'shangrao') return buildShangraoRoomPayload(form)
  if (form.room_rule === 'hangzhou') return buildHangzhouRoomPayload(form)
  if (form.room_rule === 'singapore') return buildSingaporeRoomPayload(form)
  if (form.room_rule === 'jiujiang') return buildJiujiangRoomPayload(form)
  if (form.room_rule === 'wuhan') return buildWuhanRoomPayload(form)
  if (form.room_rule === 'hongzhong') return buildHongzhongRoomPayload(form)
  if (form.room_rule === 'changchun') return buildChangchunRoomPayload(form)
  if (form.room_rule === 'guobiao') {
    return { room_rule: form.room_rule, ...buildGuobiaoRoomPayload(form) }
  }
  if (form.room_rule === 'guangdong') return buildGuangdongRoomPayload(form)
  const roomName = String(form.room_name || '').trim()
  if (String(form.duplicate_key || '').trim() && !supportsDuplicateKey(form)) throw new Error('复式房间仅支持国标麻将和川麻血流')
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
    return buildRiichiRoomPayload(form)
  }
  if (form.room_rule === 'sichuan') {
    return {
      room_rule: 'sichuan', password: String(form.password || '').trim(),
      room_config: {
        room_name: roomName, sub_rule: ['sichuan/xueliu', 'sichuan/xueliu_exchange', 'sichuan/doubles', 'sichuan/xueliu_doubles', 'sichuan/doubles_duplicate', 'sichuan/xueliu_doubles_duplicate'].includes(form.sub_rule) ? form.sub_rule : 'sichuan/standard',
        ...(String(form.duplicate_key || '').trim() ? { duplicate_key: form.duplicate_key.trim() } : {}),
        game_round: form.game_round, round_timer: form.round_timer, step_timer: form.step_timer,
        tips: form.tips, tourist_limit: form.tourist_limit, allow_spectator: form.allow_spectator,
        tactical_call: form.tactical_call !== false,
      },
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
  if (form.room_rule === 'riichi') return `${selectedRiichiPreset(form)?.label || rule + ' · 自定义'} · 起始 ${form.starting_score} 点 · ${form.riichi_hepai_limit} 番起和（不含宝牌） · 起和线：${riichiWinLineLabel(form.minimum_win_score)}${duplicate}`
  if (form.room_rule === 'hongkong') return `${rule} · ${hkProfileLabel(form)}`
  if (form.room_rule === 'changsha') return `${rule} · ${form.game_round * 4}局 · 开杠${form.open_kong_replacement_count}张 · ${form.small_hu_self_draw_only ? '卡摸' : '小胡可点和'}`
  if (form.room_rule === 'guangdong') return `${guangdongProfiles.find(p => p.value === form.sub_rule)?.label || form.sub_rule} · ${form.game_round * 4}局`
  if (form.room_rule === 'guizhou') return `${rule} · MIL 2023 · ${form.game_round * 4}局`
  if (form.room_rule === 'hangzhou') return `${rule} · MIL 2025 · ${form.game_round * 4}局`
  if (form.room_rule === 'shenyang') return `${rule} · MIL 2025 · ${form.game_round * 4}局`
  if (form.room_rule === 'nanjing') return `${rule} · MIL 2024 · ${form.game_round * 4}局`
  if (form.room_rule === 'fuzhou') return `${rule} · 十六张 · ${form.game_round * 4}局 · 底分${form.fuzhou_base_score ?? 15}`
  if (form.room_rule === 'singapore') return `${rule} · ${form.game_round}圈 · SPGG 2024`
  if (form.room_rule === 'jiujiang') return `${rule} · ${form.game_round * 4}副`
  if (form.room_rule === 'wuhan') return `${rule} · MIL 2025 · ${form.game_round * 4}局`
  if (form.room_rule === 'hongzhong') return `${rule} · MIL 2024 · ${form.game_round * 4}局`
  if (form.room_rule === 'changchun') return `${rule} · MIL 2024 · ${form.game_round * 4}局`
  if (form.room_rule === 'yixing') return `${rule} · ${form.game_round}圈 · 七小对${form.seven_pairs ? '开启' : '关闭'}`
  if (form.room_rule === 'malaysia') return `${rule} · ${form.game_round * 3}局 · 满番叠加${form.multiple_max ? '开启' : '关闭'}`
  if (form.room_rule === 'wenzhou') return `${rule} · MIL 2024 · ${form.game_round}圈`
  if (form.room_rule === 'nanchang') return `${rule} · MIL 2026 · ${form.game_round * 4}副`
  if (form.room_rule === 'shangrao') return `${rule} · ${form.game_round * 4}副`
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
    'malaysia/traditional84': '马来西亚三人传统84张规则',
    'shenyang/mil2025': '沈阳 MIL 2025',
    'nanjing/mil2024': '南京 MIL 2024',
    'fuzhou/standard': '福州十六张',
    'wenzhou/mil2024': '温州 MIL 2024',
    'nanchang/mil2026': '南昌 MIL 2026',
    'shangrao/standard': '上饶麻将',
    'hangzhou/mil2025': 'MIL 杭州（2025）',
    'wuhan/mil2025': '武汉 MIL 2025',
    'hongzhong/mil2024': '红中 MIL 2024',
    'singapore/spgg2024': '新加坡麻将',
    'jiujiang/standard': '九江麻将',
    'changchun/mil2024': '长春 MIL 2024',
    'guobiao/standard': '国标标准', 'guobiao/xiaolin': '小林', 'guobiao/kshen': 'K神', 'guobiao/lanshi': '蓝十', 'guobiao/transparent': '透明国标',
  'guobiao/transparent_realtime': '国标透明（实时手牌）',
  'guobiao/huahun': '国标（花混癞子）',
  'guobiao/sixteen': '国标麻将（十六张）',
  'guobiao/four_jokers': '国标麻将(四癞子)',
    'shanxi/mil2023': 'MIL山西（2023）',
    'riichi/standard': '立直标准', 'qingque/standard': '青雀标准', 'classical/standard': '古典标准',
    'hongkong/qingzhang': '清章十三张', 'hongkong/new13': '新章十三张', 'hongkong/new16': '新章十六张',
    'sichuan/standard': '四川标准', 'sichuan/doubles': '血战双打·2V2', 'sichuan/xueliu_doubles': '血流双打·2V2', 'changsha/classic_double_bird': '长沙经典双鸟', 'changsha/exchange_six': '长沙换六张', 'taiwan/standard': '台湾标准',
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
  if (form.room_rule === 'changsha') {
    const row = rows.find(item => item.label === '圈数')
    if (row) { row.label = '局数'; row.value = `${form.game_round * 4}局` }
    rows.push(...changshaRows(form))
  }
  if (form.room_rule === 'shenyang') {
    const round = rows.find(item => item.label === '圈数')
    if (round) { round.label = '局数'; round.value = `${form.game_round * 4}局` }
    const timer = rows.find(item => item.label === '局时')
    if (timer) { timer.label = '局时储备'; timer.value = `${form.round_timer} 秒` }
    rows.push({ label: '规则版本', value: 'MIL 2025' }, { label: '封顶', value: '六番，每家最高64分' }, { label: '特殊杠', value: '喜杠不补牌，旋风杠补牌' }, { label: '末四张', value: '分张，仅自摸' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'fuzhou') {
    const row = rows.find(item => item.label === '圈数')
    if (row) { row.label = '局数'; row.value = `${form.game_round * 4}局` }
    const timer = rows.find(item => item.label === '局时')
    if (timer) { timer.label = '局时储备'; timer.value = `${form.round_timer} 秒` }
    rows.push({ label: '底分', value: `${form.fuzhou_base_score ?? 15}分` }, { label: '花牌', value: '全部字牌及八花' }, { label: '末四张', value: '只摸不打，不补花' })
  }
  if (form.room_rule === 'nanjing') {
    const row = rows.find(item => item.label === '圈数')
    if (row) { row.label = '局数'; row.value = `${form.game_round * 4}局` }
    const timer = rows.find(item => item.label === '局时')
    if (timer) { timer.label = '局时储备'; timer.value = `${form.round_timer} 秒` }
    rows.push({ label: '起和条件', value: '四张花牌或非偶然番种' }, { label: '封顶', value: '120分' }, { label: '花牌', value: '中发白及八花' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'hangzhou') {
    const row = rows.find(item => item.label === '圈数')
    if (row) { row.label = '局数'; row.value = `${form.game_round * 4}局` }
    const timerRow = rows.find(item => item.label === '局时')
    if (timerRow) { timerRow.label = '局时储备'; timerRow.value = `${form.round_timer} 秒` }
    rows.push({ label: '财神', value: '白板' }, { label: '封顶', value: '4番' }, { label: '战术鸣牌', value: enabled(form.tactical_call) }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'wuhan') {
    const row = rows.find(item => item.label === '圈数')
    if (row) { row.label = '局数'; row.value = `${form.game_round * 4}局` }
    rows.push({ label: '起和条件', value: '开口、逐家六番' }, { label: '牌数', value: '136张' },
      { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'changchun') {
    const roundRow = rows.find(row => row.label === '圈数')
    if (roundRow) { roundRow.label = '局数'; roundRow.value = `${form.game_round * 4}局` }
    const timerRow = rows.find(row => row.label === '局时')
    if (timerRow) { timerRow.label = '局时储备'; timerRow.value = `${form.round_timer} 秒` }
    rows.push({ label: '规则版本', value: 'MIL 2024' }, { label: '封顶', value: '六番（含庄家、点和、自摸）' }, { label: '战术鸣牌', value: enabled(form.tactical_call) }, { label: '特殊杠', value: '首次三张不补牌，以后每加一张补一张' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'singapore') {
    rows.push({label:'规则版本',value:'SPGG 2024'},{label:'起和 / 封顶',value:'1台 / 5台'},{label:'起分',value:'400分'},{label:'即时支付',value:'花季与动物婚配、杠'},{label:'剩余张数提示',value:enabled(form.count_tips)},{label:'指针提示',value:enabled(form.pointer_tips)})
  }
  if (form.room_rule === 'jiujiang') {
    const row = rows.find(item => item.label === '圈数')
    if (row) { row.label = '副数'; row.value = `${form.game_round * 4}副` }
    rows.push({label:'起胡分',value:'1分（含花分）'},{label:'补发财',value:'自愿补牌；弃出的发财不计分'},{label:'剩余张数提示',value:enabled(form.count_tips)},{label:'指针提示',value:enabled(form.pointer_tips)})
  }
  if (form.room_rule === 'hongzhong') {
    const roundRow = rows.find(row => row.label === '圈数')
    if (roundRow) { roundRow.label = '局数'; roundRow.value = `${form.game_round * 4}局` }
    const timerRow = rows.find(row => row.label === '局时')
    if (timerRow) { timerRow.label = '局时储备'; timerRow.value = `${form.round_timer} 秒` }
    rows.push({ label: '规则版本', value: 'MIL 2024' }, { label: '和牌', value: '仅自摸，四番封顶' }, { label: '扎鸟', value: '至多两张，159和红中中鸟' }, { label: '流局', value: '退还杠分' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  }
  if (form.room_rule === 'guobiao') rows.push({ label: '起和番', value: `${form.hepai_limit} 番` })
  if (form.room_rule === 'riichi') rows.push(
    { label: '日麻预设', value: selectedRiichiPreset(form)?.label || '自定义规则' },
    { label: '人数', value: form.sub_rule === 'riichi/sanma' ? '3人' : '4人' },
    { label: '起始点数', value: `${form.starting_score} 点` },
    { label: '起和番', value: `${form.riichi_hepai_limit} 番（不含宝牌）` },
    { label: '起和线', value: riichiWinLineLabel(form.minimum_win_score) },
  )
  if (form.room_rule === 'guizhou') rows.push({ label: '规则版本', value: 'MIL 2023' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  if (form.room_rule === 'yixing') rows.push({ label: '七小对', value: enabled(form.seven_pairs) }, { label: '花牌', value: '固定八花' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  if (form.room_rule === 'malaysia') rows.push({ label: '满番叠加', value: enabled(form.multiple_max) }, { label: '花牌', value: '16花与4飞' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  if (form.room_rule === 'wenzhou') rows.push({ label: '规则版本', value: 'MIL 2024' }, { label: '财神', value: '每局翻财，非财神白板固定代本牌' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  if (form.room_rule === 'nanchang') rows.push({ label: '规则版本', value: 'MIL 2026 四人单打' }, { label: '精牌', value: '正副精，翻精牌仍可摸取' }, { label: '剩余张数提示', value: enabled(form.count_tips) }, { label: '指针提示', value: enabled(form.pointer_tips) })
  if (form.room_rule === 'shangrao') {
    const row = rows.find(item => item.label === '圈数'); if (row) { row.label='副数'; row.value=`${form.game_round * 4}副` }
    rows.push({label:'和牌',value:'仅自摸'}, ...Object.entries(SHANGRAO_OPTIONS).map(([key,label])=>({label,value:enabled(form['shangrao_'+key])})))
  }
  rows.push({ label: '提示', value: enabled(form.tips) })
  if (form.room_rule === 'guobiao') {
    if (form.sub_rule === 'guobiao/huahun') {
      rows.push({ label: '无癞子奖励', value: form.no_joker_bonus === 'none' ? '不额外加分' : form.no_joker_bonus === 'double' ? '无癞子翻倍' : '无癞子+8分' })
      rows.push({ label: '黑花递增计分', value: enabled(form.black_flower_progressive !== false) })
    }
    if (['guobiao/huahun', 'guobiao/four_jokers'].includes(form.sub_rule)) rows.push({ label: '癞子多面听', value: enabled(!!form.joker_multi_wait) })
    rows.push({ label: '花牌', value: isDuplicate ? '跟随复式设置' : enabled(form.sub_rule !== 'guobiao/lanshi' && form.use_flowers !== false) })
    if (['guobiao/standard', 'guobiao/blood_battle', 'guobiao/transparent', 'guobiao/transparent_realtime', 'guobiao/huahun', 'guobiao/four_jokers', 'guobiao/sixteen'].includes(form.sub_rule)) {
      rows.push({ label: '天地人和', value: form.tian_di_ren_he ? '开启（各8番）' : '关闭' })
    }
    rows.push({ label: '错和', value: enabled(form.open_cuohe) })
    if (form.open_cuohe) rows.push({ label: '错和形式', value: Number(form.cuohe_type) === 1 ? '错和 -40，其余不加分' : '错和 -30，其余各 +10' })
  }
  rows.push({ label: '限制游客', value: enabled(form.tourist_limit) }, { label: '允许观战', value: enabled(form.allow_spectator) })
  if (form.room_rule === 'sichuan') rows.push({ label: '战术鸣牌', value: enabled(form.tactical_call) })
  if (form.room_rule === 'guobiao') rows.push({ label: '战术鸣牌', value: enabled(form.tactical_call) }, { label: '鸣牌保护', value: enabled(form.claim_protection) })
  return rows
}
