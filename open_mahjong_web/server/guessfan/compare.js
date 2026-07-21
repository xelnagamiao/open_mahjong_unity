const {
  GUESS_FAN_BY_ID,
  RULE_LABEL,
  formatFanDisplay,
  formatFanField,
} = require('./catalog')

function familyRules(answer) {
  const rules = new Set(answer.rules)
  for (const rid of answer.relatedIds || []) {
    const rel = GUESS_FAN_BY_ID[rid]
    if (rel) rel.rules.forEach((r) => rules.add(r))
  }
  return rules
}

function sameArray(a, b) {
  if (a.length !== b.length) return false
  return a.every((v, i) => v === b[i])
}

function intersect(a, b) {
  const set = new Set(b)
  return a.filter((x) => set.has(x))
}

function applyRelatedGate(disableRelated, tone) {
  if (disableRelated && tone === 'yellow') return 'gray'
  return tone
}

function reqLengthRank(v) {
  if (Array.isArray(v)) return Math.max(...v.map(Number).filter(Number.isFinite), 0)
  if (v === '条件') return 0
  if (v === '全体') return 100
  const n = Number(v)
  return Number.isFinite(n) ? n : 0
}

function fanRank(v) {
  if (typeof v === 'number' && Number.isFinite(v)) return v
  const s = String(v)
  if (s === '双倍役满') return 26
  if (s === '役满') return 13
  if (s === '满贯') return 5
  const n = Number(s)
  return Number.isFinite(n) ? n : 0
}

function compareHint(guessRank, answerRank) {
  if (guessRank === answerRank) return null
  return answerRank > guessRank ? 'up' : 'down'
}

function resolveFanTone({ answer, rolledFan, guess, disableRelated }) {
  const Y = (t) => applyRelatedGate(disableRelated, t)
  const answerOpts = Array.isArray(answer.fan) ? answer.fan.map(String) : [String(answer.fan)]
  const guessOpts = Array.isArray(guess.fan) ? guess.fan.map(String) : [String(guess.fan)]
  const rolled = String(rolledFan)
  const guessPrimary = Array.isArray(guess.fan) ? guess.fan[0] : guess.fan

  if (guessOpts.includes(rolled)) {
    return { tone: 'green', hint: null }
  }
  if (answerOpts.length > 1 && guessOpts.some((g) => answerOpts.includes(g) && g !== rolled)) {
    return { tone: Y('yellow'), hint: null }
  }

  const hint = compareHint(fanRank(guessPrimary), fanRank(rolledFan))
  return { tone: 'gray', hint }
}

function compareGuess({ answer, rolledFan, guess, disableRelated = false }) {
  const Y = (t) => applyRelatedGate(disableRelated, t)

  const nameHit = guess.id === answer.id
  const nameAliasOverlap =
    !nameHit && guess.names.some((n) => answer.names.includes(n))
  const nameRelated =
    !nameHit &&
    ((answer.relatedIds || []).includes(guess.id) ||
      (guess.relatedIds || []).includes(answer.id) ||
      nameAliasOverlap)
  const nameTone = Y(nameHit ? 'green' : nameRelated ? 'yellow' : 'gray')

  const guessRules = guess.rules
  const answerRules = answer.rules
  const ruleExact = sameArray([...guessRules].sort(), [...answerRules].sort())
  const ruleIntersect = intersect(guessRules, answerRules)
  const fam = familyRules(answer)

  let rulesTone = 'gray'
  if (ruleExact) rulesTone = Y('green')
  else if (ruleIntersect.length || guessRules.some((r) => fam.has(r))) rulesTone = Y('yellow')

  const typeExact = sameArray(guess.types, answer.types)
  const typeOverlap = intersect(guess.types, answer.types)
  let typesTone = 'gray'
  if (typeExact) typesTone = Y('green')
  else if (typeOverlap.length) typesTone = Y('yellow')

  let reqTone = 'gray'
  let reqHint = null
  if (JSON.stringify(guess.reqLength) === JSON.stringify(answer.reqLength)) {
    reqTone = 'green'
  } else {
    reqHint = compareHint(reqLengthRank(guess.reqLength), reqLengthRank(answer.reqLength))
  }

  const fanResolved = resolveFanTone({ answer, rolledFan, guess, disableRelated })

  return {
    name: { value: guess.names[0], tone: nameTone },
    rules: {
      value: guessRules.map((r) => RULE_LABEL[r] || r).join('/'),
      tone: rulesTone,
    },
    types: {
      value: guess.types.join('、'),
      tone: typesTone,
    },
    reqLength: {
      value: Array.isArray(guess.reqLength) ? `[${guess.reqLength.join(',')}]` : String(guess.reqLength),
      tone: reqTone,
      hint: reqHint,
    },
    fan: {
      value: formatFanField(guess.fan),
      tone: fanResolved.tone,
      hint: fanResolved.hint,
    },
    correct: nameHit,
  }
}

function revealAnswer(answer, rolledFan) {
  return {
    id: answer.id,
    name: answer.names[0],
    names: answer.names,
    rules: answer.rules.map((r) => RULE_LABEL[r] || r),
    types: answer.types,
    reqLength: answer.reqLength,
    fan: formatFanDisplay(rolledFan),
    fanField: formatFanField(answer.fan),
  }
}

function extractTonePreview(result) {
  if (!result) return null
  return {
    name: result.name?.tone || 'gray',
    rules: result.rules?.tone || 'gray',
    types: result.types?.tone || 'gray',
    reqLength: result.reqLength?.tone || 'gray',
    fan: result.fan?.tone || 'gray',
    correct: !!result.correct,
  }
}

module.exports = { compareGuess, revealAnswer, extractTonePreview }
