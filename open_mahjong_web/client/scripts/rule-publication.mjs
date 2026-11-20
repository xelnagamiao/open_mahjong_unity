import { createHash } from 'node:crypto'
import { constants, copyFileSync, existsSync, linkSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import send from 'send'

const manifest = JSON.parse(readFileSync(new URL('./rule-publication.json', import.meta.url), 'utf8'))
const clientRoot = fileURLToPath(new URL('../', import.meta.url))
const publicRoot = join(clientRoot, 'public')
const archiveRoot = resolve(clientRoot, '../../other/rule')

// Only individually published files may be served from the research archive.
const files = new Map()
for (const name of manifest.research) {
  files.set('/rule-research/' + name, join(archiveRoot, 'research', name))
}
for (const [url, original] of Object.entries(manifest.originals)) {
  files.set('/' + url, join(archiveRoot, original))
}
for (const [alias, canonical] of Object.entries(manifest.aliases)) {
  files.set('/' + alias, files.get('/' + canonical) ?? join(publicRoot, canonical))
}

export function serveRulePublication(req, res, next) {
  if (req.method !== 'GET' && req.method !== 'HEAD') return next()
  let url
  try {
    url = decodeURIComponent(req.url.split('?')[0])
  } catch {
    return next()
  }
  const original = files.get(url)
  if (!original) return next()
  send(req, encodeURIComponent(basename(original)), { root: dirname(original) }).pipe(res)
}

export function writeRulePublication(distDir) {
  const published = new Map()
  // Some PDF aliases refer to books already included by the public/ copy.
  for (const canonical of Object.values(manifest.aliases)) {
    if (files.has('/' + canonical)) continue
    const target = join(distDir, canonical)
    published.set(digest(readFileSync(target)), target)
  }
  for (const [url, original] of files) {
    const target = join(distDir, url.slice(1))
    if (existsSync(target)) throw new Error('Duplicate published file: ' + url)
    mkdirSync(dirname(target), { recursive: true })
    const bytes = readFileSync(original)
    const hash = digest(bytes)
    const firstCopy = published.get(hash)
    if (firstCopy) {
      try {
        linkSync(firstCopy, target)
      } catch (error) {
        // Ordinary static hosting also works when the build volume lacks hard links.
        if (!['ENOSYS', 'ENOTSUP', 'EPERM', 'EXDEV', 'EOPNOTSUPP'].includes(error.code)) throw error
        copyFileSync(firstCopy, target, constants.COPYFILE_EXCL)
      }
    } else {
      writeFileSync(target, bytes, { flag: 'wx' })
      published.set(hash, target)
    }
  }
}

function digest(bytes) {
  return createHash('sha256').update(bytes).digest('hex')
}

export function rulePublicationPlugin() {
  let distDir
  return {
    name: 'rule-publication',
    configResolved(config) {
      distDir = resolve(config.root, config.build.outDir)
    },
    configureServer(server) {
      server.middlewares.use(serveRulePublication)
    },
    writeBundle: {
      order: 'post',
      sequential: true,
      handler() { writeRulePublication(distDir) }
    }
  }
}
