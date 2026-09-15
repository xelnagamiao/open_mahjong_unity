import { build } from 'esbuild'
import { mkdir, mkdtemp, readdir, rm } from 'node:fs/promises'
import { spawn } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const clientRoot = fileURLToPath(new URL('..', import.meta.url))
const cacheRoot = path.join(clientRoot, 'node_modules', '.cache')
await mkdir(cacheRoot, { recursive: true })
const outdir = await mkdtemp(path.join(cacheRoot, 'game2d-tests-'))

try {
  const testFiles = (await readdir(path.join(clientRoot, 'tests')))
    .filter((name) => /^game2d.*\.test\.js$/.test(name))
    .sort()
  await build({
    absWorkingDir: clientRoot,
    entryPoints: testFiles.map((name) => path.join('tests', name)),
    outdir,
    bundle: true,
    platform: 'node',
    format: 'esm',
    packages: 'external',
    sourcemap: 'inline',
    define: { 'import.meta.env.BASE_URL': JSON.stringify('/') },
    plugins: [{
      name: 'unused-font-assets',
      setup(builder) {
        builder.onResolve({ filter: /\.(woff2?|ttf|txt)(\?url)?$/ }, (args) => ({
          path: args.path,
          namespace: 'unused-font-assets',
        }))
        builder.onLoad({ filter: /.*/, namespace: 'unused-font-assets' }, () => ({
          contents: '',
          loader: 'text',
        }))
      },
    }],
  })

  const code = await new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [
      '--enable-source-maps', '--test',
      ...testFiles.map((name) => path.join(outdir, name)),
    ], { cwd: clientRoot, stdio: 'inherit' })
    child.once('error', reject)
    child.once('exit', (exitCode) => resolve(exitCode ?? 1))
  })
  process.exitCode = code
} finally {
  const cleanupTarget = path.resolve(outdir)
  const cleanupRelative = path.relative(path.resolve(cacheRoot), cleanupTarget)
  if (path.isAbsolute(cleanupRelative)
    || path.dirname(cleanupRelative) !== '.'
    || !cleanupRelative.startsWith('game2d-tests-')) {
    throw new Error(`Refusing to remove a directory outside the test cache: ${cleanupTarget}`)
  }
  await rm(cleanupTarget, { recursive: true, force: true })
}
