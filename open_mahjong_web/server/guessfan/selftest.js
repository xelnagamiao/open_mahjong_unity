/**
 * 猜番对抗 compare 用例自测（Node CJS）
 */
const { GUESS_FAN_BY_ID, findFanByName } = require('./catalog')
const { compareGuess } = require('./compare')

let passed = 0
let failed = 0

function assert(cond, msg) {
  if (cond) {
    passed += 1
    console.log('  OK', msg)
  } else {
    failed += 1
    console.error('  FAIL', msg)
  }
}

function run(title, fn) {
  console.log('\n' + title)
  fn()
}

run('1. 清龙 ↔ 一气通贯 名字黄', () => {
  const answer = GUESS_FAN_BY_ID['riichi:ittsu']
  const guess = GUESS_FAN_BY_ID['guobiao:qinglong']
  const r = compareGuess({ answer, rolledFan: 2, guess, disableRelated: false })
  assert(r.name.tone === 'yellow', `name=${r.name.tone}`)
  assert(!r.correct, 'not correct')
})

run('2. 同名命中绿', () => {
  const answer = GUESS_FAN_BY_ID['riichi:ittsu']
  const guess = GUESS_FAN_BY_ID['riichi:ittsu']
  const r = compareGuess({ answer, rolledFan: 2, guess })
  assert(r.name.tone === 'green', `name=${r.name.tone}`)
  assert(r.correct, 'correct')
  assert(r.fan.tone === 'green', `fan=${r.fan.tone}`)
})

run('3. 食下番数黄：答案一气通贯 roll=2，猜平和(1)', () => {
  const answer = GUESS_FAN_BY_ID['riichi:ittsu']
  const guess = GUESS_FAN_BY_ID['riichi:pinfu']
  const r = compareGuess({ answer, rolledFan: 2, guess })
  assert(r.fan.tone === 'yellow', `fan=${r.fan.tone}`)
})

run('4. 取消关联：名字/番数无黄', () => {
  const answer = GUESS_FAN_BY_ID['riichi:ittsu']
  const guess = GUESS_FAN_BY_ID['guobiao:qinglong']
  const r = compareGuess({ answer, rolledFan: 2, guess, disableRelated: true })
  assert(r.name.tone === 'gray', `name=${r.name.tone}`)

  const r2 = compareGuess({
    answer,
    rolledFan: 2,
    guess: GUESS_FAN_BY_ID['riichi:pinfu'],
    disableRelated: true,
  })
  assert(r2.fan.tone === 'gray', `fan=${r2.fan.tone}`)
})

run('5. 立直平和 + 猜国标三色三步高 → 规则单格黄', () => {
  const answer = GUESS_FAN_BY_ID['riichi:pinfu']
  const guess = GUESS_FAN_BY_ID['guobiao:sansesanbugao']
  const r = compareGuess({ answer, rolledFan: 1, guess })
  assert(r.rules.tone === 'yellow', `rules=${JSON.stringify(r.rules)}`)
  assert(typeof r.rules.value === 'string', 'rules.value is string')
  assert(!Array.isArray(r.rules.value), 'rules not multi chips')
})

run('6. 组合龙类型：猜顺子系 → 类型单格黄', () => {
  const answer = GUESS_FAN_BY_ID['guobiao:zuhelong']
  const guess = GUESS_FAN_BY_ID['guobiao:sansesanbugao']
  const r = compareGuess({ answer, rolledFan: 12, guess })
  assert(r.types.tone === 'yellow', `types=${JSON.stringify(r.types)}`)
  assert(r.types.value === '顺子系', `types.value=${r.types.value}`)
})

run('7. findFanByName 别名', () => {
  const a = findFanByName('一气通贯', ['riichi'])
  assert(a && a.id === 'riichi:ittsu', `ittsu=${a?.id}`)
  const b = findFanByName('清龙', ['guobiao'])
  assert(b && b.id === 'guobiao:qinglong', `qinglong=${b?.id}`)
})

run('8. 需求长度绿/灰+箭头', () => {
  const answer = GUESS_FAN_BY_ID['guobiao:sansesanbugao']
  const guess = GUESS_FAN_BY_ID['guobiao:qinglong']
  const r = compareGuess({ answer, rolledFan: 6, guess })
  assert(r.reqLength.tone === 'green', 'same length 3 green')
  const g2 = GUESS_FAN_BY_ID['guobiao:yibangao']
  const r2 = compareGuess({ answer, rolledFan: 6, guess: g2 })
  assert(r2.reqLength.tone === 'gray', 'diff length gray')
  assert(r2.reqLength.hint === 'up' || r2.reqLength.hint === 'down', `hint=${r2.reqLength.hint}`)
})

run('9. 类型顺序：宝牌 vs 里宝牌 → 绿（顺序一致）', () => {
  const dora = Object.values(GUESS_FAN_BY_ID).find((f) => f.names[0] === '宝牌')
  const ura = Object.values(GUESS_FAN_BY_ID).find((f) => f.names[0] === '里宝牌')
  assert(dora && ura, 'dora/ura exist')
  const r = compareGuess({ answer: dora, rolledFan: dora.fan, guess: ura })
  assert(r.types.tone === 'green', `types=${JSON.stringify(r.types)}`)
  assert(typeof r.types.value === 'string' && r.types.value.includes('、'), `value=${r.types.value}`)
})

run('10. 多类型番 vs 单类型：里宝牌 vs 立直 → 类型黄', () => {
  const ura = GUESS_FAN_BY_ID['riichi:ura_dora']
  const riichi = GUESS_FAN_BY_ID['riichi:riichi']
  const r = compareGuess({ answer: ura, rolledFan: ura.fan, guess: riichi })
  assert(r.types.tone === 'yellow', `types=${JSON.stringify(r.types)}`)
})

console.log(`\n==== ${passed} passed, ${failed} failed ====`)
process.exit(failed ? 1 : 0)
