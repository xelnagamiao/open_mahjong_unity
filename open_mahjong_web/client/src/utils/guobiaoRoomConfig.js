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
  const password = String(form.password || '').trim()
  const room_config = {
    room_name: String(form.room_name || '').trim() || undefined,
    sub_rule: form.sub_rule || 'guobiao/standard',
    game_round: Number(form.game_round) || 4,
    round_timer: Number(form.round_timer) || 0,
    step_timer: Number(form.step_timer) || 0,
    hepai_limit: Math.max(1, Math.min(64, Number(form.hepai_limit) || 8)),
    tips: !!form.tips,
    open_cuohe: !!form.open_cuohe,
    cuohe_type: form.open_cuohe ? Number(form.cuohe_type) || 0 : 0,
    tourist_limit: !!form.tourist_limit,
    allow_spectator: form.allow_spectator !== false,
    tactical_call: !!form.tactical_call,
    claim_protection: form.claim_protection !== false,
  }
  if (!room_config.room_name) delete room_config.room_name
  return { room_config, password }
}
