import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import es from '../src/i18n/locales/es.js'
import { locale, tr, setLocale, roundLabelKey } from '../src/i18n/index.js'
import { SUPPORTED_LOCALES, normalizeLocale, requestedGameLocale } from '../src/i18n/locale-utils.js'
import { translateFanName, translateRuleText, formatFanCount } from '../src/i18n/fanNames.js'
import { guessFanName } from '../src/i18n/guessFanNames.js'
import { calculatorScoreText, riichiValueText } from '../src/utils/calculatorScoreText.js'
import { suggestFans } from '../src/constants/guessFanCatalog.js'
import { createGuobiaoCalculator } from '../src/composables/useGuobiaoCalculator.js'
import { formatGuobiaoFanComposition } from '../src/constants/guobiaoFanDict.js'
import { formatFanUnit } from '../src/i18n/fanNames.js'
import { preciseFan, scorePointUnit } from '../src/utils/calculatorScoreText.js'
import * as Vue from 'vue'
import { compile } from '@vue/compiler-dom'
import { parse } from '@vue/compiler-sfc'
import { renderToString } from '@vue/server-renderer'

const unityRoot = new URL('../../../open_mahjong_unity/', import.meta.url)
const source = JSON.parse(fs.readFileSync(new URL('Assets/Resources/Localization/PlatformUI.json', unityRoot), 'utf8'))
const tokens = s => [...s.matchAll(/\{(?:[A-Za-z_][^{}\r\n]*|\d+(?::[^{}\r\n]+)?)\}/g)].map(m => m[0]).sort()
const records = list => Object.fromEntries(list.map(e => [e.key,e.text]))

test('Spanish covers current Unity source with intact placeholders and nonempty values', () => {
  for (const entry of source.entries) {
    assert.equal(typeof es.messages[entry.key], 'string', entry.key)
    assert.ok(es.messages[entry.key].trim(), entry.key)
    assert.deepEqual(tokens(es.messages[entry.key]), tokens(entry.zh || entry.key), entry.key)
  }
  for (const [key,value] of Object.entries(es.messages)) assert.ok(value.trim(), key)
})

test('Unity and Web use the same Spanish catalog, contexts and quantities', () => {
  const unity = JSON.parse(fs.readFileSync(new URL('Assets/Resources/Localization/PlatformUI.es.json', unityRoot), 'utf8'))
  // Compare entry by entry to keep a failure bounded and useful.
  const messages = records(unity.entries)
  assert.deepEqual(Object.keys(messages), Object.keys(es.messages))
  for (const key of Object.keys(es.messages)) assert.equal(messages[key], es.messages[key], key)
  assert.deepEqual(Object.fromEntries(unity.contexts.map(c => [c.name,records(c.entries)])), es.contexts)
  assert.deepEqual(Object.fromEntries(unity.units.map(({key,...forms}) => [key,forms])), es.units)
})

test('Spanish regional tags and explicit game selection are supported', () => {
  assert.ok(SUPPORTED_LOCALES.includes('es'))
  for (const tag of ['es','es-ES','es-MX','es_AR','ES-cl']) assert.equal(normalizeLocale(tag),'es')
  assert.equal(requestedGameLocale('/2d','?lang=es-MX'),'es')
  assert.equal(requestedGameLocale('/library','?lang=es'),null)
})

test('Unity language indices and WebGL browser locale preserve existing settings', () => {
  const source = fs.readFileSync(new URL('Assets/Scripts/Config/AppLanguage.cs',unityRoot),'utf8')
  for (const [name,value] of [['SimplifiedChinese',0],['TraditionalChinese',1],['English',2],['French',3],['German',4],['Russian',5],['Japanese',6],['Spanish',7]])
    assert.match(source,new RegExp(`${name} = ${value}\\b`))
  const bridge = fs.readFileSync(new URL('Assets/Plugins/WebGL/SystemLanguage.jslib',unityRoot),'utf8')
  for (const tag of ['es-ES','es_MX','fr-CA','ja-JP']) {
    const context = { navigator:{ languages:[tag],language:tag }, LibraryManager:{ library:{} },mergeInto:(to,values)=>Object.assign(to,values) }
    vm.runInNewContext(bridge,context)
    assert.equal(context.LibraryManager.library.SalasasaSystemLanguage(), tag.startsWith('es') ? 7 : tag.startsWith('fr') ? 3 : 6)
  }
})

test('MCR, Riichi and local rule scores keep distinct Spanish units', () => {
  assert.equal(translateFanName('平和','es','riichi'),'Pinfu')
  assert.equal(guessFanName('平和',['立直'],'es'),'Pinfu')
  assert.notEqual(translateFanName('平和','es','guobiao'),'Pinfu')
  assert.equal(formatFanCount(1,'es','guobiao'),'1 punto')
  assert.equal(formatFanCount(8,'es','guobiao/standard'),'8 puntos')
  assert.equal(formatFanCount(1.5,'es','guobiao'),'1.5 puntos')
  assert.equal(formatFanCount(2,'es','riichi/sanma'),'2 han')
  assert.equal(formatFanCount(2,'es','sichuan'),'2 fan')
  assert.equal(translateRuleText('30副','es','classical'),'30 puntos básicos')
  assert.equal(translateFanName('平和（1 番）','es','riichi'),'Pinfu (1 han)')
  assert.equal(translateFanName('平和 2番','es','riichi'),'Pinfu 2 han')
  assert.equal(tr('1张',{},'es'),'1 ficha')
  assert.equal(tr('12张',{},'es'),'12 fichas')
})

test('Spanish dynamic labels preserve literal names and formatter values', () => {
  for (const name of ['取消・东风・{2}・Play','取消','大三元','7番','已关闭','{2}']) {
    for (const source of [`${name}的房间`,`以 ${name} 连接`,`玩家 ${name}`]) assert.ok(tr(source,{},'es').includes(name),source)
    assert.ok(tr('{name}的房间',{name},'es').includes(name))
  }
  assert.equal(roundLabelKey(1,'wind-seat','es'),'东一局')
  assert.equal(tr(roundLabelKey(1,'wind-seat','es'),{},'es'),'Este 1')
  assert.ok(!/[\u3400-\u9fff]/.test(tr('剩余 27 张',{},'es')))
  assert.ok(tr('剩余 27 张',{},'es').includes('27'))
  assert.ok(tr('无法打开比赛观战：Missing tables',{},'es').includes('Faltan las mesas'))
})

test('Spanish translated search preserves the original MCR scoring identifier', () => {
  const translated = translateFanName('大三元','es','guobiao')
  const found = suggestFans(translated,['guobiao'],20,fan=>fan.names.map(name=>translateFanName(name,'es',fan.rules[0])))
  assert.equal(found[0].id,'guobiao:dasanyuan')
  assert.equal(found[0].names[0],'大三元')
})

async function renderRows(component, props) {
  const { descriptor,errors } = parse(fs.readFileSync(new URL(`../src/components/${component}.vue`,import.meta.url),'utf8'))
  assert.deepEqual(errors,[])
  const render = new Function('Vue',compile(descriptor.template.content,{mode:'function',prefixIdentifiers:true}).code)(Vue)
  const previous = locale.value
  try {
    setLocale('es')
    const app=Vue.createSSRApp({ render,setup:()=>({...props,conditions:'',locale,tr,formatGuobiaoFanComposition,
      translateFanName,translateRuleText,formatFanUnit,calculatorScoreText,preciseFan,riichiValueText,scorePointUnit}) })
    app.component('el-tag',{setup:(_, {slots})=>()=>Vue.h('span',{'data-fan':'true'},slots.default?.())})
    app.component('TileMiniGlyph',{render:()=>Vue.h('i')})
    app.config.warnHandler=message=>{throw new Error(message)}
    const html=await renderToString(app)
    return {html,rows:[...html.matchAll(/data-fan="true"[^>]*>([\s\S]*?)<\/span>/g)].map(m=>m[1].replace(/<!--.*?-->/g,'').trim())}
  } finally {setLocale(previous)}
}

test('actual calculator result and decomposition components keep Spanish MCR point values',async t=>{
  const errors=[]
  const state=createGuobiaoCalculator({onError:message=>errors.push(message)})
  t.after(()=>state.destroy())
  state.textInput.value='123m1235p123456s + 5p'
  assert.equal(await state.calculate({decompose:true}),true)
  assert.deepEqual(errors,[])
  assert.equal(state.best.value.fan,14)
  const expected=[['三色三同顺',8],['平和',2],['门前清',2],['单钓将',1],['连六',1]]
    .map(([name,n])=>`${translateFanName(name,'es','guobiao')} ${n} ${n===1?'punto':'puntos'}`)
  assert.deepEqual((await renderRows('GuobiaoScoreResult',{best:state.best.value})).rows,expected)
  const result=await renderRows('GuobiaoDecompositions',{decompositions:[state.result.value.decompositions[0]]})
  assert.deepEqual(result.rows,expected)
  assert.match(result.html,/14 puntos/)
})
