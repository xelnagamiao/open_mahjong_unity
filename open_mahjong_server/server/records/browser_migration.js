/* Offline fallback for exporting and atomically upgrading same-origin Unity replay data. */
(function (root) {
  'use strict';
  const STORE = 'records';
  const encode = value => new TextEncoder().encode(canonical(value)).buffer;
  // Seed fields can exceed Number.MAX_SAFE_INTEGER. Preserve integer tokens
  // before converting to JavaScript values; JSON.parse alone would corrupt them.
  function parseLossless(text) {
    let at = 0;
    function whitespace() { while (/\s/.test(text[at] || '') && at < text.length) at++; }
    function read() {
      whitespace();
      const c = text[at];
      if (c === '{' || c === '[') {
        at++; whitespace(); const end = c === '{' ? '}' : ']', value = c === '{' ? Object.create(null) : [];
        if (text[at] === end) { at++; return value; }
        while (true) {
          if (c === '{') {
            const key = read(); if (typeof key !== 'string') throw new Error('Invalid object key');
            whitespace(); if (text[at++] !== ':') throw new Error('Missing colon'); value[key] = read();
          } else value.push(read());
          whitespace(); if (text[at] === end) { at++; return value; }
          if (text[at++] !== ',') throw new Error('Missing comma');
        }
      }
      const match = /^("(?:\\.|[^"\\])*"|true|false|null|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/.exec(text.slice(at));
      if (!match) throw new Error('Invalid JSON');
      at += match[0].length;
      if (/^-?\d+$/.test(match[0]) && !Number.isSafeInteger(Number(match[0]))) return BigInt(match[0]);
      return JSON.parse(match[0]);
    }
    const result = read(); whitespace(); if (at !== text.length) throw new Error('Trailing JSON'); return result;
  }
  async function decode(value) {
    if (value == null) return null;
    if (typeof value === 'string') return parseLossless(value);
    if (typeof Blob !== 'undefined' && value instanceof Blob) value = await value.arrayBuffer();
    return parseLossless(new TextDecoder().decode(value));
  }
  function canonical(value) {
    if (typeof value === 'bigint') return value.toString();
    if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']';
    if (value && typeof value === 'object') return '{' + Object.keys(value).sort().map(k => JSON.stringify(k) + ':' + canonical(value[k])).join(',') + '}';
    if (typeof value === 'number') {
      if (!Number.isFinite(value)) throw new Error('Non-finite number');
      if (value === 0) return '0';
      if (Number.isInteger(value)) return BigInt(value).toString();
      const sign = value < 0 ? '-' : '', parts = Math.abs(value).toPrecision(17).toLowerCase().split('e');
      const point = (parts[0].includes('.') ? parts[0].indexOf('.') : parts[0].length) + Number(parts[1] || 0), digits = parts[0].replace('.', '');
      let number = point <= 0 ? '0.' + '0'.repeat(-point) + digits : point >= digits.length ? digits + '0'.repeat(point - digits.length) : digits.slice(0, point) + '.' + digits.slice(point);
      if (number.includes('.')) number = number.replace(/0+$/, '').replace(/\.$/, '');
      return sign + number;
    }
    return JSON.stringify(value);
  }
  async function hash(value) {
    const bytes = await root.crypto.subtle.digest('SHA-256', new TextEncoder().encode(canonical(value)));
    return Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join('');
  }
  function open() {
    return new Promise((resolve, reject) => {
      const request = root.indexedDB.open('omu.localRecords', 1);
      request.onupgradeneeded = () => request.result.createObjectStore(STORE);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
  function readAll(db) {
    return new Promise((resolve, reject) => {
      const entries = Object.create(null), tx = db.transaction(STORE, 'readonly');
      const request = tx.objectStore(STORE).openCursor();
      request.onsuccess = () => { const c = request.result; if (c) { entries[c.key] = c.value; c.continue(); } };
      tx.oncomplete = () => resolve(entries);
      tx.onabort = tx.onerror = () => reject(tx.error);
    });
  }
  async function decodedAll(db) {
    const raw = await readAll(db), entries = Object.create(null);
    for (const [key, value] of Object.entries(raw)) entries[key] = await decode(value);
    return entries;
  }

  async function exportLegacy(download = true) {
    const db = await open();
    try {
      const result = { format: 'omu.localRecords.export.unify', entries: await decodedAll(db) };
      if (download) {
        const url = URL.createObjectURL(new Blob([canonical(result)], { type: 'application/json' }));
        const link = document.createElement('a');
        link.href = url; link.download = 'local-replays-before.json'; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 0);
      }
      return result;
    } finally { db.close(); }
  }

  async function checksums(entries) {
    const out = Object.create(null);
    for (const [key, val] of Object.entries(entries)) out[key] = await hash(val);
    return out;
  }

  function replaceAtomic(db, expected, writes) {
    return new Promise((resolve, reject) => {
      const tx = db.transaction(STORE, 'readwrite'), store = tx.objectStore(STORE);
      let count = 0, error = null;
      function check(cursor, actual) {
        if (!Object.hasOwn(expected, cursor.key) || canonical(actual) !== canonical(expected[cursor.key]))
          throw new Error('Source changed during import: ' + cursor.key);
        count++;
        cursor.continue();
      }
      function compareBlob(cursor) {
        // IDB can finish a transaction between a Blob read and its promise
        // callback. Continue from an active IDB request while holding the lock.
        let resolved = false, actual, failure;
        decode(cursor.value).then(value => { actual = value; resolved = true; },
                                  err => { failure = err; resolved = true; });
        function waitForBlob() {
          store.count().onsuccess = () => {
            try {
              if (!resolved) return waitForBlob();
              if (failure) throw failure;
              check(cursor, actual);
            } catch (err) { error = err; tx.abort(); }
          };
        }
        waitForBlob();
      }
      const scan = store.openCursor();
      scan.onsuccess = () => {
        try {
          const cursor = scan.result;
          if (cursor) {
            const value = cursor.value;
            if (typeof Blob !== 'undefined' && value instanceof Blob) return compareBlob(cursor);
            const actual = parseLossless(typeof value === 'string' ? value : new TextDecoder().decode(value));
            check(cursor, actual);
          } else {
            if (count !== Object.keys(expected).length) throw new Error('Source keyset changed during import');
            store.clear();
            for (const [key, val] of Object.entries(writes)) store.put(encode(val), key);
          }
        } catch (err) { error = err; tx.abort(); }
      };
      tx.oncomplete = resolve;
      tx.onabort = tx.onerror = () => reject(error || tx.error || new Error('Replay migration aborted'));
    });
  }

  async function importPackage(pkg) {
    if (typeof pkg === 'string') pkg = parseLossless(pkg);
    if (pkg?.format !== 'omu.localRecords.import.unified' || !pkg.sources ||
        !pkg.entries?.__record_aliases__ || !Array.isArray(pkg.entries.__index__))
      throw new Error('Invalid unified replay import');
    if (canonical(await checksums(pkg.entries)) !== canonical(pkg.checksums)) throw new Error('Import checksum mismatch');
    const db = await open();
    try {
      const previous = await decodedAll(db), observed = await checksums(previous);
      if (canonical(observed) === canonical(pkg.checksums)) return 'already_unified';
      if (canonical(observed) !== canonical(pkg.sources)) throw new Error('Source changed after export');
      await replaceAtomic(db, previous, pkg.entries);
      return 'applied';
    } finally { db.close(); }
  }
  root.MatchRecordMigration = { exportLegacy, importPackage, canonical, hash, parseLossless };
  if (typeof module !== 'undefined') module.exports = root.MatchRecordMigration;
})(globalThis);
