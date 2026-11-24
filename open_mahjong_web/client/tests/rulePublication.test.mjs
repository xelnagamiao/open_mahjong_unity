import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { copyFile, mkdir, mkdtemp, readFile, rm } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import send from 'send'
import { serveRulePublication, writeRulePublication } from '../scripts/rule-publication.mjs'

const publicDir = fileURLToPath(new URL('../public/', import.meta.url))
const archiveDir = fileURLToPath(new URL('../../../other/rule/', import.meta.url))
const research = 'rule-research/'
const books = 'rulebooks/'
const studies = research + 'mahjong-studies/files/'
const phylogeny = research + 'mahjong-phylogeny/files/'
const groups = [
  [books + '绘图麻雀牌谱.pdf', research + 'drawing-mahjong.pdf', research + '绘图麻雀牌谱.pdf',
    research + 'drawing-mahjong/files/drawing-mahjong-local.pdf', phylogeny + 'drawing-local.pdf', studies + 'shen-yifan-1914.pdf'],
  [books + 'mil/四川麻将（SBR）竞赛规则（试行2025版）.pdf', books + '四川麻将（SBR）竞赛规则（试行2025版）.pdf', books + 'mil/四川麻将（SBR）竞赛规则（试行2025版） (1).pdf'],
  [books + 'K神麻雀规则说明书.pdf', books + 'K神麻雀规则_v2.63版_规范说明书.pdf'],
  [books + '十样锦麻将规则.pdf', books + '十样锦麻将规则书.pdf'],
  ...['culin-1895.pdf', 'fan-zengxiang-1906.pdf', 'greene-2015.pdf'].map(name => [studies + name, phylogeny + name])
]

test('published PDF addresses retain HEAD, byte ranges and conditional GET', async t => {
  const server = createServer((req, res) => {
    serveRulePublication(req, res, () => send(req, req.url.split('?')[0], { root: publicDir }).pipe(res))
  })
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  t.after(() => new Promise(resolve => {
    server.close(resolve)
    server.closeAllConnections()
  }))
  const base = 'http://127.0.0.1:' + server.address().port + '/'
  for (const group of groups) {
    const canonical = await fetch(base + group[0], { method: 'HEAD' })
    assert.equal(canonical.status, 200)
    const size = Number(canonical.headers.get('content-length'))
    for (const name of group) {
      const url = base + name.split('/').map(encodeURIComponent).join('/') + '?download=1'
      const head = await fetch(url, { method: 'HEAD' })
      assert.equal(head.status, 200, name)
      assert.match(head.headers.get('content-type'), /application\/pdf/)
      assert.equal(Number(head.headers.get('content-length')), size)
      const range = await fetch(url, { headers: { Range: 'bytes=0-4' } })
      assert.equal(range.status, 206, name)
      assert.equal(range.headers.get('content-range'), 'bytes 0-4/' + size)
      assert.equal(await range.text(), '%PDF-')
      const cached = await fetch(url, { headers: { 'If-None-Match': head.headers.get('etag'), 'Cache-Control': 'max-age=0' } })
      assert.equal(cached.status, 304, name)
    }
  }
  assert.equal((await fetch(base + research + '_README.md')).status, 404)
})

test('static exports preserve published bytes and omit unpublished research files', async t => {
  const scratch = fileURLToPath(new URL('../../../.om_workspace/', import.meta.url))
  await mkdir(scratch, { recursive: true })
  const dist = await mkdtemp(join(scratch, 'rule-publication-test-'))
  t.after(() => rm(dist, { recursive: true, force: true }))
  await mkdir(join(dist, 'rulebooks'))
  for (const name of ['K神麻雀规则说明书.pdf', '十样锦麻将规则.pdf']) {
    await copyFile(join(publicDir, 'rulebooks', name), join(dist, 'rulebooks', name))
  }
  writeRulePublication(dist)
  for (const group of groups) {
    const expected = await readFile(join(dist, group[0]))
    for (const name of group.slice(1)) {
      assert.ok((await readFile(join(dist, name))).equals(expected), name)
    }
  }
  const example = 'hongkong/snapshots/bbc-hk-mahjong-2018.html'
  assert.ok((await readFile(join(dist, research, example))).equals(await readFile(join(archiveDir, 'research', example))))
  await assert.rejects(readFile(join(dist, research, '_README.md')), { code: 'ENOENT' })
})
