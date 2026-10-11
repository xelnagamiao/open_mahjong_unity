import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse as parseTemplate } from '@vue/compiler-dom'
import { messages, textPatterns } from '../src/i18n/messages.js'
import { frenchFanNames } from '../src/i18n/fr.js'

const translate = (text, lang) => {
  const raw = text.replace(/\s+/g, ' ').trim()
  const translated = messages[lang]?.[raw]
  if (translated != null && translated !== raw) return translated
  for (const p of textPatterns[lang] || []) { const m=raw.match(p.match); if(m) return p.replace(...m.slice(1)) }
  return raw
}

test('guide static prose remains fully translated to English and French when split by links', () => {
  const vue = fs.readFileSync(new URL('../src/views/UsageGuide.vue', import.meta.url),'utf8')
  const { descriptor } = parseSfc(vue)
  const missing = []
  function walk(node, protectedText = false) {
    if (node.type===1) protectedText ||= node.props.some(p=>p.type===6&&p.name==='data-no-translate') || ['pre','code'].includes(node.tag)
    if (node.type===2 && !protectedText && /[\u3400-\u9fff]/u.test(node.content)) {
      for (const lang of ['en','fr']) if (/[\u3400-\u9fff]/u.test(translate(node.content,lang))) missing.push({lang,text:node.content.trim()})
    }
    for(const child of node.children || []) walk(child,protectedText)
  }
  walk(parseTemplate(descriptor.template.content))
  assert.deepEqual(missing,[])
})

test('preserves every supplied FFMJ scoring name', () => {
  for (const [source, value] of Object.entries(frenchFanNames)) assert.equal(messages.fr[source], value, source)
})

test('English and Japanese regulations preserve every numbered clause', () => {
  const base = new URL('../src/content/', import.meta.url)
  const cn = fs.readFileSync(new URL('accountRegulations.txt',base),'utf8')
  const clauseNumbers = value => [...value.matchAll(/^([1-4])\./gm)].map(m=>m[1])
  for(const lang of ['en','ja']) {
    const text=fs.readFileSync(new URL(`accountRegulations.${lang}.txt`,base),'utf8')
    assert.deepEqual(clauseNumbers(text),clauseNumbers(cn))
    for(const name of ['open_mahjong_unity','Salasasa Studio','salasasa.cn','https://github.com/xelnagamiao/open_mahjong_unity']) assert.ok(text.includes(name),lang+' '+name)
  }
})
