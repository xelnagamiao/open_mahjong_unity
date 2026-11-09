import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import * as Vue from 'vue'
import { compile } from '@vue/compiler-dom'
import { parse } from '@vue/compiler-sfc'
import { renderToString } from '@vue/server-renderer'
import { createGuobiaoCalculator } from '../src/composables/useGuobiaoCalculator.js'
import { formatGuobiaoFanComposition } from '../src/constants/guobiaoFanDict.js'
import { locale, setLocale, tr } from '../src/i18n/index.js'
import { translateFanName, translateRuleText, formatFanUnit } from '../src/i18n/fanNames.js'
import { calculatorScoreText, preciseFan, riichiValueText, scorePointUnit } from '../src/utils/calculatorScoreText.js'
import { frenchFanNames } from '../src/i18n/fr.js'

// Render the actual component templates so a missing rule argument at the call site fails too.
async function fanRows(component, props, language) {
  const file = new URL(`../src/components/${component}.vue`, import.meta.url)
  const { descriptor, errors } = parse(fs.readFileSync(file, 'utf8'))
  assert.deepEqual(errors, [])
  const render = new Function('Vue', compile(descriptor.template.content, { mode: 'function', prefixIdentifiers: true }).code)(Vue)
  const previous = locale.value
  try {
    setLocale(language)
    const app = Vue.createSSRApp({
      render,
      setup: () => ({ ...props, conditions: '', locale, tr, formatGuobiaoFanComposition,
        translateFanName, translateRuleText, formatFanUnit,
        calculatorScoreText, preciseFan, riichiValueText, scorePointUnit }),
    })
    app.component('el-tag', { setup: (_, { slots }) => () => Vue.h('span', { 'data-fan': 'true' }, slots.default?.()) })
    app.component('TileMiniGlyph', { render: () => Vue.h('i') })
    app.config.warnHandler = message => { throw new Error(message) }
    const html = await renderToString(app)
    return { html, rows: [...html.matchAll(/data-fan="true"[^>]*>([\s\S]*?)<\/span>/g)].map(match => match[1].replace(/<!--.*?-->/g, '').trim()) }
  } finally { setLocale(previous) }
}

async function mcrExample(t) {
  const errors = []
  const state = createGuobiaoCalculator({ onError: message => errors.push(message) })
  t.after(() => state.destroy())
  state.textInput.value = '123m1235p123456s + 5p'
  assert.equal(await state.calculate({ decompose: true }), true)
  assert.deepEqual(errors, [])
  assert.equal(state.best.value.fan, 14)
  assert.deepEqual(state.best.value.fanNames, ['三色三同顺', '平和', '门前清', '单钓将', '连六*1'])
  return state
}

test('the reported MCR hand renders points in every best-result and decomposition row', async t => {
  const state = await mcrExample(t)
  const expected = ['Смешанное тройное чоу 8 очков', 'Все чоу 2 очка', 'Закрытая рука 2 очка', 'Ожидание единственной 1 очко', 'Короткий ряд 1 очко']
  const best = await fanRows('GuobiaoScoreResult', { best: state.best.value }, 'ru')
  assert.deepEqual(best.rows, expected)
  assert.match(best.html, /очков/)
  const decomposition = await fanRows('GuobiaoDecompositions', { decompositions: [state.result.value.decompositions[0]] }, 'ru')
  assert.deepEqual(decomposition.rows, expected)
  assert.match(decomposition.html, /14 очков/)
})

test('rendered MCR badges preserve the locked French names', async t => {
  const state = await mcrExample(t)
  const expected = [['三色三同顺', '8 fans'], ['平和', '2 fans'], ['门前清', '2 fans'], ['单钓将', '1 fan'], ['连六', '1 fan']]
    .map(([name, value]) => `${frenchFanNames[name]} ${value}`)
  assert.deepEqual((await fanRows('GuobiaoScoreResult', { best: state.best.value }, 'fr')).rows, expected)
})

test('plain numeric badges retain the individual non-MCR rule', async () => {
  const score = { canWin: true, fan: 2, baseFan: 2, points: 2000, fu: 30, groups: [], payment: '', fanNames: ['平和 2番'] }
  assert.deepEqual((await fanRows('GuobiaoScoreResult', { best: { ...score, rule: 'riichi' } }, 'ru')).rows, ['Пинфу 2 хана'])
  assert.deepEqual((await fanRows('GuobiaoScoreResult', { best: { ...score, rule: 'qingque' } }, 'ru')).rows, ['Все чоу 2 фана'])
})
