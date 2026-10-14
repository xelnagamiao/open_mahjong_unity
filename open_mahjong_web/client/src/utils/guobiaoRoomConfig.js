export const supportsGuobiaoOpeningWins = subRule => ['guobiao/standard', 'guobiao/blood_battle', 'guobiao/transparent', 'guobiao/transparent_realtime', 'guobiao/huahun', 'guobiao/four_jokers', 'guobiao/sixteen'].includes(subRule || 'guobiao/standard')

const defaultHepai = {
  'guobiao/standard': 8, 'guobiao/sixteen': 8, 'guobiao/xiaolin': 1,
  'guobiao/kshen': 8, 'guobiao/lanshi': 5, 'guobiao/transparent': 8,
  'guobiao/transparent_realtime': 8, 'guobiao/huahun': 8, 'guobiao/four_jokers': 8,
}

/** Apply dependent switches as the user edits, before a configuration can be submitted. */
export function updateGuobiaoRoomConfig(form, key, value) {
  form[key] = value
  if (key === 'tips' && value) {
    if (form.sub_rule === 'guobiao/lanshi') form.tips = false
    else { form.open_cuohe = false; form.cuohe_type = 0 }
  }
  if (key === 'open_cuohe') {
    if (value) form.tips = false
    else form.cuohe_type = 0
  }
  if (key === 'sub_rule' && defaultHepai[value] != null) {
    if (['guobiao/transparent', 'guobiao/transparent_realtime', 'guobiao/huahun', 'guobiao/four_jokers', 'guobiao/sixteen'].includes(value)) form.duplicate_key = ''
    if (!['guobiao/huahun', 'guobiao/four_jokers'].includes(value)) form.joker_multi_wait = false
    if (!supportsGuobiaoOpeningWins(value)) form.tian_di_ren_he = false
    form.hepai_limit = defaultHepai[value]
    if (value === 'guobiao/huahun') form.use_flowers = true
    if (value === 'guobiao/lanshi') {
      form.use_flowers = false
      form.open_cuohe = true
      form.cuohe_type = 1
      form.tips = false
    }
  }
}

/** 国标空房间默认配置（与客户端正常建房一致） */
export function createDefaultGuobiaoRoomConfig() {
  return {
    room_name: '',
    sub_rule: 'guobiao/standard',
    game_round: 4,
    round_timer: 20,
    step_timer: 5,
    hepai_limit: 8,
    password: '',
    duplicate_key: '',
    use_flowers: true,
    tian_di_ren_he: false,
    joker_multi_wait: false,
    no_joker_bonus: "add8", black_flower_progressive: true,
    tips: false,
    open_cuohe: false,
    cuohe_type: 0,
    tourist_limit: false,
    allow_spectator: true,
    tactical_call: true,
    claim_protection: true,
  }
}

/** 组装提交给服务端的 room_config + password */
export function buildGuobiaoRoomPayload(form) {
  if (['guobiao/transparent', 'guobiao/transparent_realtime', 'guobiao/huahun', 'guobiao/four_jokers', 'guobiao/sixteen'].includes(form.sub_rule) && String(form.duplicate_key || '').trim()) {
    throw new Error(`${['guobiao/transparent', 'guobiao/transparent_realtime'].includes(form.sub_rule) ? '透明国标' : form.sub_rule === 'guobiao/sixteen' ? '国标十六张' : '四癞子'}不能使用普通复式牌墙，可用随机种子复现牌局`)
  }
  const password = String(form.password || '').trim()
  const room_config = {
    ...(String(form.duplicate_key || '').trim() ? { duplicate_key: String(form.duplicate_key).trim() } : {}),
    room_name: String(form.room_name || '').trim() || undefined,
    sub_rule: form.sub_rule || 'guobiao/standard',
    tian_di_ren_he: supportsGuobiaoOpeningWins(form.sub_rule) && !!form.tian_di_ren_he,
    no_joker_bonus: ["double", "none"].includes(form.no_joker_bonus) ? form.no_joker_bonus : "add8",
    black_flower_progressive: form.black_flower_progressive !== false,
    joker_multi_wait: ['guobiao/huahun', 'guobiao/four_jokers'].includes(form.sub_rule) && !!form.joker_multi_wait,
    // A duplicate wall owns this setting; omit independent room overrides.
    ...(!String(form.duplicate_key || '').trim() ? { use_flowers: form.sub_rule === 'guobiao/lanshi' ? false : form.sub_rule === 'guobiao/huahun' || form.use_flowers !== false } : {}),
    game_round: Number(form.game_round) || 4,
    round_timer: Number(form.round_timer) || 0,
    step_timer: Number(form.step_timer) || 0,
    hepai_limit: form.sub_rule === 'guobiao/huahun' ? 8 : form.sub_rule === 'guobiao/lanshi' ? 5 : Math.max(1, Math.min(64, Number(form.hepai_limit) || 8)),
    tips: form.sub_rule !== 'guobiao/lanshi' && !!form.tips,
    open_cuohe: form.sub_rule === 'guobiao/lanshi' || !!form.open_cuohe,
    cuohe_type: form.sub_rule === 'guobiao/lanshi' ? 1 : form.open_cuohe ? Number(form.cuohe_type) || 0 : 0,
    tourist_limit: !!form.tourist_limit,
    allow_spectator: form.allow_spectator !== false,
    tactical_call: !!form.tactical_call,
    claim_protection: form.claim_protection !== false,
  }
  if (!room_config.room_name) delete room_config.room_name
  return { room_config, password }
}
