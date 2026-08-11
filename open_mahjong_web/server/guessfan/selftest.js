/**
 * 猜番对抗 compare 用例自测（Node CJS）
 */
const { GUESS_FAN_BY_ID, findFanByName, rollFanValue } = require('./catalog')
const { compareGuess } = require('./compare')
const {
  MATCH_OPENING_COUNTDOWN_MS,
  ROUND_RESULT_WAIT_MS,
  CUSTOM_MAX_GUESSES_OPTIONS,
  CUSTOM_TIME_LIMIT_OPTIONS,
  normalizeCustomMaxGuesses,
  normalizeCustomTimeLimit,
  shouldUseMatchOpeningCountdown,
} = require('./timing')

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

run('11. Secondary fan value is yellow', () => {
  const answer = GUESS_FAN_BY_ID['guobiao:pinghe']
  const guess = GUESS_FAN_BY_ID['riichi:honitsu']
  const r = compareGuess({ answer, rolledFan: 2, guess })
  assert(r.fan.tone === 'yellow', `fan=${r.fan.tone}`)
})

run('12. Exact fan remains green at its secondary value', () => {
  const answer = GUESS_FAN_BY_ID['riichi:honitsu']
  const r = compareGuess({ answer, rolledFan: 2, guess: answer })
  assert(r.fan.tone === 'green', `fan=${r.fan.tone}`)
})

run('13. 大小四喜、三四暗刻、大小三元互为黄色关联', () => {
  const families = [
    ['guobiao:dasixi', 'guobiao:xiaosixi', 'riichi:daisushi', 'riichi:shousuushi'],
    ['guobiao:sianke', 'guobiao:sananke', 'riichi:suuankou', 'riichi:sanankou'],
    ['guobiao:dasanyuan', 'guobiao:xiaosanyuan', 'riichi:daisangen', 'riichi:shousangen'],
  ]
  for (const ids of families) {
    for (const answerId of ids) {
      for (const guessId of ids) {
        if (answerId === guessId) continue
        const answer = GUESS_FAN_BY_ID[answerId]
        const guess = GUESS_FAN_BY_ID[guessId]
        const rolledFan = Array.isArray(answer.fan) ? answer.fan[0] : answer.fan
        const r = compareGuess({ answer, rolledFan, guess })
        assert(r.name.tone === 'yellow', `${answerId} <- ${guessId}: ${r.name.tone}`)
      }
    }
  }

  const answer = GUESS_FAN_BY_ID['guobiao:dasixi']
  const guess = GUESS_FAN_BY_ID['guobiao:xiaosixi']
  const disabled = compareGuess({ answer, rolledFan: answer.fan, guess, disableRelated: true })
  assert(disabled.name.tone === 'gray', `disabled=${disabled.name.tone}`)
})

run('14. 首次开局3秒与局间6秒不叠加', () => {
  assert(MATCH_OPENING_COUNTDOWN_MS === 3000, `opening=${MATCH_OPENING_COUNTDOWN_MS}`)
  assert(ROUND_RESULT_WAIT_MS === 6000, `between=${ROUND_RESULT_WAIT_MS}`)
  assert(
    shouldUseMatchOpeningCountdown({ round: 0, openingCountdownUsed: false }),
    'first match opening uses 3s',
  )
  assert(
    !shouldUseMatchOpeningCountdown({ round: 0, openingCountdownUsed: true }),
    'used opening does not repeat 3s',
  )
  assert(
    !shouldUseMatchOpeningCountdown({ round: 1, openingCountdownUsed: false }),
    'later round does not add 3s',
  )
})

run('14.1. 个人练习与自建房限时选项', () => {
  assert(
    JSON.stringify(CUSTOM_TIME_LIMIT_OPTIONS) === JSON.stringify([40, 60, 80, 100]),
    `options=${CUSTOM_TIME_LIMIT_OPTIONS.join(',')}`,
  )
  assert(normalizeCustomTimeLimit(undefined) === 60, '旧客户端未传限时时默认 60 秒')
  for (const seconds of CUSTOM_TIME_LIMIT_OPTIONS) {
    assert(normalizeCustomTimeLimit(seconds) === seconds, `accept ${seconds}s`)
  }
  let rejected = false
  try {
    normalizeCustomTimeLimit(30)
  } catch {
    rejected = true
  }
  assert(rejected, 'reject unsupported time limit')
})

run('14.2. 个人练习与自建房猜测次数选项', () => {
  assert(
    JSON.stringify(CUSTOM_MAX_GUESSES_OPTIONS) === JSON.stringify([6, 8, 10, 12]),
    `options=${CUSTOM_MAX_GUESSES_OPTIONS.join(',')}`,
  )
  assert(normalizeCustomMaxGuesses(undefined) === 8, '旧客户端未传猜测次数时默认 8 次')
  for (const maxGuesses of CUSTOM_MAX_GUESSES_OPTIONS) {
    assert(normalizeCustomMaxGuesses(maxGuesses) === maxGuesses, `accept ${maxGuesses} guesses`)
  }
  let rejected = false
  try {
    normalizeCustomMaxGuesses(9)
  } catch {
    rejected = true
  }
  assert(rejected, 'reject unsupported max guesses')
})

run('15. 双龙会与两规则平和均为条件系', () => {
  const ids = [
    'guobiao:yiseshuanglonghui',
    'guobiao:sanseshuanglonghui',
    'guobiao:pinghe',
    'riichi:pinfu',
  ]
  for (const id of ids) {
    const fan = GUESS_FAN_BY_ID[id]
    assert(
      fan.types.length === 1 && fan.types[0] === '条件系',
      `${id}=${fan.types.join('、')}`,
    )
  }
})

run('16. 食下役抽题一律取门清主番数', () => {
  const cases = [
    ['riichi:chanta', 2],
    ['riichi:ittsu', 2],
    ['riichi:sanshoku', 2],
    ['riichi:junchan', 3],
    ['riichi:honitsu', 3],
    ['riichi:chinitsu', 6],
  ]
  for (const [id, expected] of cases) {
    const fan = GUESS_FAN_BY_ID[id]
    for (let i = 0; i < 50; i++) {
      const rolled = rollFanValue(fan)
      assert(String(rolled) === String(expected), `${id} roll=${rolled} expected=${expected}`)
      if (String(rolled) !== String(expected)) return
    }
  }
})

run('17. 答案纯全带(3番)：猜混全带黄、混一色绿、自身绿', () => {
  const answer = GUESS_FAN_BY_ID['riichi:junchan']
  const r1 = compareGuess({ answer, rolledFan: 3, guess: GUESS_FAN_BY_ID['riichi:chanta'] })
  assert(r1.fan.tone === 'yellow', `chanta fan=${r1.fan.tone}`)
  const r2 = compareGuess({ answer, rolledFan: 3, guess: GUESS_FAN_BY_ID['riichi:honitsu'] })
  assert(r2.fan.tone === 'green', `honitsu fan=${r2.fan.tone}`)
  const r3 = compareGuess({ answer, rolledFan: 3, guess: answer })
  assert(r3.fan.tone === 'green' && r3.correct, `junchan fan=${r3.fan.tone} correct=${r3.correct}`)
})

run('18. 宝牌/赤宝牌/里宝牌 互为黄色关联且番名合并', () => {
  const ids = ['riichi:dora', 'riichi:aka_dora', 'riichi:ura_dora']
  for (const answerId of ids) {
    for (const guessId of ids) {
      if (answerId === guessId) continue
      const answer = GUESS_FAN_BY_ID[answerId]
      const guess = GUESS_FAN_BY_ID[guessId]
      const r = compareGuess({ answer, rolledFan: answer.fan, guess })
      assert(r.name.tone === 'yellow', `${answerId} <- ${guessId}: ${r.name.tone}`)
      assert(!r.correct, `${answerId} <- ${guessId} not correct`)
    }
  }
  const dora = GUESS_FAN_BY_ID['riichi:dora']
  assert(
    JSON.stringify(dora.names) === JSON.stringify(['宝牌', '赤宝牌', '里宝牌']),
    `dora names=${JSON.stringify(dora.names)}`,
  )
  const disabled = compareGuess({
    answer: dora,
    rolledFan: dora.fan,
    guess: GUESS_FAN_BY_ID['riichi:aka_dora'],
    disableRelated: true,
  })
  assert(disabled.name.tone === 'gray', `disabled=${disabled.name.tone}`)
})

run('19. 海底捞月/河底捞鱼/妙手回春 互为黄色关联且番名合并', () => {
  const ids = [
    'guobiao:haidilaoyue',
    'guobiao:miaoshouhuichun',
    'riichi:haitei',
    'riichi:houtei',
  ]
  for (const answerId of ids) {
    for (const guessId of ids) {
      if (answerId === guessId) continue
      const answer = GUESS_FAN_BY_ID[answerId]
      const guess = GUESS_FAN_BY_ID[guessId]
      const rolledFan = Array.isArray(answer.fan) ? answer.fan[0] : answer.fan
      const r = compareGuess({ answer, rolledFan, guess })
      assert(r.name.tone === 'yellow', `${answerId} <- ${guessId}: ${r.name.tone}`)
      assert(!r.correct, `${answerId} <- ${guessId} not correct`)
    }
  }
  assert(
    JSON.stringify(GUESS_FAN_BY_ID['guobiao:haidilaoyue'].names) ===
      JSON.stringify(['海底捞月', '河底捞鱼', '妙手回春']),
    `haidilaoyue names=${JSON.stringify(GUESS_FAN_BY_ID['guobiao:haidilaoyue'].names)}`,
  )
  assert(
    JSON.stringify(GUESS_FAN_BY_ID['riichi:houtei'].names) ===
      JSON.stringify(['河底捞鱼', '海底捞月', '妙手回春']),
    `houtei names=${JSON.stringify(GUESS_FAN_BY_ID['riichi:houtei'].names)}`,
  )
  const disabled = compareGuess({
    answer: GUESS_FAN_BY_ID['riichi:houtei'],
    rolledFan: 1,
    guess: GUESS_FAN_BY_ID['guobiao:haidilaoyue'],
    disableRelated: true,
  })
  assert(disabled.name.tone === 'gray', `disabled=${disabled.name.tone}`)
})

console.log(`\n==== ${passed} passed, ${failed} failed ====`)
process.exit(failed ? 1 : 0)
