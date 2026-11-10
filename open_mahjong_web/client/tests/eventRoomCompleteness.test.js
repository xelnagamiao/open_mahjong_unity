import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFileSync } from 'node:fs'
import catalog from '../../shared/eventRoomCatalog.json' with { type:'json' }
import taiwan from '../../shared/taiwanRoomOptions.json' with { type:'json' }
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'
import { createEventRoomCreationForm, buildEventRoomCreation } from '../src/utils/eventRoomCreation.js'
import { eventFieldDefinitions, eventRoomRuleOptions } from '../src/utils/eventRoomExtensions.js'
import { riichiModes } from '../src/utils/riichiRoomConfig.js'
import { updateGuobiaoRoomConfig } from '../src/utils/guobiaoRoomConfig.js'
import { SHANGRAO_OPTIONS } from '../src/utils/shangraoRoomConfig.js'
const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')
const seed = 'abcdef0123456789'.repeat(4)
function save(payload) {
  const doc = readRoomSettings({})
  return applyRoomSettingsChange(doc, { kind:'manual', userId:101, eventStatus:'active', body:{revision:doc.revision,...payload} }).settings.manual
}

test('creation selectors follow Unity lobby order by rule id, retaining supported subsets', () => {
  // RuleRegistry.Ordered uses LobbyOrder, then RuleId; labels may differ between clients.
  assert.deepEqual(eventRoomRuleOptions.map(option => option.value), [
    'guobiao', 'riichi', 'qingque', 'sichuan', 'changsha', 'taiwan', 'classical',
    'hongkong', 'hongque', 'guizhou', 'yixing', 'shanxi', 'hangzhou', 'hongzhong',
    'guangdong', 'changchun', 'jiujiang', 'nanjing', 'malaysia', 'shenyang',
    'wenzhou', 'wuhan', 'nanchang', 'shangrao', 'duocai', 'beijing', 'fuzhou', 'singapore',
  ])
  assert.deepEqual(riichiModes.map(option => option.value), [
    'riichi/standard', 'riichi/langyong', 'riichi/sanma', 'riichi/washizu',
    'riichi/washizu_realtime', 'riichi/washizu_doubles',
  ])
  const guobiaoOrder = ['standard', 'xiaolin', 'kshen', 'lanshi', 'blood_battle', 'sanma',
    'transparent', 'transparent_realtime', 'huahun', 'four_jokers', 'sixteen'].map(id => `guobiao/${id}`)
  const readOptions = (path, variable) => {
    const source = readFileSync(new URL(path, import.meta.url), 'utf8')
    const body = source.match(new RegExp(`const ${variable} = \\[([\\s\\S]*?)\\n\\]`))?.[1]
    assert.ok(body, `${variable} must remain connected to its selector`)
    return [...body.matchAll(/value: '([^']+)'/g)].map(match => match[1])
  }
  assert.deepEqual(readOptions('../src/components/GuobiaoEmptyRoomConfig.vue', 'defaultSubRuleOptions'), guobiaoOrder)
  assert.deepEqual(readOptions('../src/views/game2d/CustomRoomPanel.vue', 'SUPPORTED_SUB_RULES'),
    guobiaoOrder.filter(id => ['standard', 'huahun', 'four_jokers', 'sixteen'].some(sub => id === `guobiao/${sub}`)))
})

test('all 28 event rules retain scene seeds through forms, stored presets and reload', () => {
  assert.equal(catalog.rules.length, 28)
  for (const rule of catalog.rules) {
    const form = createEventRoomForm({room_rule:rule.value, room_config:{random_seed:seed}})
    const saved = save(buildEventRoomSettings(form))
    assert.equal(saved.room_config.random_seed,seed,rule.value)
    assert.equal(createEventRoomForm(saved).scene_reproduction,true,rule.value)
  }
})

test('every audited supplementary field survives the real frontend-to-preset boundary', () => {
  for (const [rule, keys] of Object.entries(catalog.supplementFields)) for (const key of keys) {
    if (key === 'password') continue
    const form = createEventRoomForm({room_rule:rule})
    const def = eventFieldDefinitions[key]
    form[key] = typeof def.default === 'boolean' ? !form[key] : def.max ?? 'terminal_pairs'
    const saved = save(buildEventRoomSettings(form))
    assert.equal(saved.room_config[key], form[key], `${rule}/${key}`)
  }
})

test('Taiwan exports all 63 hall options and the editable fan table without input mutation', () => {
  assert.equal(taiwan.options.length,63)
  const form = createEventRoomForm({room_rule:'taiwan'})
  form.taiwan_details.minimum_tai = 3
  form.taiwan_details.scoring_preset = 'cml'
  form.taiwan_fan_overrides = { full_flush:32 }
  const before = JSON.stringify(form)
  const saved = save(buildEventRoomSettings(form))
  assert.equal(JSON.stringify(form),before)
  assert.equal(saved.room_config.detailed_config.minimum_tai,3)
  assert.equal(saved.room_config.detailed_config.fan_tai_overrides.full_flush,32)
  assert.deepEqual(createEventRoomForm(saved).taiwan_details,saved.room_config.detailed_config)
})

test('ordinary scene reproduction preserves spectator preference and formal pairs reject seeds', () => {
  for (const sub_rule of ['sichuan/doubles_duplicate','sichuan/xueliu_doubles_duplicate']) {
    for (const allow_spectator of [true,false]) {
      const form = createEventRoomCreationForm({room_rule:'sichuan',room_config:{sub_rule:sub_rule.replace('_duplicate',''),random_seed:seed,allow_spectator}})
      const payload = buildEventRoomCreation(form)
      assert.equal(payload.room_config.allow_spectator,allow_spectator)
      assert.equal(payload.room_config.random_seed,seed)
      assert.equal(Boolean(payload.room_config.duplicate_key),false)
      form.sub_rule = sub_rule
      assert.throws(()=>buildEventRoomCreation(form),/同时使用/)
      form.scene_reproduction=false
      assert.equal(buildEventRoomCreation(form).room_config.allow_spectator,false)
    }
  }
})

test('all previously missing Guobiao and Riichi variants are accepted across preset saving', () => {
  for (const sub_rule of ['guobiao/blood_battle','guobiao/sanma','riichi/langyong','riichi/washizu','riichi/washizu_realtime']) {
    const room_rule=sub_rule.split('/')[0]
    const saved=save(buildEventRoomSettings(createEventRoomForm({room_rule,room_config:{sub_rule}})))
    assert.equal(saved.room_config.sub_rule,sub_rule)
    assert.doesNotMatch(eventRoomSettingsRows(saved).find(row=>row.label==='子规则').value,/\//)
    if (sub_rule.startsWith('riichi/washizu')) {
      assert.equal(saved.room_config.red_dora,false)
      assert.equal(saved.room_config.open_xiru,false)
      assert.equal(saved.room_config.hepai_way,'head_bump')
    }
    if (sub_rule === 'riichi/langyong') assert.equal(saved.room_config.starting_score,50000)
    if (sub_rule === 'guobiao/blood_battle') {
      assert.equal(saved.room_config.hepai_limit,8)
      assert.equal(saved.room_config.open_cuohe,false)
      assert.equal(saved.room_config.tactical_call,false)
    }
  }
})

test('Guobiao key rooms force spectating off and cannot also carry a scene seed', () => {
  const form = createEventRoomForm({room_rule:'guobiao',room_config:{duplicate_key:'dup_'+'a'.repeat(32),allow_spectator:true}})
  const payload = buildEventRoomSettings(form)
  assert.equal(payload.room_config.allow_spectator,false)
  assert.equal(save(payload).room_config.allow_spectator,false)
  form.scene_reproduction=true
  form.random_seed=seed
  assert.throws(()=>buildEventRoomSettings(form),/同时使用/)
  assert.throws(()=>save({room_rule:'guobiao',room_config:{duplicate_key:form.duplicate_key,random_seed:seed}}),/同时使用/)
})

test('leaving standard Guobiao clears hidden Jinluo mode before the next room is saved', () => {
  const form=createEventRoomForm()
  form.jinluo_mode=true
  updateGuobiaoRoomConfig(form,'sub_rule','guobiao/blood_battle')
  const saved=save(buildEventRoomSettings(form))
  assert.equal(saved.room_config.jinluo_mode,false)
  assert.equal(saved.room_config.sub_rule,'guobiao/blood_battle')
})

test('all 28 rules preserve both hint switches in named presets and automatic table selection', () => {
  for (const {value:room_rule} of catalog.rules) for (const count_tips of [true,false]) {
    const payload=buildEventRoomSettings(createEventRoomForm({room_rule,room_config:{count_tips,pointer_tips:!count_tips}}))
    let doc=readRoomSettings({})
    doc=applyRoomSettingsChange(doc,{kind:'preset-create',userId:101,eventStatus:'active',body:{revision:doc.revision,name:'提示回归',...payload}},{id:'hints-preset'}).settings
    doc=applyRoomSettingsChange(doc,{kind:'auto',userId:101,eventStatus:'active',body:{revision:doc.revision,enabled:true,preset_id:'hints-preset'}}).settings
    const reloaded=readRoomSettings(JSON.stringify(doc))
    const selected=resolveRoomSelection(reloaded,{revision:reloaded.revision,preset_id:'hints-preset'})
    assert.equal(selected.room_config.count_tips,count_tips,room_rule)
    assert.equal(selected.room_config.pointer_tips,!count_tips,room_rule)
    assert.equal(reloaded.auto_match.room_config.count_tips,count_tips,room_rule)
    assert.equal(createEventRoomForm(selected).pointer_tips,!count_tips,room_rule)
  }
})

test('Shangrao named presets retain all hall switches when created, updated, reopened and selected for a table', () => {
  const form=createEventRoomForm({room_rule:'shangrao',room_config:{game_round:3,round_timer:0,step_timer:17,tips:true,count_tips:true,pointer_tips:false,random_seed:seed}})
  for (const key of Object.keys(SHANGRAO_OPTIONS)) form['shangrao_'+key]=true
  const payload=buildEventRoomSettings(form)
  const initial=JSON.stringify(payload)
  let doc=readRoomSettings({})
  const change=(kind,body,presetId)=>{doc=applyRoomSettingsChange(doc,{kind,presetId,userId:101,eventStatus:'active',body:{revision:doc.revision,...body}},{id:'shangrao-preset'}).settings}
  change('manual',payload)
  change('preset-create',{name:'上饶馆规',...payload})
  change('auto',{enabled:true,preset_id:'shangrao-preset'})
  const selected=resolveRoomSelection(readRoomSettings(JSON.stringify(doc)),{revision:doc.revision,preset_id:'shangrao-preset'})
  assert.deepEqual(save(buildEventRoomSettings(createEventRoomForm(selected))).room_config,selected.room_config)
  for (const key of Object.keys(SHANGRAO_OPTIONS)) assert.equal(selected.room_config.detailed_config[key],true,key)
  assert.equal(selected.room_config.random_seed,seed)
  assert.equal(selected.room_config.count_tips,true)
  assert.equal(selected.room_config.pointer_tips,false)
  form.shangrao_qingyise=false
  change('preset-update',{name:'上饶馆规',...buildEventRoomSettings(form)},'shangrao-preset')
  assert.equal(doc.auto_match.room_config.detailed_config.qingyise,false)
  assert.equal(doc.manual.room_config.detailed_config.qingyise,true)
  assert.equal(JSON.stringify(payload),initial)
})
