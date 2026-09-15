import { dateRangeToQueryParams, makePastDateRange } from './statsDisplay.js'

function assert(cond, msg) {
  if (!cond) throw new Error(msg)
}

assert(Object.keys(dateRangeToQueryParams(null)).length === 0, 'null range is empty')
assert(Object.keys(dateRangeToQueryParams(['2026-09-01'])).length === 0, 'incomplete range is empty')

const single = dateRangeToQueryParams(['2026-01-31', '2026-01-31'])
assert(single.date_from === '2026-01-31T00:00:00', `date_from ${single.date_from}`)
assert(single.date_to === '2026-02-01T00:00:00', `month-end exclusive date_to ${single.date_to}`)

const leap = dateRangeToQueryParams(['2024-02-28', '2024-02-29'])
assert(leap.date_to === '2024-03-01T00:00:00', `leap exclusive date_to ${leap.date_to}`)

const past = makePastDateRange(7)
assert(past.length === 2 && past[0] <= past[1], `makePastDateRange ${past}`)

console.log('statsDisplay date range helpers ok')
