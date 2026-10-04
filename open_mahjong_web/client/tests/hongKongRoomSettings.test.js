import test from 'node:test'
import assert from 'node:assert/strict'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'
import { loadHongKongForm, hongKongChoices } from '../src/utils/hongKongRoomConfig.js'

const visibleDefaults = [
  ['hongkong/qingzhang', false, 'head_bump', 'rotate'],
  ['hongkong/new13_gametower', false, 'head_bump', 'rotate'],
  ['hongkong/new13_lianhuise', true, 'head_bump', 'win_or_draw'],
  ['hongkong/qingzhang_lianhuise', true, 'head_bump', 'default'],
  ['hongkong/new16', true, 'multiple', 'win_or_draw'],
]

test('each profile selects its concrete default options, including legacy saved defaults', () => {
  for (const [sub_rule, flowers, win_claim, dealer_mode] of visibleDefaults) {
    for (const detailed_config of [{}, { flowers: 'default', win_claim: 'default', dealer_mode: 'default' }]) {
      const form = createEventRoomForm({ room_rule: 'hongkong', room_config: { sub_rule, detailed_config } })
      assert.equal(form.hk_flowers, flowers)
      assert.equal(form.hk_win_claim, win_claim)
      assert.equal(form.hk_dealer_mode, dealer_mode)
      for (const key of ['win_claim', 'dealer_mode']) {
        const options = hongKongChoices(form, key)
        assert.ok(options.some(option => option.value === form[`hk_${key}`]))
        assert.ok(options.every(option => option.label !== '规则书默认'))
      }
      if (dealer_mode === 'default') assert.equal(hongKongChoices(form, 'dealer_mode').find(o => o.value === dealer_mode).label, '庄和或流局听牌连庄')
    }
  }
})

test('changing profiles resets customized house rules to the selected profile before submitting', () => {
  const form = createEventRoomForm({ room_rule: 'hongkong' })
  for (const [sub_rule, flowers, win_claim, dealer_mode] of visibleDefaults) {
    form.hk_self_draw_only = true; form.hk_liability_twelve = false; form.hk_liability_limit = false
    form.sub_rule = sub_rule
    loadHongKongForm(form)
    const detail = buildEventRoomSettings(form).room_config.detailed_config
    assert.equal(detail.flowers, flowers)
    assert.equal(detail.win_claim, win_claim)
    assert.equal(detail.dealer_mode, dealer_mode)
    assert.equal(detail.self_draw_only, false)
    assert.equal(detail.liability_twelve, true)
    assert.equal(detail.liability_limit, true)
  }
})

test('loading an explicit remix draw policy preserves it instead of restoring tenpai-only repeats', () => {
  for (const dealer_mode of ['rotate', 'win_or_draw', 'default']) {
    const form = createEventRoomForm({ room_rule: 'hongkong', room_config: { sub_rule: 'hongkong/qingzhang_lianhuise', detailed_config: { dealer_mode, win_claim: 'multiple', liability_limit: false } } })
    assert.equal(form.hk_dealer_mode, dealer_mode)
    assert.equal(form.hk_win_claim, 'multiple')
    assert.equal(form.hk_liability_limit, false)
    assert.equal(buildEventRoomSettings(form).room_config.detailed_config.dealer_mode, dealer_mode)
  }
})

for (const sub_rule of ['hongkong/qingzhang', 'hongkong/new13', 'hongkong/new16']) {
  test(`${sub_rule}: editor round trip preserves the selected rule and supported settings`, () => {
    const saved = { room_rule: 'hongkong', room_config: { sub_rule, game_round: 2, round_timer: 0, step_timer: 7, tips: true, allow_spectator: false, detailed_config: { flowers: sub_rule !== 'hongkong/new13', new13_full_shoot: false } } }
    const before = JSON.stringify(saved)
    const form = createEventRoomForm(saved)
    const built = buildEventRoomSettings(form)
    assert.equal(built.room_config.sub_rule, sub_rule)
    assert.equal(built.room_config.game_round, 2)
    assert.equal(built.room_config.round_timer, 0)
    assert.equal(built.room_config.allow_spectator, false)
    for (const [key,value] of Object.entries(saved.room_config.detailed_config)) assert.equal(built.room_config.detailed_config[key], value)
    assert.deepEqual(buildEventRoomSettings(createEventRoomForm(built)), built)
    const rows = eventRoomSettingsRows(built)
    assert.ok(rows.find(row => row.label === '子规则').value.includes(sub_rule === 'hongkong/new16' ? '十六' : '十三'))
    form.hk_flowers = false
    assert.equal(JSON.stringify(saved), before)
  })
}

test('HK variants constrain flower availability when switching rules', () => {
  const form = createEventRoomForm({ room_rule: 'hongkong' })
  form.hk_flowers = true
  form.sub_rule = 'hongkong/new13'
  assert.equal(buildEventRoomSettings(form).room_config.detailed_config.flowers, false)
  form.hk_flowers = false
  form.sub_rule = 'hongkong/new16'
  assert.equal(buildEventRoomSettings(form).room_config.detailed_config.flowers, true)
  form.sub_rule = 'hongkong/unknown'
  assert.throws(() => buildEventRoomSettings(form), /子规则/)
})

test('恋绘色 defaults to flowers, preserves explicit no flowers, and displays every house rule', () => {
  const form = createEventRoomForm({ room_rule: 'hongkong', room_config: { sub_rule: 'hongkong/new13', detailed_config: { new13_version: 'lianhuise' } } })
  assert.equal(buildEventRoomSettings(form).room_config.detailed_config.flowers, true)
  for (const key of ['self_draw_only','liability_twelve','liability_dragons','liability_kong']) {
    for (const value of [true,false]) {
      form[`hk_${key}`] = value
      const built = buildEventRoomSettings(form)
      assert.equal(built.room_config.detailed_config[key], value)
      assert.deepEqual(buildEventRoomSettings(createEventRoomForm(built)),built)
    }
  }
  for (const win of ['default','head_bump','multiple']) for (const dealer of ['default','rotate','win_or_draw']) {
    form.hk_win_claim=win; form.hk_dealer_mode=dealer
    const built=buildEventRoomSettings(form)
    assert.deepEqual(buildEventRoomSettings(createEventRoomForm(built)),built)
    const rows=eventRoomSettingsRows(built)
    assert.equal(rows.find(r=>r.label==='子规则').value,'新章十三（恋绘色）')
    assert.ok(!rows.some(r=>r.label==='新章13版本'))
    assert.ok(rows.some(r=>r.label==='生章明杠包自摸'))
    assert.ok(!rows.some(r=>r.label==='全冲'))
  }
  form.hk_flowers=false
  assert.equal(buildEventRoomSettings(form).room_config.detailed_config.flowers,false)
  form.hk_new13_version='gametower';form.hk_flowers=true
  assert.equal(buildEventRoomSettings(form).room_config.detailed_config.flowers,false)
  assert.ok(!eventRoomSettingsRows(buildEventRoomSettings(form)).some(r=>r.label==='生章明杠包自摸'))
})

test('five visible profiles select exact versions and old new13 rooms still display their version', async () => {
  const { hkProfiles, displayHongKongProfile } = await import('../src/utils/hongKongRoomConfig.js')
  assert.deepEqual(hkProfiles.map(p=>p.label),['清章十三','新章十三（Wiki）','新章十三（恋绘色）','新章十三（恋绘色魔改）','新章十六'])
  for (const profile of hkProfiles) {
    const form=createEventRoomForm({room_rule:'hongkong',room_config:{sub_rule:profile.value}})
    const built=buildEventRoomSettings(form)
    assert.equal(built.room_config.sub_rule,profile.value)
    assert.equal(built.room_config.detailed_config.new13_version,profile.value.endsWith('new13_lianhuise')?'lianhuise':'gametower')
    assert.equal(eventRoomSettingsRows(built).find(r=>r.label==='子规则').value,profile.label)
    assert.deepEqual(buildEventRoomSettings(createEventRoomForm(built)),built)
  }
  for(const version of ['gametower','lianhuise']) {
    const form=createEventRoomForm({room_rule:'hongkong',room_config:{sub_rule:'hongkong/new13',detailed_config:{new13_version:version}}})
    assert.equal(displayHongKongProfile(form),`hongkong/new13_${version}`)
  }
})

test('remix has fixed flowers and independent liability toggles with tenpai dealer default', () => {
  const form=createEventRoomForm({room_rule:'hongkong',room_config:{sub_rule:'hongkong/qingzhang_lianhuise'}})
  for(const flower of ['default',false,true]) for(const twelve of [false,true]) for(const limit of [false,true]) {
    form.hk_flowers=flower;form.hk_liability_twelve=twelve;form.hk_liability_limit=limit
    const built=buildEventRoomSettings(form),rows=eventRoomSettingsRows(built)
    assert.equal(built.room_config.detailed_config.flowers,true)
    assert.equal(built.room_config.detailed_config.liability_twelve,twelve)
    assert.equal(built.room_config.detailed_config.liability_limit,limit)
    assert.equal(rows.find(r=>r.label==='连庄方式').value,'庄和或流局听牌连庄')
    assert.equal(rows.find(r=>r.label==='包满贯').value,limit?'开启':'关闭')
    assert.ok(!rows.some(r=>['全冲','包大三元','生章明杠包自摸','新章13版本'].includes(r.label)))
  }
})
