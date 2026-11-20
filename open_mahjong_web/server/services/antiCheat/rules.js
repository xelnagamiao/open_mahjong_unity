'use strict';

const { spawn } = require('node:child_process');
const path = require('node:path');
const { selectPython } = require('./python');

function meldOnly(decision) {
  if (decision.kind !== 'discard') return { id: decision.id, status: 'unavailable', reason: 'special_action_opportunities_not_implemented' };
  const recipients = [];
  for (let seat = 0; seat < 4; seat += 1) {
    if (seat === decision.donor_seat) continue;
    const hand = decision.hands[seat];
    const helpful = { CHI: [], PENG: [], GANG: [], MELD: [], RON: null, ALL: null };
    if (!decision.blocked[seat] && decision.remaining_tiles > 0) for (const value of decision.candidates) {
      if (value >= 51) continue;
      const count = hand.filter((t) => t === value).length;
      if (count >= 2) helpful.PENG.push(value);
      if (count === 3) helpful.GANG.push(value);
      if (seat === (decision.donor_seat + 1) % 4 && value < 40
        && [[value - 2, value - 1], [value - 1, value + 1], [value + 1, value + 2]].some(([a, b]) =>
          hand.includes(a) && hand.includes(b) && Math.floor(a / 10) === Math.floor(value / 10)
          && Math.floor(b / 10) === Math.floor(value / 10))) helpful.CHI.push(value);
    }
    helpful.MELD = [...new Set([...helpful.CHI, ...helpful.PENG, ...helpful.GANG])].sort((a, b) => a - b);
    recipients.push({ donor_id: decision.donor_id, recipient_id: decision.seat_users[seat],
      donor_seat: decision.donor_seat, recipient_seat: seat, helpful, fan_values: {}, status: 'partial' });
  }
  return { id: decision.id, status: 'partial', recipients, reason: 'qualifying_mcr_calculator_unavailable' };
}

function runPython(decisions, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(selectPython(options),
      [path.join(__dirname, 'ruleBridge.py')], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    let stdout = '';
    let stderr = '';
    let finished = false;
    const limit = 64 * 1024 * 1024;
    const timer = setTimeout(() => { child.kill(); finish(new Error('mcr_calculator_timeout')); }, options.calculator_timeout_ms || 60000);
    const finish = (error, result) => {
      if (finished) return;
      finished = true;
      clearTimeout(timer);
      if (error) reject(error); else resolve(result);
    };
    child.on('error', (error) => finish(new Error(`mcr_calculator_unavailable:${error.code || 'spawn_error'}`)));
    child.stdout.on('data', (part) => {
      stdout += part.toString('utf8');
      if (stdout.length > limit) { child.kill(); finish(new Error('mcr_calculator_output_limit')); }
    });
    child.stderr.on('data', (part) => { if (stderr.length < 2000) stderr += part.toString('utf8'); });
    child.stdin.on('error', () => { /* Exit/error is handled below; do not crash on an absent Python binary. */ });
    child.on('close', (code) => {
      if (code !== 0) return finish(new Error(`mcr_calculator_exit_${code}:${stderr.split('\n').filter(Boolean).slice(-1).join('').slice(0, 120)}`));
      try {
        const output = JSON.parse(stdout);
        if (!Array.isArray(output.results)) throw new Error('invalid_result');
        finish(null, output);
      } catch (_) { finish(new Error('mcr_calculator_invalid_json')); }
    });
    child.stdin.end(JSON.stringify({ decisions }));
  });
}

async function evaluateDecisions(decisions, options = {}) {
  const results = new Map();
  const errors = [];
  let calculatorAvailable = false;
  const batchSize = Math.max(1, Math.min(2000, Number(options.calculator_batch_size) || 800));
  if (options.compute_opportunities === false) return {
    results, calculator_available: false, errors: ['opportunity_calculation_disabled'],
  };
  let pythonFailure = options.python_failed ? 'prior_calculator_failure' : null;
  for (let i = 0; i < decisions.length; i += batchSize) {
    const batch = decisions.slice(i, i + batchSize);
    if (!pythonFailure) {
      try {
        const output = await runPython(batch, options);
        calculatorAvailable = true;
        const returned = new Map(output.results.map((r) => [r.id, r]));
        for (const d of batch) results.set(d.id, returned.get(d.id) || { id: d.id, status: 'unavailable', reason: 'missing_calculator_result' });
        continue;
      } catch (error) { pythonFailure = error.message; errors.push(pythonFailure); }
    }
    for (const decision of batch) results.set(decision.id, meldOnly(decision));
  }
  return { results, calculator_available: calculatorAvailable, errors };
}

module.exports = { evaluateDecisions, runPython, meldOnly };
